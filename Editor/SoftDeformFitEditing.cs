using System;
using UnityEditor;
using UnityEngine;

namespace SoftDeformPB.Editor
{
    internal static class SoftDeformFitEditing
    {
        internal enum Handle { TorsoClearance, OpposingCenter, OpposingCoverage }

        internal static void SetHandle(SoftDeformPBSetup setup, Handle handle, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
            Edit(setup, "Adjust Soft Deform PB support", () =>
            {
                if (handle == Handle.TorsoClearance) setup.torsoSupportClearance = Mathf.Clamp(value, 0, 0.5f);
                else if (handle == Handle.OpposingCenter) setup.opposingColliderCenter = Mathf.Clamp(value, 0.05f, 0.95f);
                else setup.opposingColliderCoverage = Mathf.Clamp(value, 0.2f, 1);
            });
        }

        internal static void ApplyDistances(SoftDeformPBSetup setup, SoftDeformFitDiagnostics.Report report)
        {
            if (!report.Valid) throw new InvalidOperationException("Resolve fit errors before applying suggested distances.");
            Edit(setup, "Fit Soft Deform PB motion distances", () =>
            {
                setup.rootMotionMaxOffset = report.SuggestedTravel;
                setup.gravitySupineRootSpread = report.SuggestedSpread;
            });
        }

        private static void Edit(SoftDeformPBSetup setup, string label, Action change)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Fit the authored avatar in Edit Mode.");
            Undo.RecordObject(setup, label);
            change();
            PrefabUtility.RecordPrefabInstancePropertyModifications(setup);
            EditorUtility.SetDirty(setup);
        }
    }
}
