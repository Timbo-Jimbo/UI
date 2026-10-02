using System;
using System.Linq;
using TimboJimbo.UI.Motion;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UI.Motion
{
    /// <summary>
    /// An animation under a foldout, with a row of preset buttons on its header line, then (unfolded) its duration,
    /// bounce, curvature and delay. The fields are the serialized truth: the preset they match is lit, as Box's corner
    /// shapes are (None by a duration of 0 alone, the others by their bounce and curvature with a duration), and none is
    /// once they are changed from all of them (or while animations that differ in them are edited together), rather than
    /// a Custom button that would do nothing.
    /// </summary>
    [CustomPropertyDrawer(typeof(MotionAnimation))]
    public sealed class MotionAnimationDrawer : PropertyDrawer
    {
        // The space EditorGUI.PrefixLabel leaves between a label and its field, which the presets keep to line up with
        // the fields under them.
        internal const float PrefixPadding = 2f;

        // None first, though it is last in the enum (so presets saved as numbers keep theirs).
        internal static readonly MotionAnimationPreset[] Presets = ((MotionAnimationPreset[])Enum.GetValues(typeof(MotionAnimationPreset)))
            .OrderBy(preset => preset != MotionAnimationPreset.None)
            .ToArray();
        internal static readonly GUIContent[] PresetNames = NamesOf(Presets);
        private static readonly string[] s_fields =
        {
            nameof(MotionAnimation.Duration), nameof(MotionAnimation.Bounce), nameof(MotionAnimation.Curvature), nameof(MotionAnimation.Delay),
        };

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            return property.isExpanded ? height + FieldsHeight(property) : height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // Settled up front, as GetPropertyHeight settled it: unfolding lays out on the next event.
            bool expanded = property.isExpanded;

            label = EditorGUI.BeginProperty(position, label, property);
            var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            // The foldout takes only the label's part of the line, so a click on the presets beside it is theirs.
            property.isExpanded = EditorGUI.Foldout(LabelRect(row), expanded, label, true);
            EditorGUI.BeginChangeCheck();
            int clicked = GUI.Toolbar(ButtonsRect(row), PresetIndexFor(property), PresetNames);
            if (EditorGUI.EndChangeCheck() && clicked >= 0)
                Use(property, Presets[clicked]);

            if (expanded)
                Fields(row, property);

            EditorGUI.EndProperty();
        }

        // The label's part of a header line, and the part beside it the buttons take.
        internal static Rect LabelRect(Rect row) => new(row.x, row.y, EditorGUIUtility.labelWidth, row.height);

        internal static Rect ButtonsRect(Rect row)
        {
            float x = row.x + EditorGUIUtility.labelWidth + PrefixPadding;
            return new Rect(x, row.y, Mathf.Max(0f, row.xMax - x), row.height);
        }

        // The height of an animation's fields, under its header line.
        internal static float FieldsHeight(SerializedProperty animation)
        {
            float height = 0f;
            foreach (var name in s_fields)
                height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(animation.FindPropertyRelative(name));
            return height;
        }

        // An animation's fields, indented, one under another below the header line `row`.
        internal static void Fields(Rect row, SerializedProperty animation)
        {
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = indent + 1;
            foreach (var name in s_fields)
            {
                var child = animation.FindPropertyRelative(name);
                row.y = row.yMax + EditorGUIUtility.standardVerticalSpacing;
                row.height = EditorGUI.GetPropertyHeight(child);
                EditorGUI.PropertyField(row, child, true);
            }
            EditorGUI.indentLevel = indent;
        }

        /// <summary>
        /// Gives every animation being edited <paramref name="preset"/>'s bounce and curvature, leaving the duration and
        /// delay alone (<see cref="MotionAnimation.Use"/> keeps them), so each keeps its own timing; None sets only the
        /// duration, to 0, and another preset given to an animation with none gives it the default duration back.
        /// </summary>
        internal static void Use(SerializedProperty animation, MotionAnimationPreset preset)
        {
            var duration = animation.FindPropertyRelative(nameof(MotionAnimation.Duration));
            var bounce = animation.FindPropertyRelative(nameof(MotionAnimation.Bounce));
            var curvature = animation.FindPropertyRelative(nameof(MotionAnimation.Curvature));

            bool none = !duration.hasMultipleDifferentValues && duration.floatValue <= 0f;
            var used = new MotionAnimation(none ? 0f : 1f).Use(preset);
            if (preset == MotionAnimationPreset.None || none)
                duration.floatValue = used.Duration;
            if (preset != MotionAnimationPreset.None)
            {
                bounce.floatValue = used.Bounce;
                curvature.floatValue = used.Curvature;
            }
        }

        // The index in Presets of the preset the animation matches (None by its duration of 0 alone, the others by their
        // bounce and curvature with a duration), or -1 when it has been changed from all of them or what decides it
        // differs between the animations being edited.
        internal static int PresetIndexFor(SerializedProperty animation)
        {
            var duration = animation.FindPropertyRelative(nameof(MotionAnimation.Duration));
            var bounce = animation.FindPropertyRelative(nameof(MotionAnimation.Bounce));
            var curvature = animation.FindPropertyRelative(nameof(MotionAnimation.Curvature));
            if (duration.hasMultipleDifferentValues)
                return -1;
            if (duration.floatValue > 0f && (bounce.hasMultipleDifferentValues || curvature.hasMultipleDifferentValues))
                return -1;

            var value = new MotionAnimation(duration.floatValue, bounce.floatValue, curvature.floatValue);
            for (int i = 0; i < Presets.Length; i++)
            {
                if (value.Matches(Presets[i]))
                    return i;
            }
            return -1;
        }

        private static GUIContent[] NamesOf(MotionAnimationPreset[] presets)
        {
            var names = new GUIContent[presets.Length];
            for (int i = 0; i < presets.Length; i++)
                names[i] = new GUIContent(ObjectNames.NicifyVariableName(presets[i].ToString()), PresetTooltip(presets[i]));
            return names;
        }

        private static string PresetTooltip(MotionAnimationPreset preset)
        {
            if (preset == MotionAnimationPreset.None)
                return "No animation: it is there at once, after its delay. Sets the duration to 0; the bounce, curvature and delay stay as they are.";

            string what = preset switch
            {
                MotionAnimationPreset.Smooth => "Settles without overshooting.",
                MotionAnimationPreset.Snappy => "A small overshoot, quick to settle.",
                MotionAnimationPreset.Bouncy => "A lively overshoot and swing back.",
                MotionAnimationPreset.Arc => "Settles without overshooting, bowing out sideways on the way.",
                _ => string.Empty,
            };
            return what + " Sets the bounce and curvature; the duration and delay stay as they are (a duration of 0 goes back to the default).";
        }
    }
}
