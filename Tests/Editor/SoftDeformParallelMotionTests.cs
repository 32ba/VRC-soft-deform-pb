using System;
using System.Linq;
using System.Reflection;
using nadena.dev.ndmf;
using nadena.dev.ndmf.platform;
using NUnit.Framework;
using SoftDeformPB.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using Object = UnityEngine.Object;

namespace SoftDeformPB.Tests.Editor
{
    // Execute the installed SDK's constraint jobs, rather than reimplementing their math in a test.
    internal static class SoftDeformNativeConstraints
    {
        public static void Evaluate(GameObject avatar)
        {
            var constraints = avatar.GetComponentsInChildren<VRCConstraintBase>(true);
            var assembly = typeof(VRCConstraintBase).Assembly;
            var manager = assembly.GetType("VRC.Dynamics.VRCConstraintManager", true);
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            foreach (var c in constraints)
                manager.GetMethod("RegisterConstraint", flags).Invoke(null, new object[] { c });
            manager.GetMethod("Sdk_ManuallyRefreshGroups", flags).Invoke(null, new object[] { constraints });
            for (int step = 0; step < 2; step++)
            {
                manager.GetMethod("UpdateConstraints", flags).Invoke(null, null);
                object job = manager.GetMethod("ScheduleReadJob", flags).Invoke(null, new object[] { default(Unity.Jobs.JobHandle) });
                foreach (string stage in new[] { "PrePhysBone", "PostPhysBone", "PostLocalAvatarProcess" })
                    job = manager.GetMethod("ScheduleExecutionJobs", flags).Invoke(null, new[]
                    {
                        Enum.Parse(assembly.GetType("VRC.Dynamics.VRCConstraintPlayerLoopStage", true), stage), job
                    });
                ((Unity.Jobs.JobHandle)job).Complete();
                manager.GetMethod("PostUpdateConstraints", flags).Invoke(null, null);
            }
        }
    }

    public sealed class SoftDeformParallelMotionTests
    {
        private static SoftDeformPBSetup Avatar(int forwardAxis = 2)
        {
            var avatar = new GameObject("Parallel test");
            avatar.AddComponent<Animator>();
            avatar.AddComponent<VRCAvatarDescriptor>();
            var setup = avatar.AddComponent<SoftDeformPBSetup>();
            setup.responseSamples = 3;
            setup.rootMotionMaxOffset = 0;
            setup.secondaryMotionStrength = 0;
            setup.verticalAngleRetention = 0.6f;
            setup.horizontalAngleRetention = 0.2f;
            Transform Side(string name, float x)
            {
                var root = Node(name, avatar.transform);
                root.localPosition = new Vector3(x, 1, 0.1f);
                root.localRotation = Quaternion.FromToRotation(SoftDeformShapeMath.AxisVector(forwardAxis), Vector3.forward);
                var pb = root.gameObject.AddComponent<VRCPhysBone>();
                pb.endpointPosition = SoftDeformShapeMath.AxisVector(forwardAxis) * 0.1f;
                pb.radius = 0.01f;
                return root;
            }
            setup.leftBreast = Side("Left", -0.1f);
            setup.rightBreast = Side("Right", 0.1f);
            return setup;
        }

        private static Transform Node(string name, Transform parent)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false);
            return node;
        }

        private static void Build(GameObject avatar)
        {
            using (new OverrideTemporaryDirectoryScope(null))
                Assert.That(AvatarProcessor.ProcessAvatar(avatar, AmbientPlatform.CurrentPlatform).Successful, Is.True);
        }

        [TestCase(0, true)]
        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(1, false)]
        [TestCase(2, true)]
        [TestCase(2, false)]
        public void NativeConstraints_RetainVerticalAndHorizontalAnglesAndReturnToRest(int forwardAxis, bool vertical)
        {
            var setup = Avatar(forwardAxis);
            var avatar = setup.gameObject;
            var physical = setup.leftBreast;
            var rest = physical.localRotation;
            var axes = SoftDeformShapeMath.InferAxes(physical, SoftDeformShapeMath.AxisVector(forwardAxis));
            try
            {
                Build(avatar);
                avatar.transform.rotation = Quaternion.Euler(17, 29, 83);
                var visual = physical.GetComponentsInChildren<Transform>().Single(t => t.name == "SoftDeformPB Visual Left");
                var discoveredVisual = typeof(SoftDeformPBMotionCheckWindow).GetMethod("FindVisualRoot", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { physical, "Left" });
                Assert.That(discoveredVisual, Is.SameAs(visual), "Motion Quality Check must measure the nested visual branch");
                var reference = avatar.GetComponentsInChildren<Transform>().Single(t => t.name == "SoftDeformPB Angle Reference Left");
                var axis = SoftDeformShapeMath.AxisVector(vertical ? axes.Horizontal : axes.Vertical);
                foreach (float angle in new[] { 30f, -30f, 0f })
                {
                    physical.localRotation = rest * Quaternion.AngleAxis(angle, axis);
                    SoftDeformNativeConstraints.Evaluate(avatar);
                    var expected = reference.rotation * Quaternion.AngleAxis(angle * (vertical ? 0.4f : 0.8f), axis);
                    Assert.That(Quaternion.Angle(visual.rotation, expected), Is.LessThan(0.12f), "Input " + angle);
                }
                physical.localRotation = rest * Quaternion.AngleAxis(20, SoftDeformShapeMath.AxisVector(forwardAxis));
                SoftDeformNativeConstraints.Evaluate(avatar);
                Assert.That(Quaternion.Angle(visual.rotation, physical.rotation), Is.LessThan(0.02f), "Twist must remain free");
                Assert.That(physical.GetComponent<VRCPhysBone>().ignoreTransforms,
                    Does.Contain(physical.Find("SoftDeformPB Parallel Vertical Left")));
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void CompressionAnimator_StrengthensRetentionAndReleasesWithoutChangingPhysicalRotation()
        {
            var setup = Avatar();
            var avatar = setup.gameObject;
            var physical = setup.leftBreast;
            setup.squishVerticalAngleRetention = 0.75f;
            setup.squishHorizontalAngleRetention = 0.5f;
            try
            {
                Build(avatar);
                var vertical = physical.Find("SoftDeformPB Parallel Vertical Left").GetComponent<VRCRotationConstraint>();
                var horizontal = vertical.transform.GetChild(0).GetComponent<VRCRotationConstraint>();
                var fx = avatar.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                var animator = avatar.GetComponent<Animator>();
                animator.runtimeAnimatorController = fx;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                foreach (float squish in new[] { 0f, 0.5f, 1f, 0f })
                {
                    animator.SetFloat(physical.GetComponent<VRCPhysBone>().parameter + "_Squish", squish);
                    animator.Update(0.02f);
                    Assert.That(vertical.GlobalWeight, Is.EqualTo(0.6f + squish * 0.3f).Within(0.0001f));
                    Assert.That(horizontal.GlobalWeight, Is.EqualTo(0.2f + squish * 0.4f).Within(0.0001f));
                    physical.localRotation = Quaternion.Euler(30, 0, 0);
                    SoftDeformNativeConstraints.Evaluate(avatar);
                    var visual = physical.GetComponentsInChildren<Transform>().Single(t => t.name == "SoftDeformPB Visual Left");
                    Assert.That(Quaternion.Angle(Quaternion.identity, visual.localRotation), Is.LessThan(0.02f));
                    Assert.That(Quaternion.Angle(physical.localRotation, Quaternion.Euler(30, 0, 0)), Is.LessThan(0.02f));
                    Assert.That(Quaternion.Angle(avatar.transform.rotation, visual.rotation),
                        Is.EqualTo(30 * (1 - vertical.GlobalWeight)).Within(0.12f));
                }
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void AuthoredRotationAndMask_AreMirroredWithoutCopyingPositionOrScale(bool quaternion, bool enabled)
        {
            var setup = Avatar();
            var avatar = setup.gameObject;
            var physical = setup.leftBreast;
            var clip = new AnimationClip { name = "Authored rotation" };
            var mask = new AvatarMask();
            var machine = new AnimatorStateMachine();
            var controller = new AnimatorController();
            try
            {
                string property = quaternion ? "m_LocalRotation.x" : "localEulerAnglesRaw.x";
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Left", typeof(Transform), property), AnimationCurve.Linear(0, 0, 1, quaternion ? 0.2f : 20));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Left", typeof(Transform), "m_LocalPosition.x"), AnimationCurve.Constant(0, 1, -0.1f));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Left", typeof(Transform), "m_LocalScale.x"), AnimationCurve.Constant(0, 1, 1.2f));
                mask.transformCount = 2;
                mask.SetTransformPath(0, ""); mask.SetTransformActive(0, false);
                mask.SetTransformPath(1, "Left"); mask.SetTransformActive(1, enabled);
                machine.AddState("Authored").motion = clip;
                controller.AddLayer(new AnimatorControllerLayer { name = "Authored", defaultWeight = 1, stateMachine = machine, avatarMask = mask });
                var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
                descriptor.baseAnimationLayers = new[] { new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = false, animatorController = controller
                }};
                descriptor.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                Build(avatar);
                var output = (AnimatorController)descriptor.baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                var finalClip = output.animationClips.Single(c => c.name == clip.name);
                var reference = avatar.GetComponentsInChildren<Transform>().Single(t => t.name == "SoftDeformPB Angle Reference Left");
                string referencePath = AnimationUtility.CalculateTransformPath(reference, avatar.transform);
                var referenceBindings = AnimationUtility.GetCurveBindings(finalClip).Where(b => b.path == referencePath).ToArray();
                Assert.That(referenceBindings.Any(b => b.propertyName == property), Is.True);
                Assert.That(referenceBindings.All(b => b.propertyName.StartsWith("m_LocalRotation.") || b.propertyName.StartsWith("localEulerAngles")), Is.True);
                foreach (var binding in referenceBindings)
                {
                    var original = binding;
                    original.path = AnimationUtility.CalculateTransformPath(physical, avatar.transform);
                    Assert.That(AnimationUtility.GetEditorCurve(finalClip, binding).keys, Is.EqualTo(AnimationUtility.GetEditorCurve(finalClip, original).keys));
                }
                var finalMask = output.layers.Single(l => l.name == "Authored").avatarMask;
                int index = Enumerable.Range(0, finalMask.transformCount).Single(i => finalMask.GetTransformPath(i) == referencePath);
                Assert.That(finalMask.GetTransformActive(index), Is.EqualTo(enabled));
            }
            finally
            {
                Object.DestroyImmediate(avatar); Object.DestroyImmediate(controller); Object.DestroyImmediate(machine);
                Object.DestroyImmediate(mask); Object.DestroyImmediate(clip);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void DirectionalRoot_NativeConstraintsFilterAnatomicalAxesAndPreserveRest(int axis)
        {
            var avatar = new GameObject("Directional root test");
            try
            {
                avatar.transform.SetPositionAndRotation(new Vector3(2, 3, 4), Quaternion.Euler(17, 29, 38));
                avatar.transform.localScale = new Vector3(1.2f, 0.8f, 1.1f);
                var setup = avatar.AddComponent<SoftDeformPBSetup>();
                setup.rootHorizontalTranslation = 0.25f;
                setup.rootVerticalTranslation = 0;
                setup.rootLongitudinalTranslation = 0.75f;
                setup.rootVerticalRotation = 0.4f;
                setup.rootHorizontalRotation = 0.7f;
                setup.rootTwistRotation = 0;
                var basis = Quaternion.Euler(10, 20, 35);
                var rest = new Vector3(-0.1f, 1, 0.2f);
                var input = Node("Translation source", avatar.transform);
                var rotationInput = Node("Rotation source", avatar.transform);
                var motion = Node("Motion root", avatar.transform);
                var pivot = Node("Pivot", motion);
                pivot.localPosition = rest;
                var rotation = Node("Rotation", pivot);
                var positionConstraint = motion.gameObject.AddComponent<VRCPositionConstraint>();
                positionConstraint.Sources.Add(new VRCConstraintSource(input, 1, Vector3.zero, Vector3.zero));
                positionConstraint.PositionOffset = -rest;
                positionConstraint.AffectsPositionX = positionConstraint.AffectsPositionY = positionConstraint.AffectsPositionZ = true;
                positionConstraint.IsActive = positionConstraint.Locked = true;
                positionConstraint.GlobalWeight = 1;
                var rotationConstraint = rotation.gameObject.AddComponent<VRCRotationConstraint>();
                rotationConstraint.Sources.Add(new VRCConstraintSource(rotationInput, 1, Vector3.zero, Vector3.zero));
                rotationConstraint.AffectsRotationX = rotationConstraint.AffectsRotationY = rotationConstraint.AffectsRotationZ = true;
                rotationConstraint.IsActive = rotationConstraint.Locked = rotationConstraint.SolveInLocalSpace = true;
                rotationConstraint.GlobalWeight = 1;
                SoftDeformDirectionalMotion.Create(setup, motion, rotation, rest, basis, positionConstraint, rotationConstraint, "Left");
                foreach (float amount in new[] { 0f, 1f, -1f, 0f })
                {
                    Vector3 displacement = basis * new Vector3(0.006f, 0.008f, 0.009f) * amount;
                    input.localPosition = rest + displacement;
                    rotationInput.localRotation = basis * Quaternion.AngleAxis(15 * amount, SoftDeformShapeMath.AxisVector(axis)) * Quaternion.Inverse(basis);
                    SoftDeformNativeConstraints.Evaluate(avatar);
                    Vector3 actualPosition = avatar.transform.InverseTransformPoint(motion.position);
                    Vector3 expectedPosition = basis * new Vector3(0.006f * 0.25f, 0, 0.009f * 0.75f) * amount;
                    Assert.That(Vector3.Distance(actualPosition, expectedPosition), Is.LessThan(0.00001f), "Translation " + amount);
                    Assert.That(actualPosition.magnitude, Is.LessThanOrEqualTo(displacement.magnitude + 0.00001f));
                    float share = axis == 0 ? 0.4f : axis == 1 ? 0.7f : 0;
                    var expectedRotation = avatar.transform.rotation * basis * Quaternion.AngleAxis(15 * amount * share, SoftDeformShapeMath.AxisVector(axis)) * Quaternion.Inverse(basis);
                    Assert.That(Quaternion.Angle(rotation.rotation, expectedRotation), Is.LessThan(0.12f), "Rotation " + amount);
                }
            }
            finally { Object.DestroyImmediate(avatar); }
        }
    }
}
