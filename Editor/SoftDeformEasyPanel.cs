using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace SoftDeformPB.Editor
{
    internal sealed class SoftDeformEasyPanel
    {
        private sealed class Pad
        {
            internal readonly string Title, X, Y, Left, Right, Bottom, Top, Hint;
            internal readonly Color Color;
            internal Pad(string title, string x, string y, string left, string right, string bottom, string top, string hint, Color color)
            { Title = title; X = x; Y = y; Left = left; Right = right; Bottom = bottom; Top = top; Hint = hint; Color = color; }
        }

        private static readonly Pad[] Pads =
        {
            new Pad("1 揺れの質感", "柔らかさ", "反発", "硬め", "柔らかめ", "反発 小", "反発 大",
                "右へ: 戻す力を弱める。上へ: 反発を強める。", new Color(.35f, .8f, .95f)),
            new Pad("2 付け根と余韻", "付け根の動き", "余韻の量", "動き 小", "動き 大", "余韻 なし", "余韻 多い",
                "右へ: 付け根の移動幅を増やす。上へ: 遅れて重なる回転を増やす。", new Color(.45f, .88f, .65f)),
            new Pad("3 伸び縮みと厚み", "伸び縮み", "厚みの補正", "伸縮 小", "伸縮 大", "補正 なし", "補正 強い",
                "右へ: 伸び・縮みの幅をまとめて増やす。上へ: 幅・高さを補う。", new Color(1, .76f, .38f)),
            new Pad("4 寝たときの形", "仰向けの広がり", "横・逆さの伸び", "広がり 小", "広がり 大", "伸び なし", "伸び 大",
                "右へ: 仰向けで広げる。上へ: 横向き・逆さの形を伸ばす。", new Color(.68f, .65f, 1)),
            new Pad("5 傾きを保つ", "左右の傾き保持", "上下の傾き保持", "左右 追従", "左右 保持", "上下 追従", "上下 保持",
                "右へ: 左右の傾きを保つ。上へ: 上下の傾きを保つ。位置は固定しない。", new Color(.4f, .83f, .85f)),
            new Pad("6 手で押したときの形", "横つぶれ", "形を残す量", "つぶれ 小", "つぶれ 大", "つぶれ許可", "元の形保持",
                "外側から触れて内へ押すと反応。上へ動かすほど、全体の縮みを制限。", new Color(.98f, .58f, .7f))
        };

        private bool extra;
        private int capturedControl, dragUndoGroup = -1, dragAxis = -1;
        private Vector2 dragStartValue, dragStartMouse;

        internal bool Draw(SerializedObject settings, VRCPhysBone left, VRCPhysBone right)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Easy · 2Dパッドで調整", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("点を動かして調整。Shiftで片方向、矢印キーで微調整。", EditorStyles.wordWrappedMiniLabel);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.Slider(settings.FindProperty("restPoseSupport"), 0, 1,
                new GUIContent("静止時の支え", "重力による静止時の垂れを抑えます。主な揺れと付け根のGravity Falloffだけに適用し、Pull・Spring・角度制限は変更しません。"));
            EditorGUILayout.LabelField("0: これまでの垂れ方  /  1: 静止時の重力を抑え、元の形に近づける", EditorStyles.wordWrappedMiniLabel);
            bool changed = EditorGUI.EndChangeCheck();
            for (int i = 0; i < 3; i++) changed |= DrawPad((SoftDeformEasyPad)i, settings, left, right);
            extra = EditorGUILayout.Foldout(extra, "寝姿勢・傾き・手で押す形も調整", true);
            if (extra)
                for (int i = 3; i < Pads.Length; i++) changed |= DrawPad((SoftDeformEasyPad)i, settings, left, right);
            return changed;
        }

        private bool DrawPad(SoftDeformEasyPad kind, SerializedObject settings, VRCPhysBone left, VRCPhysBone right)
        {
            var pad = Pads[(int)kind];
            bool changed = false;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(pad.Title, EditorStyles.boldLabel);
                var area = EditorGUILayout.GetControlRect(false, 160);
                float size = Mathf.Min(128, Mathf.Max(90, (area.width - 76) * .52f));
                var square = new Rect(area.x + 60, area.y + 5, size, size);
                var point = SoftDeformEasyTuning.Read(kind, settings, left, right);
                var marker = new Vector2(square.x + square.width * point.x, square.y + square.height * (1 - point.y));
                int id = GUIUtility.GetControlID(0x5de100 + (int)kind, FocusType.Keyboard, square);
                EditorGUIUtility.AddCursorRect(square, MouseCursor.SlideArrow);
                var e = Event.current;
                var type = e.GetTypeForControl(id);
                if (type == EventType.MouseDown && e.button == 0 &&
                    (square.Contains(e.mousePosition) || (e.mousePosition - marker).sqrMagnitude <= 64))
                {
                    GUIUtility.hotControl = GUIUtility.keyboardControl = capturedControl = id;
                    dragStartValue = point; dragStartMouse = e.mousePosition;
                    dragAxis = -1;
                    Undo.IncrementCurrentGroup(); dragUndoGroup = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("Adjust Soft Deform PB " + pad.Title);
                    if (!e.shift) changed = Apply(kind, settings, point, Pointer(square, e.mousePosition));
                    e.Use();
                }
                else if (type == EventType.MouseDrag && GUIUtility.hotControl == id)
                {
                    var next = Pointer(square, e.mousePosition);
                    if (e.shift)
                    {
                        var delta = e.mousePosition - dragStartMouse;
                        if (dragAxis < 0 && delta.sqrMagnitude >= 1) dragAxis = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? 0 : 1;
                        if (dragAxis == 0) next.y = dragStartValue.y;
                        else if (dragAxis == 1) next.x = dragStartValue.x;
                        else next = point;
                    }
                    changed = Apply(kind, settings, point, next); e.Use();
                }
                else if (type == EventType.MouseUp && GUIUtility.hotControl == id && e.button == 0)
                {
                    Release(); e.Use();
                }
                else if (type == EventType.KeyDown && GUIUtility.keyboardControl == id)
                {
                    float step = e.alt ? .001f : e.shift ? .05f : .01f;
                    var delta = e.keyCode == KeyCode.LeftArrow ? Vector2.left : e.keyCode == KeyCode.RightArrow ? Vector2.right :
                        e.keyCode == KeyCode.UpArrow ? Vector2.up : e.keyCode == KeyCode.DownArrow ? Vector2.down : Vector2.zero;
                    if (delta != Vector2.zero)
                    {
                        Undo.IncrementCurrentGroup();
                        Undo.SetCurrentGroupName("Adjust Soft Deform PB " + pad.Title);
                        changed = Apply(kind, settings, point, point + delta * step); e.Use();
                    }
                }

                point = SoftDeformEasyTuning.Read(kind, settings, left, right);
                bool approximate = SoftDeformEasyTuning.IsApproximate(kind, settings, point, left, right);
                var side = new Rect(square.xMax + 12, area.y + 7, area.xMax - square.xMax - 14, 22);
                GUI.Label(side, pad.X + " " + Level(point.x), EditorStyles.miniLabel);
                side.y += 23; GUI.Label(side, pad.Y + " " + Level(point.y), EditorStyles.miniLabel);
                side.y += 27;
                if (kind == SoftDeformEasyPad.Shape)
                {
                    float shortest = 1 - settings.FindProperty("maxSquish").floatValue;
                    float longest = 1 + settings.FindProperty("maxStretch").floatValue;
                    GUI.Label(side, $"長さ {shortest:P0}〜{longest:P0}", EditorStyles.miniLabel);
                    if (Event.current.type == EventType.Repaint) DrawShape(settings, new Rect(side.x, side.y + 26, side.width, 51));
                    side.y += 81;
                }
                else
                {
                    side.height = 46;
                    GUI.Label(side, pad.Hint, EditorStyles.wordWrappedMiniLabel);
                    side.y += 52;
                }
                side.height = 20;
                GUI.Label(side, new GUIContent(approximate ? "○ 位置の目安" : "● 現在の設定",
                    "点は現在の設定から読み取ります。左右の違いや詳細の自由な組み合わせがある場合は代表値です。操作した軸に対応する値だけを調整します。"), EditorStyles.wordWrappedMiniLabel);
                if (Event.current.type == EventType.Repaint) Paint(square, point, pad, approximate, GUIUtility.keyboardControl == id);
                var xLabels = new Rect(square.x, square.yMax + 5, square.width, 20);
                GUI.Label(xLabels, pad.Left, EditorStyles.miniLabel);
                var rightStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperRight };
                GUI.Label(xLabels, pad.Right, rightStyle);
                GUI.Label(new Rect(area.x, square.y, 57, 35), pad.Top, EditorStyles.wordWrappedMiniLabel);
                GUI.Label(new Rect(area.x, square.yMax - 25, 57, 35), pad.Bottom, EditorStyles.wordWrappedMiniLabel);
            }
            return changed;
        }

        private static bool Apply(SoftDeformEasyPad kind, SerializedObject settings, Vector2 previous, Vector2 next)
        {
            if (!SoftDeformEasyTuning.Write(kind, settings, previous, next)) return false;
            settings.ApplyModifiedProperties(); GUI.changed = true;
            return true;
        }

        internal void Release()
        {
            if (capturedControl != 0 && GUIUtility.hotControl == capturedControl) GUIUtility.hotControl = 0;
            capturedControl = 0;
            if (dragUndoGroup >= 0) Undo.CollapseUndoOperations(dragUndoGroup);
            dragUndoGroup = dragAxis = -1;
        }

        private static Vector2 Pointer(Rect rect, Vector2 mouse)
        {
            return new Vector2(Mathf.Clamp01((mouse.x - rect.x) / rect.width), Mathf.Clamp01(1 - (mouse.y - rect.y) / rect.height));
        }

        private static string Level(float value) => value <= .00001f ? "なし" : value < .25f ? "小" : value < .65f ? "中" : "大";

        private static void Paint(Rect rect, Vector2 point, Pad pad, bool approximate, bool focused)
        {
            var background = EditorGUIUtility.isProSkin ? new Color(.11f, .14f, .18f) : new Color(.92f, .95f, .97f);
            EditorGUI.DrawRect(rect, background);
            var tint = pad.Color; tint.a = EditorGUIUtility.isProSkin ? .14f : .24f;
            EditorGUI.DrawRect(new Rect(rect.x + rect.width * .5f, rect.y, rect.width * .5f, rect.height * .5f), tint);
            var grid = EditorGUIUtility.isProSkin ? new Color(.3f, .36f, .42f) : new Color(.68f, .73f, .77f);
            for (int i = 0; i <= 4; i++)
            {
                EditorGUI.DrawRect(new Rect(rect.x + rect.width * i / 4, rect.y, 1, rect.height), grid);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height * i / 4, rect.width, 1), grid);
            }
            var location = new Vector2(rect.x + point.x * rect.width, rect.y + (1 - point.y) * rect.height);
            var accent = EditorGUIUtility.isProSkin ? pad.Color : pad.Color * .65f;
            accent.a = 1;
            var cross = accent; cross.a = .45f;
            EditorGUI.DrawRect(new Rect(rect.x, location.y, rect.width, 1), cross);
            EditorGUI.DrawRect(new Rect(location.x, rect.y, 1, rect.height), cross);
            var original = Handles.color;
            Handles.BeginGUI();
            try
            {
                Handles.color = accent;
                if (!approximate) Handles.DrawSolidDisc(location, Vector3.forward, 5);
                Handles.DrawWireDisc(location, Vector3.forward, 7);
                if (focused) Handles.DrawWireDisc(location, Vector3.forward, 10);
            }
            finally { Handles.color = original; Handles.EndGUI(); }
        }

        private static void DrawShape(SerializedObject settings, Rect area)
        {
            float F(string name) => settings.FindProperty(name).floatValue;
            var scale = SoftDeformShapeMath.EvaluateScale(Vector3.one, new SoftDeformAxisMap(2, 0, 1),
                F("maxSquish") > 0 ? 1 : 0, 0, F("squashDepth"), F("stretchDepth"), F("volumeRetention"), F("horizontalShare"));
            var center = new Vector2(area.x + area.width * .5f, area.y + 19);
            var color = Handles.color;
            Handles.BeginGUI();
            try
            {
                Ellipse(center, 18, 10, new Color(.55f, .58f, .62f));
                Ellipse(center, 18 * scale.x, 10 * scale.y, new Color(.32f, .8f, .6f));
            }
            finally { Handles.color = color; Handles.EndGUI(); }
            GUI.Label(new Rect(area.x, area.y + 37, area.width, 16), "縮む形の概略", EditorStyles.centeredGreyMiniLabel);
        }

        private static void Ellipse(Vector2 center, float width, float height, Color color)
        {
            var vertices = new Vector3[33];
            for (int i = 0; i < vertices.Length; i++)
            {
                float angle = i * Mathf.PI * 2 / (vertices.Length - 1);
                vertices[i] = center + new Vector2(Mathf.Cos(angle) * width, Mathf.Sin(angle) * height);
            }
            Handles.color = color; Handles.DrawAAPolyLine(2, vertices);
        }
    }
}
