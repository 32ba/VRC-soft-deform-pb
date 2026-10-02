using System.Linq;
using nadena.dev.modular_avatar.core;
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
    public sealed class SoftDeformClothingTests
    {
        [TestCase(false, false, true, false, false)]
        [TestCase(true, false, true, false, false)]
        [TestCase(false, true, true, false, false)]
        [TestCase(true, true, false, false, false)]
        [TestCase(true, false, true, true, false)]
        [TestCase(false, true, true, false, true)]
        [TestCase(true, false, false, true, true)]
        public void MergeArmature_OutfitReceivesVisualMotionAndRetainsRestSkin(bool renamed, bool copiedPhysBones, bool active, bool animatedDetail, bool parallel)
        {
            var avatar = new GameObject("Clothing merge test");
            var sourceMesh = new Mesh { name = "Clothing test skin" };
            var before = new Mesh(); var bodySample = new Mesh(); var clothesSample = new Mesh();
            AnimationClip clip = null; AnimatorController controller = null; AnimatorStateMachine states = null;
            try
            {
                avatar.AddComponent<Animator>();
                avatar.AddComponent<VRCAvatarDescriptor>();
                Transform Bone(Transform parent, string name, Vector3 position)
                {
                    var bone = new GameObject(name).transform;
                    bone.SetParent(parent, false); bone.localPosition = position; return bone;
                }
                var chest = Bone(avatar.transform, "Chest", Vector3.up);
                var left = Bone(chest, "Left", new Vector3(-0.1f, 0, 0.1f));
                var leftTip = Bone(left, "Tip", Vector3.forward * 0.1f);
                var right = Bone(chest, "Right", new Vector3(0.1f, 0, 0.1f));
                var rightTip = Bone(right, "Tip", Vector3.forward * 0.1f);
                var leftPb = left.gameObject.AddComponent<VRCPhysBone>(); leftPb.radius = 0.01f;
                var rightPb = right.gameObject.AddComponent<VRCPhysBone>(); rightPb.radius = 0.01f;
                var outfit = Bone(avatar.transform, "Outfit", Vector3.zero);
                var armature = Bone(outfit, "Armature", Vector3.up);
                string prefix = renamed ? "Out_" : ""; string suffix = renamed ? "_Cloth" : "";
                var clothLeft = Bone(armature, prefix + "Left" + suffix, left.localPosition);
                var clothLeftTip = Bone(clothLeft, prefix + "Tip" + suffix, leftTip.localPosition);
                var clothRight = Bone(armature, prefix + "Right" + suffix, right.localPosition);
                var clothRightTip = Bone(clothRight, prefix + "Tip" + suffix, rightTip.localPosition);
                var detail = Bone(clothLeftTip, "Clothing detail", Vector3.right * 0.02f);
                if (animatedDetail)
                {
                    clip = new AnimationClip { name = "Outfit detail scale" };
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(
                        AnimationUtility.CalculateTransformPath(detail, outfit), typeof(Transform), "m_LocalScale.x"),
                        AnimationCurve.Linear(0, 1, 1, 1.5f));
                    states = new AnimatorStateMachine(); states.AddState("Detail").motion = clip;
                    controller = new AnimatorController();
                    controller.AddLayer(new AnimatorControllerLayer { name = "Outfit detail", defaultWeight = 1, stateMachine = states });
                    var mergeAnimator = outfit.gameObject.AddComponent<ModularAvatarMergeAnimator>();
                    mergeAnimator.animator = controller; mergeAnimator.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
                    mergeAnimator.pathMode = MergeAnimatorPathMode.Relative;
                }
                if (copiedPhysBones)
                {
                    clothLeft.gameObject.AddComponent<VRCPhysBone>().radius = 0.01f;
                    clothRight.gameObject.AddComponent<VRCPhysBone>().radius = 0.01f;
                }
                var merge = armature.gameObject.AddComponent<ModularAvatarMergeArmature>();
                merge.LockMode = ArmatureLockMode.NotLocked;
                merge.prefix = prefix; merge.suffix = suffix; merge.mergeTarget.Set(chest.gameObject);
                var physicalBones = new[] { left, leftTip, right, rightTip, leftTip };
                sourceMesh.vertices = new[] { left.position, leftTip.position, right.position, rightTip.position, detail.position };
                sourceMesh.triangles = new[] { 0, 1, 2, 1, 3, 2, 0, 4, 1 };
                sourceMesh.boneWeights = Enumerable.Range(0, 5).Select(i => new BoneWeight { boneIndex0 = i, weight0 = 1 }).ToArray();
                sourceMesh.bindposes = physicalBones.Select(t => t.worldToLocalMatrix).ToArray();
                sourceMesh.RecalculateBounds();
                var body = Bone(avatar.transform, "Body", Vector3.zero).gameObject.AddComponent<SkinnedMeshRenderer>();
                body.sharedMesh = sourceMesh; body.bones = physicalBones; body.rootBone = chest;
                var clothes = Bone(outfit, "Clothes", Vector3.zero).gameObject.AddComponent<SkinnedMeshRenderer>();
                var clothingMesh = Object.Instantiate(sourceMesh);
                try
                {
                    clothes.sharedMesh = clothingMesh;
                    clothes.bones = new[] { clothLeft, clothLeftTip, clothRight, clothRightTip, detail };
                    clothes.rootBone = clothLeft;
                    clothingMesh.bindposes = clothes.bones.Select(t => t.worldToLocalMatrix).ToArray();
                    var bindposes = clothingMesh.bindposes; var weights = clothingMesh.boneWeights;
                    clothes.BakeMesh(before); outfit.gameObject.SetActive(active);
                    var setup = avatar.AddComponent<SoftDeformPBSetup>();
                    setup.leftBreast = left; setup.rightBreast = right; setup.responseSamples = 3;
                    if (parallel)
                    {
                        setup.verticalAngleRetention = 0.6f;
                        setup.horizontalAngleRetention = 0.2f;
                        setup.squishVerticalAngleRetention = 0.5f;
                        setup.rootHorizontalTranslation = 0.3f;
                        setup.rootVerticalRotation = 0.4f;
                        setup.lateralCompressionDepth = 0.25f;
                        setup.gatheringAngleCorrection = 6;
                        setup.minimumCompressionRatio = 0.6f;
                        var support = outfit.gameObject.AddComponent<SoftDeformPBClothingSupport>();
                        support.maximumRootTravel = 0.006f;
                        support.maximumSwingAngle = 25;
                        support.maximumSeparationIncrease = 0.008f;
                    }
                    using (new OverrideTemporaryDirectoryScope(null))
                        Assert.That(AvatarProcessor.ProcessAvatar(avatar, AmbientPlatform.CurrentPlatform).Successful, Is.True);
                    var visualLeft = left.GetComponentsInChildren<Transform>().Single(t => t.name == "SoftDeformPB Visual Left");
                    var visualRight = right.GetComponentsInChildren<Transform>().Single(t => t.name == "SoftDeformPB Visual Right");
                    for (int i = 0; i < 4; i++)
                        Assert.That(clothes.bones[i], Is.SameAs(body.bones[i]), "Merged outfit bone " + i);
                    Assert.That(clothes.bones[4].IsChildOf(visualLeft), Is.True, "Clothing-only branch must receive visual deformation");
                    if (animatedDetail)
                    {
                        var scale = clothes.bones[4].GetComponent<VRCScaleConstraint>();
                        Assert.That(scale, Is.Not.Null, "Merged animated detail needs scale tracking");
                        Assert.That(scale.Sources[0].SourceTransform, Is.SameAs(detail));
                        var fx = avatar.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                        var mergedClip = fx.animationClips.Single(c => c.name == clip.name);
                        var binding = AnimationUtility.GetCurveBindings(mergedClip).Single(b => b.propertyName == "m_LocalScale.x");
                        Assert.That(avatar.transform.Find(binding.path), Is.SameAs(detail));
                        Assert.That(AnimationUtility.GetEditorCurve(mergedClip, binding).Evaluate(1), Is.EqualTo(1.5f));
                    }
                    Assert.That(clothes.rootBone.IsChildOf(visualLeft), Is.True);
                    Assert.That(leftPb.ignoreTransforms, Does.Contain(visualLeft));
                    Assert.That(rightPb.ignoreTransforms, Does.Contain(visualRight));
                    Assert.That(avatar.GetComponentsInChildren<VRCPhysBone>(true).Length, Is.EqualTo(8));
                    Assert.That(outfit.gameObject.activeSelf, Is.EqualTo(active));
                    Assert.That(clothingMesh.bindposes, Is.EqualTo(bindposes));
                    Assert.That(clothingMesh.boneWeights, Is.EqualTo(weights));
                    clothes.BakeMesh(clothesSample);
                    for (int i = 0; i < before.vertexCount; i++)
                        Assert.That(Vector3.Distance(before.vertices[i], clothesSample.vertices[i]), Is.LessThan(0.00001f));
                    if (parallel)
                    {
                        left.localRotation = Quaternion.Euler(25, -15, 0);
                        SoftDeformNativeConstraints.Evaluate(avatar);
                        body.BakeMesh(bodySample); clothes.BakeMesh(clothesSample);
                        for (int i = 0; i < 4; i++)
                            Assert.That(Vector3.Distance(bodySample.vertices[i], clothesSample.vertices[i]), Is.LessThan(0.00001f));
                    }
                    visualLeft.localScale = new Vector3(1.2f, 0.95f, 0.9f);
                    visualLeft.localRotation = Quaternion.Euler(7, -3, 2);
                    visualRight.localScale = visualLeft.localScale;
                    body.BakeMesh(bodySample); clothes.BakeMesh(clothesSample);
                    for (int i = 0; i < 4; i++)
                        Assert.That(Vector3.Distance(bodySample.vertices[i], clothesSample.vertices[i]), Is.LessThan(0.00001f));
                }
                finally { Object.DestroyImmediate(clothingMesh); }
            }
            finally
            {
                Object.DestroyImmediate(avatar); Object.DestroyImmediate(sourceMesh);
                Object.DestroyImmediate(before); Object.DestroyImmediate(bodySample); Object.DestroyImmediate(clothesSample);
                if (controller != null) Object.DestroyImmediate(controller);
                if (clip != null) Object.DestroyImmediate(clip);
                if (states != null) Object.DestroyImmediate(states);
            }
        }
    }
}
