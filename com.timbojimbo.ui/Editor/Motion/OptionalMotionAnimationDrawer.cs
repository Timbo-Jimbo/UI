using TimboJimbo.UI.Motion;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UI.Motion
{
    /// <summary>
    /// An animation that may be inherited: the preset row of <see cref="MotionAnimationDrawer"/> with Inherit first, lit
    /// while it is unset, and while it is set the preset its fields match, which then unfold under a foldout. Inherit
    /// unsets it, keeping its fields for when it is set again; a preset clicked on Inherit sets it, from the default
    /// animation.
    /// </summary>
    [CustomPropertyDrawer(typeof(OptionalMotionAnimation))]
    public sealed class OptionalMotionAnimationDrawer : PropertyDrawer
    {
        private static readonly GUIContent[] s_buttons = Buttons();

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            var set = property.FindPropertyRelative("_set");
            if (set.boolValue && !set.hasMultipleDifferentValues && property.isExpanded)
                height += MotionAnimationDrawer.FieldsHeight(property.FindPropertyRelative("_animation"));
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var set = property.FindPropertyRelative("_set");
            var animation = property.FindPropertyRelative("_animation");
            // Settled up front, as GetPropertyHeight settled it: what changes here lays out on the next event.
            bool shown = set.boolValue && !set.hasMultipleDifferentValues;
            bool expanded = shown && property.isExpanded;

            label = EditorGUI.BeginProperty(position, label, property);
            var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            // Only a set animation has fields to unfold; an inherited one's label is a plain label.
            var labelRect = MotionAnimationDrawer.LabelRect(row);
            if (shown)
                property.isExpanded = EditorGUI.Foldout(labelRect, property.isExpanded, label, true);
            else
                EditorGUI.LabelField(labelRect, label);

            // Inherit is lit while it is unset, a preset while it is set and matches it; none while they differ between
            // the objects edited together.
            int lit = -1;
            if (!set.hasMultipleDifferentValues)
            {
                int preset = MotionAnimationDrawer.PresetIndexFor(animation);
                lit = !set.boolValue ? 0 : preset >= 0 ? preset + 1 : -1;
            }
            EditorGUI.BeginChangeCheck();
            int clicked = GUI.Toolbar(MotionAnimationDrawer.ButtonsRect(row), lit, s_buttons);
            if (EditorGUI.EndChangeCheck() && clicked >= 0)
            {
                if (clicked == 0)
                {
                    set.boolValue = false;
                }
                else
                {
                    if (!set.boolValue || set.hasMultipleDifferentValues)
                        Reset(animation);
                    set.boolValue = true;
                    MotionAnimationDrawer.Use(animation, MotionAnimationDrawer.Presets[clicked - 1]);
                }
            }

            if (expanded)
                MotionAnimationDrawer.Fields(row, animation);

            EditorGUI.EndProperty();
        }

        // The default animation's values.
        private static void Reset(SerializedProperty animation)
        {
            var value = MotionAnimation.Default;
            animation.FindPropertyRelative(nameof(MotionAnimation.Duration)).floatValue = value.Duration;
            animation.FindPropertyRelative(nameof(MotionAnimation.Bounce)).floatValue = value.Bounce;
            animation.FindPropertyRelative(nameof(MotionAnimation.Curvature)).floatValue = value.Curvature;
            animation.FindPropertyRelative(nameof(MotionAnimation.Delay)).floatValue = value.Delay;
        }

        private static GUIContent[] Buttons()
        {
            var presets = MotionAnimationDrawer.PresetNames;
            var buttons = new GUIContent[presets.Length + 1];
            buttons[0] = new GUIContent("Inherit", "No animation of its own: it moves on the one it inherits.");
            for (int i = 0; i < presets.Length; i++)
                buttons[i + 1] = presets[i];
            return buttons;
        }
    }
}
