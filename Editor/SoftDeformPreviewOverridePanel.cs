using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SoftDeformPB.Editor
{
    internal sealed class SoftDeformPreviewOverridePanel
    {
        private GameObject cachedAvatar;
        private SoftDeformPreviewOverrides.Option[] options = Array.Empty<SoftDeformPreviewOverrides.Option>();
        private SkinnedMeshRenderer[] renderers = Array.Empty<SkinnedMeshRenderer>();
        private int parameterIndex, rendererIndex, shapeIndex;
        private bool showExtra, showShapes;
        private string shapeFilter = "";
        private string selectedRendererPath = "";

        internal bool Draw(GameObject avatar, SoftDeformPreviewOverrides settings, bool running)
        {
            if (avatar == null) return false;
            if (cachedAvatar != avatar)
            {
                cachedAvatar = avatar;
                options = SoftDeformPreviewOverrides.ReadOptions(avatar);
                renderers = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0).ToArray();
                rendererIndex = Math.Max(0, Array.FindIndex(renderers, r =>
                    AnimationUtility.CalculateTransformPath(r.transform, avatar.transform) == selectedRendererPath));
                shapeIndex = 0;
            }
            settings.Initialize(options);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("確認中の体型・シェイプ", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("左のチェックで値を固定。サイズ用のパラメーターなら衣装も既存の設定に従って切り替わります。",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUI.BeginChangeCheck();
            bool changed = false;
            for (int i = 0; i < settings.parameters.Count; i++)
            {
                var entry = settings.parameters[i];
                var option = options.FirstOrDefault(p => p.Name == entry.name);
                using (new EditorGUILayout.HorizontalScope())
                {
                    entry.enabled = EditorGUILayout.Toggle(entry.enabled, GUILayout.Width(18));
                    using (new EditorGUI.DisabledScope(!entry.enabled || option == null))
                    {
                        string label = option?.Label ?? entry.name + "（見つかりません）";
                        if (option?.Type == AnimatorControllerParameterType.Bool)
                            entry.value = EditorGUILayout.Toggle(label, entry.value > .5f) ? 1 : 0;
                        else if (option?.Type == AnimatorControllerParameterType.Int)
                            entry.value = EditorGUILayout.IntSlider(label, (int)entry.value, 0, 255);
                        else entry.value = EditorGUILayout.Slider(label, entry.value, option?.Radial == true ? 0 : -1, 1);
                    }
                    if (GUILayout.Button(new GUIContent("×", "この固定を解除して一覧から削除"), GUILayout.Width(22)))
                    { settings.parameters.RemoveAt(i--); changed = true; }
                }
            }
            if (settings.parameters.Count == 0)
                EditorGUILayout.LabelField("サイズ用パラメーターが自動検出されない場合は、下から追加できます。", EditorStyles.wordWrappedMiniLabel);

            showExtra = EditorGUILayout.Foldout(showExtra, "ほかの体型・表示パラメーターを追加", true);
            if (showExtra)
            {
                if (options.Length == 0)
                    EditorGUILayout.HelpBox("Expression ParametersとFXに共通するパラメーターがありません。個別のシェイプ固定を使えます。", MessageType.None);
                else using (new EditorGUILayout.HorizontalScope())
                {
                    parameterIndex = EditorGUILayout.Popup(Mathf.Clamp(parameterIndex, 0, options.Length - 1), options.Select(p => p.Label).ToArray());
                    using (new EditorGUI.DisabledScope(settings.parameters.Any(p => p.name == options[parameterIndex].Name)))
                        if (GUILayout.Button("追加", GUILayout.Width(50))) { settings.AddParameter(options[parameterIndex]); changed = true; }
                }
            }

            showShapes = EditorGUILayout.Foldout(showShapes, "個別のシェイプを固定（Animatorより優先）", true);
            if (showShapes)
            {
                EditorGUILayout.LabelField("指定したメッシュとシェイプだけを固定します。衣装を一緒に変えるには上のサイズ用パラメーターを使うか、衣装のシェイプも追加してください。",
                    EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(!running))
                {
                    if (renderers.Length > 0)
                    {
                        int nextRenderer = EditorGUILayout.Popup("メッシュ", Mathf.Clamp(rendererIndex, 0, renderers.Length - 1),
                            renderers.Select(r => AnimationUtility.CalculateTransformPath(r.transform, avatar.transform)).ToArray());
                        if (nextRenderer != rendererIndex) { rendererIndex = nextRenderer; shapeIndex = 0; }
                        var renderer = renderers[rendererIndex];
                        selectedRendererPath = AnimationUtility.CalculateTransformPath(renderer.transform, avatar.transform);
                        shapeFilter = EditorGUILayout.TextField("名前で絞り込む", shapeFilter);
                        var names = Enumerable.Range(0, renderer.sharedMesh.blendShapeCount).Select(renderer.sharedMesh.GetBlendShapeName)
                            .Where(n => n.IndexOf(shapeFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                        if (names.Length > 0)
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                shapeIndex = EditorGUILayout.Popup("シェイプ", Mathf.Clamp(shapeIndex, 0, names.Length - 1), names);
                                string path = selectedRendererPath;
                                using (new EditorGUI.DisabledScope(settings.shapes.Any(s => s.rendererPath == path && s.name == names[shapeIndex])))
                                    if (GUILayout.Button("固定に追加", GUILayout.Width(84)))
                                    {
                                        settings.shapes.Add(new SoftDeformPreviewOverrides.Shape { rendererPath = path, name = names[shapeIndex],
                                            value = renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex(names[shapeIndex])) });
                                        changed = true;
                                    }
                            }
                        }
                        else EditorGUILayout.LabelField("この名前のシェイプはありません。", EditorStyles.miniLabel);
                    }
                    for (int i = 0; i < settings.shapes.Count; i++)
                    {
                        var entry = settings.shapes[i];
                        Transform target = string.IsNullOrEmpty(entry.rendererPath) ? avatar.transform : avatar.transform.Find(entry.rendererPath);
                        var renderer = target != null ? target.GetComponent<SkinnedMeshRenderer>() : null;
                        bool valid = renderer?.sharedMesh != null && renderer.sharedMesh.GetBlendShapeIndex(entry.name) >= 0;
                        EditorGUILayout.LabelField(entry.rendererPath, EditorStyles.miniLabel);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            entry.enabled = EditorGUILayout.Toggle(entry.enabled, GUILayout.Width(18));
                            using (new EditorGUI.DisabledScope(!entry.enabled || !valid))
                                entry.value = EditorGUILayout.Slider(entry.name, entry.value, 0, 100);
                            if (GUILayout.Button(new GUIContent("×", "この固定を解除して一覧から削除"), GUILayout.Width(22)))
                            { settings.shapes.RemoveAt(i--); changed = true; }
                        }
                        if (!valid) EditorGUILayout.HelpBox("このメッシュかシェイプが生成後に見つかりません。現在のメッシュから追加し直してください。", MessageType.Warning);
                    }
                }
                if (!running) EditorGUILayout.LabelField("個別の固定は、揺れの確認を開始してから追加できます。", EditorStyles.wordWrappedMiniLabel);
            }
            changed |= EditorGUI.EndChangeCheck();
            if (settings.parameters.Any(p => p.enabled) || settings.shapes.Any(s => s.enabled))
                EditorGUILayout.LabelField("固定は確認用コピーだけに適用し、元アバターや採用する揺れ設定には保存しません。", EditorStyles.wordWrappedMiniLabel);
            return changed;
        }
    }
}
