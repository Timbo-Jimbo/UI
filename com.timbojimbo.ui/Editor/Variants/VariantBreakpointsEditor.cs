using TimboJimbo.UI.Variants;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UI.Variants
{
    /// <summary>
    /// Inspector for <see cref="VariantBreakpoints"/>: the set and group it selects in, what it measures, and its
    /// breakpoints as rows of a value and the variant from there up (picked from the group's own), then a line reading
    /// what it measures now and the variant that gives.
    /// </summary>
    [CustomEditor(typeof(VariantBreakpoints))]
    public sealed class VariantBreakpointsEditor : UnityEditor.Editor
    {
        private static readonly GUIContent s_from = new("From", "The least it measures for this breakpoint's variant to be selected.");
        private static readonly GUIContent s_remove = new("×", "Remove this breakpoint.");

        // The readout follows the preview, which samples again whenever a breakpoint gives another variant.
        private void OnEnable() => VariantPreview.Sampled += Repaint;
        private void OnDisable() => VariantPreview.Sampled -= Repaint;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var breakpoints = (VariantBreakpoints)target;

            var set = VariantComponentGUI.Target(serializedObject.FindProperty("_target"), breakpoints.Target);
            var group = serializedObject.FindProperty("_group");
            VariantComponentGUI.Group(group, set);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_measure"));

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Breakpoints", "Default below them all", EditorStyles.miniLabel);
            var list = serializedObject.FindProperty("_breakpoints");
            int removed = -1;
            for (int i = 0; i < list.arraySize; i++)
            {
                var element = list.GetArrayElementAtIndex(i);
                var min = element.FindPropertyRelative(nameof(VariantBreakpoints.Breakpoint.Min));
                var variant = element.FindPropertyRelative(nameof(VariantBreakpoints.Breakpoint.Variant));
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(s_from, GUILayout.Width(36));
                    min.floatValue = EditorGUILayout.FloatField(min.floatValue, GUILayout.Width(64));
                    EditorGUI.BeginChangeCheck();
                    string chosen = VariantFields.VariantPopup(GUIContent.none, variant.stringValue, set, group.stringValue, "Default");
                    if (EditorGUI.EndChangeCheck())
                        variant.stringValue = chosen;
                    if (GUILayout.Button(s_remove, EditorStyles.miniButton, GUILayout.Width(20)))
                        removed = i;
                }
            }
            if (removed >= 0)
                list.DeleteArrayElementAtIndex(removed);
            if (GUILayout.Button("Add Breakpoint"))
                Add(list);

            var size = breakpoints.DrawnSize;
            string now = breakpoints.VariantFor(size);
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Now", $"{breakpoints.MeasureOf(size):0.##}: {(now.Length == 0 ? "Default" : now)}", EditorStyles.miniLabel);

            serializedObject.ApplyModifiedProperties();
        }

        // A new breakpoint above the largest there is, on Default until a variant is picked.
        private static void Add(SerializedProperty list)
        {
            float largest = 0f;
            for (int i = 0; i < list.arraySize; i++)
                largest = Mathf.Max(largest, list.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(VariantBreakpoints.Breakpoint.Min)).floatValue);
            list.arraySize++;
            var added = list.GetArrayElementAtIndex(list.arraySize - 1);
            added.FindPropertyRelative(nameof(VariantBreakpoints.Breakpoint.Min)).floatValue = list.arraySize == 1 ? 0f : largest + 100f;
            added.FindPropertyRelative(nameof(VariantBreakpoints.Breakpoint.Variant)).stringValue = "";
        }
    }
}
