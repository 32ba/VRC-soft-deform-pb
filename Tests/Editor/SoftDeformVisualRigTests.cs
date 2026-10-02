using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.platform;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace SoftDeformPB.Tests.Editor
{
    public sealed class SoftDeformVisualRigTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void ProcessAvatar_RemapsBodyAndClothingBranchesWithoutChangingRestSkinOrPhysics(bool animatedScale, bool parallel)
        {
            var avatar = new GameObject("Visual rig test");
            var mesh = new Mesh { name = "Shared source skin" };
            var before = new Mesh();
            var after = new Mesh();
            AnimatorController authoredController = null;
            AnimationClip authoredClip = null;
            AnimatorStateMachine authoredStateMachine = null;
            try
            {
                avatar.AddComponent<Animator>();
                var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
                Transform Bone(Transform parent, string name, Vector3 position)
                {
                    var bone = new GameObject(name).transform;
                    bone.SetParent(parent, false);
                    bone.localPosition = position;
                    return bone;
                }
                var chest = Bone(avatar.transform, "Chest", Vector3.up);
                var left = Bone(chest, "Custom left", new Vector3(-0.1f, 0.0f, 0.1f));
                left.localRotation = Quaternion.Euler(10, 20, 30);
                left.localScale = new Vector3(1.2f, 0.8f, 1.1f);
                var middle = Bone(left, "Middle", Vector3.forward * 0.05f);
                middle.localScale = new Vector3(0.9f, 1.1f, 1.0f);
                if (animatedScale)
                {
                    authoredClip = new AnimationClip { name = "Authored clothing scale" };
                    AnimationUtility.SetEditorCurve(authoredClip,
                        EditorCurveBinding.FloatCurve("Chest/Custom left/Middle", typeof(Transform), "m_LocalScale.x"),
                        AnimationCurve.Linear(0, 0.9f, 1, 1.5f));
                    authoredStateMachine = new AnimatorStateMachine { name = "Clothing state machine" };
                    authoredStateMachine.AddState("Clothing size").motion = authoredClip;
                    authoredController = new AnimatorController { name = "Clothing FX" };
                    authoredController.AddLayer(new AnimatorControllerLayer
                    {
                        name = "Clothing size", defaultWeight = 1, stateMachine = authoredStateMachine
                    });
                    var layers = descriptor.baseAnimationLayers ?? new[]
                    {
                        new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.FX }
                    };
                    int index = System.Array.FindIndex(layers, l => l.type == VRCAvatarDescriptor.AnimLayerType.FX);
                    layers[index].isDefault = false;
                    layers[index].animatorController = authoredController;
                    descriptor.baseAnimationLayers = layers;
                    descriptor.specialAnimationLayers = descriptor.specialAnimationLayers ??
                        System.Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                }
                var tip = Bone(middle, "End", Vector3.forward * 0.05f);
                var branch = Bone(middle, "Clothing branch", Vector3.right * 0.02f);
                var ignored = Bone(left, "Ignored helper", Vector3.left * 0.02f);
                var right = Bone(chest, "Custom right", new Vector3(0.1f, 0.0f, 0.1f));
                var rightTip = Bone(right, "End", Vector3.forward * 0.1f);
                var leftPhysBone = left.gameObject.AddComponent<VRCPhysBone>();
                leftPhysBone.ignoreTransforms = new System.Collections.Generic.List<Transform> { ignored };
                var rightPhysBone = right.gameObject.AddComponent<VRCPhysBone>();
                var additional = Bone(chest, "Additional PhysBone", Vector3.zero).gameObject.AddComponent<VRCPhysBone>();
                additional.rootTransform = left;
                var bones = new[] { left, middle, tip, branch, right, rightTip, chest, null };
                mesh.vertices = Enumerable.Range(0, 6).Select(i => new Vector3(i * 0.01f, i % 2 * 0.02f, 0.1f)).ToArray();
                mesh.triangles = new[] { 0, 1, 2, 3, 4, 5 };
                mesh.boneWeights = Enumerable.Range(0, 6).Select(i => new BoneWeight { boneIndex0 = i, weight0 = 1 }).ToArray();
                mesh.bindposes = bones.Select(b => b != null ? b.worldToLocalMatrix * avatar.transform.localToWorldMatrix : Matrix4x4.identity).ToArray();
                mesh.RecalculateBounds();
                SkinnedMeshRenderer Renderer(string name)
                {
                    var renderer = Bone(avatar.transform, name, Vector3.zero).gameObject.AddComponent<SkinnedMeshRenderer>();
                    renderer.sharedMesh = mesh;
                    renderer.bones = bones;
                    renderer.rootBone = left;
                    return renderer;
                }
                var body = Renderer("Body");
                var clothes = Renderer("Clothes");
                clothes.gameObject.SetActive(false);
                body.BakeMesh(before);
                var originalBindposes = mesh.bindposes;
                var originalWeights = mesh.boneWeights;
                var originalScale = left.localScale;
                var setup = avatar.AddComponent<SoftDeformPBSetup>();
                setup.leftBreast = left;
                setup.rightBreast = right;
                setup.responseSamples = 3;
                if (parallel)
                {
                    setup.verticalAngleRetention = 0.6f;
                    setup.squishHorizontalAngleRetention = 0.5f;
                    setup.rootHorizontalTranslation = 0.3f;
                    setup.rootVerticalRotation = 0.4f;
                }
                using (new OverrideTemporaryDirectoryScope(null))
                    Assert.That(AvatarProcessor.ProcessAvatar(avatar, AmbientPlatform.CurrentPlatform).Successful, Is.True);

                var visualRoot = left.GetComponentsInChildren<Transform>().Single(t => t.name == "SoftDeformPB Visual Left");
                Assert.That(visualRoot, Is.Not.Null);
                Assert.That(visualRoot.GetComponent<VRCParentConstraint>(), Is.Null);
                Assert.That(visualRoot.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(visualRoot.localScale, Is.EqualTo(Vector3.one));
                Assert.That(left.localScale, Is.EqualTo(originalScale));
                Assert.That(visualRoot.Find("Ignored helper"), Is.Null);
                Assert.That(leftPhysBone.ignoreTransforms, Does.Contain(ignored));
                Assert.That(leftPhysBone.ignoreTransforms, Does.Contain(visualRoot));
                Assert.That(additional.ignoreTransforms, Does.Contain(visualRoot));
                Assert.That(rightPhysBone.ignoreTransforms, Does.Contain(right.GetComponentsInChildren<Transform>().Single(t => t.name == "SoftDeformPB Visual Right")));
                var constraints = avatar.GetComponentsInChildren<VRCParentConstraint>(true);
                Assert.That(constraints, Has.Length.EqualTo(4));
                Assert.That(constraints.All(c => c.IsActive && c.SolveInLocalSpace && c.Sources.Count == 1), Is.True);
                Assert.That(constraints.All(c => c.GetComponent<VRCPhysBone>() == null), Is.True);
                var scaleConstraints = avatar.GetComponentsInChildren<VRCScaleConstraint>(true);
                Assert.That(scaleConstraints, Has.Length.EqualTo(animatedScale ? 1 : 0));
                if (animatedScale)
                {
                    var scale = scaleConstraints.Single();
                    Assert.That(scale.transform, Is.SameAs(visualRoot.Find("Middle")));
                    Assert.That(scale.Sources[0].SourceTransform, Is.SameAs(middle));
                    Assert.That(scale.ScaleOffset, Is.EqualTo(Vector3.one));
                    Assert.That(scale.IsActive && scale.SolveInLocalSpace, Is.True);
                    var fx = descriptor.baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                    var clip = fx.animationClips.Single(c => c.name == "Authored clothing scale");
                    var binding = AnimationUtility.GetCurveBindings(clip).Single(b => b.propertyName == "m_LocalScale.x");
                    Assert.That(avatar.transform.Find(binding.path), Is.SameAs(middle));
                    Assert.That(AnimationUtility.GetEditorCurve(clip, binding).Evaluate(1), Is.EqualTo(1.5f));
                }
                Assert.That(body.bones, Is.EqualTo(clothes.bones));
                Assert.That(body.bones[0], Is.SameAs(visualRoot));
                Assert.That(body.bones[3], Is.SameAs(visualRoot.Find("Middle/Clothing branch")));
                Assert.That(body.bones[6], Is.SameAs(chest));
                Assert.That(body.bones[7], Is.Null);
                Assert.That(body.rootBone, Is.SameAs(visualRoot));
                Assert.That(clothes.rootBone, Is.SameAs(visualRoot));
                Assert.That(clothes.gameObject.activeSelf, Is.False);
                Assert.That(mesh.bindposes, Is.EqualTo(originalBindposes));
                Assert.That(mesh.boneWeights, Is.EqualTo(originalWeights));
                body.BakeMesh(after);
                Assert.That(after.vertexCount, Is.EqualTo(before.vertexCount));
                for (int index = 0; index < before.vertexCount; index++)
                    Assert.That(Vector3.Distance(after.vertices[index], before.vertices[index]), Is.LessThan(0.00001f));
            }
            finally
            {
                Object.DestroyImmediate(avatar);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(before);
                Object.DestroyImmediate(after);
                if (authoredController != null) Object.DestroyImmediate(authoredController);
                if (authoredClip != null) Object.DestroyImmediate(authoredClip);
                if (authoredStateMachine != null) Object.DestroyImmediate(authoredStateMachine);
            }
        }
    }
}
