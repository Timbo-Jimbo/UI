using System;
using System.Collections.Generic;
using System.Linq;
using TimboJimbo.UI.Variants;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UI.Variants
{
    /// <summary>Draws a variant's value with the field its property's type calls for: a colour picker, an enum popup, an object field.</summary>
    internal static class VariantFields
    {
        public static void Draw(GUIContent label, SerializedProperty value, VariantAccessor accessor)
        {
            var vector = value.FindPropertyRelative("_vector");
            var integer = value.FindPropertyRelative("_integer");
            var text = value.FindPropertyRelative("_string");
            var reference = value.FindPropertyRelative("_object");
            var type = accessor.ValueType;
            var v = vector.vector4Value;

            EditorGUI.BeginChangeCheck();
            switch (accessor.Kind)
            {
                case VariantValueKind.Bool:
                {
                    bool on = EditorGUILayout.Toggle(label, integer.longValue != 0);
                    if (EditorGUI.EndChangeCheck()) integer.longValue = on ? 1 : 0;
                    return;
                }
                case VariantValueKind.Int:
                {
                    long chosen;
                    if (type.IsEnum)
                    {
                        var current = (Enum)Enum.ToObject(type, integer.longValue);
                        var picked = type.IsDefined(typeof(FlagsAttribute), false)
                            ? EditorGUILayout.EnumFlagsField(label, current)
                            : EditorGUILayout.EnumPopup(label, current);
                        chosen = Convert.ToInt64(picked);
                    }
                    else
                    {
                        chosen = EditorGUILayout.LongField(label, integer.longValue);
                    }
                    if (EditorGUI.EndChangeCheck()) integer.longValue = chosen;
                    return;
                }
                case VariantValueKind.Float:
                {
                    float chosen = EditorGUILayout.FloatField(label, v.x);
                    if (EditorGUI.EndChangeCheck()) vector.vector4Value = new Vector4(chosen, 0f, 0f, 0f);
                    return;
                }
                case VariantValueKind.Color:
                {
                    Color chosen = EditorGUILayout.ColorField(label, v);
                    if (EditorGUI.EndChangeCheck()) vector.vector4Value = chosen;
                    return;
                }
                case VariantValueKind.Vector2:
                {
                    Vector2 chosen = EditorGUILayout.Vector2Field(label, v);
                    if (EditorGUI.EndChangeCheck()) vector.vector4Value = chosen;
                    return;
                }
                case VariantValueKind.Vector3:
                {
                    Vector3 chosen = EditorGUILayout.Vector3Field(label, v);
                    if (EditorGUI.EndChangeCheck()) vector.vector4Value = chosen;
                    return;
                }
                case VariantValueKind.Vector4:
                {
                    var chosen = EditorGUILayout.Vector4Field(label, v);
                    if (EditorGUI.EndChangeCheck()) vector.vector4Value = chosen;
                    return;
                }
                case VariantValueKind.Rect:
                {
                    var chosen = EditorGUILayout.RectField(label, new Rect(v.x, v.y, v.z, v.w));
                    if (EditorGUI.EndChangeCheck()) vector.vector4Value = new Vector4(chosen.x, chosen.y, chosen.width, chosen.height);
                    return;
                }
                case VariantValueKind.Quaternion:
                {
                    var euler = EditorGUILayout.Vector3Field(label, new Quaternion(v.x, v.y, v.z, v.w).eulerAngles);
                    if (EditorGUI.EndChangeCheck())
                    {
                        var rotation = Quaternion.Euler(euler);
                        vector.vector4Value = new Vector4(rotation.x, rotation.y, rotation.z, rotation.w);
                    }
                    return;
                }
                case VariantValueKind.Vector2Int:
                {
                    var chosen = EditorGUILayout.Vector2IntField(label, new Vector2Int(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y)));
                    if (EditorGUI.EndChangeCheck()) vector.vector4Value = new Vector4(chosen.x, chosen.y, 0f, 0f);
                    return;
                }
                case VariantValueKind.Vector3Int:
                {
                    var chosen = EditorGUILayout.Vector3IntField(label, new Vector3Int(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y), Mathf.RoundToInt(v.z)));
                    if (EditorGUI.EndChangeCheck()) vector.vector4Value = new Vector4(chosen.x, chosen.y, chosen.z, 0f);
                    return;
                }
                case VariantValueKind.String:
                {
                    string chosen = EditorGUILayout.TextField(label, text.stringValue);
                    if (EditorGUI.EndChangeCheck()) text.stringValue = chosen;
                    return;
                }
                case VariantValueKind.Object:
                {
                    var chosen = EditorGUILayout.ObjectField(label, reference.objectReferenceValue, type, true);
                    if (EditorGUI.EndChangeCheck()) reference.objectReferenceValue = chosen;
                    return;
                }
                default:
                    EditorGUI.EndChangeCheck();
                    return;
            }
        }

        /// <summary>
        /// One of a set's variants in <paramref name="groupName"/>, by name: <paramref name="empty"/> (Default, or None
        /// for a state) for the empty name, then the group's variants. A name it does not have stays, shown as missing;
        /// with no set, or no such group, the name is typed.
        /// </summary>
        public static string VariantPopup(GUIContent label, string current, VariantSet set, string groupName, string empty)
        {
            int g = set != null ? set.IndexOfGroup(groupName) : -1;
            if (g < 0) return EditorGUILayout.TextField(label, current);

            var options = new List<string> { empty };
            options.AddRange(set.Groups[g].Variants.Select(v => v.Name));
            return Popup(label, current, options, emptyFirst: true);
        }

        /// <summary>One of a set's groups, by name; a name it does not have stays, shown as missing. With no set, it is typed.</summary>
        public static string GroupPopup(GUIContent label, string current, VariantSet set)
        {
            if (set == null || set.Groups.Count == 0) return EditorGUILayout.TextField(label, current);
            return Popup(label, current, set.Groups.Select(g => g.Name).ToList(), emptyFirst: false);
        }

        // A popup of names, `current` picked, kept and marked missing when it is not among them; with `emptyFirst`, the
        // first option stands for the empty name.
        private static string Popup(GUIContent label, string current, List<string> options, bool emptyFirst)
        {
            int index = emptyFirst && string.IsNullOrEmpty(current) ? 0 : options.IndexOf(current);
            if (emptyFirst && index == 0 && !string.IsNullOrEmpty(current)) index = -1;
            if (index < 0)
            {
                options.Add($"{current} (missing)");
                index = options.Count - 1;
            }
            int chosen = EditorGUILayout.Popup(label, index, options.ToArray());
            if (chosen == index) return current;
            return emptyFirst && chosen == 0 ? "" : options[chosen];
        }
    }
}
