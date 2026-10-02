using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace SoftDeformPB.Editor
{
    [CustomEditor(typeof(SoftDeformPBSetup))]
    internal sealed class SoftDeformPBInspector : UnityEditor.Editor
    {
        private readonly SoftDeformFitPanel fit = new SoftDeformFitPanel();
        private readonly SoftDeformTuningGuide guide = new SoftDeformTuningGuide();
        private readonly SoftDeformEasyPanel easy = new SoftDeformEasyPanel();
        private VRCPhysBone authoredLeft, authoredRight;
        private bool details;
        private bool mainMotion = true, rootMotion, secondaryMotion, stretch, posture;
        private bool retention, compression, collision, generation, fitting;
        private void OnEnable() { fit.RestorePreferences(); guide.RestorePreferences(); Undo.undoRedoPerformed += Changed; EditorApplication.hierarchyChanged += Changed; }
        private void OnDisable() { easy.Release(); Undo.undoRedoPerformed -= Changed; EditorApplication.hierarchyChanged -= Changed; }
        private void Changed() { fit.Invalidate(); Repaint(); SceneView.RepaintAll(); }
        private void OnSceneGUI() { fit.DrawScene((SoftDeformPBSetup)target); }

        public override void OnInspectorGUI()
        {
            float previousWidth = EditorGUIUtility.labelWidth;
            try
            {
                EditorGUIUtility.labelWidth = Mathf.Clamp(EditorGUIUtility.currentViewWidth * .44f, 160, 230);
                DrawInspector();
            }
            finally { EditorGUIUtility.labelWidth = previousWidth; }
        }

        private void DrawInspector()
        {
            var setup = (SoftDeformPBSetup)target;
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            Field("leftBreast");
            Field("rightBreast");
            authoredLeft = FindPhysBone(setup, setup.leftBreast);
            authoredRight = FindPhysBone(setup, setup.rightBreast);
            if (easy.Draw(serializedObject, authoredLeft, authoredRight)) fit.Invalidate();
            EditorGUILayout.Space();
            if (GUILayout.Button("動かして確認", GUILayout.Height(25)))
            {
                serializedObject.ApplyModifiedProperties();
                SoftDeformPBMotionCheckWindow.OpenFor(setup);
            }
            EditorGUILayout.LabelField("『動かして確認』では、Play Modeを続けたままパッドで調整し、変更前と比較できます。", EditorStyles.wordWrappedMiniLabel);
            details = EditorGUILayout.Foldout(details, "詳細設定を開く", true);
            if (details) DrawDetails(setup);
            if (serializedObject.ApplyModifiedProperties()) fit.Invalidate();
            DrawValidation(setup);
        }

        private void DrawDetails(SoftDeformPBSetup setup)
        {
            guide.DrawNavigation(FocusGroup, () =>
            {
                serializedObject.ApplyModifiedProperties();
                SoftDeformPBMotionCheckWindow.OpenFor(setup);
            });
            EditorGUILayout.HelpBox(
                "『動かして確認』のパッドで調整すると、Play Modeを続けたまま確認用アバターを更新できます。『採用して終了』で元のSetupへ反映します。", MessageType.Info);

            if (Section(ref mainMotion, "主な揺れ (Main motion)"))
            {
                Field("preserveExistingMotion", "Keep authored motion");
                bool preserve = serializedObject.FindProperty("preserveExistingMotion").boolValue;
                if (preserve)
                {
                    EditorGUILayout.HelpBox(
                        "変える力だけ左のチェックをONにします。ONは左右の基礎値を上書き、OFFは元のPhysBoneの値を使用。左右それぞれのカーブと計算方式は維持します。", MessageType.None);
                    Force("motionPull", SoftDeformMotionForceOverrides.Pull, "Return strength (Pull)",
                        "Higher values pull the chain toward its rest pose more strongly.");
                    Force("motionSpring", SoftDeformMotionForceOverrides.Spring, "Bounce (Spring / Momentum)",
                        "Controls wobble. Uses Spring in Simplified integration and Momentum in Advanced integration.");
                    bool advanced = (authoredLeft != null && authoredLeft.integrationType == VRCPhysBoneBase.IntegrationType.Advanced) ||
                                    (authoredRight != null && authoredRight.integrationType == VRCPhysBoneBase.IntegrationType.Advanced);
                    Force("motionStiffness", SoftDeformMotionForceOverrides.Stiffness, "Rest-pose stiffness",
                        "Additional rest-pose stiffness for Advanced integration. Simplified PhysBones do not use it.", advanced);
                    Force("motionImmobile", SoftDeformMotionForceOverrides.Immobile, "Motion resistance (Immobile)",
                        "Higher values reduce movement. Each side retains its authored Immobile Type.");
                    Force("motionGravity", SoftDeformMotionForceOverrides.Gravity, "Gravity",
                        "Strength of gravity on the chain. The per-side gravity curve stays intact.");
                    Force("motionGravityFalloff", SoftDeformMotionForceOverrides.GravityFalloff, "Gravity falloff at rest",
                        "Reduces gravity as the chain approaches its rest direction. One allows it to return to rest.");
                    if (!advanced)
                        EditorGUILayout.HelpBox("StiffnessはAdvancedのPhysBoneで作用します。Simplifiedでは使われません。", MessageType.None);
                    EditorGUILayout.LabelField($"元の計算方式: 左 {Integration(authoredLeft)} / 右 {Integration(authoredRight)}", EditorStyles.wordWrappedMiniLabel);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "左右をSimplifiedで設定し直し、力と角度のカーブも置き換えます。一つずつ比較するときは『元の揺れを維持して調整』をONにしてください。", MessageType.None);
                    Field("motionPull", "Return strength (Pull)");
                    Field("motionSpring", "Bounce (Spring)");
                    Field("motionImmobile", "Motion resistance (Immobile)");
                    Field("motionGravity", "Gravity");
                    Field("motionGravityFalloff", "Gravity falloff at rest");
                }
                bool gravitySelected = !preserve || (serializedObject.FindProperty("motionForceOverrides").intValue & (int)SoftDeformMotionForceOverrides.Gravity) != 0;
                bool zeroGravity = gravitySelected ? serializedObject.FindProperty("motionGravity").floatValue == 0 :
                    authoredLeft != null && authoredRight != null && authoredLeft.gravity == 0 && authoredRight.gravity == 0;
                if (zeroGravity)
                    EditorGUILayout.HelpBox("重力が0なので、『静止姿勢で重力を弱める』を変えても重力の作用は変わりません。", MessageType.None);
                bool clothingLimits = setup.GetComponentsInChildren<SoftDeformPBClothingSupport>(true)
                    .Any(support => support.enabled && (support.targetSetup == null || support.targetSetup == setup));
                using (new EditorGUI.DisabledScope(preserve && !clothingLimits))
                {
                    Field("motionMaxPitch", "Vertical swing limit");
                    Field("motionMaxYaw", "Horizontal swing limit");
                }
                if (preserve)
                    EditorGUILayout.HelpBox(clothingLimits
                        ? "Clothing Supportが、この角度と衣装の共通予算から角度制限を生成します。衣装側の上限で狭まる場合があります。"
                        : "角度は元のPhysBoneの制限を使うため、この2項目は効きません。衣装と共通の上限を設けるときはClothing Supportを追加します。", MessageType.None);
            }

            if (Section(ref rootMotion, "付け根の移動 (Attachment movement)"))
            {
                Field("rootMotionMaxOffset", "Maximum travel");
                using (new EditorGUI.DisabledScope(serializedObject.FindProperty("rootMotionMaxOffset").floatValue <= 0))
                {
                    Field("rootVerticalTranslation", "Vertical travel share");
                    Field("rootHorizontalTranslation", "Horizontal travel share");
                    Field("rootLongitudinalTranslation", "Forward travel share");
                    Field("rootMotionPull", "Return strength (Pull)");
                    Field("rootMotionSpring", "Bounce (Spring)");
                    Field("rootMotionImmobile", "Motion resistance (Immobile)");
                    Field("rootMotionGravity", "Gravity");
                    Field("rootMotionGravityFalloff", "Gravity falloff at rest");
                    Field("rootMotionRotationShare", "Rotation contribution");
                    Field("rootMotionMaxAngle", "Driver angle limit");
                    Field("rootVerticalRotation", "Vertical rotation share");
                    Field("rootHorizontalRotation", "Horizontal rotation share");
                    Field("rootTwistRotation", "Twist share");
                }
                EditorGUILayout.HelpBox(
                    serializedObject.FindProperty("rootMotionMaxOffset").floatValue <= 0
                        ? "移動上限が0なので、配下の追加の移動・回転は生成されません。仰向けでの付け根の移動は別設定です。"
                        : "距離は胸の共通親のローカル単位です。方向別の量は振幅だけを変え、Pull・Springの応答は各方向で共通です。", MessageType.None);
            }

            if (Section(ref secondaryMotion, "後から重なる余韻 (Delayed rebound)"))
            {
                Field("secondaryMotionStrength", "Rebound contribution");
                using (new EditorGUI.DisabledScope(serializedObject.FindProperty("secondaryMotionStrength").floatValue <= 0))
                {
                    Field("secondaryMotionPull", "Return strength (Pull)", "Higher values return the helper to rest more strongly. This is a force, not a delay in seconds.");
                    Field("secondaryMotionSpring", "Bounce (Spring)");
                    Field("secondaryMotionMaxAngle", "Helper angle limit");
                }
                EditorGUILayout.HelpBox(
                    serializedObject.FindProperty("secondaryMotionStrength").floatValue <= 0
                        ? "余韻を混ぜる量が0なので、配下の追加揺れは生成されません。主な揺れのSpringによる反発は残ります。"
                        : "身体と衣装の描画側に、遅れて重なる回転を追加します。混ぜる量は大きさ、Pull・Springは応答を調整します。伸縮と衝突は追加しません。", MessageType.None);
            }

            if (Section(ref stretch, "伸び縮みと厚み (Stretch and squish)"))
            {
                Field("maxStretch", "Maximum stretch");
                Field("maxSquish", "Maximum squish");
                EditorGUILayout.LabelField($"設定した長さ範囲: 元100% → 最短 {1 - serializedObject.FindProperty("maxSquish").floatValue:P0} / 最長 {1 + serializedObject.FindProperty("maxStretch").floatValue:P0}", EditorStyles.wordWrappedMiniLabel);
                Field("stretchMotion", "Motion-driven length change");
                if (serializedObject.FindProperty("stretchMotion").floatValue <= 0)
                    EditorGUILayout.HelpBox("動きによる伸び縮みが0なので、掴み・衝突による伸縮だけが残ります。", MessageType.None);
                else if (serializedObject.FindProperty("maxStretch").floatValue <= 0 && serializedObject.FindProperty("maxSquish").floatValue <= 0)
                    EditorGUILayout.HelpBox("伸び・縮みの上限がどちらも0なので、動きによる伸縮の余地がありません。", MessageType.None);
                Field("squashDepth", "Squish shape compensation");
                Field("stretchDepth", "Stretch shape compensation");
                Field("volumeRetention", "Volume retention");
                Field("horizontalShare", "Horizontal compensation share");
                EditorGUILayout.HelpBox(
                    "長さの上限は元の長さに対する割合です。厚み・細さ補正は、身体と衣装で共通の描画用ボーンの横幅・高さを変えます。", MessageType.None);
                guide.DrawShapeExample(serializedObject);
            }

            if (Section(ref posture, "寝たとき・逆さの形 (Posture response)"))
            {
                Field("gravitySupineSpread", "Supine widening");
                Field("gravitySupineCompression", "Supine shortening");
                Field("gravitySupineRootSpread", "Supine outward offset");
                Field("gravitySideElongation", "Side-lying elongation");
                Field("gravityInvertedElongation", "Inverted elongation");
                EditorGUILayout.HelpBox("立ち姿勢では姿勢補正は中立です。形の補正と、PhysBoneの重力による動きは別です。うつ伏せで床から押される変形は追加しません。", MessageType.None);
            }
            if (Section(ref retention, "見た目の傾きを保つ (Angle retention)"))
            {
                Field("verticalAngleRetention", "Vertical retention");
                Field("horizontalAngleRetention", "Horizontal retention");
                Field("squishVerticalAngleRetention", "Extra vertical when squished");
                Field("squishHorizontalAngleRetention", "Extra horizontal when squished");
                EditorGUILayout.HelpBox("付け根が移動しても、描画側の元の傾きを残す量です。0はPhysBoneの回転に追従、1は対象の回転を打ち消します。位置は固定しません。", MessageType.None);
            }
            if (Section(ref compression, "横から押したときの形 (Directional compression)"))
            {
                Field("lateralCompressionDepth", "Lateral narrowing");
                Field("lateralVerticalShare", "Upward compensation share");
                Field("lateralResponseDistance", "Inward travel for full response");
                Field("gatheringAngleCorrection", "Gathering angle correction");
                Field("minimumCompressionRatio", "Minimum compression ratio");
                EditorGUILayout.HelpBox(
                    "外側の手・指の接触と内向きのボーン移動の両方で反応します。『これ以上つぶさない比率』を上げると、縮みの上限や仰向けの短縮もビルド時に制限されます。", MessageType.None);
            }
            if (Section(ref collision, "胴体・反対側の支え (Collision support)"))
            {
                Field("reuseTorsoColliders", "Reuse torso colliders");
                Field("torsoSupportClearance", "Torso rest clearance");
                Field("opposingColliderCoverage", "Opposing support coverage");
                Field("opposingColliderCenter", "Opposing support position");
            }
            if (Section(ref fitting, "ボーンと衣装の適合を確認 (Fit and diagnose)")) fit.Draw(setup);
            if (Section(ref generation, "生成と試験用初期値"))
            {
                Field("responseSamples", "Shape response samples");
                Field("parameterPrefix");
                EditorGUILayout.HelpBox("主な揺れと付け根の力を試験用の値に戻します。元の揺れの維持と力の選択も解除します。ボーン、形、余韻、方向別の量、衝突設定は維持します。", MessageType.None);
                if (GUILayout.Button("重力試験用の初期値に戻す"))
                {
                    serializedObject.ApplyModifiedProperties();
                    Undo.RecordObject(setup, "Use gravity trial defaults");
                    setup.UseGravityTrialDefaults();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(setup);
                    EditorUtility.SetDirty(setup);
                    serializedObject.Update();
                    fit.Invalidate();
                }
            }
        }

        private void DrawValidation(SoftDeformPBSetup setup)
        {
            if (setup.leftBreast == null || setup.rightBreast == null)
            {
                EditorGUILayout.HelpBox("ビルド前に左右の胸ボーンを指定してください。", MessageType.Error);
            }
            else if (setup.leftBreast == setup.rightBreast)
            {
                EditorGUILayout.HelpBox("左右には別のボーンを指定してください。", MessageType.Error);
            }
            else if (authoredLeft == null || authoredRight == null)
            {
                EditorGUILayout.HelpBox("指定したボーンの既存PhysBoneを特定できません。『ボーンと衣装の適合を確認』で割り当てを確認してください。", MessageType.Error);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "指定するボーンには既存のPhysBoneが必要です。付け根やドライバーの追加はビルドコピーへ行います。",
                    MessageType.None);
            }
        }

        private void FocusGroup(int index)
        {
            mainMotion = index == 0; rootMotion = index == 1; secondaryMotion = index == 2;
            stretch = index == 3; posture = index == 4; retention = index == 5;
            compression = index == 6; collision = index == 7; fitting = generation = false;
        }

        private static bool Section(ref bool expanded, string title)
        {
            EditorGUILayout.Space();
            expanded = EditorGUILayout.Foldout(expanded, title, true);
            return expanded;
        }

        private void Field(string name, string label = null, string tooltip = null)
        {
            var property = serializedObject.FindProperty(name);
            var field = typeof(SoftDeformPBSetup).GetField(name);
            var range = (RangeAttribute)Attribute.GetCustomAttribute(field, typeof(RangeAttribute));
            var hint = (TooltipAttribute)Attribute.GetCustomAttribute(field, typeof(TooltipAttribute));
            if (SoftDeformTuningGuide.TryGet(name, out var entry))
            {
                label = entry.Label;
                tooltip = entry.Effect + "\n" + (tooltip ?? hint?.tooltip);
            }
            var content = new GUIContent(label ?? property.displayName, tooltip ?? hint?.tooltip);
            if (range != null && property.propertyType == SerializedPropertyType.Float)
                EditorGUILayout.Slider(property, range.min, range.max, content);
            else if (range != null && property.propertyType == SerializedPropertyType.Integer)
                EditorGUILayout.IntSlider(property, (int)range.min, (int)range.max, content);
            else if (property.propertyType == SerializedPropertyType.Boolean)
            {
                var rect = EditorGUILayout.GetControlRect();
                EditorGUI.BeginProperty(rect, content, property);
                EditorGUI.BeginChangeCheck();
                bool value = EditorGUI.Toggle(rect, content, property.boolValue);
                if (EditorGUI.EndChangeCheck()) property.boolValue = value;
                EditorGUI.EndProperty();
            }
            else EditorGUILayout.PropertyField(property, content);
            guide.DrawHint(name);
        }

        private void Force(string name, SoftDeformMotionForceOverrides flag, string label, string tooltip, bool applicable = true)
        {
            var mask = serializedObject.FindProperty("motionForceOverrides");
            if (SoftDeformTuningGuide.TryGet(name, out var entry))
            {
                label = entry.Label;
                tooltip = entry.Effect + "\n" + tooltip;
            }
            var rect = EditorGUILayout.GetControlRect();
            var toggleRect = rect;
            toggleRect.width = 18;
            EditorGUI.BeginProperty(toggleRect, new GUIContent("この力を上書き: " + label, tooltip), mask);
            bool selected = (mask.intValue & (int)flag) != 0;
            EditorGUI.BeginChangeCheck();
            bool next = EditorGUI.Toggle(toggleRect, selected);
            if (EditorGUI.EndChangeCheck())
            {
                mask.intValue = next ? mask.intValue | (int)flag : mask.intValue & ~(int)flag;
                selected = next;
            }
            EditorGUI.EndProperty();
            rect.xMin += 20;
            using (new EditorGUI.DisabledScope(!selected || !applicable))
            {
                if (selected)
                    EditorGUI.Slider(rect, serializedObject.FindProperty(name), 0, 1, new GUIContent(label, tooltip));
                else
                {
                    bool wasMixed = EditorGUI.showMixedValue;
                    try
                    {
                        EditorGUI.showMixedValue = authoredLeft != null && authoredRight != null &&
                            AuthoredForce(authoredLeft, name) != AuthoredForce(authoredRight, name);
                        var originalBone = authoredLeft != null ? authoredLeft : authoredRight;
                        if (originalBone != null)
                            EditorGUI.FloatField(rect, new GUIContent(label, tooltip), AuthoredForce(originalBone, name));
                        else EditorGUI.LabelField(rect, label, "未指定");
                    }
                    finally { EditorGUI.showMixedValue = wasMixed; }
                }
            }
            string original = $"左 {AuthoredValue(authoredLeft, name)} / 右 {AuthoredValue(authoredRight, name)}";
            string status = !applicable ? "Simplifiedではこの項目は作用しません。元の基礎値: " + original : !selected
                ? "元の基礎値を使用: " + original
                : $"次のビルドで左右 {serializedObject.FindProperty(name).floatValue:0.###} に変更。元: {original}";
            EditorGUILayout.LabelField(status, EditorStyles.wordWrappedMiniLabel);
            guide.DrawHint(name);
        }

        private static string Integration(VRCPhysBone bone) => bone == null ? "未指定" : bone.integrationType.ToString();

        private static string AuthoredValue(VRCPhysBone bone, string name)
        {
            if (bone == null) return "未指定";
            return AuthoredForce(bone, name).ToString("0.###");
        }

        private static float AuthoredForce(VRCPhysBone bone, string name)
        {
            switch (name)
            {
                case "motionPull": return bone.pull;
                case "motionSpring": return bone.spring;
                case "motionStiffness": return bone.stiffness;
                case "motionImmobile": return bone.immobile;
                case "motionGravity": return bone.gravity;
                default: return bone.gravityFalloff;
            }
        }

        private static VRCPhysBone FindPhysBone(SoftDeformPBSetup setup, Transform bone)
        {
            if (bone == null) return null;
            try { return SoftDeformPBGeneratePass.FindExistingPhysBone(setup.transform, setup, bone); }
            catch (InvalidOperationException) { return null; }
        }
    }
}
