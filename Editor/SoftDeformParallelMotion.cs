using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace SoftDeformPB.Editor
{
    internal static class SoftDeformParallelMotion
    {
        internal sealed class Entry
        {
            public Transform Physical, Reference, Branch;
            public bool CopyFullPose;
            public Transform[] ExtraIgnored;
        }

        internal sealed class BuildState
        {
            public readonly List<Entry> Entries = new List<Entry>();
            public BuildState() { }
        }

        public static void Create(BuildContext context, SoftDeformPBSetup setup, Transform physical,
            Transform visual, SoftDeformAxisMap axes, string prefix, string side)
        {
            if (setup.verticalAngleRetention == 0 && setup.horizontalAngleRetention == 0 &&
                setup.squishVerticalAngleRetention == 0 && setup.squishHorizontalAngleRetention == 0) return;

            // A sibling follows the attachment/root motion, but not the PhysBone's own rotation.
            // Authored rotation curves are mirrored after Merge Armature has resolved their paths.
            var reference = Node("SoftDeformPB Angle Reference " + side, physical.parent);
            reference.localRotation = physical.localRotation;
            var vertical = Node("SoftDeformPB Parallel Vertical " + side, physical);
            var horizontal = Node("SoftDeformPB Parallel Horizontal " + side, vertical);
            visual.SetParent(horizontal, false);
            Rotation(vertical, reference, axes.Horizontal, setup.verticalAngleRetention);
            Rotation(horizontal, reference, axes.Vertical, setup.horizontalAngleRetention);
            var entry = new Entry { Physical = physical, Reference = reference, Branch = vertical };
            context.GetState<BuildState>().Entries.Add(entry);
            ExcludeFromPhysics(context.AvatarRootObject, entry);

            if (setup.squishVerticalAngleRetention == 0 && setup.squishHorizontalAngleRetention == 0) return;
            var controller = new AnimatorController { name = "SoftDeformPB Parallel " + side };
            controller.AddParameter(prefix + "_Squish", AnimatorControllerParameterType.Float);
            var tree = new BlendTree
            {
                name = "Compression angle retention", blendType = BlendTreeType.Simple1D,
                blendParameter = prefix + "_Squish", useAutomaticThresholds = false
            };
            for (int i = 0; i <= 1; i++)
            {
                var clip = new AnimationClip { name = "SoftDeformPB Parallel " + side + " Squish " + i };
                SetWeight(clip, "", Retention(setup.verticalAngleRetention, setup.squishVerticalAngleRetention, i));
                SetWeight(clip, horizontal.name,
                    Retention(setup.horizontalAngleRetention, setup.squishHorizontalAngleRetention, i));
                tree.AddChild(clip, i);
                context.AssetSaver.SaveAsset(clip);
            }
            var machine = new AnimatorStateMachine { name = "Compression angle retention" };
            var state = machine.AddState("Retain angle");
            state.motion = tree;
            state.writeDefaultValues = false;
            machine.defaultState = state;
            controller.AddLayer(new AnimatorControllerLayer
            {
                name = "SoftDeformPB Parallel " + side, defaultWeight = 1, stateMachine = machine
            });
            context.AssetSaver.SaveAsset(tree);
            context.AssetSaver.SaveAsset(state);
            context.AssetSaver.SaveAsset(machine);
            context.AssetSaver.SaveAsset(controller);
            var merge = vertical.gameObject.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Relative;
            merge.matchAvatarWriteDefaults = false;
            merge.deleteAttachedAnimator = false;
        }

        internal static float Retention(float normal, float compressed, float squish)
        {
            return normal + (1 - normal) * compressed * Mathf.Clamp01(squish);
        }

        private static void SetWeight(AnimationClip clip, string path, float value)
        {
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(path, typeof(VRCRotationConstraint), "GlobalWeight"),
                AnimationCurve.Constant(0, 1.0f / 60, value));
        }

        private static Transform Node(string name, Transform parent)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false);
            node.gameObject.layer = parent.gameObject.layer;
            return node;
        }

        private static void Rotation(Transform target, Transform reference, int axis, float weight)
        {
            var constraint = target.gameObject.AddComponent<VRCRotationConstraint>();
            constraint.Sources.Add(new VRCConstraintSource(reference, 1, Vector3.zero, Vector3.zero));
            constraint.AffectsRotationX = axis == 0;
            constraint.AffectsRotationY = axis == 1;
            constraint.AffectsRotationZ = axis == 2;
            constraint.RotationAtRest = Vector3.zero;
            constraint.GlobalWeight = weight;
            constraint.SolveInLocalSpace = false;
            constraint.IsActive = constraint.Locked = true;
        }

        internal static void ExcludeFromPhysics(GameObject avatar, Entry entry)
        {
            foreach (var pb in avatar.GetComponentsInChildren<VRCPhysBone>(true))
            {
                var root = pb.rootTransform != null ? pb.rootTransform : pb.transform;
                var ignored = pb.ignoreTransforms != null ? new List<Transform>(pb.ignoreTransforms) : new List<Transform>();
                foreach (var node in new[] { entry.Branch, entry.Reference }.Concat(entry.ExtraIgnored ?? System.Array.Empty<Transform>()))
                    if (node.IsChildOf(root) && !ignored.Contains(node)) ignored.Add(node);
                pb.ignoreTransforms = ignored;
            }
        }
    }

    internal sealed class SoftDeformParallelReferencePass : Pass<SoftDeformParallelReferencePass>
    {
        protected override void Execute(BuildContext context)
        {
            var animation = context.Extension<AnimatorServicesContext>();
            foreach (var entry in context.GetState<SoftDeformParallelMotion.BuildState>().Entries)
            {
                SoftDeformParallelMotion.ExcludeFromPhysics(context.AvatarRootObject, entry);
                string source = animation.ObjectPathRemapper.GetVirtualPathForObject(entry.Physical);
                string destination = animation.ObjectPathRemapper.GetVirtualPathForObject(entry.Reference);
                foreach (var clip in animation.AnimationIndex.GetClipsForObjectPath(source).ToArray())
                foreach (var binding in clip.GetFloatCurveBindings().ToArray())
                {
                    if (binding.path != source || binding.type != typeof(Transform) ||
                        !(binding.propertyName.StartsWith("m_LocalRotation.") ||
                          binding.propertyName.StartsWith("localEulerAngles") ||
                          (entry.CopyFullPose && (binding.propertyName.StartsWith("m_LocalPosition.") ||
                                                  binding.propertyName.StartsWith("m_LocalScale."))))) continue;
                    var copy = binding;
                    copy.path = destination;
                    clip.SetFloatCurve(copy, clip.GetFloatCurve(binding));
                }
                foreach (var controller in animation.ControllerContext.GetAllControllers())
                foreach (var layer in controller.Layers)
                {
                    var mask = layer.AvatarMask;
                    if (mask != null && mask.Elements.TryGetValue(source, out float weight))
                        mask.Elements = mask.Elements.SetItem(destination, weight);
                }
            }
        }
    }
}
