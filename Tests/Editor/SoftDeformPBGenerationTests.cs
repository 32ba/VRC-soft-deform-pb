using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.platform;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace SoftDeformPB.Tests.Editor
{
    public sealed class SoftDeformPBGenerationTests
    {
        [Test]
        public void ProcessAvatar_RetunesExistingPhysBonesAndAddsShapeResponse()
        {
            var avatar = new GameObject("SoftDeformPB Test Avatar");
            AnimatorController authoredController = null;
            AnimationClip authoredClip = null;
            try
            {
                avatar.AddComponent<Animator>();
                var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();

                var chest = new GameObject("Chest").transform;
                chest.SetParent(avatar.transform, false);
                var left = CreateBreastBone(chest, "Breast_L", new Vector3(-0.1f, 0.1f, 0.1f));
                var right = CreateBreastBone(chest, "Breast_R", new Vector3(0.1f, 0.1f, 0.1f));
                right.localRotation = Quaternion.Euler(0.0f, 0.0f, 180.0f);
                authoredClip = new AnimationClip { name = "Authored Breast Motion" };
                AnimationUtility.SetEditorCurve(
                    authoredClip,
                    EditorCurveBinding.FloatCurve("Chest/Breast_L", typeof(Transform), "m_LocalPosition.x"),
                    AnimationCurve.Constant(0.0f, 1.0f / 60.0f, left.localPosition.x));
                authoredController = new AnimatorController { name = "Authored FX Controller" };
                var authoredStateMachine = new AnimatorStateMachine { name = "Authored State Machine" };
                var authoredState = authoredStateMachine.AddState("Authored State");
                authoredState.motion = authoredClip;
                authoredController.AddLayer(new AnimatorControllerLayer
                {
                    name = "Authored Layer",
                    defaultWeight = 1.0f,
                    stateMachine = authoredStateMachine
                });
                var baseLayers = descriptor.baseAnimationLayers ?? new[]
                {
                    new VRCAvatarDescriptor.CustomAnimLayer
                    {
                        type = VRCAvatarDescriptor.AnimLayerType.FX
                    }
                };
                int fxLayerIndex = System.Array.FindIndex(
                    baseLayers,
                    layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX);
                Assert.That(fxLayerIndex, Is.GreaterThanOrEqualTo(0));
                var authoredFxLayer = baseLayers[fxLayerIndex];
                authoredFxLayer.isDefault = false;
                authoredFxLayer.animatorController = authoredController;
                baseLayers[fxLayerIndex] = authoredFxLayer;
                descriptor.baseAnimationLayers = baseLayers;
                descriptor.specialAnimationLayers =
                    descriptor.specialAnimationLayers ?? System.Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                var leftPhysBone = left.gameObject.AddComponent<VRCPhysBone>();
                leftPhysBone.pull = 0.83f;
                leftPhysBone.spring = 0.91f;
                var rightPhysBone = right.gameObject.AddComponent<VRCPhysBone>();
                rightPhysBone.pull = 0.77f;
                rightPhysBone.spring = 0.88f;
                var secondaryHost = new GameObject("Secondary PhysBone Host");
                secondaryHost.transform.SetParent(chest, false);
                var secondaryLeftPhysBone = secondaryHost.AddComponent<VRCPhysBone>();
                secondaryLeftPhysBone.rootTransform = left;
                secondaryLeftPhysBone.parameter = "Secondary_L";
                secondaryLeftPhysBone.pull = 0.21f;

                var setup = avatar.AddComponent<SoftDeformPBSetup>();
                setup.leftBreast = left;
                setup.rightBreast = right;
                setup.responseSamples = 3;
                setup.parameterPrefix = "SoftDeformPB";
                setup.motionGravityFalloff = 0.37f;
                setup.rootMotionGravityFalloff = 0.61f;
                float expectedMaxSquish = setup.maxSquish;
                float expectedMaxStretch = setup.maxStretch;
                float expectedStretchMotion = setup.stretchMotion;
                float expectedPull = setup.motionPull;
                float expectedSpring = setup.motionSpring;
                float expectedImmobile = setup.motionImmobile;
                float expectedGravity = setup.motionGravity;
                float expectedGravityFalloff = setup.motionGravityFalloff;
                float expectedSupineRootSpread = setup.gravitySupineRootSpread;
                float expectedMaxPitch = setup.motionMaxPitch;
                float expectedMaxYaw = setup.motionMaxYaw;
                float expectedRootMaxOffset = setup.rootMotionMaxOffset;
                float expectedRootMaxAngle = setup.rootMotionMaxAngle;
                float expectedRootRotationShare = setup.rootMotionRotationShare;
                float expectedRootPull = setup.rootMotionPull;
                float expectedRootSpring = setup.rootMotionSpring;
                float expectedRootGravity = setup.rootMotionGravity;
                float expectedRootImmobile = setup.rootMotionImmobile;
                float expectedRootFalloff = setup.rootMotionGravityFalloff;
                float expectedSupineSpread = setup.gravitySupineSpread;
                Vector3 leftRestLocalPosition = left.localPosition;
                Quaternion leftRestLocalRotation = left.localRotation;
                Vector3 rightRestLocalPosition = right.localPosition;
                Quaternion rightRestLocalRotation = right.localRotation;
                Vector3 leftRestWorldPosition = left.position;
                Quaternion leftRestWorldRotation = left.rotation;
                Vector3 rightRestWorldPosition = right.position;
                Quaternion rightRestWorldRotation = right.rotation;

                using (new OverrideTemporaryDirectoryScope(null))
                {
                    Assert.That(AvatarProcessor.ProcessAvatar(avatar, AmbientPlatform.CurrentPlatform).Successful, Is.True);
                }

                Assert.That(avatar.GetComponent<SoftDeformPBSetup>(), Is.Null);

                var physBones = avatar.GetComponentsInChildren<VRCPhysBone>(true);
                Assert.That(physBones, Has.Length.EqualTo(9));
                Assert.That(
                    physBones.Select(physBone => physBone.parameter),
                    Is.EquivalentTo(new[] { "SoftDeformPB_L", "SoftDeformPB_R", "Secondary_L", "", "", "", "", "", "" }));
                Assert.That(leftPhysBone.integrationType, Is.EqualTo(VRC.Dynamics.VRCPhysBoneBase.IntegrationType.Simplified));
                Assert.That(rightPhysBone.integrationType, Is.EqualTo(VRC.Dynamics.VRCPhysBoneBase.IntegrationType.Simplified));
                Assert.That(leftPhysBone.pull, Is.EqualTo(expectedPull));
                Assert.That(leftPhysBone.spring, Is.EqualTo(expectedSpring));
                Assert.That(leftPhysBone.immobile, Is.EqualTo(expectedImmobile));
                Assert.That(rightPhysBone.pull, Is.EqualTo(expectedPull));
                Assert.That(rightPhysBone.spring, Is.EqualTo(expectedSpring));
                Assert.That(rightPhysBone.immobile, Is.EqualTo(expectedImmobile));
                Assert.That(leftPhysBone.gravity, Is.EqualTo(expectedGravity));
                Assert.That(rightPhysBone.gravity, Is.EqualTo(expectedGravity));
                Assert.That(leftPhysBone.gravityFalloff, Is.EqualTo(expectedGravityFalloff));
                Assert.That(rightPhysBone.gravityFalloff, Is.EqualTo(expectedGravityFalloff));
                foreach (VRCPhysBone bone in new[] { leftPhysBone, rightPhysBone })
                foreach (float t in new[] { 0.0f, 0.5f, 1.0f })
                {
                    Assert.That(bone.CalcPull(t), Is.EqualTo(expectedPull).Within(0.000001f));
                    Assert.That(bone.CalcSpring(t), Is.EqualTo(expectedSpring).Within(0.000001f));
                    Assert.That(bone.CalcImmobile(t), Is.EqualTo(expectedImmobile).Within(0.000001f));
                    Assert.That(bone.CalcGravity(t), Is.EqualTo(expectedGravity * Mathf.Lerp(0.45f, 1.0f, t)).Within(0.000001f));
                    Assert.That(bone.CalcGravityFalloff(t), Is.EqualTo(expectedGravityFalloff).Within(0.000001f));
                }
                Assert.That(leftPhysBone.maxAngleX, Is.EqualTo(expectedMaxPitch));
                Assert.That(leftPhysBone.maxAngleZ, Is.EqualTo(expectedMaxYaw));
                Assert.That(leftPhysBone.maxAngleXCurve.Evaluate(0.0f), Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(leftPhysBone.maxAngleZCurve.Evaluate(0.0f), Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(leftPhysBone.maxSquish, Is.EqualTo(expectedMaxSquish));
                Assert.That(leftPhysBone.maxStretch, Is.EqualTo(expectedMaxStretch));
                Assert.That(leftPhysBone.stretchMotion, Is.EqualTo(expectedStretchMotion));
                Assert.That(leftPhysBone.isAnimated, Is.True);
                Assert.That(rightPhysBone.isAnimated, Is.True);
                Assert.That(secondaryLeftPhysBone.pull, Is.EqualTo(0.21f));
                Assert.That(secondaryLeftPhysBone.maxSquish, Is.Zero);

                Assert.That(left.parent.name, Is.EqualTo("SoftDeformPB Motion Restore Left"));
                Assert.That(right.parent.name, Is.EqualTo("SoftDeformPB Motion Restore Right"));
                Assert.That(left.parent.parent.name, Is.EqualTo("SoftDeformPB Motion Rotation Left"));
                Assert.That(right.parent.parent.name, Is.EqualTo("SoftDeformPB Motion Rotation Right"));
                Assert.That(left.localPosition, Is.EqualTo(leftRestLocalPosition));
                Assert.That(right.localPosition, Is.EqualTo(rightRestLocalPosition));
                Assert.That(Quaternion.Angle(left.localRotation, leftRestLocalRotation), Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(right.localRotation, rightRestLocalRotation), Is.LessThan(0.001f));
                Assert.That(Vector3.Distance(left.position, leftRestWorldPosition), Is.LessThan(0.0001f));
                Assert.That(Vector3.Distance(right.position, rightRestWorldPosition), Is.LessThan(0.0001f));
                Assert.That(Quaternion.Angle(left.rotation, leftRestWorldRotation), Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(right.rotation, rightRestWorldRotation), Is.LessThan(0.001f));

                var rootDrivers = physBones
                    .Where(physBone => physBone.name.StartsWith("SoftDeformPB Root Driver "))
                    .ToArray();
                Assert.That(rootDrivers, Has.Length.EqualTo(4));
                Assert.That(rootDrivers.Count(driver => driver.name.Contains("Outward")), Is.EqualTo(2));
                Assert.That(rootDrivers.Count(driver => driver.name.Contains("Horizontal")), Is.EqualTo(2));
                Assert.That(rootDrivers.All(driver => driver.rootTransform == driver.transform), Is.True);
                Assert.That(rootDrivers.All(driver => driver.transform.childCount == 1), Is.True);
                Assert.That(rootDrivers.All(driver => driver.limitType == VRCPhysBoneBase.LimitType.Angle), Is.True);
                Assert.That(rootDrivers.All(driver => driver.maxAngleX == expectedRootMaxAngle), Is.True);
                Assert.That(rootDrivers.All(driver => driver.maxAngleZ == expectedRootMaxAngle), Is.True);
                Assert.That(rootDrivers.All(driver => driver.pull == expectedRootPull), Is.True);
                Assert.That(rootDrivers.All(driver => driver.spring == expectedRootSpring), Is.True);
                Assert.That(rootDrivers.All(driver => driver.gravity == expectedRootGravity), Is.True);
                foreach (VRCPhysBone driver in rootDrivers)
                foreach (float t in new[] { 0.0f, 0.5f, 1.0f })
                {
                    Assert.That(driver.CalcPull(t), Is.EqualTo(expectedRootPull).Within(0.000001f));
                    Assert.That(driver.CalcSpring(t), Is.EqualTo(expectedRootSpring).Within(0.000001f));
                    Assert.That(driver.CalcImmobile(t), Is.EqualTo(expectedRootImmobile).Within(0.000001f));
                    Assert.That(driver.CalcGravity(t), Is.EqualTo(expectedRootGravity).Within(0.000001f));
                    Assert.That(driver.CalcGravityFalloff(t), Is.EqualTo(expectedRootFalloff).Within(0.000001f));
                }
                float expectedLeverLength = expectedRootMaxOffset /
                    (2.0f * Mathf.Sin(expectedRootMaxAngle * Mathf.Deg2Rad * 0.5f));
                Assert.That(
                    rootDrivers.All(driver =>
                        Mathf.Abs(driver.transform.GetChild(0).localPosition.magnitude - expectedLeverLength) < 0.0001f),
                    Is.True);

                var fxController = descriptor.baseAnimationLayers
                    .Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX)
                    .animatorController as AnimatorController;
                Assert.That(fxController, Is.Not.Null);
                Assert.That(
                    fxController.parameters.Select(parameter => parameter.name),
                    Does.Contain("SoftDeformPB_L_Squish"));
                Assert.That(
                    fxController.parameters.Select(parameter => parameter.name),
                    Does.Contain("SoftDeformPB_L_Stretch"));
                Assert.That(
                    fxController.parameters.Select(parameter => parameter.name),
                    Does.Contain("SoftDeformPB_R_Squish"));
                Assert.That(
                    fxController.parameters.Select(parameter => parameter.name),
                    Does.Contain("SoftDeformPB_R_Stretch"));
                Assert.That(
                    fxController.parameters.Select(parameter => parameter.name),
                    Does.Contain("SoftDeformPB_GravityHorizontal"));
                Assert.That(
                    fxController.parameters.Select(parameter => parameter.name),
                    Does.Contain("SoftDeformPB_GravityLongitudinal"));

                var animatedPaths = fxController.animationClips
                    .SelectMany(AnimationUtility.GetCurveBindings)
                    .Select(binding => binding.path)
                    .ToArray();
                const string LeftPhysicalPath = "Chest/SoftDeformPB Motion Root Left/SoftDeformPB Motion Pivot Left/SoftDeformPB Motion Rotation Left/SoftDeformPB Motion Restore Left/Breast_L";
                const string LeftAnimatedPath = LeftPhysicalPath + "/SoftDeformPB Visual Left";
                const string RightAnimatedPath = "Chest/SoftDeformPB Motion Root Right/SoftDeformPB Motion Pivot Right/SoftDeformPB Motion Rotation Right/SoftDeformPB Motion Restore Right/Breast_R/SoftDeformPB Visual Right";
                const string LeftPivotPath = "Chest/SoftDeformPB Motion Root Left/SoftDeformPB Motion Pivot Left";
                const string RightPivotPath = "Chest/SoftDeformPB Motion Root Right/SoftDeformPB Motion Pivot Right";
                Assert.That(animatedPaths, Does.Contain(LeftAnimatedPath));
                Assert.That(animatedPaths, Does.Contain(RightAnimatedPath));
                Assert.That(animatedPaths, Does.Contain(LeftPivotPath));
                Assert.That(animatedPaths, Does.Contain(RightPivotPath));
                var remappedAuthoredClip = fxController.animationClips
                    .Single(clip => clip.name == "Authored Breast Motion");
                var remappedAuthoredBinding = AnimationUtility.GetCurveBindings(remappedAuthoredClip).Single();
                Assert.That(remappedAuthoredBinding.path, Is.EqualTo(LeftPhysicalPath));
                Assert.That(
                    AnimationUtility.GetEditorCurve(remappedAuthoredClip, remappedAuthoredBinding).Evaluate(0.0f),
                    Is.EqualTo(leftRestLocalPosition.x).Within(0.0001f));

                var animatedProperties = fxController.animationClips
                    .Where(clip => clip.name.StartsWith("SoftDeformPB_"))
                    .SelectMany(AnimationUtility.GetCurveBindings)
                    .Where(binding => binding.path == LeftAnimatedPath)
                    .Select(binding => binding.propertyName)
                    .Distinct()
                    .ToArray();
                Assert.That(
                    animatedProperties.Where(property => property.StartsWith("m_LocalScale.")),
                    Is.EquivalentTo(new[] { "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" }));
                Assert.That(fxController.animationClips.Where(clip => clip.name.StartsWith("SoftDeformPB_"))
                    .SelectMany(AnimationUtility.GetCurveBindings)
                    .Where(binding => binding.path == LeftPhysicalPath), Is.Empty);
                Assert.That(leftPhysBone.ignoreTransforms, Does.Contain(left.Find("SoftDeformPB Visual Left")));
                Assert.That(secondaryLeftPhysBone.ignoreTransforms, Does.Contain(left.Find("SoftDeformPB Visual Left")));
                Assert.That(SampleClipProperty(fxController, "SoftDeformPB_Left_LongitudinalNegative_Shape_0_0",
                    LeftAnimatedPath, "m_LocalScale.z"), Is.EqualTo(0.9f).Within(0.0001f));
                Assert.That(
                    animatedProperties.Where(property => property.StartsWith("m_LocalPosition.")),
                    Is.Empty);
                Assert.That(
                    animatedProperties.Where(property => property.StartsWith("m_LocalRotation.")),
                    Is.Empty);
                Assert.That(
                    SampleClipProperty(
                        fxController,
                        "SoftDeformPB_Left_Upright_Shape_0_0",
                        LeftAnimatedPath,
                        "m_LocalScale.x"),
                    Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(
                    SampleClipProperty(
                        fxController,
                        "SoftDeformPB_Left_LongitudinalNegative_Shape_0_0",
                        LeftAnimatedPath,
                        "m_LocalScale.x"),
                    Is.EqualTo(1.0f + expectedSupineSpread).Within(0.0001f));
                Assert.That(
                    SampleClipProperty(
                        fxController,
                        "SoftDeformPB_Left_Upright_RootOffset",
                        LeftPivotPath,
                        "m_LocalPosition.x"),
                    Is.EqualTo(leftRestLocalPosition.x).Within(0.0001f));
                Assert.That(
                    SampleClipProperty(
                        fxController,
                        "SoftDeformPB_Right_Upright_RootOffset",
                        RightPivotPath,
                        "m_LocalPosition.x"),
                    Is.EqualTo(rightRestLocalPosition.x).Within(0.0001f));
                Assert.That(
                    SampleClipProperty(
                        fxController,
                        "SoftDeformPB_Left_LongitudinalNegative_RootOffset",
                        LeftPivotPath,
                        "m_LocalPosition.x"),
                    Is.EqualTo(leftRestLocalPosition.x - expectedSupineRootSpread).Within(0.0001f));
                Assert.That(
                    SampleClipProperty(
                        fxController,
                        "SoftDeformPB_Right_LongitudinalNegative_RootOffset",
                        RightPivotPath,
                        "m_LocalPosition.x"),
                    Is.EqualTo(rightRestLocalPosition.x + expectedSupineRootSpread).Within(0.0001f));
                Assert.That(
                    SampleClipProperty(
                        fxController,
                        "SoftDeformPB_Left_LongitudinalPositive_RootOffset",
                        LeftPivotPath,
                        "m_LocalPosition.x"),
                    Is.EqualTo(leftRestLocalPosition.x).Within(0.0001f));

                var gravityConstraints = avatar.GetComponentsInChildren<VRCRotationConstraint>(true);
                Assert.That(gravityConstraints, Has.Length.EqualTo(5));
                Assert.That(gravityConstraints.Count(constraint => constraint.FreezeToWorld), Is.EqualTo(1));
                Assert.That(gravityConstraints.All(constraint => constraint.IsActive), Is.True);
                Assert.That(
                    gravityConstraints.Where(constraint => constraint.FreezeToWorld).All(constraint =>
                        constraint.GetEffectiveTargetTransform() != left &&
                        constraint.GetEffectiveTargetTransform() != right),
                    Is.True);

                var rootRotationConstraints = gravityConstraints
                    .Where(constraint => constraint.gameObject.name.StartsWith("SoftDeformPB Motion Rotation "))
                    .ToArray();
                Assert.That(rootRotationConstraints, Has.Length.EqualTo(2));
                Assert.That(
                    rootRotationConstraints.All(constraint =>
                        constraint.gameObject.name.StartsWith("SoftDeformPB Motion Rotation ")),
                    Is.True);
                Assert.That(rootRotationConstraints.All(constraint => constraint.SolveInLocalSpace), Is.True);
                Assert.That(
                    rootRotationConstraints.All(constraint =>
                        Mathf.Abs(constraint.GlobalWeight - expectedRootRotationShare) < 0.0001f),
                    Is.True);
                Assert.That(
                    rootRotationConstraints.All(constraint =>
                        constraint.Sources.Count == 1 &&
                        constraint.Sources[0].SourceTransform.name.StartsWith("SoftDeformPB Root Driver Outward ")),
                    Is.True);

                var rootPositionConstraints = avatar.GetComponentsInChildren<VRCPositionConstraint>(true);
                Assert.That(rootPositionConstraints, Has.Length.EqualTo(2));
                Assert.That(rootPositionConstraints.All(constraint => constraint.IsActive), Is.True);
                Assert.That(rootPositionConstraints.All(constraint => constraint.GlobalWeight == 1.0f), Is.True);
                Assert.That(rootPositionConstraints.All(constraint => !constraint.SolveInLocalSpace), Is.True);
                Assert.That(
                    rootPositionConstraints.All(constraint =>
                        constraint.Sources.Count == 2 &&
                        constraint.Sources.All(source =>
                            source.SourceTransform.name.StartsWith("SoftDeformPB Root Driver Tip ") &&
                            Mathf.Abs(source.Weight - 0.5f) < 0.0001f)),
                    Is.True);
                Assert.That(
                    rootPositionConstraints.All(constraint =>
                        constraint.gameObject.GetComponent<VRCPhysBone>() == null),
                    Is.True);

                var gravitySenders = avatar.GetComponentsInChildren<VRCContactSender>(true);
                Assert.That(gravitySenders, Has.Length.EqualTo(1));
                Assert.That(gravitySenders.All(sender => !sender.localOnly), Is.True);
                Assert.That(gravitySenders.All(sender => sender.shapeType == ContactBase.ShapeType.Sphere), Is.True);

                var gravityReceivers = avatar.GetComponentsInChildren<VRCContactReceiver>(true);
                Assert.That(gravityReceivers, Has.Length.EqualTo(3));
                Assert.That(gravityReceivers.All(receiver => !receiver.localOnly), Is.True);
                Assert.That(gravityReceivers.All(receiver => receiver.allowSelf && !receiver.allowOthers), Is.True);
                Assert.That(
                    gravityReceivers.All(receiver =>
                        receiver.shapeType == ContactBase.ShapeType.Box &&
                        receiver.receiverType == ContactReceiver.ReceiverType.Proximity &&
                        receiver.useFaceProximity),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(avatar);
                if (authoredController != null) Object.DestroyImmediate(authoredController);
                if (authoredClip != null) Object.DestroyImmediate(authoredClip);
            }
        }

        [Test]
        public void Defaults_UseReferencePreservingGravity()
        {
            var avatar = new GameObject("Reference default test");
            try
            {
                var setup = avatar.AddComponent<SoftDeformPBSetup>();
                Assert.That(setup.rootMotionGravityFalloff, Is.EqualTo(1.0f));
                Assert.That(setup.motionGravityFalloff, Is.EqualTo(1.0f));
                Assert.That(setup.motionGravity, Is.EqualTo(0.0784f).Within(0.000001f));
            }
            finally
            {
                Object.DestroyImmediate(avatar);
            }
        }

        [TestCase(0.0f, 15.0f)]
        [TestCase(0.00001f, 30.0f)]
        [TestCase(0.05f, 1.0f)]
        public void ProcessAvatar_RootTravelRespectsZeroAndSmallOrLargeLimits(float maxOffset, float angle)
        {
            var avatar = new GameObject("Root travel test");
            try
            {
                avatar.AddComponent<Animator>();
                var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();
                var chest = new GameObject("Chest").transform;
                chest.SetParent(avatar.transform, false);
                var setup = avatar.AddComponent<SoftDeformPBSetup>();
                setup.leftBreast = CreateBreastBone(chest, "Breast_L", new Vector3(-0.1f, 0.1f, 0.1f));
                setup.rightBreast = CreateBreastBone(chest, "Breast_R", new Vector3(0.1f, 0.1f, 0.1f));
                setup.leftBreast.gameObject.AddComponent<VRCPhysBone>();
                setup.rightBreast.gameObject.AddComponent<VRCPhysBone>();
                setup.responseSamples = 3;
                setup.rootMotionMaxOffset = maxOffset;
                setup.rootMotionMaxAngle = angle;
                Transform left = setup.leftBreast;
                Vector3 rest = left.position;

                using (new OverrideTemporaryDirectoryScope(null))
                    Assert.That(AvatarProcessor.ProcessAvatar(avatar, AmbientPlatform.CurrentPlatform).Successful, Is.True);

                var drivers = avatar.GetComponentsInChildren<VRCPhysBone>(true)
                    .Where(bone => bone.name.StartsWith("SoftDeformPB Root Driver ")).ToArray();
                Assert.That(drivers.Length, Is.EqualTo(maxOffset == 0.0f ? 0 : 4));
                Assert.That(avatar.GetComponentsInChildren<VRCPositionConstraint>(true).Length,
                    Is.EqualTo(maxOffset == 0.0f ? 0 : 2));
                Assert.That(Vector3.Distance(left.position, rest), Is.LessThan(0.000001f));
                foreach (VRCPhysBone driver in drivers)
                {
                    Transform tip = driver.transform.GetChild(0);
                    Vector3 endpoint = tip.localPosition;
                    float chord = Vector3.Distance(endpoint, Quaternion.AngleAxis(angle, Vector3.right) * endpoint);
                    Assert.That(chord, Is.EqualTo(maxOffset).Within(0.000001f));
                    Assert.That(driver.CalcGravityFalloff(1.0f), Is.EqualTo(1.0f));
                }
                Assert.That(avatar.GetComponentsInChildren<VRCContactReceiver>(true), Has.Length.EqualTo(3));
                var fx = (AnimatorController)descriptor.baseAnimationLayers
                    .Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                const string path = "Chest/SoftDeformPB Motion Root Left/SoftDeformPB Motion Pivot Left/SoftDeformPB Motion Rotation Left/SoftDeformPB Motion Restore Left/Breast_L/SoftDeformPB Visual Left";
                foreach (string axis in new[] { "x", "y", "z" })
                    Assert.That(SampleClipProperty(fx, "SoftDeformPB_Left_Upright_Shape_0_0", path, "m_LocalScale." + axis),
                        Is.EqualTo(1.0f).Within(0.000001f));
            }
            finally
            {
                Object.DestroyImmediate(avatar);
            }
        }

        [Test]
        public void TrialDefaults_PreserveTargetsAndShapeSettingsAndSupportUndo()
        {
            var avatar = new GameObject("Trial defaults test");
            var defaultsHost = new GameObject("Default values");
            try
            {
                var setup = avatar.AddComponent<SoftDeformPBSetup>();
                var defaults = defaultsHost.AddComponent<SoftDeformPBSetup>();
                setup.leftBreast = CreateBreastBone(avatar.transform, "L", Vector3.left);
                setup.rightBreast = CreateBreastBone(avatar.transform, "R", Vector3.right);
                setup.gravitySupineSpread = 0.13f;
                setup.gravitySupineRootSpread = 0.005f;
                setup.squashDepth = 0.22f;
                setup.parameterPrefix = "KeepMe";
                setup.secondaryMotionStrength = 0.22f;
                setup.opposingColliderCoverage = 0.7f;
                setup.motionPull = 0.9f;
                setup.rootMotionGravityFalloff = 0.2f;
                string before = JsonUtility.ToJson(setup);
                Undo.RecordObject(setup, "Trial defaults test");
                setup.UseGravityTrialDefaults();
                Undo.FlushUndoRecordObjects();

                foreach (var field in typeof(SoftDeformPBSetup).GetFields())
                    if (field.Name.StartsWith("motion") || field.Name.StartsWith("rootMotion"))
                        Assert.That(field.GetValue(setup), Is.EqualTo(field.GetValue(defaults)), field.Name);
                Assert.That(setup.leftBreast.name, Is.EqualTo("L"));
                Assert.That(setup.rightBreast.name, Is.EqualTo("R"));
                Assert.That(setup.gravitySupineSpread, Is.EqualTo(0.13f));
                Assert.That(setup.gravitySupineRootSpread, Is.EqualTo(0.005f));
                Assert.That(setup.squashDepth, Is.EqualTo(0.22f));
                Assert.That(setup.parameterPrefix, Is.EqualTo("KeepMe"));
                Assert.That(setup.secondaryMotionStrength, Is.EqualTo(0.22f));
                Assert.That(setup.opposingColliderCoverage, Is.EqualTo(0.7f));
                Undo.PerformUndo();
                Assert.That(JsonUtility.ToJson(setup), Is.EqualTo(before));
            }
            finally
            {
                Object.DestroyImmediate(avatar);
                Object.DestroyImmediate(defaultsHost);
            }
        }

        [Test]
        public void ProcessAvatar_UsesPerSideGravitySensorsWhenParentsDiffer()
        {
            var avatar = new GameObject("SoftDeformPB Split Parent Test Avatar");
            try
            {
                avatar.AddComponent<Animator>();
                avatar.AddComponent<VRCAvatarDescriptor>();

                var chest = new GameObject("Chest").transform;
                chest.SetParent(avatar.transform, false);
                var leftParent = new GameObject("Left Parent").transform;
                leftParent.SetParent(chest, false);
                var rightParent = new GameObject("Right Parent").transform;
                rightParent.SetParent(chest, false);
                var left = CreateBreastBone(leftParent, "Breast_L", new Vector3(-0.1f, 0.1f, 0.1f));
                var right = CreateBreastBone(rightParent, "Breast_R", new Vector3(0.1f, 0.1f, 0.1f));
                left.gameObject.AddComponent<VRCPhysBone>();
                right.gameObject.AddComponent<VRCPhysBone>();

                var setup = avatar.AddComponent<SoftDeformPBSetup>();
                setup.leftBreast = left;
                setup.rightBreast = right;
                setup.responseSamples = 3;
                setup.parameterPrefix = "SoftDeformPB";

                using (new OverrideTemporaryDirectoryScope(null))
                {
                    Assert.That(AvatarProcessor.ProcessAvatar(avatar, AmbientPlatform.CurrentPlatform).Successful, Is.True);
                }

                Assert.That(avatar.GetComponentsInChildren<VRCRotationConstraint>(true), Has.Length.EqualTo(6));
                Assert.That(avatar.GetComponentsInChildren<VRCPositionConstraint>(true), Has.Length.EqualTo(2));
                Assert.That(avatar.GetComponentsInChildren<VRCPhysBone>(true), Has.Length.EqualTo(8));
                Assert.That(avatar.GetComponentsInChildren<VRCContactSender>(true), Has.Length.EqualTo(2));
                Assert.That(avatar.GetComponentsInChildren<VRCContactReceiver>(true), Has.Length.EqualTo(6));

                var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
                var fxController = descriptor.baseAnimationLayers
                    .Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX)
                    .animatorController as AnimatorController;
                Assert.That(fxController, Is.Not.Null);
                Assert.That(
                    fxController.parameters.Select(parameter => parameter.name),
                    Does.Contain("SoftDeformPB_L_GravityHorizontal"));
                Assert.That(
                    fxController.parameters.Select(parameter => parameter.name),
                    Does.Contain("SoftDeformPB_R_GravityHorizontal"));
            }
            finally
            {
                Object.DestroyImmediate(avatar);
            }
        }

        private static Transform CreateBreastBone(Transform parent, string name, Vector3 localPosition)
        {
            var bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.localPosition = localPosition;

            var endpoint = new GameObject(name + "_End").transform;
            endpoint.SetParent(bone, false);
            endpoint.localPosition = new Vector3(0.0f, 0.0f, 0.12f);
            return bone;
        }

        private static float SampleClipProperty(
            AnimatorController controller,
            string clipName,
            string path,
            string propertyName)
        {
            AnimationClip clip = controller.animationClips.Single(candidate => candidate.name == clipName);
            EditorCurveBinding binding = AnimationUtility.GetCurveBindings(clip)
                .Single(candidate => candidate.path == path && candidate.propertyName == propertyName);
            return AnimationUtility.GetEditorCurve(clip, binding).Evaluate(0.0f);
        }

    }
}
