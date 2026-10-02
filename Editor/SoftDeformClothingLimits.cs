using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace SoftDeformPB.Editor
{
    internal sealed class SoftDeformClothingLimits
    {
        internal float Travel = float.PositiveInfinity;
        internal float Angle = float.PositiveInfinity;
        internal float Separation = float.PositiveInfinity;
        internal readonly List<string> Sources = new List<string>();

        internal float AttachmentBudget => Mathf.Min(Travel, Separation * 0.5f);

        // Resolve and validate every component before changing any build-copy settings.
        internal static Dictionary<SoftDeformPBSetup, SoftDeformClothingLimits> Resolve(
            GameObject avatar, SoftDeformPBSetup[] setups, SoftDeformPBClothingSupport[] supports)
        {
            var result = new Dictionary<SoftDeformPBSetup, SoftDeformClothingLimits>();
            foreach (var support in supports.Where(s => s.enabled))
            {
                foreach (var field in typeof(SoftDeformPBClothingSupport).GetFields())
                {
                    var range = field.GetCustomAttributes(typeof(RangeAttribute), false).OfType<RangeAttribute>().FirstOrDefault();
                    if (range == null) continue;
                    float value = Convert.ToSingle(field.GetValue(support));
                    if (float.IsNaN(value) || float.IsInfinity(value) || value < range.min || value > range.max)
                        throw Error(support, $"{field.Name} must be finite and between {range.min} and {range.max}.");
                }
                var setup = support.targetSetup;
                if (setup == null)
                {
                    if (setups.Length != 1)
                        throw Error(support, "Assign Target Setup when the avatar does not have exactly one Soft Deform PB Setup.");
                    setup = setups[0];
                }
                if (!setups.Contains(setup) || !setup.transform.IsChildOf(avatar.transform))
                    throw Error(support, "Target Setup must belong to this avatar.");
                if (setup.leftBreast == null || setup.rightBreast == null || setup.leftBreast.parent == null ||
                    setup.leftBreast.parent != setup.rightBreast.parent)
                    throw Error(support, "Clothing limits require both selected breast bones to share a chest parent. Without Clothing Support, split-parent rigs remain supported.");
                if (!result.TryGetValue(setup, out var limits)) result.Add(setup, limits = new SoftDeformClothingLimits());
                limits.Travel = Mathf.Min(limits.Travel, support.maximumRootTravel);
                limits.Angle = Mathf.Min(limits.Angle, support.maximumSwingAngle);
                limits.Separation = Mathf.Min(limits.Separation, support.maximumSeparationIncrease);
                limits.Sources.Add(support.name);
            }
            return result;
        }

        internal void Apply(SoftDeformPBSetup setup)
        {
            float requestedTravel = setup.rootMotionMaxOffset + setup.gravitySupineRootSpread;
            float translationShare = requestedTravel > 0 ? Mathf.Min(1, AttachmentBudget / requestedTravel) : 1;
            setup.rootMotionMaxOffset *= translationShare;
            setup.gravitySupineRootSpread *= translationShare;

            float requestedAngle = AngularBudget(setup);
            float rotationShare = requestedAngle > 0 ? Mathf.Min(1, Angle / requestedAngle) : 1;
            setup.motionMaxPitch *= rotationShare;
            setup.motionMaxYaw *= rotationShare;
            setup.rootMotionRotationShare *= rotationShare;
            setup.secondaryMotionStrength *= rotationShare;
            setup.gatheringAngleCorrection *= rotationShare;
        }

        internal static float AngularBudget(SoftDeformPBSetup setup)
        {
            // Pitch + yaw bounds the swing of Polar limits; it is conservative for Angle/Hinge.
            // Axis-filtered root rotations compose three Euler rotations, so reserve all three.
            float rootAxes = setup.rootHorizontalRotation == 1 && setup.rootVerticalRotation == 1 && setup.rootTwistRotation == 1 ? 1 : 3;
            float root = setup.rootMotionMaxOffset > 0 ? rootAxes * setup.rootMotionMaxAngle * setup.rootMotionRotationShare : 0;
            return setup.motionMaxPitch + setup.motionMaxYaw + root +
                   setup.secondaryMotionMaxAngle * setup.secondaryMotionStrength + setup.gatheringAngleCorrection;
        }

        internal static void ConfigurePrimary(VRCPhysBone pb)
        {
            if (pb.limitType == VRCPhysBoneBase.LimitType.None) pb.limitType = VRCPhysBoneBase.LimitType.Polar;
            // A displaced limit center could force an authored rest pose out of a tight clothing limit.
            // This explicit clothing restriction replaces the center only on the build copy.
            pb.limitRotation = Vector3.zero;
            pb.limitRotationXCurve = AnimationCurve.Constant(0, 1, 1);
            pb.limitRotationYCurve = AnimationCurve.Constant(0, 1, 1);
            pb.limitRotationZCurve = AnimationCurve.Constant(0, 1, 1);
        }

        private static InvalidOperationException Error(SoftDeformPBClothingSupport support, string message) =>
            new InvalidOperationException($"[Soft Deform PB] Clothing Support '{support.name}': {message}");
    }
}
