using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace SoftDeformPB.Editor
{
    // These values belong to the check window, never to the avatar or the upload.
    [Serializable]
    internal sealed class SoftDeformPreviewOverrides
    {
        [Serializable]
        internal sealed class Parameter
        {
            public string name;
            public bool enabled;
            public float value;
        }

        [Serializable]
        internal sealed class Shape
        {
            public string rendererPath;
            public string name;
            public bool enabled = true;
            public float value;
        }

        public List<Parameter> parameters = new List<Parameter>();
        public List<Shape> shapes = new List<Shape>();
        public bool initialized;

        internal sealed class Option
        {
            internal string Name, Label;
            internal AnimatorControllerParameterType Type;
            internal float DefaultValue;
            internal bool Radial, DrivesShapes;
        }

        internal void Initialize(Option[] options)
        {
            if (initialized || options.Length == 0) return;
            initialized = true;
            foreach (Option option in options.Where(p => p.DrivesShapes))
                AddParameter(option);
        }

        internal void AddParameter(Option option)
        {
            if (parameters.Any(p => p.name == option.Name)) return;
            parameters.Add(new Parameter { name = option.Name, value = option.DefaultValue });
        }

        internal static Option[] ReadOptions(GameObject avatar)
        {
            if (avatar == null) return Array.Empty<Option>();
            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            var expressions = descriptor?.expressionParameters?.parameters;
            RuntimeAnimatorController fx = SoftDeformLivePreview.FindFxController(avatar);
            while (fx is AnimatorOverrideController overridden) fx = overridden.runtimeAnimatorController;
            if (expressions == null || !(fx is AnimatorController controller)) return Array.Empty<Option>();
            var types = controller.parameters.GroupBy(p => p.name).ToDictionary(g => g.Key, g => g.First().type);
            var shapeParameters = FindShapeParameters(controller);
            var labels = new Dictionary<string, (string label, bool radial)>();
            var seen = new HashSet<VRCExpressionsMenu>();
            void ReadMenu(VRCExpressionsMenu menu)
            {
                if (menu == null || !seen.Add(menu)) return;
                foreach (var control in menu.controls)
                {
                    if (control == null) continue;
                    if (!string.IsNullOrEmpty(control.parameter?.name) && !labels.ContainsKey(control.parameter.name))
                        labels.Add(control.parameter.name, (control.name, false));
                    if (control.subParameters != null)
                        foreach (var parameter in control.subParameters)
                            if (!string.IsNullOrEmpty(parameter?.name) && !labels.ContainsKey(parameter.name))
                                labels.Add(parameter.name, (control.name, control.type == VRCExpressionsMenu.Control.ControlType.RadialPuppet));
                    ReadMenu(control.subMenu);
                }
            }
            ReadMenu(descriptor.expressionsMenu);
            return expressions.Where(p => p != null && !string.IsNullOrEmpty(p.name) &&
                                         types.ContainsKey(p.name) && types[p.name] != AnimatorControllerParameterType.Trigger)
                .GroupBy(p => p.name).Select(g => g.First()).Select(p =>
                {
                    labels.TryGetValue(p.name, out var menu);
                    return new Option { Name = p.name, Label = string.IsNullOrEmpty(menu.label) || menu.label == p.name
                        ? p.name : menu.label + " (" + p.name + ")", Type = types[p.name], DefaultValue = p.defaultValue,
                        Radial = menu.radial, DrivesShapes = shapeParameters.Contains(p.name) };
                }).OrderByDescending(p => p.DrivesShapes).ThenBy(p => p.Name).ToArray();
        }

        private static HashSet<string> FindShapeParameters(AnimatorController controller)
        {
            var result = new HashSet<string>();
            var visited = new HashSet<Motion>();
            bool HasShapes(Motion motion)
            {
                if (motion is AnimationClip clip)
                    return AnimationUtility.GetCurveBindings(clip).Any(b => b.type == typeof(SkinnedMeshRenderer) &&
                                                                           b.propertyName.StartsWith("blendShape.", StringComparison.Ordinal));
                if (!(motion is BlendTree tree) || !visited.Add(tree)) return false;
                bool hasShapes = false;
                foreach (ChildMotion child in tree.children)
                    if (HasShapes(child.motion))
                    {
                        hasShapes = true;
                        if (tree.blendType == BlendTreeType.Direct) result.Add(child.directBlendParameter);
                    }
                if (hasShapes && tree.blendType != BlendTreeType.Direct)
                {
                    result.Add(tree.blendParameter);
                    if (tree.blendType != BlendTreeType.Simple1D) result.Add(tree.blendParameterY);
                }
                return hasShapes;
            }
            void ReadMachine(AnimatorStateMachine machine)
            {
                foreach (var child in machine.states)
                {
                    visited.Clear();
                    if (HasShapes(child.state.motion) && child.state.timeParameterActive)
                        result.Add(child.state.timeParameter);
                }
                foreach (var child in machine.stateMachines) ReadMachine(child.stateMachine);
            }
            foreach (var layer in controller.layers) ReadMachine(layer.stateMachine);
            return result;
        }
    }
}
