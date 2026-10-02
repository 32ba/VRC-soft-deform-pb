using System;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.platform;
using NUnit.Framework;
using SoftDeformPB.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using Object = UnityEngine.Object;

namespace SoftDeformPB.Tests.Editor
{
    public sealed class SoftDeformPostureTests
    {
        private static SoftDeformPBSetup Avatar(int axis = 2, float sign = 1, bool split = false)
        {
            var avatar = new GameObject("Posture test"); avatar.AddComponent<Animator>(); avatar.AddComponent<VRCAvatarDescriptor>();
            var setup = avatar.AddComponent<SoftDeformPBSetup>(); setup.responseSamples = 3;
            setup.rootMotionMaxOffset = 0; setup.secondaryMotionStrength = 0;
            setup.gravitySideElongation = .2f; setup.gravityInvertedElongation = .15f;
            Transform Side(string name, float x)
            {
                var frame = avatar.transform;
                if (split)
                {
                    frame = new GameObject(name + " Frame").transform; frame.SetParent(avatar.transform, false);
                    frame.localRotation = Quaternion.Euler(15, x * 100, 10);
                }
                var bone = new GameObject(name).transform; bone.SetParent(frame, false);
                bone.position = new Vector3(x, 1, .1f);
                bone.rotation = Quaternion.AngleAxis(x > 0 ? 180 : 0, Vector3.forward) *
                    Quaternion.FromToRotation(SoftDeformShapeMath.AxisVector(axis) * sign, Vector3.forward);
                var pb = bone.gameObject.AddComponent<VRCPhysBone>(); pb.radius = .01f;
                pb.endpointPosition = SoftDeformShapeMath.AxisVector(axis) * (.1f * sign);
                return bone;
            }
            setup.leftBreast = Side("Left", -.12f); setup.rightBreast = Side("Right", .12f);
            return setup;
        }

        private static Animator Build(GameObject avatar)
        {
            using (new OverrideTemporaryDirectoryScope(null)) Assert.That(AvatarProcessor.ProcessAvatar(avatar, AmbientPlatform.CurrentPlatform).Successful, Is.True);
            var animator = avatar.GetComponent<Animator>();
            animator.runtimeAnimatorController = avatar.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers
                .Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
            return animator;
        }

        // Deterministic Animator test only. Native Contacts are tested separately in the live avatar probe.
        private static void Input(Animator animator, Vector3 gravity)
        {
            foreach (var receiver in animator.GetComponentsInChildren<VRCContactReceiver>(true).Where(r => r.parameter.Contains("_Gravity")))
            {
                Vector3 positive = animator.transform.InverseTransformDirection(receiver.transform.rotation * receiver.rotation * Vector3.forward);
                float value = .5f + (Vector3.Dot(gravity, positive) * .1f + .002f) / .204f;
                animator.SetFloat(receiver.parameter, value);
            }
            animator.Update(.02f);
        }

        private static Transform[] Visuals(GameObject avatar) => new[] { "Left", "Right" }.Select(s =>
            avatar.GetComponentsInChildren<Transform>(true).Single(t => t.name == "SoftDeformPB Visual " + s)).ToArray();

        [TestCase(0, 1f)] [TestCase(0, -1f)] [TestCase(1, 1f)]
        [TestCase(1, -1f)] [TestCase(2, 1f)] [TestCase(2, -1f)]
        public void FinalAnimator_SeparatesSixCardinalPosturesOnMirroredAxes(int axis, float sign)
        {
            var setup = Avatar(axis, sign); var avatar = setup.gameObject;
            var maps = new[] { setup.leftBreast, setup.rightBreast }.Select(t =>
                SoftDeformShapeMath.InferAxes(t, SoftDeformShapeMath.AxisVector(axis) * sign)).ToArray();
            try
            {
                var animator = Build(avatar); var visuals = Visuals(avatar);
                foreach (var pose in new (Vector3 g, float h, float v, float l)[] { (Vector3.down, 1f, 1f, 1f),
                    (Vector3.up, 1 / 1.15f, 1.15f, 1f), (Vector3.left, 1.2f, 1 / 1.2f, 1f),
                    (Vector3.right, 1.2f, 1 / 1.2f, 1f), (Vector3.back, 1.2f, 1 / (1.2f * .9f), .9f),
                    (Vector3.forward, 1f, 1f, 1f) })
                {
                    Input(animator, pose.g);
                    for (int side = 0; side < 2; side++)
                    {
                        var scale = visuals[side].localScale; var map = maps[side];
                        Assert.That(scale[map.Horizontal], Is.EqualTo(pose.h).Within(.0001f), $"{pose.g}, side {side}");
                        Assert.That(scale[map.Vertical], Is.EqualTo(pose.v).Within(.0001f));
                        Assert.That(scale[map.Longitudinal], Is.EqualTo(pose.l).Within(.0001f));
                        Assert.That(scale.x * scale.y * scale.z, Is.EqualTo(1).Within(.0001f));
                    }
                }
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [TestCase(false)] [TestCase(true)]
        public void UpwardSensor_PreservesAuthoredUpAcrossSharedAndSplitParents(bool split)
        {
            var setup = Avatar(2, 1, split); var avatar = setup.gameObject;
            try
            {
                var animator = Build(avatar);
                var receivers = avatar.GetComponentsInChildren<VRCContactReceiver>(true);
                var upward = receivers.Where(r => r.parameter.EndsWith("_GravityUpward")).ToArray();
                Assert.That(upward.Length, Is.EqualTo(split ? 2 : 1));
                Assert.That(receivers.Length, Is.EqualTo(split ? 6 : 3));
                foreach (var r in upward)
                {
                    Assert.That(Vector3.Dot(r.transform.rotation * r.rotation * Vector3.forward, Vector3.up), Is.GreaterThan(.99999f));
                    Assert.That(r.allowSelf && !r.allowOthers && !r.localOnly && r.useFaceProximity, Is.True);
                }
                Input(animator, Vector3.down);
                var upright = Visuals(avatar).Select(t => t.localScale).ToArray();
                Input(animator, Vector3.up);
                Assert.That(Vector3.Distance(Visuals(avatar)[0].localScale, upright[0]), Is.GreaterThan(.15f));
                Input(animator, Vector3.down);
                Assert.That(Vector3.Distance(Visuals(avatar)[0].localScale, upright[0]), Is.LessThan(.00001f));
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [TestCase(false)] [TestCase(true)]
        public void FinalAnimator_FullRotationsRemainContinuousWithCombinedCompression(bool touching)
        {
            var setup = Avatar(); var avatar = setup.gameObject;
            setup.minimumCompressionRatio = .85f; setup.gravitySideElongation = .3f; setup.gravityInvertedElongation = .3f;
            setup.lateralCompressionDepth = .3f; setup.gatheringAngleCorrection = 8;
            try
            {
                var animator = Build(avatar); var visuals = Visuals(avatar);
                foreach (var p in animator.parameters)
                    if (p.name.EndsWith("_Squish") || p.name.EndsWith("_Inward") || p.name.EndsWith("_OuterTouch"))
                        animator.SetFloat(p.name, touching ? 1 : 0);
                foreach (var axis in new[] { Vector3.right, Vector3.forward, new Vector3(1, 0, 1).normalized })
                {
                    Vector3[] start = null, previous = null;
                    for (int degrees = 0; degrees <= 360; degrees++)
                    {
                        Input(animator, Quaternion.AngleAxis(degrees, axis) * Vector3.down);
                        var current = visuals.Select(t => t.localScale).ToArray();
                        if (start == null) start = current;
                        for (int side = 0; side < 2; side++)
                        {
                            for (int a = 0; a < 3; a++) Assert.That(current[side][a], Is.InRange(.85f - .00001f, 2f));
                            if (previous != null) Assert.That(Vector3.Distance(current[side], previous[side]), Is.LessThan(.025f), $"{axis}, {degrees}");
                            if (degrees == 360) Assert.That(Vector3.Distance(current[side], start[side]), Is.LessThan(.0001f));
                        }
                        previous = current;
                    }
                }
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void NeutralNewSettings_KeepLegacyShapeSamplesAndNoExtraInvertedClips()
        {
            var setup = Avatar(); var avatar = setup.gameObject;
            setup.gravitySideElongation = 0; setup.gravityInvertedElongation = 0;
            try
            {
                var animator = Build(avatar); var fx = (AnimatorController)animator.runtimeAnimatorController;
                Assert.That(fx.animationClips.Any(c => c.name.Contains("Inverted")), Is.False);
                foreach (var g in new[] { Vector3.down, Vector3.up, Vector3.left, Vector3.right, Vector3.forward })
                {
                    Input(animator, g);
                    foreach (var visual in Visuals(avatar)) Assert.That(Vector3.Distance(visual.localScale, Vector3.one), Is.LessThan(.0001f));
                }
                Input(animator, Vector3.back);
                Assert.That(Vector3.Distance(Visuals(avatar)[0].localScale, new Vector3(1.2f, 1 / 1.08f, .9f)), Is.LessThan(.0001f));
            }
            finally { Object.DestroyImmediate(avatar); }
        }
    }
}
