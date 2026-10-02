using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using nadena.dev.ndmf.fluent;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(SoftDeformPB.Editor.SoftDeformPBNDMFPlugin))]

namespace SoftDeformPB.Editor
{
    internal sealed class SoftDeformPBNDMFPlugin : Plugin<SoftDeformPBNDMFPlugin>
    {
        public override string QualifiedName => "net.32ba.soft-deform-pb";
        public override string DisplayName => "Soft Deform PB";

        protected override void Configure()
        {
            var sequence = InPhase(BuildPhase.Generating);
            sequence.WithRequiredExtension(typeof(AnimatorServicesContext), s =>
            {
                s.Run(SoftDeformPBGeneratePass.Instance)
                    .BeforePlugin("nadena.dev.modular-avatar");
            });
            InPhase(BuildPhase.Transforming).WithRequiredExtension(typeof(AnimatorServicesContext), s =>
            {
                s.AfterPlugin("nadena.dev.modular-avatar")
                    .Run(SoftDeformClothingPass.Instance);
                s.Run(SoftDeformVisualScalePass.Instance);
                s.Run(SoftDeformParallelReferencePass.Instance);
                s.Run(SoftDeformAnimationConflictPass.Instance);
            });
            InPhase(BuildPhase.PlatformFinish).WithRequiredExtension(typeof(AnimatorServicesContext), s =>
                s.Run(SoftDeformFinalValidationPass.Instance));
        }
    }

    internal sealed class SoftDeformPBGeneratePass : Pass<SoftDeformPBGeneratePass>
    {
        private const float ClipDuration = 1.0f / 60.0f;
        private const float GravityReferenceRadius = 0.1f;
        private const float GravitySenderRadius = 0.002f;

        private readonly struct GravitySensor
        {
            public readonly string HorizontalParameter;
            public readonly string LongitudinalParameter;
            public readonly string UpwardParameter;
            public readonly float TargetHorizontalSign;
            public readonly float TargetLongitudinalSign;

            public GravitySensor(
                string horizontalParameter,
                string longitudinalParameter,
                string upwardParameter,
                float targetHorizontalSign = 1.0f,
                float targetLongitudinalSign = 1.0f)
            {
                HorizontalParameter = horizontalParameter;
                LongitudinalParameter = longitudinalParameter;
                UpwardParameter = upwardParameter;
                TargetHorizontalSign = targetHorizontalSign;
                TargetLongitudinalSign = targetLongitudinalSign;
            }

            public Vector2 Threshold(Vector2 gravity)
            {
                return new Vector2(Threshold(gravity.x), Threshold(gravity.y));
            }

            public static float Threshold(float gravity)
            {
                return 0.5f + (gravity * GravityReferenceRadius + GravitySenderRadius) /
                    (2 * (GravityReferenceRadius + GravitySenderRadius));
            }

            public GravitySensor WithTargetSigns(float horizontalSign, float longitudinalSign)
            {
                return new GravitySensor(
                    HorizontalParameter,
                    LongitudinalParameter,
                    UpwardParameter,
                    horizontalSign,
                    longitudinalSign);
            }
        }

        private readonly struct GravitySample
        {
            public readonly string Name;
            public readonly Vector2 Gravity;

            public GravitySample(string name, float horizontal, float longitudinal)
            {
                Name = name;
                Gravity = new Vector2(horizontal, longitudinal);
            }
        }

        private static readonly GravitySample[] GravitySamples =
        {
            new GravitySample("Upright", 0.0f, 0.0f),
            new GravitySample("HorizontalNegative", -1.0f, 0.0f),
            new GravitySample("HorizontalPositive", 1.0f, 0.0f),
            new GravitySample("LongitudinalNegative", 0.0f, -1.0f),
            new GravitySample("LongitudinalPositive", 0.0f, 1.0f)
        };

        protected override void Execute(BuildContext context)
        {
            var setups = context.AvatarRootObject.GetComponentsInChildren<SoftDeformPBSetup>(true);
            var supports = context.AvatarRootObject.GetComponentsInChildren<SoftDeformPBClothingSupport>(true);

            var claimedBones = new HashSet<Transform>();
            foreach (var setup in setups)
            {
                ValidateSetup(context.AvatarRootObject.transform, setup, claimedBones);
            }
            ValidateParameterPrefixes(context.AvatarRootObject.transform, setups);
            var clothingLimits = SoftDeformClothingLimits.Resolve(context.AvatarRootObject, setups, supports);

            foreach (var setup in setups)
            {
                if (clothingLimits.TryGetValue(setup, out var limits)) limits.Apply(setup);
                var leftChain = SoftDeformRigGeometry.Capture(setup.leftBreast,
                    FindExistingPhysBone(context.AvatarRootObject.transform, setup, setup.leftBreast));
                var rightChain = SoftDeformRigGeometry.Capture(setup.rightBreast,
                    FindExistingPhysBone(context.AvatarRootObject.transform, setup, setup.rightBreast));
                bool usesSharedGravitySensor = TryCreateSharedGravitySensors(
                    context.AvatarRootObject.transform,
                    setup,
                    setup.leftBreast,
                    setup.rightBreast,
                    out GravitySensor leftGravitySensor,
                    out GravitySensor rightGravitySensor);
                GenerateSide(
                    context,
                    setup,
                    setup.leftBreast,
                    FindExistingPhysBone(context.AvatarRootObject.transform, setup, setup.leftBreast),
                    usesSharedGravitySensor ? leftGravitySensor : (GravitySensor?)null,
                    "Left",
                    "L",
                    limits != null);
                GenerateSide(
                    context,
                    setup,
                    setup.rightBreast,
                    FindExistingPhysBone(context.AvatarRootObject.transform, setup, setup.rightBreast),
                    usesSharedGravitySensor ? rightGravitySensor : (GravitySensor?)null,
                    "Right",
                    "R",
                    limits != null);
                SoftDeformCollisionRig.Create(context.AvatarRootObject, leftChain, rightChain, setup);
                int driverCount = setup.rootMotionMaxOffset > 0.0f ? 4 : 0;
                Debug.Log(
                    usesSharedGravitySensor
                        ? $"[Soft Deform PB] '{setup.name}': generated {driverCount} root-motion drivers, {driverCount} root constraints, 2 posture-response layers, and a shared gravity sensor (4 contacts, 1 constraint)."
                        : $"[Soft Deform PB] '{setup.name}': generated {driverCount} root-motion drivers, {driverCount} root constraints, 2 posture-response layers, and per-side gravity sensors (8 contacts, 2 constraints).",
                    setup);
                Object.DestroyImmediate(setup);
            }
            foreach (var support in supports) Object.DestroyImmediate(support);

            // Merge Armature matches children through the authored hierarchy.
            // Keep those paths available until MA has merged the outfit bones.
            // The generated motion parents already exist for animator merging.
            foreach (var side in context.GetState<SoftDeformBuildState>().Sides)
                side.Physical.SetParent(side.OriginalParent, false);
        }

        private static bool TryCreateSharedGravitySensors(
            Transform avatarRoot,
            SoftDeformPBSetup setup,
            Transform left,
            Transform right,
            out GravitySensor leftSensor,
            out GravitySensor rightSensor)
        {
            leftSensor = default;
            rightSensor = default;
            if (left.parent != right.parent) return false;

            SoftDeformAxisMap leftAxes = SoftDeformShapeMath.InferAxes(left, SoftDeformRigGeometry.InferLocalDirection(left,
                FindExistingPhysBone(avatarRoot, setup, left)));
            SoftDeformAxisMap rightAxes = SoftDeformShapeMath.InferAxes(right, SoftDeformRigGeometry.InferLocalDirection(right,
                FindExistingPhysBone(avatarRoot, setup, right)));
            Vector3 leftHorizontal = AxisInParent(left, leftAxes.Horizontal);
            Vector3 rightHorizontal = AxisInParent(right, rightAxes.Horizontal);
            Vector3 leftLongitudinal = AxisInParent(left, leftAxes.Longitudinal);
            Vector3 rightLongitudinal = AxisInParent(right, rightAxes.Longitudinal);

            float horizontalDot = Vector3.Dot(leftHorizontal, rightHorizontal);
            float longitudinalDot = Vector3.Dot(leftLongitudinal, rightLongitudinal);
            const float AxisAlignmentThreshold = 0.999f;
            if (Mathf.Abs(horizontalDot) < AxisAlignmentThreshold ||
                Mathf.Abs(longitudinalDot) < AxisAlignmentThreshold)
                return false;

            string parameterPrefix = SanitizeParameterPrefix(setup.parameterPrefix);
            string horizontalParameter = parameterPrefix + "_GravityHorizontal";
            string longitudinalParameter = parameterPrefix + "_GravityLongitudinal";
            string tag = "SoftDeformPB.Gravity." + parameterPrefix + ".Shared";
            leftSensor = CreateGravitySensor(
                left.parent,
                leftHorizontal,
                leftLongitudinal,
                horizontalParameter,
                longitudinalParameter,
                parameterPrefix + "_GravityUpward",
                tag,
                "Shared");
            rightSensor = leftSensor.WithTargetSigns(
                horizontalDot < 0.0f ? -1.0f : 1.0f,
                longitudinalDot < 0.0f ? -1.0f : 1.0f);
            return true;
        }

        internal static void ValidateSetup(Transform avatarRoot, SoftDeformPBSetup setup, ISet<Transform> claimedBones)
        {
            const SoftDeformMotionForceOverrides supported = SoftDeformMotionForceOverrides.Pull |
                SoftDeformMotionForceOverrides.Spring | SoftDeformMotionForceOverrides.Stiffness |
                SoftDeformMotionForceOverrides.Immobile | SoftDeformMotionForceOverrides.Gravity |
                SoftDeformMotionForceOverrides.GravityFalloff;
            if ((setup.motionForceOverrides & ~supported) != 0)
                throw BuildError(setup, "motionForceOverrides contains an unsupported force.");
            foreach (var field in typeof(SoftDeformPBSetup).GetFields())
            {
                var range = field.GetCustomAttributes(typeof(RangeAttribute), false).OfType<RangeAttribute>().FirstOrDefault();
                if (range == null) continue;
                float value = Convert.ToSingle(field.GetValue(setup));
                if (float.IsNaN(value) || float.IsInfinity(value) || value < range.min || value > range.max)
                    throw BuildError(setup, $"{field.Name} must be finite and between {range.min} and {range.max}.");
            }
            if (setup.leftBreast == null || setup.rightBreast == null)
                throw BuildError(setup, "Both leftBreast and rightBreast must be assigned.");
            if (setup.leftBreast == setup.rightBreast)
                throw BuildError(setup, "Left and right breast bones must be different transforms.");
            if (!setup.leftBreast.IsChildOf(avatarRoot) || !setup.rightBreast.IsChildOf(avatarRoot))
                throw BuildError(setup, "Both breast bones must be inside the avatar hierarchy.");
            if (setup.leftBreast.parent == null || setup.rightBreast.parent == null)
                throw BuildError(setup, "Breast bones must have a parent transform.");
            if (string.IsNullOrWhiteSpace(setup.parameterPrefix))
                throw BuildError(setup, "parameterPrefix must not be empty.");
            if (setup.responseSamples < 3)
                throw BuildError(setup, "responseSamples must be at least 3.");
            if (float.IsNaN(setup.rootMotionMaxOffset) || float.IsInfinity(setup.rootMotionMaxOffset) ||
                setup.rootMotionMaxOffset < 0.0f || setup.rootMotionMaxOffset > 0.05f)
                throw BuildError(setup, "rootMotionMaxOffset must be finite and between 0 and 0.05.");
            if (float.IsNaN(setup.rootMotionMaxAngle) || float.IsInfinity(setup.rootMotionMaxAngle) ||
                setup.rootMotionMaxAngle < 1.0f || setup.rootMotionMaxAngle > 30.0f)
                throw BuildError(setup, "rootMotionMaxAngle must be finite and between 1 and 30 degrees.");

            foreach (var bone in new[] { setup.leftBreast, setup.rightBreast })
            {
                if (claimedBones.Any(existing => bone.IsChildOf(existing) || existing.IsChildOf(bone)))
                    throw BuildError(setup, $"Bone '{bone.name}' overlaps another selected physical chain.");
                if (!claimedBones.Add(bone))
                    throw BuildError(setup, $"Bone '{bone.name}' is referenced by more than one setup.");

                SoftDeformRigGeometry.InferLocalDirection(bone, FindExistingPhysBone(avatarRoot, setup, bone));
                var scale = bone.lossyScale;
                if (!SoftDeformRigGeometry.Finite(scale) || Mathf.Abs(scale.x * scale.y * scale.z) < 1e-12f)
                    throw BuildError(setup, $"Bone '{bone.name}' has a zero or invalid transform scale.");

            }
        }

        internal static void ValidateParameterPrefixes(Transform avatar, SoftDeformPBSetup[] setups)
        {
            var selected = new Dictionary<string, VRCPhysBone>(StringComparer.Ordinal);
            var setupPrefixes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var setup in setups)
            {
                string setupPrefix = SanitizeParameterPrefix(setup.parameterPrefix);
                if (!setupPrefixes.Add(setupPrefix))
                    throw BuildError(setup, $"Duplicate generated parameter prefix '{setupPrefix}'. Give each setup a distinct prefix.");
                foreach (var entry in new[] { (bone: setup.leftBreast, suffix: "L"), (bone: setup.rightBreast, suffix: "R") })
                {
                    var pb = FindExistingPhysBone(avatar, setup, entry.bone);
                    string prefix = string.IsNullOrWhiteSpace(pb.parameter) ? setupPrefix + "_" + entry.suffix : pb.parameter;
                    if (selected.ContainsKey(prefix))
                        throw BuildError(setup, $"Duplicate PhysBone parameter prefix '{prefix}' on '{selected[prefix].name}' and '{pb.name}'. Each chain needs distinct Squish/Stretch values.");
                    selected.Add(prefix, pb);
                    if (SoftDeformCompressionRig.Enabled(setup))
                        foreach (string suffix in new[] { "_Inward", "_OuterTouch" })
                            if (avatar.GetComponentsInChildren<VRCContactReceiver>(true).Any(r => r.parameter == prefix + suffix))
                                throw BuildError(setup, $"Contact receiver already writes generated parameter '{prefix + suffix}'. Choose a distinct PhysBone parameter prefix.");
                }
            }
            foreach (var other in avatar.GetComponentsInChildren<VRCPhysBone>(true))
                if (!string.IsNullOrWhiteSpace(other.parameter) && selected.TryGetValue(other.parameter, out var owner) && owner != other)
                    throw new InvalidOperationException($"[Soft Deform PB] PhysBone '{other.name}' also writes '{other.parameter}', used by '{owner.name}'. Assign distinct PhysBone parameter prefixes.");
        }

        internal static VRCPhysBone FindExistingPhysBone(
            Transform avatarRoot,
            SoftDeformPBSetup setup,
            Transform bone)
        {
            var candidates = avatarRoot.GetComponentsInChildren<VRCPhysBone>(true)
                .Where(physBone =>
                    physBone != null &&
                    (physBone.rootTransform != null ? physBone.rootTransform : physBone.transform) == bone)
                .ToArray();

            var attachedToBone = candidates.Where(physBone => physBone.transform == bone).ToArray();
            if (attachedToBone.Length == 1) return attachedToBone[0];
            if (attachedToBone.Length > 1)
                throw BuildError(setup, $"Bone '{bone.name}' has more than one attached VRC Phys Bone.");
            if (candidates.Length == 1) return candidates[0];
            if (candidates.Length == 0)
                throw BuildError(setup, $"Bone '{bone.name}' must already be the root of a VRC Phys Bone.");

            throw BuildError(
                setup,
                $"Bone '{bone.name}' is the explicit root of more than one VRC Phys Bone and none is attached directly.");
        }

        private static InvalidOperationException BuildError(SoftDeformPBSetup setup, string message)
        {
            string owner = setup != null ? setup.name : "unknown";
            return new InvalidOperationException($"[Soft Deform PB] '{owner}': {message}");
        }

        private static void GenerateSide(
            BuildContext context,
            SoftDeformPBSetup setup,
            Transform target,
            VRCPhysBone existingPhysBone,
            GravitySensor? sharedGravitySensor,
            string sideName,
            string sideSuffix,
            bool clothingSupport)
        {
            Transform originalParent = target.parent;
            Vector3 localDirection = SoftDeformRigGeometry.InferLocalDirection(target, existingPhysBone);
            Vector3 direction = localDirection.normalized;
            var axes = SoftDeformShapeMath.InferAxes(target, direction);
            string fallbackPrefix = SanitizeParameterPrefix(setup.parameterPrefix) + "_" + sideSuffix;
            string prefix = PrepareExistingPhysBone(existingPhysBone, setup, fallbackPrefix, clothingSupport);
            if (clothingSupport) SoftDeformClothingLimits.ConfigurePrimary(existingPhysBone);
            float longitudinalOutwardSign = GetAxis(direction, axes.Longitudinal) < 0.0f ? -1.0f : 1.0f;
            Vector3 horizontalAxisInParent = AxisInParent(target, axes.Horizontal);
            Vector3 rootMotionHorizontalAxis = Vector3.Dot(horizontalAxisInParent, target.localPosition) < 0.0f
                ? -horizontalAxisInParent
                : horizontalAxisInParent;
            GravitySensor gravitySensor = sharedGravitySensor ?? CreateGravitySensor(
                target.parent,
                horizontalAxisInParent,
                AxisInParent(target, axes.Longitudinal),
                prefix + "_GravityHorizontal",
                prefix + "_GravityLongitudinal",
                prefix + "_GravityUpward",
                "SoftDeformPB.Gravity." + SanitizeParameterPrefix(prefix) + "." + sideName,
                sideName);
            Transform motionPivot = CreateRootMotionDriver(
                setup,
                target,
                direction,
                rootMotionHorizontalAxis,
                sideName);

            Transform visualRoot = SoftDeformVisualRig.Create(context, target, sideName);
            SoftDeformParallelMotion.Create(context, setup, target, visualRoot, axes, prefix, sideName);
            var opposite = target == setup.leftBreast ? setup.rightBreast : setup.leftBreast;
            Vector3 horizontalWorld = target.TransformDirection(SoftDeformShapeMath.AxisVector(axes.Horizontal));
            Vector3 outwardWorld = Vector3.Dot(horizontalWorld, target.position - opposite.position) >= 0 ? horizontalWorld : -horizontalWorld;
            var compression = SoftDeformCompressionRig.Create(context, setup, target, visualRoot, existingPhysBone,
                localDirection, axes, target.parent.InverseTransformDirection(outwardWorld), prefix, sideName);
            SoftDeformSecondaryMotion.Create(context.AvatarRootObject, setup, target, visualRoot, localDirection, sideName);

            AnimatorController controller = CreateController(
                context,
                setup,
                Vector3.one,
                axes,
                longitudinalOutwardSign,
                gravitySensor,
                prefix,
                sideName,
                compression,
                setup.minimumCompressionRatio == 0 ? 0 : existingPhysBone.maxSquish *
                    SoftDeformCompressionMath.CurveUpperBound(existingPhysBone.maxSquishCurve));
            context.GetState<SoftDeformBuildState>().Sides.Add(new SoftDeformBuildState.Side
            {
                Physical = target, Visual = visualRoot, PhysBone = existingPhysBone, Prefix = prefix,
                OriginalParent = originalParent, MotionParent = target.parent,
                ClipNames = new HashSet<string>(controller.animationClips.Select(c => c.name))
            });
            var merge = (compression == null ? visualRoot : compression.AnimationRoot).gameObject.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Relative;
            merge.matchAvatarWriteDefaults = false;
            merge.deleteAttachedAnimator = false;
            merge.layerPriority = 0;

            AnimatorController postureController = CreatePostureRootOffsetController(
                context,
                setup,
                motionPivot.localPosition,
                rootMotionHorizontalAxis,
                longitudinalOutwardSign,
                gravitySensor,
                sideName);
            var postureMerge = motionPivot.gameObject.AddComponent<ModularAvatarMergeAnimator>();
            postureMerge.animator = postureController;
            postureMerge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            postureMerge.pathMode = MergeAnimatorPathMode.Relative;
            postureMerge.matchAvatarWriteDefaults = false;
            postureMerge.deleteAttachedAnimator = false;
            postureMerge.layerPriority = 0;
        }

        private static Transform CreateRootMotionDriver(
            SoftDeformPBSetup setup,
            Transform target,
            Vector3 localDirection,
            Vector3 horizontalInChest,
            string sideName)
        {
            Transform chest = target.parent;
            Vector3 originalLocalPosition = target.localPosition;
            Quaternion originalLocalRotation = target.localRotation;
            Vector3 originalLocalScale = target.localScale;

            Vector3 outwardInChest = originalLocalRotation * Vector3.Scale(originalLocalScale, localDirection);
            outwardInChest = outwardInChest.sqrMagnitude > 1e-8f
                ? outwardInChest.normalized
                : Vector3.forward;

            var motionRootObject = new GameObject($"SoftDeformPB Motion Root {sideName}");
            Transform motionRoot = motionRootObject.transform;
            motionRoot.SetParent(chest, false);
            motionRoot.localPosition = Vector3.zero;
            motionRoot.localRotation = Quaternion.identity;
            motionRoot.localScale = Vector3.one;

            var pivotObject = new GameObject($"SoftDeformPB Motion Pivot {sideName}");
            Transform pivot = pivotObject.transform;
            pivot.SetParent(motionRoot, false);
            pivot.localPosition = originalLocalPosition;
            pivot.localRotation = Quaternion.identity;
            pivot.localScale = Vector3.one;

            var rotationRootObject = new GameObject($"SoftDeformPB Motion Rotation {sideName}");
            Transform rotationRoot = rotationRootObject.transform;
            rotationRoot.SetParent(pivot, false);
            rotationRoot.localPosition = Vector3.zero;
            rotationRoot.localRotation = Quaternion.identity;
            rotationRoot.localScale = Vector3.one;

            var restoreObject = new GameObject($"SoftDeformPB Motion Restore {sideName}");
            Transform restore = restoreObject.transform;
            restore.SetParent(rotationRoot, false);
            restore.localPosition = -originalLocalPosition;
            restore.localRotation = Quaternion.identity;
            restore.localScale = Vector3.one;

            target.SetParent(restore, false);
            target.localPosition = originalLocalPosition;
            target.localRotation = originalLocalRotation;
            target.localScale = originalLocalScale;

            if (setup.rootMotionMaxOffset == 0.0f) return pivot;

            // A lever tip travels along a chord, not a tangent. Each tip is bounded
            // by maxOffset, so their convex average is bounded by it as well.
            float leverLength = SoftDeformShapeMath.RootLeverLength(
                setup.rootMotionMaxOffset, setup.rootMotionMaxAngle);
            Transform outwardDriver = CreateRootLever(
                chest, originalLocalPosition, outwardInChest, leverLength, setup, $"Outward {sideName}");
            Transform outwardTip = outwardDriver.GetChild(0);
            Transform horizontalDriver = CreateRootLever(
                chest, originalLocalPosition, horizontalInChest, leverLength, setup, $"Horizontal {sideName}");
            Transform horizontalTip = horizontalDriver.GetChild(0);

            var positionConstraint = motionRootObject.AddComponent<VRCPositionConstraint>();
            positionConstraint.Sources.Add(new VRCConstraintSource(outwardTip, 0.5f, Vector3.zero, Vector3.zero));
            positionConstraint.Sources.Add(new VRCConstraintSource(horizontalTip, 0.5f, Vector3.zero, Vector3.zero));
            positionConstraint.PositionAtRest = Vector3.zero;
            positionConstraint.PositionOffset = -originalLocalPosition;
            positionConstraint.AffectsPositionX = true;
            positionConstraint.AffectsPositionY = true;
            positionConstraint.AffectsPositionZ = true;
            positionConstraint.GlobalWeight = 1.0f;
            positionConstraint.SolveInLocalSpace = false;
            positionConstraint.IsActive = true;
            positionConstraint.Locked = true;

            var rotationConstraint = rotationRootObject.AddComponent<VRCRotationConstraint>();
            rotationConstraint.Sources.Add(new VRCConstraintSource(outwardDriver, 1.0f, Vector3.zero, Vector3.zero));
            rotationConstraint.RotationAtRest = Vector3.zero;
            rotationConstraint.RotationOffset = Quaternion.Inverse(outwardDriver.localRotation).eulerAngles;
            rotationConstraint.AffectsRotationX = true;
            rotationConstraint.AffectsRotationY = true;
            rotationConstraint.AffectsRotationZ = true;
            rotationConstraint.GlobalWeight = setup.rootMotionRotationShare;
            rotationConstraint.SolveInLocalSpace = true;
            rotationConstraint.IsActive = true;
            rotationConstraint.Locked = true;
            SoftDeformDirectionalMotion.Create(setup, motionRoot, rotationRoot, originalLocalPosition,
                Quaternion.LookRotation(outwardInChest, Vector3.Cross(outwardInChest, horizontalInChest)),
                positionConstraint, rotationConstraint, sideName);
            return pivot;
        }

        private static Transform CreateRootLever(
            Transform chest,
            Vector3 targetLocalPosition,
            Vector3 leverDirectionInChest,
            float leverLength,
            SoftDeformPBSetup setup,
            string driverName)
        {
            Vector3 direction = leverDirectionInChest.sqrMagnitude > 1e-8f
                ? leverDirectionInChest.normalized
                : Vector3.forward;
            var driverObject = new GameObject($"SoftDeformPB Root Driver {driverName}");
            Transform driver = driverObject.transform;
            driver.SetParent(chest, false);
            driver.localPosition = targetLocalPosition - direction * leverLength;
            driver.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
            driver.localScale = Vector3.one;

            var tipObject = new GameObject($"SoftDeformPB Root Driver Tip {driverName}");
            Transform tip = tipObject.transform;
            tip.SetParent(driver, false);
            tip.localPosition = Vector3.up * leverLength;
            tip.localRotation = Quaternion.identity;
            tip.localScale = Vector3.one;

            var driverPhysBone = driverObject.AddComponent<VRCPhysBone>();
            ConfigureRootDriverPhysBone(driverPhysBone, setup);
            return driver;
        }

        private static void ConfigureRootDriverPhysBone(VRCPhysBone physBone, SoftDeformPBSetup setup)
        {
            physBone.version = VRCPhysBoneBase.Version.Version_1_1;
            physBone.integrationType = VRCPhysBoneBase.IntegrationType.Simplified;
            physBone.rootTransform = physBone.transform;
            physBone.parameter = string.Empty;
            physBone.pull = setup.rootMotionPull;
            physBone.pullCurve = ConstantCurve(1.0f);
            physBone.spring = setup.rootMotionSpring;
            physBone.springCurve = ConstantCurve(1.0f);
            physBone.immobile = setup.rootMotionImmobile;
            physBone.immobileCurve = ConstantCurve(1.0f);
            physBone.gravity = setup.rootMotionGravity;
            physBone.gravityCurve = ConstantCurve(1.0f);
            physBone.gravityFalloff = setup.rootMotionGravityFalloff;
            physBone.gravityFalloffCurve = ConstantCurve(1.0f);
            SoftDeformRestPose.Apply(physBone, setup.restPoseSupport);
            physBone.limitType = VRCPhysBoneBase.LimitType.Angle;
            physBone.maxAngleX = setup.rootMotionMaxAngle;
            physBone.maxAngleXCurve = ConstantCurve(1.0f);
            physBone.maxAngleZ = setup.rootMotionMaxAngle;
            physBone.maxAngleZCurve = ConstantCurve(1.0f);
            physBone.radius = 0.0f;
            physBone.radiusCurve = ConstantCurve(0.0f);
            physBone.allowCollision = VRCPhysBoneBase.AdvancedBool.False;
            physBone.allowGrabbing = VRCPhysBoneBase.AdvancedBool.False;
            physBone.allowPosing = VRCPhysBoneBase.AdvancedBool.False;
            physBone.isAnimated = false;
        }

        private static GravitySensor CreateGravitySensor(
            Transform chest,
            Vector3 horizontalAxis,
            Vector3 longitudinalAxis,
            string horizontalParameter,
            string longitudinalParameter,
            string upwardParameter,
            string tag,
            string sensorName)
        {
            var referenceObject = new GameObject($"SoftDeformPB Gravity Reference {sensorName}");
            Transform reference = referenceObject.transform;
            reference.SetParent(chest, false);
            reference.localPosition = Vector3.zero;
            reference.rotation = Quaternion.identity;

            var worldLock = referenceObject.AddComponent<VRCRotationConstraint>();
            worldLock.IsActive = true;
            worldLock.GlobalWeight = 1.0f;
            worldLock.SolveInLocalSpace = false;
            worldLock.FreezeToWorld = true;
            worldLock.Locked = true;

            var senderObject = new GameObject("World Down Sender");
            senderObject.transform.SetParent(reference, false);
            senderObject.transform.localPosition = Vector3.down * GravityReferenceRadius;
            var sender = senderObject.AddComponent<VRCContactSender>();
            sender.shapeType = ContactBase.ShapeType.Sphere;
            sender.radius = GravitySenderRadius;
            sender.localOnly = false;
            sender.collisionTags = new List<string> { tag };

            CreateGravityReceiver(chest, horizontalAxis, horizontalParameter, tag, sensorName, "Horizontal");
            CreateGravityReceiver(chest, longitudinalAxis, longitudinalParameter, tag, sensorName, "Longitudinal");
            // Capture authored up in the stable parent frame, independently of mirrored breast axes.
            CreateGravityReceiver(chest, chest.InverseTransformDirection(Vector3.up), upwardParameter, tag, sensorName, "Upward");

            return new GravitySensor(horizontalParameter, longitudinalParameter, upwardParameter);
        }

        private static Vector3 AxisInParent(Transform target, int axis)
        {
            Vector3 direction = target.localRotation * Vector3.Scale(target.localScale, SoftDeformShapeMath.AxisVector(axis));
            return direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.forward;
        }

        private static void CreateGravityReceiver(
            Transform chest,
            Vector3 positiveAxis,
            string parameter,
            string tag,
            string sideName,
            string axisName)
        {
            var receiverObject = new GameObject($"SoftDeformPB Gravity {axisName} {sideName}");
            receiverObject.transform.SetParent(chest, false);
            Vector3 axis = positiveAxis.sqrMagnitude > 1e-8f ? positiveAxis.normalized : Vector3.forward;
            Vector3 up = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) < 0.99f ? Vector3.up : Vector3.forward;

            var receiver = receiverObject.AddComponent<VRCContactReceiver>();
            receiver.shapeType = ContactBase.ShapeType.Box;
            receiver.size = Vector3.one * (2.0f * (GravityReferenceRadius + GravitySenderRadius));
            receiver.rotation = Quaternion.LookRotation(axis, up);
            receiver.localOnly = false;
            receiver.allowSelf = true;
            receiver.allowOthers = false;
            receiver.receiverType = ContactReceiver.ReceiverType.Proximity;
            receiver.useFaceProximity = true;
            receiver.parameter = parameter;
            receiver.collisionTags = new List<string> { tag };
        }

        private static string PrepareExistingPhysBone(
            VRCPhysBone physBone,
            SoftDeformPBSetup setup,
            string fallbackPrefix,
            bool clothingSupport)
        {
            physBone.version = VRCPhysBoneBase.Version.Version_1_1;
            if (string.IsNullOrWhiteSpace(physBone.parameter))
                physBone.parameter = fallbackPrefix;

            if (!setup.preserveExistingMotion)
            {
                physBone.integrationType = VRCPhysBoneBase.IntegrationType.Simplified;
                physBone.pull = setup.motionPull;
                physBone.pullCurve = ConstantCurve(1.0f);
                physBone.spring = setup.motionSpring;
                physBone.springCurve = ConstantCurve(1.0f);
                physBone.immobile = setup.motionImmobile;
                physBone.immobileCurve = ConstantCurve(1.0f);
                physBone.gravity = setup.motionGravity;
                physBone.gravityCurve = AnimationCurve.Linear(0.0f, 0.45f, 1.0f, 1.0f);
                physBone.gravityFalloff = setup.motionGravityFalloff;
                physBone.gravityFalloffCurve = ConstantCurve(1.0f);
            }
            else
            {
                // Preserve chain-specific response curves and integration while tuning selected base forces.
                var overrides = setup.motionForceOverrides;
                if ((overrides & SoftDeformMotionForceOverrides.Pull) != 0) physBone.pull = setup.motionPull;
                if ((overrides & SoftDeformMotionForceOverrides.Spring) != 0) physBone.spring = setup.motionSpring;
                if ((overrides & SoftDeformMotionForceOverrides.Stiffness) != 0) physBone.stiffness = setup.motionStiffness;
                if ((overrides & SoftDeformMotionForceOverrides.Immobile) != 0) physBone.immobile = setup.motionImmobile;
                if ((overrides & SoftDeformMotionForceOverrides.Gravity) != 0) physBone.gravity = setup.motionGravity;
                if ((overrides & SoftDeformMotionForceOverrides.GravityFalloff) != 0) physBone.gravityFalloff = setup.motionGravityFalloff;
            }
            SoftDeformRestPose.Apply(physBone, setup.restPoseSupport);
            // An explicit clothing budget still takes priority over authored limits.
            if (!setup.preserveExistingMotion || clothingSupport)
            {
                physBone.maxAngleX = setup.motionMaxPitch;
                physBone.maxAngleXCurve = ConstantCurve(1.0f);
                physBone.maxAngleZ = setup.motionMaxYaw;
                physBone.maxAngleZCurve = ConstantCurve(1.0f);
            }
            physBone.maxSquish = SoftDeformCompressionMath.EffectiveMaxSquish(setup.maxSquish,
                setup.minimumCompressionRatio, setup.minimumCompressionRatio == 0 ? 1 :
                    SoftDeformCompressionMath.CurveUpperBound(physBone.maxSquishCurve));
            physBone.maxStretch = setup.maxStretch;
            physBone.stretchMotion = setup.stretchMotion;
            physBone.isAnimated = true;
            return physBone.parameter;
        }

        private static AnimationCurve ConstantCurve(float value)
        {
            value = Mathf.Clamp01(value);
            return AnimationCurve.Linear(0.0f, value, 1.0f, value);
        }

        private static AnimatorController CreateController(
            BuildContext context,
            SoftDeformPBSetup setup,
            Vector3 restScale,
            SoftDeformAxisMap axes,
            float longitudinalOutwardSign,
            GravitySensor gravitySensor,
            string prefix,
            string sideName,
            SoftDeformCompressionRig compression,
            float maximumPhysicalCompression)
        {
            string squishParameter = prefix + VRCPhysBoneBase.PARAM_SQUISH;
            string stretchParameter = prefix + VRCPhysBoneBase.PARAM_STRETCH;

            var controller = new AnimatorController { name = $"SoftDeformPB_{sideName}_Controller" };
            controller.AddParameter(squishParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(stretchParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(gravitySensor.HorizontalParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(gravitySensor.LongitudinalParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(gravitySensor.UpwardParameter, AnimatorControllerParameterType.Float);
            if (compression != null)
            {
                controller.AddParameter(compression.InwardParameter, AnimatorControllerParameterType.Float);
                controller.AddParameter(compression.TouchParameter, AnimatorControllerParameterType.Float);
            }

            var gravityTree = new BlendTree
            {
                name = $"SoftDeformPB_{sideName}_GravityResponse",
                blendType = BlendTreeType.FreeformCartesian2D,
                blendParameter = gravitySensor.HorizontalParameter,
                blendParameterY = gravitySensor.LongitudinalParameter,
                useAutomaticThresholds = false
            };

            var generatedAssets = new List<Object> { controller, gravityTree };
            int sampleCount = Mathf.Clamp(setup.responseSamples, 3, 9);
            Motion PostureMotion(GravitySample gravitySample, float upward = 0)
            {
                var targetGravity = new Vector2(
                    gravitySample.Gravity.x * gravitySensor.TargetHorizontalSign,
                    gravitySample.Gravity.y * gravitySensor.TargetLongitudinalSign);
                Vector3 gravityScale = SoftDeformShapeMath.EvaluatePostureScale(
                    restScale,
                    axes,
                    targetGravity.x,
                    targetGravity.y,
                    upward,
                    longitudinalOutwardSign,
                    setup.gravitySupineSpread,
                    SoftDeformCompressionMath.GravityCompression(setup.gravitySupineCompression,
                        setup.minimumCompressionRatio, maximumPhysicalCompression),
                    setup.gravitySideElongation, setup.gravityInvertedElongation);

                BlendTree Shape(float lateral, string label)
                {
                    var shapeTree = new BlendTree
                    {
                        name = $"SoftDeformPB_{sideName}_{gravitySample.Name}{label}_ShapeResponse",
                        blendType = BlendTreeType.FreeformCartesian2D,
                        blendParameter = squishParameter,
                        blendParameterY = stretchParameter,
                        useAutomaticThresholds = false
                    };
                    generatedAssets.Add(shapeTree);

                    for (int y = 0; y < sampleCount; y++)
                    {
                        float stretch = y / (float)(sampleCount - 1);
                        for (int x = 0; x < sampleCount; x++)
                        {
                            float squish = x / (float)(sampleCount - 1);
                            Vector3 scale = SoftDeformShapeMath.EvaluateScale(
                                gravityScale,
                                axes,
                                squish,
                                stretch,
                                setup.squashDepth,
                                setup.stretchDepth,
                                setup.volumeRetention,
                                setup.horizontalShare);

                            scale = SoftDeformCompressionMath.LateralScale(scale, axes, lateral,
                                setup.lateralCompressionDepth, setup.volumeRetention,
                                setup.lateralVerticalShare, setup.minimumCompressionRatio);

                            var clip = CreateShapeClip(
                                sideName,
                                gravitySample.Name + label,
                                x,
                                y,
                                scale,
                                axes,
                                compression == null ? string.Empty : compression.ShapePath);
                            if (compression != null)
                            {
                                // Parallel motion already restores part of this angle. Correct only what remains.
                                float remainingAngle = 1 - SoftDeformParallelMotion.Retention(
                                    setup.horizontalAngleRetention, setup.squishHorizontalAngleRetention, squish);
                                for (int axis = 0; axis < 3; axis++)
                                    SetConstantCurve(clip, "localEulerAnglesRaw." + "xyz"[axis],
                                        compression.CorrectionEuler[axis] * lateral * remainingAngle);
                            }
                            shapeTree.AddChild(clip, new Vector2(squish, stretch));
                            generatedAssets.Add(clip);
                        }
                    }
                    return shapeTree;
                }

                var neutral = Shape(0, string.Empty);
                if (compression == null)
                {
                    return neutral;
                }
                var inwardTree = new BlendTree
                {
                    name = $"SoftDeformPB_{sideName}_{gravitySample.Name}_Inward",
                    blendType = BlendTreeType.Simple1D, blendParameter = compression.InwardParameter,
                    useAutomaticThresholds = false
                };
                inwardTree.AddChild(neutral, compression.StartThreshold);
                inwardTree.AddChild(Shape(0.5f, "_LateralHalf"), (compression.StartThreshold + compression.FullThreshold) / 2);
                inwardTree.AddChild(Shape(1, "_LateralFull"), compression.FullThreshold);
                var touchTree = new BlendTree
                {
                    name = $"SoftDeformPB_{sideName}_{gravitySample.Name}_OuterTouch",
                    blendType = BlendTreeType.Simple1D, blendParameter = compression.TouchParameter,
                    useAutomaticThresholds = false
                };
                touchTree.AddChild(neutral, 0); touchTree.AddChild(inwardTree, 1);
                generatedAssets.Add(inwardTree); generatedAssets.Add(touchTree);
                return touchTree;
            }

            foreach (GravitySample gravitySample in GravitySamples)
            {
                Motion motion = PostureMotion(gravitySample);
                if (gravitySample.Gravity == Vector2.zero && setup.gravityInvertedElongation > 0)
                {
                    // The old 2D center represented both upright and inverted. Only this center
                    // needs the third input; cardinal supine/prone/side motions remain shared.
                    var verticalTree = new BlendTree
                    {
                        name = $"SoftDeformPB_{sideName}_UpwardResponse",
                        blendType = BlendTreeType.Simple1D, blendParameter = gravitySensor.UpwardParameter,
                        useAutomaticThresholds = false
                    };
                    verticalTree.AddChild(motion, GravitySensor.Threshold(0));
                    verticalTree.AddChild(PostureMotion(new GravitySample("Inverted", 0, 0), 1), GravitySensor.Threshold(1));
                    generatedAssets.Add(verticalTree);
                    motion = verticalTree;
                }
                gravityTree.AddChild(motion, gravitySensor.Threshold(gravitySample.Gravity));
            }

            var stateMachine = new AnimatorStateMachine { name = $"SoftDeformPB_{sideName}_StateMachine" };
            var state = stateMachine.AddState("Shape Response");
            state.motion = gravityTree;
            state.writeDefaultValues = false;
            var layer = new AnimatorControllerLayer
            {
                name = $"SoftDeformPB {sideName}",
                defaultWeight = 1.0f,
                stateMachine = stateMachine
            };
            controller.AddLayer(layer);
            generatedAssets.Add(stateMachine);
            generatedAssets.Add(state);

            context.AssetSaver.SaveAssets(generatedAssets);
            return controller;
        }

        private static AnimatorController CreatePostureRootOffsetController(
            BuildContext context,
            SoftDeformPBSetup setup,
            Vector3 restPosition,
            Vector3 outwardInChest,
            float longitudinalOutwardSign,
            GravitySensor gravitySensor,
            string sideName)
        {
            var controller = new AnimatorController { name = $"SoftDeformPB_{sideName}_PostureRootController" };
            controller.AddParameter(gravitySensor.HorizontalParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(gravitySensor.LongitudinalParameter, AnimatorControllerParameterType.Float);

            var gravityTree = new BlendTree
            {
                name = $"SoftDeformPB_{sideName}_PostureRootResponse",
                blendType = BlendTreeType.FreeformCartesian2D,
                blendParameter = gravitySensor.HorizontalParameter,
                blendParameterY = gravitySensor.LongitudinalParameter,
                useAutomaticThresholds = false
            };

            var generatedAssets = new List<Object> { controller, gravityTree };
            float outwardSign = longitudinalOutwardSign < 0.0f ? -1.0f : 1.0f;
            Vector3 normalizedOutward = outwardInChest.sqrMagnitude > 1e-8f
                ? outwardInChest.normalized
                : Vector3.zero;
            foreach (GravitySample gravitySample in GravitySamples)
            {
                float targetLongitudinalGravity =
                    gravitySample.Gravity.y * gravitySensor.TargetLongitudinalSign;
                float supine = Mathf.Clamp01(-targetLongitudinalGravity * outwardSign);
                Vector3 position = restPosition +
                    normalizedOutward * (Mathf.Max(0.0f, setup.gravitySupineRootSpread) * supine);
                var clip = CreatePositionClip(
                    $"SoftDeformPB_{sideName}_{gravitySample.Name}_RootOffset",
                    position);
                gravityTree.AddChild(clip, gravitySensor.Threshold(gravitySample.Gravity));
                generatedAssets.Add(clip);
            }

            var stateMachine = new AnimatorStateMachine
            {
                name = $"SoftDeformPB_{sideName}_PostureRootStateMachine"
            };
            var state = stateMachine.AddState("Posture Root Offset");
            state.motion = gravityTree;
            state.writeDefaultValues = false;
            controller.AddLayer(new AnimatorControllerLayer
            {
                name = $"SoftDeformPB {sideName} Posture Root",
                defaultWeight = 1.0f,
                stateMachine = stateMachine
            });
            generatedAssets.Add(stateMachine);
            generatedAssets.Add(state);

            context.AssetSaver.SaveAssets(generatedAssets);
            return controller;
        }

        private static AnimationClip CreateShapeClip(
            string sideName,
            string gravityName,
            int x,
            int y,
            Vector3 scale,
            SoftDeformAxisMap axes,
            string path)
        {
            var clip = new AnimationClip
            {
                name = $"SoftDeformPB_{sideName}_{gravityName}_Shape_{x}_{y}"
            };
            SetConstantCurve(clip, ScaleProperty(axes.Horizontal), GetAxis(scale, axes.Horizontal), path);
            SetConstantCurve(clip, ScaleProperty(axes.Vertical), GetAxis(scale, axes.Vertical), path);
            SetConstantCurve(clip, ScaleProperty(axes.Longitudinal), GetAxis(scale, axes.Longitudinal), path);
            return clip;
        }

        private static AnimationClip CreatePositionClip(string name, Vector3 position)
        {
            var clip = new AnimationClip { name = name };
            SetConstantCurve(clip, "m_LocalPosition.x", position.x);
            SetConstantCurve(clip, "m_LocalPosition.y", position.y);
            SetConstantCurve(clip, "m_LocalPosition.z", position.z);
            return clip;
        }

        private static void SetConstantCurve(AnimationClip clip, string propertyName, float value, string path = "")
        {
            var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), propertyName);
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0.0f, ClipDuration, value));
        }

        private static string ScaleProperty(int axis)
        {
            return axis == 0 ? "m_LocalScale.x" : axis == 1 ? "m_LocalScale.y" : "m_LocalScale.z";
        }

        private static float GetAxis(Vector3 value, int axis)
        {
            return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
        }

        private static string SanitizeParameterPrefix(string value)
        {
            var chars = value.Trim().Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
            return new string(chars);
        }
    }
}
