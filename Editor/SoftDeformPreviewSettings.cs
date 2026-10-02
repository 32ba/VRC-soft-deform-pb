using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SoftDeformPB.Editor
{
    // Store tuning values across Play Mode reloads without serializing scene bone references.
    [Serializable]
    internal sealed class SoftDeformPreviewSettings
    {
        [Serializable]
        private sealed class Value
        {
            public string name;
            public float number;
            public int integer;
            public bool flag;
            public string text;
        }

        private static readonly FieldInfo[] Fields = typeof(SoftDeformPBSetup)
            .GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(f => f.FieldType == typeof(float) || f.FieldType == typeof(int) ||
                        f.FieldType == typeof(bool) || f.FieldType == typeof(string) || f.FieldType.IsEnum)
            .OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();

        [SerializeField] private Value[] values;

        internal static SoftDeformPreviewSettings Capture(SoftDeformPBSetup setup)
        {
            return new SoftDeformPreviewSettings
            {
                values = Fields.Select(field =>
                {
                    var value = new Value { name = field.Name };
                    object current = field.GetValue(setup);
                    if (field.FieldType == typeof(float)) value.number = (float)current;
                    else if (field.FieldType == typeof(bool)) value.flag = (bool)current;
                    else if (field.FieldType == typeof(string)) value.text = (string)current;
                    else value.integer = Convert.ToInt32(current);
                    return value;
                }).ToArray()
            };
        }

        internal bool SameAs(SoftDeformPreviewSettings other)
        {
            return other != null && JsonUtility.ToJson(this) == JsonUtility.ToJson(other);
        }

        internal void Apply(SoftDeformPBSetup setup, SoftDeformPreviewSettings baseline = null, bool recordUndo = false)
        {
            if (setup == null || values == null) return;
            var changed = Fields.Select(field => new
                {
                    Field = field,
                    Value = values.FirstOrDefault(v => v.name == field.Name),
                    Previous = baseline?.values?.FirstOrDefault(v => v.name == field.Name)
                })
                .Where(pair => pair.Value != null && (baseline == null ||
                    JsonUtility.ToJson(pair.Value) != JsonUtility.ToJson(pair.Previous)))
                .ToArray();
            if (changed.Length == 0) return;
            if (recordUndo) Undo.RecordObject(setup, "Soft Deform PB 調整値を採用");
            foreach (var pair in changed)
            {
                Type type = pair.Field.FieldType;
                object value;
                if (type == typeof(float)) value = pair.Value.number;
                else if (type == typeof(bool)) value = pair.Value.flag;
                else if (type == typeof(string)) value = pair.Value.text;
                else if (type.IsEnum) value = Enum.ToObject(type, pair.Value.integer);
                else value = pair.Value.integer;
                pair.Field.SetValue(setup, value);
            }
            if (recordUndo)
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(setup);
                EditorUtility.SetDirty(setup);
            }
        }
    }
}
