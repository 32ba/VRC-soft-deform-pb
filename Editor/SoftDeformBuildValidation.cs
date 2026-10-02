using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace SoftDeformPB.Editor
{
    internal sealed class SoftDeformBuildState
    {
        internal sealed class Side
        {
            public Transform Physical;
            public Transform Visual;
            public Transform OriginalParent;
            public Transform MotionParent;
            public VRCPhysBone PhysBone;
            public string Prefix;
            public HashSet<string> ClipNames;
        }
        public readonly List<Side> Sides = new List<Side>();
        public SoftDeformBuildState() { }
    }

    internal sealed class SoftDeformAnimationConflictPass : Pass<SoftDeformAnimationConflictPass>
    {
        protected override void Execute(BuildContext context)
        {
            var animation = context.Extension<AnimatorServicesContext>();
            foreach (var side in context.GetState<SoftDeformBuildState>().Sides)
            {
                string path = animation.ObjectPathRemapper.GetVirtualPathForObject(side.Visual);
                string physicalPath = animation.ObjectPathRemapper.GetVirtualPathForObject(side.Physical);
                foreach (string axis in new[] { "x", "y", "z" })
                {
                    if (animation.AnimationIndex.GetClipsForBinding(EditorCurveBinding.FloatCurve(
                        physicalPath, typeof(Transform), "m_LocalScale." + axis)).Any(c => side.ClipNames.Contains(c.Name)))
                        throw new InvalidOperationException($"[Soft Deform PB] A generated shape curve targets physical bone '{physicalPath}' instead of its visual branch.");
                    var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalScale." + axis);
                    foreach (var clip in animation.AnimationIndex.GetClipsForBinding(binding))
                    {
                        if (side.ClipNames.Contains(clip.Name)) continue;
                        string controllers = string.Join(", ", animation.ControllerContext.GetAllControllers()
                            .Where(c => c.AllReachableNodes().Contains(clip)).Select(c => c.Name));
                        throw new InvalidOperationException($"[Soft Deform PB] Scale animation conflict at '{path}', '{binding.propertyName}': controller '{controllers}', clip '{clip.Name}'. Keep authored scale animation on the original physical bones.");
                    }
                }
            }
        }
    }

    internal sealed class SoftDeformFinalValidationPass : Pass<SoftDeformFinalValidationPass>
    {
        internal static void ValidateConstraintOwnership(Transform physical, IEnumerable<VRCConstraintBase> constraints)
        {
            if (constraints.Any(c => (c.TargetTransform != null ? c.TargetTransform : c.transform) == physical))
                throw new InvalidOperationException($"[Soft Deform PB] A Constraint also drives the primary PhysBone transform '{physical.name}'. Move the constraint to a separate parent.");
        }

        protected override void Execute(BuildContext context)
        {
            var sides = context.GetState<SoftDeformBuildState>().Sides;
            if (sides.Count == 0) return;
            var animation = context.Extension<AnimatorServicesContext>();
            var controllers = animation.ControllerContext.GetAllControllers().ToArray();
            var constraints = context.AvatarRootObject.GetComponentsInChildren<VRCConstraintBase>(true);
            var physBones = context.AvatarRootObject.GetComponentsInChildren<VRCPhysBone>(true);
            foreach (var side in sides)
            {
                if (side.Physical == null || side.Visual == null || side.PhysBone == null)
                    throw new InvalidOperationException("[Soft Deform PB] A generated visual or physical root was removed before final validation.");
                foreach (string suffix in new[] { "_Squish", "_Stretch" })
                {
                    string parameter = side.PhysBone.parameter + suffix;
                    if (!controllers.Any(c => c.Parameters.TryGetValue(parameter, out var p) && p.type == AnimatorControllerParameterType.Float))
                        throw new InvalidOperationException($"[Soft Deform PB] Missing Float parameter '{parameter}' in the final Animator output.");
                }
                string visualPath = animation.ObjectPathRemapper.GetVirtualPathForObject(side.Visual);
                foreach (string axis in new[] { "x", "y", "z" })
                    if (!animation.AnimationIndex.GetClipsForBinding(EditorCurveBinding.FloatCurve(
                        visualPath, typeof(Transform), "m_LocalScale." + axis)).Any())
                        throw new InvalidOperationException($"[Soft Deform PB] Missing final visual scale curve '{visualPath}/m_LocalScale.{axis}'.");
                ValidateConstraintOwnership(side.Physical, constraints);
                foreach (var physBone in physBones)
                {
                    var root = physBone.rootTransform != null ? physBone.rootTransform : physBone.transform;
                    if (side.Visual.IsChildOf(root) && !SoftDeformRigGeometry.Ignored(physBone, side.Visual))
                        throw new InvalidOperationException($"[Soft Deform PB] PhysBone '{physBone.name}' still reaches the visual branch '{side.Visual.name}'.");
                }
            }
        }
    }
}
