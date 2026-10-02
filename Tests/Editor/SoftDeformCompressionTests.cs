using System;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.platform;
using NUnit.Framework;
using SoftDeformPB.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using Object = UnityEngine.Object;

namespace SoftDeformPB.Tests.Editor
{
    public sealed class SoftDeformCompressionTests
    {
        private static SoftDeformPBSetup Avatar(int axis = 2)
        {
            var avatar = new GameObject("Compression test");
            avatar.AddComponent<Animator>(); avatar.AddComponent<VRCAvatarDescriptor>();
            var setup = avatar.AddComponent<SoftDeformPBSetup>();
            setup.responseSamples = 3; setup.rootMotionMaxOffset = 0; setup.secondaryMotionStrength = 0;
            setup.gravitySupineRootSpread = 0;
            setup.lateralCompressionDepth = 0.3f; setup.gatheringAngleCorrection = 8;
            setup.minimumCompressionRatio = 0.6f;
            Transform Side(string name, float x)
            {
                var t = new GameObject(name).transform; t.SetParent(avatar.transform, false);
                t.localPosition = new Vector3(x, 1, 0.1f);
                t.localRotation = Quaternion.FromToRotation(SoftDeformShapeMath.AxisVector(axis), Vector3.forward);
                var pb = t.gameObject.AddComponent<VRCPhysBone>(); pb.radius = 0.01f;
                pb.endpointPosition = SoftDeformShapeMath.AxisVector(axis) * 0.1f;
                return t;
            }
            setup.leftBreast = Side("Left", -0.1f); setup.rightBreast = Side("Right", 0.1f);
            return setup;
        }

        private static void Build(GameObject avatar)
        {
            using (new OverrideTemporaryDirectoryScope(null))
                Assert.That(AvatarProcessor.ProcessAvatar(avatar, AmbientPlatform.CurrentPlatform).Successful, Is.True);
        }

        private static Animator PlayFx(GameObject avatar)
        {
            var animator = avatar.GetComponent<Animator>();
            animator.runtimeAnimatorController = avatar.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers
                .Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
            foreach (var p in animator.parameters.Where(p => p.name.Contains("Gravity")))
                animator.SetFloat(p.name, 0.5f + 0.002f / 0.204f);
            animator.Update(0.02f);
            return animator;
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Animator_RequiresInwardMotionAndContact_AndReleasesBothSides(int axis)
        {
            var setup = Avatar(axis); var avatar = setup.gameObject;
            var physical = new[] { setup.leftBreast, setup.rightBreast };
            var axes = SoftDeformShapeMath.InferAxes(physical[0], SoftDeformShapeMath.AxisVector(axis));
            try
            {
                Build(avatar); var animator = PlayFx(avatar);
                var visuals = new[] { "Left", "Right" }.Select(s => avatar.GetComponentsInChildren<Transform>()
                    .Single(t => t.name == "SoftDeformPB Visual " + s)).ToArray();
                var initial = visuals.Select(t => t.localScale).ToArray();
                foreach (var input in new[] { new Vector2(1, 0), new Vector2(0.5f, 1), new Vector2(0, 0) })
                {
                    foreach (var p in physical)
                    {
                        string prefix = p.GetComponent<VRCPhysBone>().parameter;
                        animator.SetFloat(prefix + "_Inward", input.x); animator.SetFloat(prefix + "_OuterTouch", input.y);
                    }
                    animator.Update(0.02f);
                    for (int i = 0; i < 2; i++)
                        Assert.That(Vector3.Distance(visuals[i].localScale, initial[i]), Is.LessThan(0.00001f), "No false lateral response");
                }
                // A one-sided contact must not activate the opposite side.
                animator.SetFloat(physical[0].GetComponent<VRCPhysBone>().parameter + "_Inward", 1);
                animator.SetFloat(physical[0].GetComponent<VRCPhysBone>().parameter + "_OuterTouch", 1);
                animator.Update(0.02f);
                Assert.That(visuals[0].localScale[axes.Horizontal], Is.EqualTo(initial[0][axes.Horizontal] * 0.7f).Within(0.0001f));
                Assert.That(visuals[0].localScale[axes.Vertical], Is.GreaterThan(initial[0][axes.Vertical]));
                Assert.That(visuals[0].localScale[axes.Longitudinal], Is.GreaterThan(initial[0][axes.Longitudinal]));
                Assert.That(Vector3.Distance(visuals[1].localScale, initial[1]), Is.LessThan(0.00001f));
                var leftGather = visuals[0].parent;
                Assert.That(Quaternion.Angle(Quaternion.identity, leftGather.localRotation), Is.EqualTo(8).Within(0.01f));
                animator.SetFloat(physical[1].GetComponent<VRCPhysBone>().parameter + "_Inward", 1);
                animator.SetFloat(physical[1].GetComponent<VRCPhysBone>().parameter + "_OuterTouch", 1);
                animator.Update(0.02f);
                Assert.That(Quaternion.Angle(leftGather.localRotation, Quaternion.Inverse(visuals[1].parent.localRotation)), Is.LessThan(0.01f));
                foreach (var p in physical)
                {
                    string prefix = p.GetComponent<VRCPhysBone>().parameter;
                    animator.SetFloat(prefix + "_Inward", 0); animator.SetFloat(prefix + "_OuterTouch", 0);
                }
                animator.Update(0.02f);
                for (int i = 0; i < 2; i++)
                {
                    Assert.That(Vector3.Distance(visuals[i].localScale, initial[i]), Is.LessThan(0.00001f));
                    Assert.That(Quaternion.Angle(Quaternion.identity, visuals[i].parent.localRotation), Is.LessThan(0.01f));
                    Assert.That(physical[i].GetComponent<VRCPhysBone>().ignoreTransforms, Does.Contain(visuals[i].parent));
                }
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [TestCase(1f, 0f, 0f, 0f)]
        [TestCase(0f, 1f, 1f, 0f)]
        [TestCase(0.25f, 0.5f, 0.5f, 4.5f)]
        public void GatheringCorrection_OnlyRestoresAngleRemainingAfterParallelMotion(
            float normal, float compressed, float squish, float expectedDegrees)
        {
            var setup = Avatar(); var avatar = setup.gameObject; var physical = setup.leftBreast;
            setup.horizontalAngleRetention = normal; setup.squishHorizontalAngleRetention = compressed;
            try
            {
                Build(avatar); var animator = PlayFx(avatar);
                string prefix = physical.GetComponent<VRCPhysBone>().parameter;
                animator.SetFloat(prefix + "_Inward", 1);
                animator.SetFloat(prefix + "_OuterTouch", 1);
                animator.SetFloat(prefix + "_Squish", squish);
                animator.Update(0.02f);
                var gather = avatar.GetComponentsInChildren<Transform>().Single(t => t.name == "SoftDeformPB Gather Left");
                Assert.That(Quaternion.Angle(Quaternion.identity, gather.localRotation), Is.EqualTo(expectedDegrees).Within(0.01f));
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompressionBudget_CoversCurveOvershootAndContinuousAnimatorBlending(bool weighted)
        {
            var setup = Avatar(); var avatar = setup.gameObject; var physical = setup.leftBreast;
            setup.maxSquish = 0.9f; setup.gravitySupineCompression = 0.3f;
            setup.minimumCompressionRatio = 0.65f; setup.lateralCompressionDepth = 0.5f;
            var a = new Keyframe(0, 0.8f, 0, 6);
            var b = new Keyframe(1, 0.9f, -5, 0);
            if (weighted) { a.weightedMode = WeightedMode.Out; a.outWeight = 0.7f; b.weightedMode = WeightedMode.In; b.inWeight = 0.2f; }
            var curve = new AnimationCurve(a, b);
            physical.GetComponent<VRCPhysBone>().maxSquishCurve = curve;
            var original = curve.keys;
            float bound = SoftDeformCompressionMath.CurveUpperBound(curve);
            try
            {
                Assert.That(bound, Is.GreaterThan(1));
                Build(avatar); var pb = physical.GetComponent<VRCPhysBone>();
                Assert.That(pb.maxSquishCurve.keys, Is.EqualTo(original));
                for (int i = 0; i <= 1000; i++)
                    Assert.That(1 - pb.maxSquish * curve.Evaluate(i / 1000f), Is.GreaterThanOrEqualTo(0.65f - 0.000001f));
                var animator = PlayFx(avatar);
                var visual = physical.GetComponentsInChildren<Transform>().Single(t => t.name == "SoftDeformPB Visual Left");
                foreach (float input in new[] { 0f, 0.17f, 0.42f, 0.83f, 1f })
                {
                    animator.SetFloat(pb.parameter + "_Inward", input);
                    animator.SetFloat(pb.parameter + "_OuterTouch", 0.63f);
                    animator.SetFloat(pb.parameter + "_Squish", input);
                    animator.SetFloat(pb.parameter + "_Stretch", 1 - input);
                    foreach (var parameter in animator.parameters.Where(p => p.name.Contains("GravityLongitudinal")))
                        animator.SetFloat(parameter.name, input);
                    animator.Update(0.02f);
                    Assert.That(visual.localScale.x, Is.GreaterThanOrEqualTo(0.65f - 0.000001f));
                    Assert.That(visual.localScale.y, Is.GreaterThanOrEqualTo(0.65f - 0.000001f));
                    Assert.That(visual.localScale.z * (1 - pb.maxSquish * bound), Is.GreaterThanOrEqualTo(0.65f - 0.000001f));
                }
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void PostureCompression_UsesRemainingBudgetAndBoundsAllGeneratedClips()
        {
            var setup = Avatar(); var avatar = setup.gameObject;
            var physical = setup.leftBreast;
            setup.maxSquish = 0.2f; setup.minimumCompressionRatio = 0.7f; setup.gravitySupineCompression = 0.3f;
            setup.lateralCompressionDepth = 0.5f;
            try
            {
                Build(avatar);
                var fx = avatar.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                float smallest = float.PositiveInfinity;
                foreach (var clip in fx.animationClips.Where(c => c.name.StartsWith("SoftDeformPB_Left_")))
                foreach (var binding in AnimationUtility.GetCurveBindings(clip).Where(b => b.propertyName == "m_LocalScale.z" && b.path.EndsWith("SoftDeformPB Visual Left")))
                    smallest = Mathf.Min(smallest, AnimationUtility.GetEditorCurve(clip, binding).Evaluate(0));
                Assert.That(smallest, Is.EqualTo(0.875f).Within(0.00001f));
                Assert.That(physical.GetComponent<VRCPhysBone>().maxSquish, Is.EqualTo(0.2f));
                Assert.That(smallest * 0.8f, Is.EqualTo(0.7f).Within(0.00001f));
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [TestCase(VRCPhysBoneBase.AdvancedBool.False, true, true, false, false)]
        [TestCase(VRCPhysBoneBase.AdvancedBool.True, false, false, true, true)]
        [TestCase(VRCPhysBoneBase.AdvancedBool.Other, true, false, true, false)]
        [TestCase(VRCPhysBoneBase.AdvancedBool.Other, false, true, false, true)]
        public void ContactGate_PreservesCollisionPermissionsAndPrivateProbeIsolation(VRCPhysBoneBase.AdvancedBool permission,
            bool self, bool others, bool expectedSelf, bool expectedOthers)
        {
            var setup = Avatar(); var avatar = setup.gameObject;
            var pb = setup.leftBreast.GetComponent<VRCPhysBone>();
            pb.allowCollision = permission; pb.collisionFilter.allowSelf = self; pb.collisionFilter.allowOthers = others;
            try
            {
                Build(avatar);
                var receivers = avatar.GetComponentsInChildren<VRCContactReceiver>();
                var touch = receivers.Single(r => r.name == "SoftDeformPB Outer Touch Left");
                var inward = receivers.Single(r => r.name == "SoftDeformPB Inward Left");
                Assert.That(touch.allowSelf, Is.EqualTo(expectedSelf)); Assert.That(touch.allowOthers, Is.EqualTo(expectedOthers));
                Assert.That(touch.collisionTags, Is.EquivalentTo(new[] { "Hand", "Finger" }));
                Assert.That(inward.allowSelf && !inward.allowOthers && !inward.localOnly, Is.True);
                Assert.That(receivers.Single(r => r.name == "SoftDeformPB Inward Right").collisionTags, Is.Not.EqualTo(inward.collisionTags));
                Assert.That(avatar.GetComponentsInChildren<VRCPhysBone>().Length, Is.EqualTo(2), "Sensors do not add physics chains");
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [TestCase("_Inward")]
        [TestCase("_OuterTouch")]
        public void ExistingContactWriter_IsRejectedBeforeGeneration(string suffix)
        {
            var setup = Avatar();
            try
            {
                var receiver = setup.gameObject.AddComponent<VRCContactReceiver>();
                receiver.parameter = setup.parameterPrefix + "_L" + suffix;
                Assert.Throws<InvalidOperationException>(() => SoftDeformPBGeneratePass.ValidateParameterPrefixes(setup.transform, new[] { setup }));
                Assert.That(setup.leftBreast.parent, Is.SameAs(setup.transform));
            }
            finally { Object.DestroyImmediate(setup.gameObject); }
        }

        [Test]
        public void CompressionReference_MirrorsAuthoredPoseAndRemainsOutsidePhysics()
        {
            var setup = Avatar(); var avatar = setup.gameObject;
            var physical = setup.leftBreast;
            var clip = new AnimationClip { name = "Authored compression reference" };
            var controller = new AnimatorController(); var machine = new AnimatorStateMachine();
            try
            {
                foreach (string property in new[] { "m_LocalPosition.x", "m_LocalScale.y", "localEulerAnglesRaw.z" })
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Left", typeof(Transform), property), AnimationCurve.Linear(0, 0.4f, 1, 0.7f));
                machine.AddState("Authored").motion = clip;
                controller.AddLayer(new AnimatorControllerLayer { name = "Authored", stateMachine = machine, defaultWeight = 1 });
                var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
                descriptor.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.FX, animatorController = controller, isDefault = false } };
                descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                Build(avatar);
                var reference = avatar.GetComponentsInChildren<Transform>().Single(t => t.name == "SoftDeformPB Compression Reference Left");
                var fx = descriptor.baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                var output = fx.animationClips.Single(c => c.name == clip.name);
                string path = AnimationUtility.CalculateTransformPath(reference, avatar.transform);
                foreach (string property in new[] { "m_LocalPosition.x", "m_LocalScale.y", "localEulerAnglesRaw.z" })
                {
                    var curve = AnimationUtility.GetEditorCurve(output, EditorCurveBinding.FloatCurve(path, typeof(Transform), property));
                    Assert.That(curve, Is.Not.Null); Assert.That(curve.Evaluate(0.5f), Is.EqualTo(0.55f).Within(0.00001f));
                }
                Assert.That(reference.parent, Is.SameAs(physical.parent));
            }
            finally { Object.DestroyImmediate(avatar); Object.DestroyImmediate(controller); Object.DestroyImmediate(machine); Object.DestroyImmediate(clip); }
        }
    }
}
