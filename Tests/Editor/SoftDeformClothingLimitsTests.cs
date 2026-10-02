using System;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.platform;
using NUnit.Framework;
using SoftDeformPB.Editor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using Object = UnityEngine.Object;

namespace SoftDeformPB.Tests.Editor
{
    public sealed class SoftDeformClothingLimitsTests
    {
        private static SoftDeformPBSetup Avatar()
        {
            var avatar = new GameObject("Clothing limits test");
            avatar.AddComponent<Animator>(); avatar.AddComponent<VRCAvatarDescriptor>();
            var setup = avatar.AddComponent<SoftDeformPBSetup>(); setup.responseSamples = 3;
            setup.lateralCompressionDepth = 0.3f; setup.gatheringAngleCorrection = 8;
            setup.horizontalAngleRetention = 0.2f; setup.verticalAngleRetention = 0.6f;
            setup.squishHorizontalAngleRetention = 0.5f;
            Transform Side(string name, float x)
            {
                var t = new GameObject(name).transform; t.SetParent(avatar.transform, false);
                t.localPosition = new Vector3(x, 1, 0.1f);
                var pb = t.gameObject.AddComponent<VRCPhysBone>(); pb.endpointPosition = Vector3.forward * 0.1f;
                pb.radius = 0.01f; pb.limitRotation = new Vector3(20, -10, 15);
                pb.allowGrabbing = VRCPhysBoneBase.AdvancedBool.Other; pb.grabFilter.allowSelf = false; pb.grabFilter.allowOthers = true;
                return t;
            }
            setup.leftBreast = Side("Left", -0.1f); setup.rightBreast = Side("Right", 0.1f);
            return setup;
        }

        private static SoftDeformPBClothingSupport Support(SoftDeformPBSetup setup, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(setup.transform, false);
            return go.AddComponent<SoftDeformPBClothingSupport>();
        }

        private static SoftDeformClothingLimits Resolve(SoftDeformPBSetup setup) =>
            SoftDeformClothingLimits.Resolve(setup.gameObject, setup.GetComponentsInChildren<SoftDeformPBSetup>(true),
                setup.GetComponentsInChildren<SoftDeformPBClothingSupport>(true))[setup];

        private static void Build(GameObject avatar)
        {
            using (new OverrideTemporaryDirectoryScope(null))
                Assert.That(AvatarProcessor.ProcessAvatar(avatar, AmbientPlatform.CurrentPlatform).Successful, Is.True);
        }

        [Test]
        public void MultipleOutfits_CombineTightestLimitsIncludingInactiveOutfits()
        {
            var setup = Avatar(); var avatar = setup.gameObject;
            try
            {
                var a = Support(setup, "A"); a.maximumRootTravel = 0.004f; a.maximumSwingAngle = 35; a.maximumSeparationIncrease = 0.015f;
                var b = Support(setup, "B"); b.gameObject.SetActive(false); b.maximumRootTravel = 0.008f; b.maximumSwingAngle = 18; b.maximumSeparationIncrease = 0.006f;
                var limits = Resolve(setup);
                Assert.That(limits.Travel, Is.EqualTo(0.004f)); Assert.That(limits.Angle, Is.EqualTo(18));
                Assert.That(limits.Separation, Is.EqualTo(0.006f)); Assert.That(limits.Sources.Count, Is.EqualTo(2));
                float proportion = setup.rootMotionMaxOffset / setup.gravitySupineRootSpread;
                limits.Apply(setup);
                Assert.That(setup.rootMotionMaxOffset + setup.gravitySupineRootSpread, Is.EqualTo(0.003f).Within(0.000001f));
                Assert.That(setup.rootMotionMaxOffset / setup.gravitySupineRootSpread, Is.EqualTo(proportion).Within(0.0001f));
                Assert.That(SoftDeformClothingLimits.AngularBudget(setup), Is.EqualTo(18).Within(0.0001f));
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [TestCase(0f, 0f, 0f)]
        [TestCase(0.004f, 12f, 0.006f)]
        public void FinalBuild_AppliesCombinedBudgetsPreservesInteractionAndStripsAuthoringComponents(float travel, float angle, float separation)
        {
            var setup = Avatar(); var avatar = setup.gameObject; var left = setup.leftBreast;
            var pb = left.GetComponent<VRCPhysBone>();
            try
            {
                var support = Support(setup, "Outfit"); support.maximumRootTravel = travel;
                support.maximumSwingAngle = angle; support.maximumSeparationIncrease = separation;
                Build(avatar);
                Assert.That(avatar.GetComponentsInChildren<SoftDeformPBClothingSupport>(true), Is.Empty);
                Assert.That(avatar.GetComponentsInChildren<SoftDeformPBSetup>(true), Is.Empty);
                Assert.That(pb.limitType, Is.EqualTo(VRCPhysBoneBase.LimitType.Polar));
                Assert.That(pb.limitRotation, Is.EqualTo(Vector3.zero));
                Assert.That(pb.maxAngleX + pb.maxAngleZ, Is.LessThanOrEqualTo(angle + 0.0001f));
                Assert.That(pb.allowGrabbing, Is.EqualTo(VRCPhysBoneBase.AdvancedBool.Other));
                Assert.That(pb.grabFilter.allowSelf, Is.False); Assert.That(pb.grabFilter.allowOthers, Is.True);
                Assert.That(pb.maxStretch, Is.EqualTo(0.15f)); Assert.That(pb.maxSquish, Is.EqualTo(0.35f));
                Assert.That(left.GetComponents<VRCConstraintBase>(), Is.Empty);
                if (travel == 0)
                {
                    Assert.That(avatar.GetComponentsInChildren<VRCPhysBone>().Count(p => p.name.StartsWith("SoftDeformPB Root Driver")), Is.Zero);
                    Assert.That(avatar.GetComponentsInChildren<Transform>().Count(t => t.name.StartsWith("SoftDeformPB Gather")), Is.EqualTo(2), "Shape compression still works when rotation and attachment motion are locked");
                }
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void DisabledSupport_PreservesExistingLimitCenterAndAddsNoRuntimeComponent()
        {
            var setup = Avatar(); var avatar = setup.gameObject; var pb = setup.leftBreast.GetComponent<VRCPhysBone>();
            var originalCenter = pb.limitRotation;
            try
            {
                var support = Support(setup, "Disabled"); support.enabled = false; support.maximumSwingAngle = float.NaN;
                Build(avatar);
                Assert.That(pb.limitType, Is.EqualTo(VRCPhysBoneBase.LimitType.None));
                Assert.That(pb.limitRotation, Is.EqualTo(originalCenter)); Assert.That(pb.maxAngleX, Is.EqualTo(45));
                Assert.That(avatar.GetComponentsInChildren<SoftDeformPBClothingSupport>(true), Is.Empty);
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [TestCase(-0.01f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(0.11f)]
        public void InvalidLimit_IsRejectedWithoutChangingSourceSettings(float value)
        {
            var setup = Avatar(); var avatar = setup.gameObject; var parent = setup.leftBreast.parent;
            try
            {
                Support(setup, "Invalid").maximumRootTravel = value;
                Assert.Throws<InvalidOperationException>(() => Resolve(setup));
                Assert.That(setup.rootMotionMaxOffset, Is.EqualTo(0.01f)); Assert.That(setup.leftBreast.parent, Is.SameAs(parent));
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TargetMustBeUnambiguousAndInsideTheAvatar(bool externalTarget)
        {
            var setup = Avatar(); var avatar = setup.gameObject; var other = new GameObject("Other");
            try
            {
                var support = Support(setup, "Outfit"); var second = other.AddComponent<SoftDeformPBSetup>();
                if (externalTarget) support.targetSetup = second;
                else other.transform.SetParent(avatar.transform, false);
                Assert.Throws<InvalidOperationException>(() => Resolve(setup));
                if (!externalTarget)
                {
                    support.targetSetup = setup;
                    Assert.That(Resolve(setup).Sources.Count, Is.EqualTo(1));
                }
            }
            finally { Object.DestroyImmediate(avatar); if (other != null) Object.DestroyImmediate(other); }
        }

        [Test]
        public void SplitParentLimit_RejectsAnUndefinedCommonMeasurementFrame()
        {
            var setup = Avatar(); var avatar = setup.gameObject;
            try
            {
                var parent = new GameObject("Other frame").transform; parent.SetParent(avatar.transform, false);
                setup.rightBreast.SetParent(parent, true); Support(setup, "Outfit");
                Assert.Throws<InvalidOperationException>(() => Resolve(setup));
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeConstraints_ComposeRootTravelAndPostureWithinSeparationBudget(bool directional)
        {
            var setup = Avatar(); var avatar = setup.gameObject;
            var physical = new[] { setup.leftBreast, setup.rightBreast }; var rest = physical.Select(t => t.localPosition).ToArray();
            try
            {
                setup.rootMotionMaxOffset = 0.04f; setup.gravitySupineRootSpread = 0.03f;
                if (directional) { setup.rootHorizontalTranslation = 0.4f; setup.rootVerticalRotation = 0.5f; }
                var support = Support(setup, "Outfit"); support.maximumRootTravel = 0.006f; support.maximumSeparationIncrease = 0.008f; support.maximumSwingAngle = 20;
                var limits = Resolve(setup);
                Build(avatar);
                var animator = avatar.GetComponent<Animator>();
                animator.runtimeAnimatorController = avatar.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
                var drivers = avatar.GetComponentsInChildren<VRCPhysBone>().Where(p => p.name.StartsWith("SoftDeformPB Root Driver")).ToArray();
                var rotations = drivers.Select(p => p.transform.localRotation).ToArray();
                foreach (float gravity in new[] { 0f, 0.5f, 1f })
                foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward, Vector3.one.normalized })
                {
                    foreach (var parameter in animator.parameters.Where(p => p.name.Contains("Gravity"))) animator.SetFloat(parameter.name, gravity);
                    animator.Update(0.02f);
                    for (int i = 0; i < drivers.Length; i++)
                        drivers[i].transform.localRotation = rotations[i] * Quaternion.AngleAxis(drivers[i].maxAngleX, axis * (drivers[i].name.Contains("Left") ? -1 : 1));
                    SoftDeformNativeConstraints.Evaluate(avatar);
                    var points = physical.Select(t => avatar.transform.InverseTransformPoint(t.position)).ToArray();
                    for (int side = 0; side < 2; side++)
                        Assert.That(Vector3.Distance(points[side], rest[side]), Is.LessThanOrEqualTo(limits.AttachmentBudget + 0.00001f));
                    Assert.That(Vector3.Distance(points[0], points[1]), Is.LessThanOrEqualTo(Vector3.Distance(rest[0], rest[1]) + limits.Separation + 0.00001f));
                }
            }
            finally { Object.DestroyImmediate(avatar); }
        }
    }
}
