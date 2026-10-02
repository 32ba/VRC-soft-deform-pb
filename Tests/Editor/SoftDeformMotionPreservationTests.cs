using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.platform;
using NUnit.Framework;
using SoftDeformPB.Editor;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace SoftDeformPB.Tests.Editor
{
    public sealed class SoftDeformMotionPreservationTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void Build_PreservesPerSideMotionWhileAddingShapeAndHonoringExplicitClothingLimits(bool clothing)
        {
            VerifyMotion(clothing, SoftDeformMotionForceOverrides.None);
        }

        [TestCase(SoftDeformMotionForceOverrides.Pull)]
        [TestCase(SoftDeformMotionForceOverrides.Spring)]
        [TestCase(SoftDeformMotionForceOverrides.Stiffness)]
        [TestCase(SoftDeformMotionForceOverrides.Immobile)]
        [TestCase(SoftDeformMotionForceOverrides.Gravity)]
        [TestCase(SoftDeformMotionForceOverrides.GravityFalloff)]
        [TestCase(SoftDeformMotionForceOverrides.Pull | SoftDeformMotionForceOverrides.Spring |
                  SoftDeformMotionForceOverrides.Stiffness | SoftDeformMotionForceOverrides.Immobile |
                  SoftDeformMotionForceOverrides.Gravity | SoftDeformMotionForceOverrides.GravityFalloff)]
        public void Build_SelectedForceOverridesKeepOtherForcesCurvesAndIntegration(SoftDeformMotionForceOverrides forces)
        {
            VerifyMotion(false, forces);
        }

        [Test]
        public void Build_SelectedForceOverridesStillHonorClothingBudget()
        {
            VerifyMotion(true, SoftDeformMotionForceOverrides.Pull | SoftDeformMotionForceOverrides.Spring);
        }

        private static void VerifyMotion(bool clothing, SoftDeformMotionForceOverrides forces)
        {
            var source = new GameObject("Authored motion source");
            GameObject built = null;
            try
            {
                source.AddComponent<Animator>();
                source.AddComponent<VRCAvatarDescriptor>();
                var setup = source.AddComponent<SoftDeformPBSetup>();
                setup.preserveExistingMotion = true;
                setup.motionForceOverrides = forces;
                setup.motionPull = .25f; setup.motionSpring = .36f; setup.motionStiffness = .64f;
                setup.motionImmobile = .49f; setup.motionGravity = .16f; setup.motionGravityFalloff = .81f;
                setup.responseSamples = 3;
                setup.rootMotionMaxOffset = setup.gravitySupineRootSpread = setup.secondaryMotionStrength = 0;
                setup.maxSquish = .12f; setup.maxStretch = .03f;
                setup.motionMaxPitch = 30; setup.motionMaxYaw = 20;
                var originals = new VRCPhysBone[2];
                for (int i = 0; i < 2; i++)
                {
                    var bone = new GameObject(i == 0 ? "Left" : "Right").transform;
                    bone.SetParent(source.transform, false);
                    bone.localPosition = new Vector3(i == 0 ? -.12f : .12f, 1, .1f);
                    var pb = bone.gameObject.AddComponent<VRCPhysBone>();
                    originals[i] = pb;
                    pb.endpointPosition = Vector3.forward * .1f; pb.radius = .01f;
                    pb.integrationType = i == 0 ? VRCPhysBoneBase.IntegrationType.Advanced : VRCPhysBoneBase.IntegrationType.Simplified;
                    pb.pull = .81f + i * .1f; pb.spring = .64f - i * .2f;
                    pb.immobile = .17f + i * .07f; pb.gravity = -.1f + i * .3f; pb.gravityFalloff = .73f;
                    pb.immobileType = i == 0 ? VRCPhysBoneBase.ImmobileType.World : VRCPhysBoneBase.ImmobileType.AllMotion;
                    pb.pullCurve = AnimationCurve.EaseInOut(0, .2f + i * .1f, 1, .9f);
                    pb.springCurve = AnimationCurve.Linear(0, .4f, 1, .8f - i * .1f);
                    pb.immobileCurve = AnimationCurve.Linear(0, .1f, 1, .7f);
                    pb.gravityCurve = AnimationCurve.Linear(0, .3f, 1, .6f);
                    pb.gravityFalloffCurve = AnimationCurve.EaseInOut(0, .2f, 1, 1);
                    pb.stiffness = .35f; pb.stiffnessCurve = AnimationCurve.Linear(0, .5f, 1, 1);
                    pb.limitType = VRCPhysBoneBase.LimitType.Polar;
                    pb.maxAngleX = 35 + i * 5; pb.maxAngleZ = 25 + i * 3;
                    pb.maxAngleXCurve = AnimationCurve.EaseInOut(0, .45f, 1, 1);
                    pb.maxAngleZCurve = AnimationCurve.Linear(0, .4f, 1, .9f);
                    pb.limitRotation = new Vector3(-8, 0, i == 0 ? 2 : -2);
                    if (i == 0) setup.leftBreast = bone; else setup.rightBreast = bone;
                }
                if (clothing)
                {
                    var outfit = new GameObject("Outfit"); outfit.transform.SetParent(source.transform, false);
                    outfit.AddComponent<SoftDeformPBClothingSupport>().maximumSwingAngle = 20;
                }
                string originalSetup = EditorJsonUtility.ToJson(setup);
                var originalSettings = originals.Select(p => EditorJsonUtility.ToJson(p)).ToArray();
                built = Object.Instantiate(source);
                using (new OverrideTemporaryDirectoryScope(null))
                    Assert.That(AvatarProcessor.ProcessAvatar(built, AmbientPlatform.CurrentPlatform).Successful, Is.True);
                for (int i = 0; i < 2; i++)
                {
                    var expected = originals[i];
                    var actual = built.GetComponentsInChildren<VRCPhysBone>(true).Single(p => p.name == expected.name);
                    Assert.That(actual.integrationType, Is.EqualTo(expected.integrationType));
                    Assert.That(actual.pull, Is.EqualTo(forces.HasFlag(SoftDeformMotionForceOverrides.Pull) ? setup.motionPull : expected.pull));
                    Assert.That(actual.spring, Is.EqualTo(forces.HasFlag(SoftDeformMotionForceOverrides.Spring) ? setup.motionSpring : expected.spring));
                    Assert.That(actual.immobile, Is.EqualTo(forces.HasFlag(SoftDeformMotionForceOverrides.Immobile) ? setup.motionImmobile : expected.immobile));
                    Assert.That(actual.gravity, Is.EqualTo(forces.HasFlag(SoftDeformMotionForceOverrides.Gravity) ? setup.motionGravity : expected.gravity));
                    Assert.That(actual.gravityFalloff, Is.EqualTo(forces.HasFlag(SoftDeformMotionForceOverrides.GravityFalloff) ? setup.motionGravityFalloff : expected.gravityFalloff));
                    Assert.That(actual.stiffness, Is.EqualTo(forces.HasFlag(SoftDeformMotionForceOverrides.Stiffness) ? setup.motionStiffness : expected.stiffness));
                    Assert.That(actual.immobileType, Is.EqualTo(expected.immobileType));
                    var actualCurves = new[] { actual.pullCurve, actual.springCurve, actual.immobileCurve, actual.gravityCurve, actual.gravityFalloffCurve, actual.stiffnessCurve };
                    var expectedCurves = new[] { expected.pullCurve, expected.springCurve, expected.immobileCurve, expected.gravityCurve, expected.gravityFalloffCurve, expected.stiffnessCurve };
                    for (int c = 0; c < actualCurves.Length; c++)
                        Assert.That(actualCurves[c].keys, Is.EqualTo(expectedCurves[c].keys));
                    if (clothing)
                    {
                        Assert.That(actual.maxAngleX + actual.maxAngleZ, Is.LessThanOrEqualTo(20.0001f));
                        Assert.That(actual.limitRotation, Is.EqualTo(Vector3.zero));
                        Assert.That(actual.maxAngleXCurve.Evaluate(0), Is.EqualTo(1));
                    }
                    else
                    {
                        Assert.That(actual.maxAngleX, Is.EqualTo(expected.maxAngleX));
                        Assert.That(actual.maxAngleZ, Is.EqualTo(expected.maxAngleZ));
                        Assert.That(actual.maxAngleXCurve.keys, Is.EqualTo(expected.maxAngleXCurve.keys));
                        Assert.That(actual.maxAngleZCurve.keys, Is.EqualTo(expected.maxAngleZCurve.keys));
                        Assert.That(actual.limitRotation, Is.EqualTo(expected.limitRotation));
                    }
                    Assert.That(actual.maxSquish, Is.EqualTo(.12f));
                    Assert.That(actual.maxStretch, Is.EqualTo(.03f));
                    Assert.That(actual.isAnimated, Is.True);
                    Assert.That(actual.parameter, Is.Not.Empty);
                }
                Assert.That(built.GetComponentsInChildren<Transform>().Count(t => t.name.StartsWith("SoftDeformPB Visual ")), Is.EqualTo(2));
                Assert.That(EditorJsonUtility.ToJson(setup), Is.EqualTo(originalSetup));
                Assert.That(originals.Select(p => EditorJsonUtility.ToJson(p)).ToArray(), Is.EqualTo(originalSettings));
            }
            finally
            {
                Object.DestroyImmediate(built);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void GravityTrialDefaults_ExplicitlyRestoresRetuning()
        {
            var go = new GameObject("Gravity trial selection");
            try
            {
                var setup = go.AddComponent<SoftDeformPBSetup>();
                Assert.That(setup.preserveExistingMotion, Is.False, "Existing serialized setups retain their previous behavior");
                setup.preserveExistingMotion = true;
                setup.motionForceOverrides = SoftDeformMotionForceOverrides.Pull | SoftDeformMotionForceOverrides.Spring;
                setup.UseGravityTrialDefaults();
                Assert.That(setup.preserveExistingMotion, Is.False);
                Assert.That(setup.motionForceOverrides, Is.EqualTo(SoftDeformMotionForceOverrides.None));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ExistingSerializedSetup_HasNoImplicitForceOverrides()
        {
            var go = new GameObject("Existing motion settings");
            try
            {
                var setup = go.AddComponent<SoftDeformPBSetup>();
                EditorJsonUtility.FromJsonOverwrite("{\"MonoBehaviour\":{\"preserveExistingMotion\":true,\"motionPull\":0.49}}", setup);
                Assert.That(setup.preserveExistingMotion, Is.True);
                Assert.That(setup.motionPull, Is.EqualTo(.49f));
                Assert.That(setup.motionForceOverrides, Is.EqualTo(SoftDeformMotionForceOverrides.None));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void UnsupportedForceOverride_IsRejectedBeforeGeneration()
        {
            var go = new GameObject("Invalid force override");
            try
            {
                var setup = go.AddComponent<SoftDeformPBSetup>();
                setup.motionForceOverrides = (SoftDeformMotionForceOverrides)128;
                string before = EditorJsonUtility.ToJson(setup);
                Assert.That(() => SoftDeformPBGeneratePass.ValidateSetup(go.transform, setup,
                    new System.Collections.Generic.HashSet<Transform>()),
                    Throws.InvalidOperationException.With.Message.Contains("motionForceOverrides"));
                Assert.That(EditorJsonUtility.ToJson(setup), Is.EqualTo(before));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
