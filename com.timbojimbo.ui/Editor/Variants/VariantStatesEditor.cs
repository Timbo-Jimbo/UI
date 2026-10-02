using TimboJimbo.UI.Variants;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TimboJimboEditor.UI.Variants
{
    /// <summary>
    /// Inspector for <see cref="VariantStates"/>: the set it selects in (found on it or above it when none is given), the
    /// group, and a variant for each state picked from that group's own, with a note when nothing on it or under it is a
    /// raycast target, so that it would hear no pointer.
    /// </summary>
    [CustomEditor(typeof(VariantStates))]
    public sealed class VariantStatesEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var states = (VariantStates)target;

            var set = VariantComponentGUI.Target(serializedObject.FindProperty("_target"), states.Target);
            var group = serializedObject.FindProperty("_group");
            VariantComponentGUI.Group(group, set);
            VariantComponentGUI.Variant(serializedObject.FindProperty("_hover"), set, group.stringValue, "None");
            VariantComponentGUI.Variant(serializedObject.FindProperty("_pressed"), set, group.stringValue, "None");
            VariantComponentGUI.Variant(serializedObject.FindProperty("_focused"), set, group.stringValue, "None");
            VariantComponentGUI.Variant(serializedObject.FindProperty("_disabled"), set, group.stringValue, "Default");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_interactable"));

            if (!TakesPointer(states))
                EditorGUILayout.HelpBox("Nothing on it or under it is a raycast target, so it hears no pointer. Turn on Raycast Target on a graphic here.", MessageType.Info);

            serializedObject.ApplyModifiedProperties();
        }

        private static bool TakesPointer(VariantStates states)
        {
            foreach (var graphic in states.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic.raycastTarget)
                    return true;
            }
            return false;
        }
    }

    /// <summary>The fields the variant set's companions share: the set they select in, its group, and a variant of it.</summary>
    internal static class VariantComponentGUI
    {
        /// <summary>The set field; left empty, a line says which set is found on it or above it. Returns the set used.</summary>
        public static VariantSet Target(SerializedProperty property, VariantSet used)
        {
            EditorGUILayout.PropertyField(property, new GUIContent("Variant Set", property.tooltip));
            if (property.objectReferenceValue == null)
            {
                string found = used != null ? $"{used.name}'s, found above" : "None on it or above it";
                EditorGUILayout.LabelField(" ", found, EditorStyles.miniLabel);
            }
            return used;
        }

        public static void Group(SerializedProperty property, VariantSet set)
        {
            EditorGUI.BeginChangeCheck();
            string chosen = VariantFields.GroupPopup(new GUIContent(property.displayName, property.tooltip), property.stringValue, set);
            if (EditorGUI.EndChangeCheck())
                property.stringValue = chosen;
        }

        public static void Variant(SerializedProperty property, VariantSet set, string group, string empty)
        {
            EditorGUI.BeginChangeCheck();
            string chosen = VariantFields.VariantPopup(new GUIContent(property.displayName, property.tooltip), property.stringValue, set, group, empty);
            if (EditorGUI.EndChangeCheck())
                property.stringValue = chosen;
        }
    }
}
