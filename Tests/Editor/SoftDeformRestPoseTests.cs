using System.Linq;
using NUnit.Framework;
using SoftDeformPB.Editor;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace SoftDeformPB.Tests.Editor
{
    public sealed class SoftDeformRestPoseTests
    {
        [TestCase(false, .65f)]
        [TestCase(true, .65f)]
        [TestCase(false, 1f)]
        [TestCase(true, 1f)]
        public void Build_SupportsMainAndRootRestWithoutChangingOtherForcesOrAuthoredPose(bool preserve, float support)
        {
            var source = new GameObject("Rest pose source");
            GameObject template = null;
            try
            {
                source.AddComponent<Animator>();
                source.AddComponent<VRCAvatarDescriptor>();
                var setup = source.AddComponent<SoftDeformPBSetup>();
                setup.preserveExistingMotion = preserve;
                setup.motionGravity = .27f;
                setup.motionGravityFalloff = .35f;
                setup.responseSamples = 3;
                setup.rootMotionMaxOffset = .01f;
                setup.rootMotionGravity = .62f;
                setup.rootMotionGravityFalloff = .2f;
                for (int i = 0; i < 2; i++)
                {
                    var root = new GameObject(i == 0 ? "Left" : "Right").transform;
                    root.SetParent(source.transform, false);
                    root.localPosition = new Vector3(i == 0 ? -.1f : .1f, 1, .1f);
                    var end = new GameObject("End").transform;
                    end.SetParent(root, false);
                    end.localPosition = Vector3.forward * .1f;
                    var pb = root.gameObject.AddComponent<VRCPhysBone>();
                    pb.gravity = .19f + i * .12f;
                    pb.gravityFalloff = .4f + i * .1f;
                    pb.gravityFalloffCurve = WeightedCurve();
                    if (i == 0) setup.leftBreast = root; else setup.rightBreast = root;
                }
                var baseline = SoftDeformPreviewSettings.Capture(setup);
                setup.restPoseSupport = support;
                var tuned = SoftDeformPreviewSettings.Capture(setup);
                baseline.Apply(setup);
                string before = EditorJsonUtility.ToJson(setup);
                string[] boneBefore = source.GetComponentsInChildren<VRCPhysBone>()
                    .Select(b => EditorJsonUtility.ToJson(b)).ToArray();
                template = SoftDeformLivePreview.CreateTemplate(source);
                using (var a = SoftDeformLivePreview.Build(template, baseline))
                using (var b = SoftDeformLivePreview.Build(template, tuned))
                {
                    var expected = a.Avatar.GetComponentsInChildren<VRCPhysBone>(true)
                        .Where(p => p.parameter.StartsWith("SoftDeformPB_") || p.name.StartsWith("SoftDeformPB Root Driver "))
                        .ToArray();
                    Assert.That(expected.Length, Is.EqualTo(6));
                    foreach (var original in expected)
                    {
                        var actual = b.Avatar.GetComponentsInChildren<VRCPhysBone>(true).Single(p => p.name == original.name);
                        foreach (float time in new[] { 0f, .13f, .45f, .89f, 1f })
                            Assert.That(Effective(actual, time), Is.EqualTo(Mathf.Lerp(Effective(original, time), 1, support)).Within(.0001f));
                        Assert.That(actual.pull, Is.EqualTo(original.pull));
                        Assert.That(actual.spring, Is.EqualTo(original.spring));
                        Assert.That(actual.gravity, Is.EqualTo(original.gravity));
                        Assert.That(actual.immobile, Is.EqualTo(original.immobile));
                        Assert.That(actual.integrationType, Is.EqualTo(original.integrationType));
                        Assert.That(actual.maxAngleX, Is.EqualTo(original.maxAngleX));
                        Assert.That(actual.maxAngleZ, Is.EqualTo(original.maxAngleZ));
                        Assert.That(Vector3.Distance(actual.transform.position, original.transform.position), Is.LessThan(.00001f));
                    }
                }
                Assert.That(EditorJsonUtility.ToJson(setup), Is.EqualTo(before));
                Assert.That(source.GetComponentsInChildren<VRCPhysBone>().Select(p => EditorJsonUtility.ToJson(p)).ToArray(), Is.EqualTo(boneBefore));
            }
            finally
            {
                if (template != null) Object.DestroyImmediate(template);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void DisabledSupport_PreservesAllSettingsAndCurveIdentity()
        {
            var owner = new GameObject("Disabled rest support");
            try
            {
                var pb = owner.AddComponent<VRCPhysBone>();
                pb.gravity = .4f;
                pb.gravityFalloff = .35f;
                pb.gravityFalloffCurve = WeightedCurve();
                var curve = pb.gravityFalloffCurve;
                string before = EditorJsonUtility.ToJson(pb);
                SoftDeformRestPose.Apply(pb, 0);
                Assert.That(EditorJsonUtility.ToJson(pb), Is.EqualTo(before));
                Assert.That(pb.gravityFalloffCurve, Is.SameAs(curve));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [TestCase(0f, .4f)]
        [TestCase(.35f, 1f)]
        public void WeightedCurves_SupportTheEntireChainIncludingZeroBaseFalloff(float falloff, float support)
        {
            var owner = new GameObject("Weighted rest support");
            try
            {
                var pb = owner.AddComponent<VRCPhysBone>();
                pb.gravity = -.2f;
                pb.gravityFalloff = falloff;
                var original = WeightedCurve();
                pb.gravityFalloffCurve = original;
                SoftDeformRestPose.Apply(pb, support);
                for (int i = 0; i <= 100; i++)
                {
                    float time = i / 100f;
                    Assert.That(Effective(pb, time), Is.EqualTo(Mathf.Lerp(falloff * original.Evaluate(time), 1, support)).Within(.0001f));
                }
                Assert.That(pb.gravity, Is.EqualTo(-.2f));
                Assert.That(original.keys[0].value, Is.EqualTo(.2f));
                Assert.That(pb.gravityFalloffCurve.postWrapMode, Is.EqualTo(original.postWrapMode));
                Assert.That(pb.gravityFalloffCurve.keys[0].outWeight, Is.EqualTo(original.keys[0].outWeight));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void ZeroGravity_RemainsUnchangedWhenSupportIsEnabled()
        {
            var owner = new GameObject("No gravity rest support");
            try
            {
                var pb = owner.AddComponent<VRCPhysBone>();
                pb.gravity = 0;
                pb.gravityFalloff = .25f;
                pb.gravityFalloffCurve = WeightedCurve();
                string before = EditorJsonUtility.ToJson(pb);
                SoftDeformRestPose.Apply(pb, 1);
                Assert.That(EditorJsonUtility.ToJson(pb), Is.EqualTo(before));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        private static float Effective(VRCPhysBone bone, float time) => bone.gravityFalloff *
            (bone.gravityFalloffCurve == null || bone.gravityFalloffCurve.length == 0 ? 1 : bone.gravityFalloffCurve.Evaluate(time));

        private static AnimationCurve WeightedCurve()
        {
            var a = new Keyframe(0, .2f, 0, .3f, .2f, .35f) { weightedMode = WeightedMode.Both };
            var b = new Keyframe(1, .9f, .5f, 0, .3f, .2f) { weightedMode = WeightedMode.Both };
            return new AnimationCurve(a, b) { postWrapMode = WrapMode.PingPong };
        }
    }
}
