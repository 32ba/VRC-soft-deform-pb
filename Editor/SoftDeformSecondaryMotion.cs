using System.Collections.Generic;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace SoftDeformPB.Editor
{
    internal static class SoftDeformSecondaryMotion
    {
        public static void Create(GameObject avatar, SoftDeformPBSetup setup, Transform physical,
            Transform visual, Vector3 direction, string side)
        {
            if (setup.secondaryMotionStrength == 0) return;
            var driver = new GameObject("SoftDeformPB Secondary Driver " + side).transform;
            driver.SetParent(physical, false);
            var tip = new GameObject("SoftDeformPB Secondary Tip " + side).transform;
            tip.SetParent(driver, false);
            tip.localPosition = direction;

            foreach (var parentPhysBone in avatar.GetComponentsInChildren<VRCPhysBone>(true))
            {
                var root = parentPhysBone.rootTransform != null ? parentPhysBone.rootTransform : parentPhysBone.transform;
                if (!driver.IsChildOf(root)) continue;
                var ignored = parentPhysBone.ignoreTransforms != null
                    ? new List<Transform>(parentPhysBone.ignoreTransforms) : new List<Transform>();
                if (!ignored.Contains(driver)) ignored.Add(driver);
                parentPhysBone.ignoreTransforms = ignored;
            }

            var physBone = driver.gameObject.AddComponent<VRCPhysBone>();
            physBone.version = VRCPhysBoneBase.Version.Version_1_1;
            physBone.integrationType = VRCPhysBoneBase.IntegrationType.Simplified;
            physBone.rootTransform = driver;
            physBone.parameter = string.Empty;
            physBone.pull = setup.secondaryMotionPull;
            physBone.pullCurve = AnimationCurve.Constant(0, 1, 1);
            physBone.spring = setup.secondaryMotionSpring;
            physBone.springCurve = AnimationCurve.Constant(0, 1, 1);
            physBone.immobile = 0;
            physBone.gravity = 0;
            physBone.gravityFalloff = 1;
            physBone.limitType = VRCPhysBoneBase.LimitType.Angle;
            physBone.maxAngleX = physBone.maxAngleZ = setup.secondaryMotionMaxAngle;
            physBone.maxAngleXCurve = AnimationCurve.Constant(0, 1, 1);
            physBone.maxAngleZCurve = AnimationCurve.Constant(0, 1, 1);
            physBone.radius = 0;
            physBone.maxStretch = physBone.maxSquish = physBone.stretchMotion = 0;
            physBone.allowCollision = VRCPhysBoneBase.AdvancedBool.False;
            physBone.allowGrabbing = VRCPhysBoneBase.AdvancedBool.False;
            physBone.allowPosing = VRCPhysBoneBase.AdvancedBool.False;
            physBone.isAnimated = false;

            // The driver starts at identity in the moving physical bone's frame.
            // Only this local lag is added, so the primary rotation is applied once.
            // Zero gravity and positive Pull return it to identity in every posture.
            var rotation = visual.gameObject.AddComponent<VRCRotationConstraint>();
            rotation.Sources.Add(new VRCConstraintSource(driver, 1, Vector3.zero, Vector3.zero));
            rotation.RotationAtRest = Vector3.zero;
            rotation.RotationOffset = Vector3.zero;
            rotation.AffectsRotationX = rotation.AffectsRotationY = rotation.AffectsRotationZ = true;
            rotation.GlobalWeight = setup.secondaryMotionStrength;
            rotation.SolveInLocalSpace = true;
            rotation.IsActive = true;
            rotation.Locked = true;
        }
    }
}
