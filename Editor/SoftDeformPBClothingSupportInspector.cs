using System;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace SoftDeformPB.Editor
{
    [CustomEditor(typeof(SoftDeformPBClothingSupport))]
    internal sealed class SoftDeformPBClothingSupportInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox(
                "Limits apply to the body and all bound clothing. Multiple enabled supports use the tightest value for each limit, including outfits that start inactive. Values are fixed at build time. Primary swing limits are centered on the authored pose; root motion, rebound and gathering share the angle budget. This does not simulate fabric or limit mesh width/stretch.",
                MessageType.Info);
            var support = (SoftDeformPBClothingSupport)target;
            if (!support.enabled) return;
            var descriptor = support.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (descriptor == null)
            {
                EditorGUILayout.HelpBox("Place this component inside an avatar with Soft Deform PB Setup.", MessageType.Warning);
                return;
            }
            try
            {
                var setups = descriptor.GetComponentsInChildren<SoftDeformPBSetup>(true);
                var supports = descriptor.GetComponentsInChildren<SoftDeformPBClothingSupport>(true);
                var limits = SoftDeformClothingLimits.Resolve(descriptor.gameObject, setups, supports);
                var setup = support.targetSetup != null ? support.targetSetup : setups[0];
                var value = limits[setup];
                EditorGUILayout.HelpBox(
                    $"Combined limits for {setup.name}: attachment travel at most {value.AttachmentBudget:G3} chest-local units per side; additional separation at most {value.Separation:G3}; rotation budget {value.Angle:G3} degrees. Shared chest parent: {setup.leftBreast.parent.name}.",
                    MessageType.None);
            }
            catch (InvalidOperationException e) { EditorGUILayout.HelpBox(e.Message, MessageType.Error); }
        }
    }
}
