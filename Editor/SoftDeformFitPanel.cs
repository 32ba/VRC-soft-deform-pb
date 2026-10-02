using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;

namespace SoftDeformPB.Editor
{
    internal sealed class SoftDeformFitPanel
    {
        private SoftDeformFitDiagnostics.Report report;
        private bool details;
        private bool handles;
        private Matrix4x4 leftMatrix, rightMatrix;

        internal void Invalidate() { report = null; }
        internal void RestorePreferences() { handles = SessionState.GetBool("SoftDeformPB.FitHandles", false); }

        private void Refresh(SoftDeformPBSetup setup, bool measureSkin = true)
        {
            report = SoftDeformFitDiagnostics.Analyze(setup, measureSkin);
            leftMatrix = setup.leftBreast != null ? setup.leftBreast.localToWorldMatrix : Matrix4x4.identity;
            rightMatrix = setup.rightBreast != null ? setup.rightBreast.localToWorldMatrix : Matrix4x4.identity;
            SceneView.RepaintAll();
        }

        internal void Draw(SoftDeformPBSetup setup)
        {
            EditorGUILayout.LabelField("Fit and diagnose", EditorStyles.boldLabel);
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("Fit the authored avatar in Edit Mode. Use Motion Quality Check for the built avatar's movement.", MessageType.Info);
                return;
            }
            if (report == null) Refresh(setup);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh fit check")) Refresh(setup);
                using (new EditorGUI.DisabledScope(report.Chains == null))
                    if (GUILayout.Button("Frame in Scene"))
                    {
                        var bounds = new Bounds(report.Chains[0].Origin, Vector3.one * report.Chains[0].Reach * 3);
                        bounds.Encapsulate(new Bounds(report.Chains[1].Origin, Vector3.one * report.Chains[1].Reach * 3));
                        SceneView.lastActiveSceneView?.Frame(bounds, false);
                    }
            }
            bool nextHandles = EditorGUILayout.ToggleLeft("Show axes and support handles in Scene", handles);
            if (nextHandles != handles) { handles = nextHandles; SessionState.SetBool("SoftDeformPB.FitHandles", handles); SceneView.RepaintAll(); }
            if (handles && SceneView.lastActiveSceneView != null && !SceneView.lastActiveSceneView.drawGizmos)
                EditorGUILayout.HelpBox("Enable Gizmos in the Scene toolbar to display these handles.", MessageType.Info);
            foreach (var notice in report.Notices.Where(n => n.Severity == MessageType.Error)) EditorGUILayout.HelpBox(notice.Text, notice.Severity);
            if (report.Chains != null)
            {
                for (int i = 0; i < 2; i++)
                {
                    var c = report.Chains[i];
                    EditorGUILayout.LabelField(i == 0 ? "Left" : "Right", $"Reach {c.Reach * 100:G3} cm; path {c.Length * 100:G3} cm; {c.SegmentCount} segments");
                }
                EditorGUILayout.LabelField("Suggested local distances", $"Travel {report.SuggestedTravel:G3}, supine spread {report.SuggestedSpread:G3}");
                using (new EditorGUI.DisabledScope(!report.Valid))
                    if (GUILayout.Button("Apply distances from bone reach"))
                    {
                        SoftDeformFitEditing.ApplyDistances(setup, report); Refresh(setup);
                    }
                EditorGUILayout.HelpBox("The suggestion uses 8% / 6% of the shorter parent-local forward reach and changes only these two distances. Shape sliders may change the surface without changing bone reach. Refresh to measure the current skin shape.", MessageType.None);
                if (handles) EditorGUILayout.HelpBox("Axes: cyan length, green vertical, blue horizontal. Drag the orange sphere's center or radius; drag a magenta generated plane for extra clearance. Both sides share the settings. Existing supports are shown without edit handles.", MessageType.None);
            }
            int warnings = report.Notices.Count(n => n.Severity == MessageType.Warning);
            details = EditorGUILayout.Foldout(details, $"Details: {warnings} warnings, {report.Skins.Count} skinned renderers", true);
            if (!details) return;
            foreach (var notice in report.Notices.Where(n => n.Severity != MessageType.Error))
            {
                EditorGUILayout.HelpBox(notice.Text, notice.Severity);
                if (notice.Context != null && GUILayout.Button("Select " + notice.Context.name)) Selection.activeObject = notice.Context;
            }
            if (report.Chains != null)
                for (int side = 0; side < 2; side++)
                {
                    var c = report.Chains[side];
                    using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField(side == 0 ? "Left PhysBone" : "Right PhysBone", c.PhysBone, c.PhysBone.GetType(), true);
                    if (report.Torso != null)
                        EditorGUILayout.LabelField("Torso support", report.Torso[side].Existing != null ? "Reuse " + report.Torso[side].Existing.name : report.Torso[side].SharedWithLeft ? "Share generated left plane" : "Generate plane");
                    if (report.Opposing != null) EditorGUILayout.LabelField("Opposing sphere radius", $"{report.Opposing[side].Radius * 100:G3} cm (requested {report.Opposing[side].RequestedRadius * 100:G3})");
                }
            foreach (var skin in report.Skins)
            {
                string visibility = skin.Renderer.gameObject.activeInHierarchy && skin.Renderer.enabled ? "" : " [inactive]";
                EditorGUILayout.LabelField(skin.Renderer.name + visibility, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Weighted vertices L/R: {skin.LeftVertices} / {skin.RightVertices}; MA-mapped bones: {skin.MappedBones}");
                if (skin.Measured) EditorGUILayout.LabelField("Current weighted bounds", skin.Bounds.size.ToString("F4") + " in left parent frame");
                if (skin.LeftVertices + skin.RightVertices == 0) EditorGUILayout.LabelField("No weights connected to the selected chest chains.", EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.HelpBox("This is a pre-build bone/weight and sampled collision check. It does not prove surface fit or lack of clothing penetration. Check the final NDMF avatar in motion.", MessageType.None);
        }

        internal void DrawScene(SoftDeformPBSetup setup)
        {
            if (!handles || EditorApplication.isPlayingOrWillChangePlaymode || setup.leftBreast == null || setup.rightBreast == null) return;
            if (report == null || leftMatrix != setup.leftBreast.localToWorldMatrix || rightMatrix != setup.rightBreast.localToWorldMatrix) Refresh(setup, false);
            if (report.Chains == null) return;
            var color = Handles.color;
            var depth = Handles.zTest;
            try
            {
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                var labelStyle = new GUIStyle(EditorStyles.helpBox) { fontSize = 11 };
                labelStyle.normal.textColor = Color.white;
                for (int side = 0; side < 2; side++)
                {
                    var c = report.Chains[side];
                    var direction = c.Root.InverseTransformVector(c.Direction);
                    var axes = SoftDeformShapeMath.InferAxes(c.Root, direction);
                    void Axis(int axis, Color tint, float sign = 1)
                    {
                        Handles.color = tint; var vector = c.Root.TransformDirection(SoftDeformShapeMath.AxisVector(axis)) * sign;
                        Handles.ArrowHandleCap(0, c.Origin, Quaternion.LookRotation(vector), c.Reach * 0.65f, EventType.Repaint);
                    }
                    Axis(axes.Longitudinal, Color.cyan, direction[axes.Longitudinal] < 0 ? -1 : 1);
                    Axis(axes.Vertical, Color.green); Axis(axes.Horizontal, new Color(.25f, .55f, 1));
                    Handles.Label(c.Origin + c.Frame.up * c.Reach * 1.7f, side == 0 ? "Left" : "Right", labelStyle);
                    Handles.color = new Color(1, 1, 1, .45f);
                    for (int sample = 0; sample + 16 < c.Samples.Length; sample += 17)
                    {
                        Handles.DrawLine(c.Samples[sample].Position, c.Samples[sample + 16].Position);
                        Sphere(c.Samples[sample + 16].Position, c.Samples[sample + 16].Radius);
                    }
                    if (report.Torso != null)
                    {
                        var torso = report.Torso[side]; Handles.color = Color.magenta;
                        if (!torso.SharedWithLeft)
                        {
                            if (torso.Existing == null || torso.Existing.shapeType == VRCPhysBoneColliderBase.ShapeType.Plane)
                            {
                                var rotation = Quaternion.FromToRotation(Vector3.up, torso.Direction);
                                var x = rotation * Vector3.right * c.Reach; var z = rotation * Vector3.forward * c.Reach;
                                Handles.DrawPolyLine(torso.Center - x - z, torso.Center + x - z, torso.Center + x + z, torso.Center - x + z, torso.Center - x - z);
                            }
                            else if (torso.Existing.shapeType == VRCPhysBoneColliderBase.ShapeType.Capsule)
                            {
                                var root = torso.Existing.rootTransform != null ? torso.Existing.rootTransform : torso.Existing.transform;
                                var half = torso.Direction * torso.Existing.height * SoftDeformRigGeometry.MaximumScale(root) * .5f;
                                Sphere(torso.Center - half, torso.Radius); Sphere(torso.Center + half, torso.Radius);
                                Handles.DrawLine(torso.Center - half, torso.Center + half);
                            }
                            else Sphere(torso.Center, torso.Radius);
                            if (torso.Existing == null)
                            {
                                EditorGUI.BeginChangeCheck();
                                var point = Handles.Slider(torso.Center, -c.Direction, HandleUtility.GetHandleSize(torso.Center) * .07f, Handles.ConeHandleCap, 0);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    float delta = Vector3.Dot(torso.Center - point, c.Direction) / c.Reach;
                                    SoftDeformFitEditing.SetHandle(setup, SoftDeformFitEditing.Handle.TorsoClearance, setup.torsoSupportClearance + delta);
                                    Refresh(setup, false); GUI.changed = true; return;
                                }
                            }
                        }
                    }
                    if (report.Opposing == null) continue;
                    var opposing = report.Opposing[side]; Handles.color = new Color(1, .6f, .1f);
                    Sphere(opposing.Center, opposing.Radius);
                    EditorGUI.BeginChangeCheck();
                    var center = Handles.Slider(opposing.Center, c.Direction, HandleUtility.GetHandleSize(opposing.Center) * .065f, Handles.CubeHandleCap, 0);
                    if (EditorGUI.EndChangeCheck())
                    {
                        SoftDeformFitEditing.SetHandle(setup, SoftDeformFitEditing.Handle.OpposingCenter, Vector3.Dot(center - c.Origin, c.Direction) / c.Reach);
                        Refresh(setup, false); GUI.changed = true; return;
                    }
                    EditorGUI.BeginChangeCheck();
                    float radius = Handles.RadiusHandle(Quaternion.identity, opposing.Center, opposing.RequestedRadius, true);
                    if (EditorGUI.EndChangeCheck())
                    {
                        SoftDeformFitEditing.SetHandle(setup, SoftDeformFitEditing.Handle.OpposingCoverage, radius / c.Reach);
                        Refresh(setup, false); GUI.changed = true; return;
                    }
                }
            }
            finally { Handles.color = color; Handles.zTest = depth; }
        }

        private static void Sphere(Vector3 center, float radius)
        {
            Handles.DrawWireDisc(center, Vector3.up, radius);
            Handles.DrawWireDisc(center, Vector3.right, radius);
            Handles.DrawWireDisc(center, Vector3.forward, radius);
        }
    }
}
