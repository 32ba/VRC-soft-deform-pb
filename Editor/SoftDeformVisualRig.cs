using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace SoftDeformPB.Editor
{
    internal static class SoftDeformVisualRig
    {
        internal sealed class BuildState
        {
            public readonly Dictionary<Transform, Transform> Descendants = new Dictionary<Transform, Transform>();
            public BuildState() { }
        }

        public static Transform Create(BuildContext context, Transform physicalRoot, string sideName)
        {
            string rootName = "SoftDeformPB Visual " + sideName;
            while (physicalRoot.Find(rootName) != null) rootName += "_";
            var visualRoot = new GameObject(rootName).transform;
            visualRoot.SetParent(physicalRoot, false);
            visualRoot.gameObject.layer = physicalRoot.gameObject.layer;
            Complete(context, physicalRoot, visualRoot);
            return visualRoot;
        }

        public static void Complete(BuildContext context, Transform physicalRoot, Transform visualRoot)
        {
            var avatar = context.AvatarRootObject;
            var renderers = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var required = new HashSet<Transform> { physicalRoot };
            var mapping = new Dictionary<Transform, Transform> { { physicalRoot, visualRoot } };
            foreach (var pair in context.GetState<BuildState>().Descendants)
            {
                if (pair.Key == null || pair.Value == null || !pair.Key.IsChildOf(physicalRoot)) continue;
                mapping.Add(pair.Key, pair.Value);
                for (var current = pair.Key; current != physicalRoot; current = current.parent)
                    required.Add(current);
            }
            foreach (var renderer in renderers)
            foreach (var bone in renderer.bones.Concat(new[] { renderer.rootBone }))
            {
                if (bone == null || !bone.IsChildOf(physicalRoot) || bone.IsChildOf(visualRoot)) continue;
                for (var current = bone; current != physicalRoot; current = current.parent)
                    required.Add(current);
            }

            // Include bones added by Merge Armature, while excluding our own proxies.
            // Keep authored transforms and animations on the physical side.
            var sources = physicalRoot.GetComponentsInChildren<Transform>(true)
                .Where(required.Contains).ToArray();

            foreach (var source in sources)
            {
                if (source == physicalRoot) continue;
                if (!mapping.TryGetValue(source, out var visual))
                {
                    visual = new GameObject(source.name).transform;
                    mapping.Add(source, visual);
                    context.GetState<BuildState>().Descendants.Add(source, visual);
                }
                visual.SetParent(mapping[source.parent], false);
                visual.localPosition = source.localPosition;
                visual.localRotation = source.localRotation;
                visual.localScale = source.localScale;
                visual.gameObject.layer = source.gameObject.layer;
                var constraint = visual.GetComponent<VRCParentConstraint>();
                if (constraint == null)
                {
                    constraint = visual.gameObject.AddComponent<VRCParentConstraint>();
                    constraint.Sources.Add(new VRCConstraintSource(source, 1.0f, Vector3.zero, Vector3.zero));
                }
                constraint.PositionAtRest = source.localPosition;
                constraint.RotationAtRest = source.localEulerAngles;
                constraint.AffectsPositionX = constraint.AffectsPositionY = constraint.AffectsPositionZ = true;
                constraint.AffectsRotationX = constraint.AffectsRotationY = constraint.AffectsRotationZ = true;
                constraint.GlobalWeight = 1.0f;
                constraint.SolveInLocalSpace = true;
                constraint.IsActive = true;
                constraint.Locked = true;
            }

            // Every PhysBone that can reach this branch must exclude it, including
            // additional components whose explicit root references the same chain.
            foreach (var physBone in avatar.GetComponentsInChildren<VRCPhysBone>(true))
            {
                var root = physBone.rootTransform != null ? physBone.rootTransform : physBone.transform;
                if (!physicalRoot.IsChildOf(root)) continue;
                var ignored = physBone.ignoreTransforms != null
                    ? new List<Transform>(physBone.ignoreTransforms)
                    : new List<Transform>();
                if (!ignored.Contains(visualRoot)) ignored.Add(visualRoot);
                physBone.ignoreTransforms = ignored;
            }

            foreach (var renderer in renderers)
            {
                var bones = renderer.bones;
                bool changed = false;
                for (int index = 0; index < bones.Length; index++)
                {
                    if (bones[index] == null || !mapping.TryGetValue(bones[index], out var visual)) continue;
                    bones[index] = visual;
                    changed = true;
                }
                if (changed) renderer.bones = bones;
                if (renderer.rootBone != null && mapping.TryGetValue(renderer.rootBone, out var visualRootBone))
                    renderer.rootBone = visualRootBone;
            }
        }
    }

    internal sealed class SoftDeformClothingPass : Pass<SoftDeformClothingPass>
    {
        protected override void Execute(BuildContext context)
        {
            foreach (var side in context.GetState<SoftDeformBuildState>().Sides)
            {
                if (side.Physical == null || side.MotionParent == null || side.Visual == null)
                    throw new System.InvalidOperationException("[Soft Deform PB] A selected bone or generated motion parent was removed while merging clothing.");
                side.Physical.SetParent(side.MotionParent, false);
                SoftDeformVisualRig.Complete(context, side.Physical, side.Visual);
            }
        }
    }

    internal sealed class SoftDeformVisualScalePass : Pass<SoftDeformVisualScalePass>
    {
        protected override void Execute(BuildContext context)
        {
            var descendants = context.GetState<SoftDeformVisualRig.BuildState>().Descendants;
            if (descendants.Count == 0) return;
            var animation = context.Extension<AnimatorServicesContext>();
            foreach (var pair in descendants)
            {
                if (pair.Key == null || pair.Value == null) continue;
                string path = animation.ObjectPathRemapper.GetVirtualPathForObject(pair.Key);
                bool animated = new[] { "x", "y", "z" }.Any(axis => animation.AnimationIndex
                    .GetClipsForBinding(EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalScale." + axis))
                    .Any());
                if (!animated) continue;

                // Parent constraints follow position and rotation only. Preserve authored
                // descendant scale animation without adding scale constraints to static bones.
                var constraint = pair.Value.gameObject.AddComponent<VRCScaleConstraint>();
                constraint.Sources.Add(new VRCConstraintSource(pair.Key, 1.0f, Vector3.zero, Vector3.zero));
                constraint.ScaleAtRest = pair.Key.localScale;
                constraint.ScaleOffset = Vector3.one;
                constraint.AffectsScaleX = constraint.AffectsScaleY = constraint.AffectsScaleZ = true;
                constraint.GlobalWeight = 1.0f;
                constraint.SolveInLocalSpace = true;
                constraint.IsActive = true;
                constraint.Locked = true;
            }
        }
    }
}
