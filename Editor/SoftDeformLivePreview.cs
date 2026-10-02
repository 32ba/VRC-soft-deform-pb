using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.platform;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using Object = UnityEngine.Object;

namespace SoftDeformPB.Editor
{
    internal sealed class SoftDeformLivePreview : IDisposable
    {
        internal GameObject Avatar { get; private set; }
        private readonly Object[] ownedAssets;

        private SoftDeformLivePreview(GameObject avatar, Object[] assets)
        {
            Avatar = avatar;
            ownedAssets = assets;
        }

        internal static GameObject CreateTemplate(GameObject source)
        {
            // The inactive parent prevents Awake/Apply-on-Play while taking the authored copy.
            var holder = new GameObject("SoftDeformPB Preview Template") { hideFlags = HideFlags.HideAndDontSave };
            holder.SetActive(false);
            try
            {
                GameObject template = Object.Instantiate(source, holder.transform, true);
                template.SetActive(false);
                // HideAndDontSave also sets NotEditable, disabling SerializedProperty controls.
                template.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
                template.transform.SetParent(null, true);
                RemoveActivators(template);
                return template;
            }
            finally { Object.DestroyImmediate(holder); }
        }

        private static void RemoveActivators(GameObject avatar)
        {
            foreach (MonoBehaviour component in avatar.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component != null && component.GetType().FullName == "nadena.dev.ndmf.runtime.AvatarActivator")
                    Object.DestroyImmediate(component);
            }
        }

        internal static SoftDeformLivePreview Build(GameObject template, SoftDeformPreviewSettings settings)
        {
            if (template == null) throw new InvalidOperationException("確認用の元データがありません。Play Modeを終了して確認を開始してください。");
            var borrowedAssets = new HashSet<Object>(EditorUtility.CollectDependencies(new Object[] { template }));
            GameObject clone = Object.Instantiate(template);
            clone.name = template.name + " · SoftDeformPB Preview";
            clone.hideFlags = HideFlags.DontSave;
            RemoveActivators(clone);
            try
            {
                SoftDeformPBSetup setup = clone.GetComponentInChildren<SoftDeformPBSetup>(true);
                if (setup == null) throw new InvalidOperationException("確認用コピーにSetupがありません。");
                settings.Apply(setup);
                using (new OverrideTemporaryDirectoryScope(null))
                {
                    if (!AvatarProcessor.ProcessAvatar(clone, AmbientPlatform.CurrentPlatform).Successful)
                        throw new InvalidOperationException("アバターの生成に失敗しました。Consoleのビルドエラーを確認してください。");
                }
                // The SDK can bind parameters before our motion graph is connected.
                Animator animator = clone.GetComponent<Animator>();
                RuntimeAnimatorController fx = FindFxController(clone);
                if (animator != null && fx != null) animator.runtimeAnimatorController = fx;
                return new SoftDeformLivePreview(clone, CollectOwnedAssets(clone, borrowedAssets));
            }
            catch
            {
                var assets = CollectOwnedAssets(clone, borrowedAssets);
                Object.DestroyImmediate(clone);
                foreach (Object asset in assets) if (asset != null) Object.DestroyImmediate(asset);
                throw;
            }
        }

        private static Object[] CollectOwnedAssets(GameObject clone, HashSet<Object> borrowedAssets)
        {
            return EditorUtility.CollectDependencies(new Object[] { clone })
                .Where(asset => asset != null && !(asset is GameObject) && !(asset is Component) &&
                                !EditorUtility.IsPersistent(asset) && !borrowedAssets.Contains(asset))
                .Distinct().ToArray();
        }

        internal static RuntimeAnimatorController FindFxController(GameObject avatar)
        {
            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            return descriptor?.baseAnimationLayers?.FirstOrDefault(layer =>
                layer.type == VRCAvatarDescriptor.AnimLayerType.FX && !layer.isDefault).animatorController;
        }

        internal static Action ConnectParameters(GameObject avatar, AnimatorControllerPlayable fx,
            SoftDeformPreviewOverrides overrides = null)
        {
            var definitions = Enumerable.Range(0, fx.GetParameterCount()).Select(fx.GetParameter)
                .Where(p => p.type != AnimatorControllerParameterType.Trigger).ToArray();
            var parameters = definitions.ToDictionary(p => p.name, p => p.type);
            var accesses = parameters.ToDictionary(pair => pair.Key,
                pair => (IAnimParameterAccess)new ParameterAccess(fx, pair.Key, pair.Value));
            var restoreValues = definitions.ToDictionary(p => p.name, p => p.type == AnimatorControllerParameterType.Bool
                ? (p.defaultBool ? 1f : 0f) : p.type == AnimatorControllerParameterType.Int ? p.defaultInt : p.defaultFloat);
            var overriddenNames = new HashSet<string>();
            IAnimParameterAccess Access(string name)
            {
                return accesses.TryGetValue(name, out IAnimParameterAccess access) ? access : null;
            }
            var bones = avatar.GetComponentsInChildren<VRCPhysBoneBase>(true)
                .Where(bone => !string.IsNullOrEmpty(bone.parameter)).ToArray();
            var receivers = avatar.GetComponentsInChildren<ContactReceiver>(true)
                .Where(receiver => !string.IsNullOrEmpty(receiver.parameter)).ToArray();
            void RefreshBindings()
            {
                if (!fx.IsValid()) return;
                foreach (VRCPhysBoneBase bone in bones)
                {
                    if (bone == null) continue;
                    bone.param_IsGrabbed = Access(bone.parameter + VRCPhysBoneBase.PARAM_ISGRABBED) ?? bone.param_IsGrabbed;
                    bone.param_IsPosed = Access(bone.parameter + VRCPhysBoneBase.PARAM_ISPOSED) ?? bone.param_IsPosed;
                    bone.param_Angle = Access(bone.parameter + VRCPhysBoneBase.PARAM_ANGLE) ?? bone.param_Angle;
                    bone.param_Stretch = Access(bone.parameter + VRCPhysBoneBase.PARAM_STRETCH) ?? bone.param_Stretch;
                    bone.param_Squish = Access(bone.parameter + VRCPhysBoneBase.PARAM_SQUISH) ?? bone.param_Squish;
                }
                foreach (ContactReceiver receiver in receivers)
                    if (receiver != null) receiver.paramAccess = Access(receiver.parameter) ?? receiver.paramAccess;
                if (overrides == null) return;
                var nextNames = new HashSet<string>();
                foreach (var parameter in overrides.parameters)
                    if (parameter.enabled && !float.IsNaN(parameter.value) && !float.IsInfinity(parameter.value) &&
                        parameters.TryGetValue(parameter.name, out var type) && type != AnimatorControllerParameterType.Trigger &&
                        accesses.TryGetValue(parameter.name, out IAnimParameterAccess access))
                    {
                        access.floatVal = parameter.value;
                        nextNames.Add(parameter.name);
                    }
                foreach (string name in overriddenNames)
                    if (!nextNames.Contains(name) && restoreValues.TryGetValue(name, out float value))
                        accesses[name].floatVal = value;
                overriddenNames = nextNames;
            }
            var defaults = avatar.GetComponent<VRCAvatarDescriptor>()?.expressionParameters?.parameters;
            if (defaults != null)
                foreach (var parameter in defaults)
                    if (parameter != null && !string.IsNullOrEmpty(parameter.name) &&
                        accesses.TryGetValue(parameter.name, out IAnimParameterAccess access))
                    {
                        access.floatVal = parameter.defaultValue;
                        restoreValues[parameter.name] = parameter.defaultValue;
                    }
            if (parameters.TryGetValue("IsLocal", out AnimatorControllerParameterType localType) &&
                localType == AnimatorControllerParameterType.Bool) fx.SetBool("IsLocal", true);
            if (accesses.TryGetValue("Upright", out IAnimParameterAccess upright)) upright.floatVal = 1;
            if (accesses.TryGetValue("Grounded", out IAnimParameterAccess grounded)) grounded.boolVal = true;
            foreach (var access in accesses) restoreValues[access.Key] = access.Value.floatVal;
            RefreshBindings();
            return RefreshBindings;
        }

        private sealed class ParameterAccess : IAnimParameterAccess
        {
            private readonly AnimatorControllerPlayable controller;
            private readonly int hash;
            private readonly AnimatorControllerParameterType type;
            internal ParameterAccess(AnimatorControllerPlayable controller, string name, AnimatorControllerParameterType type)
            {
                this.controller = controller;
                hash = Animator.StringToHash(name);
                this.type = type;
            }
            public bool boolVal
            {
                get => controller.IsValid() && (type == AnimatorControllerParameterType.Bool
                    ? controller.GetBool(hash) : floatVal > .5f);
                set { if (controller.IsValid()) { if (type == AnimatorControllerParameterType.Bool) controller.SetBool(hash, value); else floatVal = value ? 1 : 0; } }
            }
            public int intVal
            {
                get => controller.IsValid() ? (type == AnimatorControllerParameterType.Int ? controller.GetInteger(hash) : (int)floatVal) : 0;
                set { if (controller.IsValid()) { if (type == AnimatorControllerParameterType.Int) controller.SetInteger(hash, value); else floatVal = value; } }
            }
            public float floatVal
            {
                get => !controller.IsValid() ? 0 : type == AnimatorControllerParameterType.Bool ? (controller.GetBool(hash) ? 1 : 0) :
                    type == AnimatorControllerParameterType.Int ? controller.GetInteger(hash) : controller.GetFloat(hash);
                set
                {
                    if (!controller.IsValid()) return;
                    if (type == AnimatorControllerParameterType.Bool) controller.SetBool(hash, value > .5f);
                    else if (type == AnimatorControllerParameterType.Int) controller.SetInteger(hash, (int)value);
                    else controller.SetFloat(hash, value);
                }
            }
        }

        public void Dispose()
        {
            if (Avatar != null) { Avatar.SetActive(false); Object.DestroyImmediate(Avatar); }
            Avatar = null;
            foreach (Object asset in ownedAssets) if (asset != null) Object.DestroyImmediate(asset);
        }
    }
}
