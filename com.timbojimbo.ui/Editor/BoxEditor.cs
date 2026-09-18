using System;
using TimboJimbo.UI;
using UnityEditor;
using UnityEditor.UI;
using UnityEngine;

namespace TimboJimboEditor.UI
{
    /// <summary>
    /// Inspector for <see cref="Box"/>, laid out in the same order as Img's: what it looks like (colour and
    /// gradient), the material, the shape, the effects, then the raycast and mask controls.
    /// </summary>
    [CustomEditor(typeof(Box))]
    [CanEditMultipleObjects]
    public sealed class BoxEditor : GraphicEditor
    {
        private const string RadiiLockedPrefKey = "TimboJimbo.UI.Box.RadiiLocked";
        private const string InsetLockedPrefKey = "TimboJimbo.UI.Box.InsetLocked";
        private const float PresetTolerance = 1e-3f;

        private static readonly string[] s_shapeNames = Enum.GetNames(typeof(CornerShape));
        private static readonly GUIContent s_maskingLabel = new("Masking", "None: no masking. Draw and Mask: draw the box and clip its children to it. Mask Only: clip children without drawing the box (colour, gradient and blur are ignored).");
        private static readonly GUIContent s_cornerShapeLabel = new("Corner Shape", "Presets on the Curvature slider. Near the size limit the shader eases toward circular so pills stay clean.");
        private static readonly GUIContent s_uniformRadiusLabel = new("Corner Radius", "Clamped so radii sharing an edge fit within it; a short box becomes a pill.");
        private static readonly GUIContent s_uniformInsetLabel = new("Inset", "Offsets the drawn box (and any Mask taken from it) within the RectTransform, in canvas units. Positive shrinks inward, negative spills outward. Corners stay concentric.");
        private static readonly GUIContent[] s_sideLabels =
        {
            new("Left"), new("Right"), new("Top"), new("Bottom")
        };
        private static readonly GUIContent[] s_cornerLabels =
        {
            new("Top Left"), new("Top Right"), new("Bottom Right"), new("Bottom Left")
        };

        private SerializedProperty _masking;
        private SerializedProperty _concentric;
        private SerializedProperty _cornerRadii;
        private SerializedProperty _cornerCurvature;
        private SerializedProperty _inset;
        private SerializedProperty _strokeWidth;
        private SerializedProperty _blurRadius;
        private SerializedProperty _offset;
        private SerializedProperty _blendMode;
        private GradientSection _gradient;

        protected override void OnEnable()
        {
            base.OnEnable();
            _masking = serializedObject.FindProperty("_masking");
            _concentric = serializedObject.FindProperty("_concentric");
            _cornerRadii = serializedObject.FindProperty("_cornerRadii");
            _cornerCurvature = serializedObject.FindProperty("_cornerCurvature");
            _inset = serializedObject.FindProperty("_inset");
            _strokeWidth = serializedObject.FindProperty("_strokeWidth");
            _blurRadius = serializedObject.FindProperty("_blurRadius");
            _offset = serializedObject.FindProperty("_offset");
            _blendMode = serializedObject.FindProperty("_blendMode");
            _gradient = new GradientSection(serializedObject);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Mask Only draws nothing, so its colour, gradient and blur are hidden and treated as white / off / 0.
            // The Masking control itself is drawn lower down, next to the raycast and mask controls.
            var maskOnly = !_masking.hasMultipleDifferentValues && _masking.enumValueIndex == (int)MaskMode.MaskOnly;

            if (!maskOnly)
            {
                EditorGUILayout.PropertyField(m_Color);
                _gradient.Draw();
            }

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(m_Material);

            EditorGUILayout.Space();
            Header("Shape");
            EditorGUILayout.PropertyField(_concentric, new GUIContent("Concentric", "Inherit corner radii and curvature from the nearest parent Box, matched per corner so the rounded rects stay concentric."));
            if (_concentric.hasMultipleDifferentValues || !_concentric.boolValue)
            {
                DrawLockable(_cornerRadii, s_uniformRadiusLabel, s_cornerLabels, RadiiLockedPrefKey, allowNegative: false);
                DrawCurvature();
            }
            DrawLockable(_inset, s_uniformInsetLabel, s_sideLabels, InsetLockedPrefKey, allowNegative: true);
            EditorGUILayout.PropertyField(_strokeWidth, new GUIContent("Stroke Width", "Border ring width. 0 fills the box. Positive draws the ring inside the edge; negative draws it outside, spilling past the box."));
            if (!maskOnly)
                EditorGUILayout.PropertyField(_blurRadius, new GUIContent("Blur Radius", "Edge softness in canvas units. 0 is a crisp anti-aliased edge; larger gives a soft shadow."));
            EditorGUILayout.PropertyField(_offset, new GUIContent("Offset", "Shifts the drawn box without moving the RectTransform, so a shadow can share the panel's anchors."));

            EditorGUILayout.Space();
            Header("Effects");
            EditorGUILayout.PropertyField(_blendMode, new GUIContent("Blend Mode", "How the box composites with what is behind it."));

            EditorGUILayout.Space();
            RaycastControlsGUI();
            MaskableControlsGUI();

            // The Masking mode owns a Mask component; a change adds or removes it, so apply and sync
            // immediately, then restart the GUI pass so the appearance fields above re-evaluate against it.
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_masking, s_maskingLabel);
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                foreach (var t in targets)
                    ((Box)t).SyncMaskComponent();
                GUIUtility.ExitGUI();
            }

            serializedObject.ApplyModifiedProperties();
        }

        // One field locked to a single value (default), or four independent fields, toggled by a lock button
        // like a Transform's uniform-scale lock. The lock state is editor-only (EditorPrefs), not serialized.
        private static void DrawLockable(SerializedProperty prop, GUIContent uniformLabel, GUIContent[] labels, string prefKey, bool allowNegative = false)
        {
            var locked = EditorPrefs.GetBool(prefKey, true);

            float Clamp(float v) => allowNegative ? v : Mathf.Max(0f, v);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (locked)
                {
                    var value = prop.vector4Value;
                    var uniform = Mathf.Approximately(value.x, value.y) && Mathf.Approximately(value.y, value.z) && Mathf.Approximately(value.z, value.w);

                    EditorGUI.showMixedValue = prop.hasMultipleDifferentValues || !uniform;
                    EditorGUI.BeginChangeCheck();
                    var single = EditorGUILayout.FloatField(uniformLabel, value.x);
                    if (EditorGUI.EndChangeCheck())
                    {
                        single = Clamp(single);
                        prop.vector4Value = new Vector4(single, single, single, single);
                    }
                    EditorGUI.showMixedValue = false;
                }
                else
                {
                    EditorGUILayout.LabelField(uniformLabel);
                }

                var iconName = locked ? "Linked" : "Unlinked";
                if (EditorGUIUtility.isProSkin)
                    iconName = "d_" + iconName;
                var icon = EditorGUIUtility.IconContent(iconName);
                var lockContent = new GUIContent(icon.image, locked
                    ? "Locked to one value. Click to edit each independently."
                    : "Editing each value. Click to lock them to one.");
                EditorGUI.BeginChangeCheck();
                var nowLocked = GUILayout.Toggle(locked, lockContent, EditorStyles.miniButton, GUILayout.Width(30f), GUILayout.Height(18f));
                if (EditorGUI.EndChangeCheck())
                {
                    // Locking collapses to a single value so the fields agree with what is drawn.
                    if (nowLocked)
                    {
                        var v = prop.vector4Value.x;
                        prop.vector4Value = new Vector4(v, v, v, v);
                    }
                    EditorPrefs.SetBool(prefKey, nowLocked);
                }
                locked = nowLocked;
            }

            if (locked) return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUI.showMixedValue = prop.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                var value = prop.vector4Value;
                value.x = Clamp(EditorGUILayout.FloatField(labels[0], value.x));
                value.y = Clamp(EditorGUILayout.FloatField(labels[1], value.y));
                value.z = Clamp(EditorGUILayout.FloatField(labels[2], value.z));
                value.w = Clamp(EditorGUILayout.FloatField(labels[3], value.w));
                if (EditorGUI.EndChangeCheck())
                    prop.vector4Value = value;
                EditorGUI.showMixedValue = false;
            }
        }

        private void DrawCurvature()
        {
            // A row of preset buttons; the float is the serialized truth. When the curvature sits between
            // presets (a custom value on the slider) no button is highlighted, rather than showing a Custom
            // button that would do nothing when clicked.
            var selected = PresetIndexFor(_cornerCurvature.floatValue);
            var fieldRect = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), s_cornerShapeLabel);

            EditorGUI.showMixedValue = _cornerCurvature.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            var clicked = GUI.Toolbar(fieldRect, selected, s_shapeNames);
            if (EditorGUI.EndChangeCheck())
                _cornerCurvature.floatValue = ((CornerShape)clicked).ToCurvature();
            EditorGUI.showMixedValue = false;

            using (new EditorGUI.IndentLevelScope())
                EditorGUILayout.PropertyField(_cornerCurvature, new GUIContent("Curvature", "0 = circular, 1 = squarest (cubic)."));
        }

        // The preset index whose curvature matches, or -1 when the value sits between presets (custom).
        private static int PresetIndexFor(float curvature)
        {
            foreach (CornerShape shape in Enum.GetValues(typeof(CornerShape)))
            {
                if (Mathf.Abs(shape.ToCurvature() - curvature) < PresetTolerance)
                    return (int)shape;
            }

            return -1;
        }

        private static void Header(string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
    }
}
