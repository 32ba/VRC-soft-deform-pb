using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SoftDeformPB.Editor
{
    // All guide state belongs to the editor, never to the avatar's serialized configuration.
    internal sealed class SoftDeformTuningGuide
    {
        internal sealed class Entry
        {
            internal readonly string Label, Lower, Higher;
            internal Entry(string label, string lower, string higher = null)
            { Label = label; Lower = lower; Higher = higher; }
            internal string Effect => Higher == null ? Lower : "小さく: " + Lower + "  /  大きく: " + Higher;
        }

        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>
        {
            { "leftBreast", new Entry("左の胸ボーン", "既存のPhysBoneが動かしている根元を指定します。") },
            { "rightBreast", new Entry("右の胸ボーン", "左とは別の根元を指定します。") },
            { "restPoseSupport", new Entry("静止時の支え", "0はこれまでの垂れ方", "静止時の重力を抑え、元の形へ近づける。Pull・Spring・角度制限は変えない") },
            { "preserveExistingMotion", new Entry("元の揺れを維持して調整", "ON: チェックした力だけ変更。OFF: 主な揺れを一括で設定し直す。") },
            { "motionPull", new Entry("復元力 (Pull)", "元の向きへ戻す力が弱い", "元の向きへ戻す力が強い") },
            { "motionSpring", new Entry("反発 (Spring / Momentum)", "戻る途中の反発が控えめ", "戻る途中に揺れが残りやすい") },
            { "motionStiffness", new Entry("姿勢を保つ硬さ (Stiffness)", "向きを保つ働きが弱い", "向きを保つ働きが強い。Advancedのみ") },
            { "motionImmobile", new Entry("動きの抑制 (Immobile)", "移動の影響を受けやすい", "移動の影響を抑える。元のImmobile Typeに従う") },
            { "motionGravity", new Entry("重力", "下へ向ける作用が弱い", "下へ向ける作用が強い") },
            { "motionGravityFalloff", new Entry("静止姿勢で重力を弱める", "静止姿勢でも重力を受ける", "元の向きに近いほど重力を弱める。1で静止時は0") },
            { "motionMaxPitch", new Entry("縦に振れる角度の上限", "上下の振れを狭める", "上下に振れる余地を広げる") },
            { "motionMaxYaw", new Entry("横に振れる角度の上限", "左右の振れを狭める", "左右に振れる余地を広げる") },
            { "rootMotionMaxOffset", new Entry("付け根の移動上限", "0で追加の移動・回転なし", "付け根ごと動ける範囲が広がる") },
            { "rootVerticalTranslation", new Entry("上下へ動く量", "上下の移動を抑える", "上下の移動を通す。1で元の量") },
            { "rootHorizontalTranslation", new Entry("左右へ動く量", "左右の移動を抑える", "左右の移動を通す。1で元の量") },
            { "rootLongitudinalTranslation", new Entry("前後へ動く量", "前後の移動を抑える", "前後の移動を通す。1で元の量") },
            { "rootMotionPull", new Entry("付け根の復元力 (Pull)", "付け根を戻す力が弱い", "付け根を戻す力が強い") },
            { "rootMotionSpring", new Entry("付け根の反発 (Spring)", "付け根の反発が控えめ", "付け根が戻る途中に揺れが残りやすい") },
            { "rootMotionImmobile", new Entry("付け根の動きの抑制", "移動の影響を受けやすい", "移動の影響を抑える") },
            { "rootMotionGravity", new Entry("付け根の重力", "下へ向ける作用が弱い", "下へ向ける作用が強い") },
            { "rootMotionGravityFalloff", new Entry("付け根の静止時重力減衰", "静止姿勢でも重力を受ける", "静止姿勢の重力を弱める") },
            { "rootMotionRotationShare", new Entry("付け根の回転を混ぜる量", "0で追加の回転なし", "付け根の動きから伝わる回転が大きい") },
            { "rootMotionMaxAngle", new Entry("付け根ドライバーの角度", "同じ移動上限を小さな角度で駆動", "同じ移動上限を大きな角度で駆動") },
            { "rootVerticalRotation", new Entry("縦の回転を通す量", "縦の回転を抑える", "縦の回転を通す。1で元の量") },
            { "rootHorizontalRotation", new Entry("横の回転を通す量", "横の回転を抑える", "横の回転を通す。1で元の量") },
            { "rootTwistRotation", new Entry("ねじれを通す量", "ねじれを抑える", "ねじれを通す。1で元の量") },
            { "secondaryMotionStrength", new Entry("余韻を混ぜる量", "0で追加の余韻なし", "遅れて重なる回転が大きい") },
            { "secondaryMotionPull", new Entry("余韻の復元力 (Pull)", "追加の揺れを戻す力が弱い", "追加の揺れを戻す力が強い。秒数ではない") },
            { "secondaryMotionSpring", new Entry("余韻の反発 (Spring)", "追加の反発が控えめ", "追加の揺れが残りやすい") },
            { "secondaryMotionMaxAngle", new Entry("余韻ドライバーの角度上限", "追加の回転の範囲が狭い", "追加の回転の範囲が広い") },
            { "maxStretch", new Entry("伸びの上限", "0で伸びない", "元の長さより伸びる余地が増える") },
            { "maxSquish", new Entry("縮みの上限", "0で縮まない", "元の長さより縮む余地が増える") },
            { "stretchMotion", new Entry("動きによる伸び縮み", "0で掴み・衝突による伸縮だけ", "アバターの動きで長さが変わりやすい") },
            { "squashDepth", new Entry("縮んだときの厚み補正", "幅・高さを補う量が少ない", "縮んだ分、幅・高さを大きく補う") },
            { "stretchDepth", new Entry("伸びたときの細さ補正", "幅・高さを細くする量が少ない", "伸びた分、幅・高さを細くする") },
            { "volumeRetention", new Entry("体積を保つ補正量", "0で幅・高さの補正なし", "長さの変化に対する幅・高さの補正が強い") },
            { "horizontalShare", new Entry("補正を横幅へ振り分ける", "高さ側へ補正を振り分ける", "横幅側へ補正を振り分ける") },
            { "gravitySupineSpread", new Entry("仰向けで横へ広がる量", "0で広がる補正なし", "仰向けで横幅が広がる") },
            { "gravitySupineCompression", new Entry("仰向けで短くなる量", "0で短くする補正なし", "仰向けで前への張り出しを短くする") },
            { "gravitySupineRootSpread", new Entry("仰向けで付け根を外へ移動", "0で姿勢による移動なし", "仰向けで左右の付け根が外へ離れる") },
            { "gravitySideElongation", new Entry("横向きで横に伸びる量", "0で横向きの形の補正なし", "横幅が伸び、高さは細くなる") },
            { "gravityInvertedElongation", new Entry("逆さで縦に伸びる量", "0で逆さの形の補正なし", "高さが伸び、横幅は細くなる") },
            { "verticalAngleRetention", new Entry("縦の傾きを保つ量", "0で主PhysBoneの回転に追従", "元の縦の傾きを保つ。1で対象の回転を打ち消す") },
            { "horizontalAngleRetention", new Entry("横の傾きを保つ量", "0で主PhysBoneの回転に追従", "元の横の傾きを保つ。1で対象の回転を打ち消す") },
            { "squishVerticalAngleRetention", new Entry("縮んだときの追加の縦保持", "追加の保持が少ない", "縮むほど、残っている縦の回転も抑える") },
            { "squishHorizontalAngleRetention", new Entry("縮んだときの追加の横保持", "追加の保持が少ない", "縮むほど、残っている横の回転も抑える") },
            { "lateralCompressionDepth", new Entry("横から押されたときの細さ", "0で追加の横つぶれなし", "外側の接触と内向きの動きで横幅を狭める") },
            { "lateralVerticalShare", new Entry("横つぶれの補正を上へ配分", "前へふくらませる側に配分", "上へふくらませる側に配分") },
            { "lateralResponseDistance", new Entry("横つぶれが最大になる移動量", "少ない内向き移動で最大になる", "大きく内へ動かないと最大にならない") },
            { "gatheringAngleCorrection", new Entry("寄せたときの向き補正", "0で追加の向き補正なし", "寄せたときに元の向きへ補正する量が増える") },
            { "minimumCompressionRatio", new Entry("これ以上つぶさない比率", "0で追加の共通制限なし", "元の長さ・幅を多く残し、縮みを制限する") },
            { "reuseTorsoColliders", new Entry("既存の胴体コライダーを利用", "ON: 適したものを再利用。OFF: 胴体用を生成。登録済みのものは残る。") },
            { "torsoSupportClearance", new Entry("胴体との静止時の隙間", "胴体側の支えを近くに置く", "胴体側の支えを後ろへ離す") },
            { "opposingColliderCoverage", new Entry("反対側の支えの大きさ", "要求する支えが小さい", "要求する支えが大きい。静止時に接触しない範囲で制限") },
            { "opposingColliderCenter", new Entry("反対側の支えの位置", "付け根側に置く", "先端側に置く") },
            { "responseSamples", new Entry("形の応答のサンプル数", "生成するカーブが粗い", "細かく補間する。物理のフレームレートは変わらない") },
            { "parameterPrefix", new Entry("生成パラメーターの接頭辞", "生成する名前を識別するもの。揺れの見た目には影響しません。") }
        };

        private static readonly string[] Goals =
        {
            "揺れの硬さ・反発を変えたい", "付け根ごと動かしたい", "止まった後の余韻を変えたい",
            "伸び縮み・厚みを変えたい", "寝たときの形を変えたい", "傾きを保って動かしたい",
            "横から押したときの形を変えたい", "胴体・反対側との支えを調整したい"
        };
        private static readonly string[] GoalHints =
        {
            "まず復元力 (Pull) を1項目だけ変更。次に反発を調整します。柔らかさと余韻は別の設定です。",
            "まず付け根の移動上限を決め、上下・左右・前後の量を調整。0なら配下の設定は効きません。",
            "余韻を混ぜる量で見た目の大きさ、余韻のPull・Springで追加の揺れの応答を調整します。",
            "伸び・縮みの上限は長さ、厚み・細さ補正は幅と高さです。下の見本で両方を比較できます。",
            "仰向け・横向き・逆さでの形を個別に調整します。通常の立ち姿勢では姿勢補正は中立です。",
            "0は回転に追従、1は対象の回転を打ち消します。付け根の位置を固定する設定ではありません。",
            "外側の手・指の接触と内向きの移動で反応します。つぶさない比率を上げると他の縮みも制限されます。",
            "支えの位置と大きさを調整します。静止時の接触を避ける制限があるため、要求値どおりにならない場合があります。"
        };

        private bool showEffects = true;
        private int goal;
        private int shapeMode;
        private float shapeAmount = 1;

        internal void RestorePreferences() => showEffects = SessionState.GetBool("SoftDeformPB.Tuning.ShowEffects", true);

        internal static bool TryGet(string name, out Entry entry) => Entries.TryGetValue(name, out entry);

        internal void DrawNavigation(Action<int> focus, Action openMotion)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("見た目から調整する", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                goal = EditorGUILayout.Popup("変えたいこと", goal, Goals);
                if (EditorGUI.EndChangeCheck()) focus(goal);
                EditorGUILayout.LabelField(GoalHints[goal], EditorStyles.wordWrappedLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("この設定を開く")) focus(goal);
                    if (GUILayout.Button("動かして確認")) openMotion();
                }
                bool next = EditorGUILayout.ToggleLeft("値を変えたときの効果を表示", showEffects);
                if (next != showEffects)
                {
                    showEffects = next;
                    SessionState.SetBool("SoftDeformPB.Tuning.ShowEffects", showEffects);
                }
            }
        }

        internal void DrawHint(string name)
        {
            if (showEffects && TryGet(name, out var entry))
                EditorGUILayout.LabelField(entry.Effect, EditorStyles.wordWrappedMiniLabel);
        }

        internal void DrawShapeExample(SerializedObject settings)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("伸び縮みの見本", EditorStyles.boldLabel);
            shapeMode = GUILayout.Toolbar(shapeMode, new[] { "縮むとき", "伸びるとき" });
            shapeAmount = EditorGUILayout.Slider(new GUIContent("見本の変形量", "このスライダーは見本だけを動かし、アバターの設定は変えません。"), shapeAmount, 0, 1);
            float Read(string name) => settings.FindProperty(name).floatValue;
            float limit = Read(shapeMode == 0 ? "maxSquish" : "maxStretch");
            float amount = limit > 0 ? shapeAmount : 0;
            float squish = shapeMode == 0 ? amount : 0;
            float stretch = shapeMode == 1 ? amount : 0;
            var scale = SoftDeformShapeMath.EvaluateScale(Vector3.one, new SoftDeformAxisMap(2, 0, 1),
                squish, stretch, Read("squashDepth"), Read("stretchDepth"), Read("volumeRetention"), Read("horizontalShare"));
            float length = shapeMode == 0 ? 1 - limit * amount : 1 + limit * amount;
            EditorGUILayout.LabelField($"長さ {length:P0}  /  横幅 {scale.x:P0}  /  高さ {scale.y:P0}", EditorStyles.wordWrappedLabel);
            var canvas = EditorGUILayout.GetControlRect(false, 168);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(canvas, EditorGUIUtility.isProSkin ? new Color(.12f, .15f, .18f) : new Color(.88f, .92f, .94f));
                var originalColor = Handles.color;
                Handles.BeginGUI();
                try
                {
                    Projection(new Rect(canvas.x, canvas.y, canvas.width * .5f, canvas.height), "正面: 横幅と高さ", scale.x, scale.y);
                    Projection(new Rect(canvas.x + canvas.width * .5f, canvas.y, canvas.width * .5f, canvas.height), "横: 長さと高さ", length, scale.y);
                }
                finally { Handles.color = originalColor; Handles.EndGUI(); }
            }
            EditorGUILayout.LabelField("灰: 元の形 / 緑: 設定による補正の見本", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("長さの上限と形の補正式を使った概略図です。衣装の制限・元のカーブ・姿勢補正・実際のメッシュは含みません。", EditorStyles.wordWrappedMiniLabel);
            if (limit <= 0)
                EditorGUILayout.HelpBox("この方向の長さの上限が0なので、見本も変形しません。", MessageType.None);
        }

        private static void Projection(Rect area, string label, float width, float height)
        {
            GUI.Label(new Rect(area.x + 6, area.y + 6, area.width - 12, 22), label, EditorStyles.miniLabel);
            var center = new Vector2(area.center.x, area.y + 87);
            float radiusX = Mathf.Min(30, (area.width - 20) / 5);
            Ellipse(center, radiusX, 29, new Color(.55f, .58f, .62f), 1.5f);
            Ellipse(center, radiusX * width, 29 * height, EditorGUIUtility.isProSkin ? new Color(.32f, .9f, .7f) : new Color(0, .5f, .36f), 3);
        }

        private static void Ellipse(Vector2 center, float radiusX, float radiusY, Color color, float lineWidth)
        {
            var points = new Vector3[65];
            for (int i = 0; i < points.Length; i++)
            {
                float angle = i * Mathf.PI * 2 / (points.Length - 1);
                points[i] = new Vector3(center.x + Mathf.Cos(angle) * radiusX, center.y + Mathf.Sin(angle) * radiusY, 0);
            }
            Handles.color = color;
            Handles.DrawAAPolyLine(lineWidth, points);
        }
    }
}
