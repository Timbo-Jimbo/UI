using System;
using TimboJimbo.UI;
using UnityEditor;
using UnityEditor.UI;
using UnityEngine;

namespace TimboJimboEditor.UI
{
    /// <summary>
    /// Inspector for <see cref="Box"/>: its colour (the tint over every layer), the material, the shape, the three
    /// layers in the order they are drawn, the effects, then the raycast and mask controls. Each layer is a header
    /// with a toggle that turns it on, which folds open to the layer's paint and edge.
    /// </summary>
    [CustomEditor(typeof(Box))]
    [CanEditMultipleObjects]
    public sealed class BoxEditor : GraphicEditor
    {
        private const string RadiiLockedPrefKey = "TimboJimbo.UI.Box.RadiiLocked";
        private const string InsetLockedPrefKey = "TimboJimbo.UI.Box.InsetLocked";
        private const float PresetTolerance = 1e-3f;

        private static readonly string[] s_shapeNames = Enum.GetNames(typeof(CornerShape));
        private const float FoldoutWidth = 14f;

        private static readonly GUIContent s_colorLabel = new("Color", "Tints every layer.");
        private static readonly GUIContent s_shadowLabel = new("Shadow", "Drawn first, under the others; usually a drop shadow.");
        private static readonly GUIContent s_fillLabel = new("Fill", "Drawn second; usually the box's fill.");
        private static readonly GUIContent s_borderLabel = new("Border", "Drawn last, over the others; usually a border ring.");
        private static readonly GUIContent s_maskingLabel = new("Masking", "None: no masking. Draw and Mask: draw the box and clip its children to everything it draws (a shadow or a ring outside the edge widens the mask). Mask Only: clip children to the plain shape without drawing the box (the layers are ignored).");
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
        private SerializedProperty _shadow;
        private SerializedProperty _fill;
        private SerializedProperty _border;
        private SerializedProperty _blendMode;
        private GradientSection _shadowGradient;
        private GradientSection _fillGradient;
        private GradientSection _borderGradient;

        protected override void OnEnable()
        {
            base.OnEnable();
            _masking = serializedObject.FindProperty("_masking");
            _concentric = serializedObject.FindProperty("_concentric");
            _cornerRadii = serializedObject.FindProperty("_cornerRadii");
            _cornerCurvature = serializedObject.FindProperty("_cornerCurvature");
            _inset = serializedObject.FindProperty("_inset");
            _shadow = serializedObject.FindProperty("_shadow");
            _fill = serializedObject.FindProperty("_fill");
            _border = serializedObject.FindProperty("_border");
            _blendMode = serializedObject.FindProperty("_blendMode");
            _shadowGradient = new GradientSection(_shadow);
            _fillGradient = new GradientSection(_fill);
            _borderGradient = new GradientSection(_border);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Mask Only draws its plain shape instead of its layers, so the colour and the layers are hidden.
            // The Masking control itself is drawn lower down, next to the raycast and mask controls.
            var maskOnly = !_masking.hasMultipleDifferentValues && _masking.enumValueIndex == (int)MaskMode.MaskOnly;

            if (!maskOnly)
                EditorGUILayout.PropertyField(m_Color, s_colorLabel);

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

            if (!maskOnly)
            {
                EditorGUILayout.Space();
                Header("Layers");
                DrawLayer(_shadow, _shadowGradient, s_shadowLabel);
                DrawLayer(_fill, _fillGradient, s_fillLabel);
                DrawLayer(_border, _borderGradient, s_borderLabel);
            }

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

        // A layer as a header row (a foldout arrow and the toggle that turns it on) and, folded open, its paint (colour and
        // gradient) and its edge (stroke, blur, offset, spread). The fold state is the property's own isExpanded.
        private static void DrawLayer(SerializedProperty layer, GradientSection gradient, GUIContent label)
        {
            var enabled = layer.FindPropertyRelative(nameof(BoxLayer.Enabled));
            var row = EditorGUILayout.GetControlRect();
            layer.isExpanded = EditorGUI.Foldout(new Rect(row.x, row.y, FoldoutWidth, row.height), layer.isExpanded, GUIContent.none, true);

            var toggleRect = new Rect(row.x + FoldoutWidth, row.y, row.width - FoldoutWidth, row.height);
            var toggleLabel = EditorGUI.BeginProperty(toggleRect, label, enabled);
            EditorGUI.showMixedValue = enabled.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            var on = EditorGUI.ToggleLeft(toggleRect, toggleLabel, enabled.boolValue);
            if (EditorGUI.EndChangeCheck())
                enabled.boolValue = on;
            EditorGUI.showMixedValue = false;
            EditorGUI.EndProperty();

            if (!layer.isExpanded)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(layer.FindPropertyRelative(nameof(BoxLayer.Color)));
                gradient.Draw();
                EditorGUILayout.PropertyField(layer.FindPropertyRelative(nameof(BoxLayer.Stroke)));
                EditorGUILayout.PropertyField(layer.FindPropertyRelative(nameof(BoxLayer.Blur)));
                EditorGUILayout.PropertyField(layer.FindPropertyRelative(nameof(BoxLayer.Offset)));
                EditorGUILayout.PropertyField(layer.FindPropertyRelative(nameof(BoxLayer.Spread)));
            }
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
