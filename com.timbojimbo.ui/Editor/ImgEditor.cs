using TimboJimbo.UI;
using UnityEditor;
using UnityEditor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace TimboJimboEditor.UI
{
    /// <summary>
    /// A purpose-built inspector for <see cref="Img"/>. It does not fall back to the stock Image inspector:
    /// what is drawn (Sprite or Texture, colour, gradient), the material, the image type, the effects
    /// (blend mode, colour blend, blur) and the raycast / mask controls are laid out here in that order.
    /// </summary>
    [CustomEditor(typeof(Img))]
    [CanEditMultipleObjects]
    public sealed class ImgEditor : GraphicEditor
    {
        // Fill-origin option labels, indexed by Image.FillMethod, ported from Unity's ImageEditor.
        private static readonly string[] s_originHorizontal = { "Left", "Right" };
        private static readonly string[] s_originVertical = { "Bottom", "Top" };
        private static readonly string[] s_origin90 = { "BottomLeft", "TopLeft", "TopRight", "BottomRight" };
        private static readonly string[] s_origin180 = { "Bottom", "Left", "Top", "Right" };
        private static readonly string[] s_origin360 = { "Bottom", "Right", "Top", "Left" };

        private SerializedProperty _sprite;
        private SerializedProperty _texture;
        private SerializedProperty _type;
        private SerializedProperty _preserveAspect;
        private SerializedProperty _useSpriteMesh;
        private SerializedProperty _fillCenter;
        private SerializedProperty _fillMethod;
        private SerializedProperty _fillOrigin;
        private SerializedProperty _fillAmount;
        private SerializedProperty _fillClockwise;
        private SerializedProperty _pixelsPerUnitMultiplier;
        private SerializedProperty _tileSize;
        private SerializedProperty _tileSpriteRotation;
        private SerializedProperty _tileGridRotation;
        private SerializedProperty _tileSpacing;
        private SerializedProperty _tileStagger;
        private SerializedProperty _tileOffset;
        private SerializedProperty _tilePan;

        private SerializedProperty _blendMode;
        private SerializedProperty _colorBlendMode;
        private SerializedProperty _colorBlendFactor;
        private SerializedProperty _blurRadius;
        private SerializedProperty _blurQuality;
        private SerializedProperty _blurJitter;
        private GradientSection _gradient;

        protected override void OnEnable()
        {
            base.OnEnable();
            _sprite = serializedObject.FindProperty("m_Sprite");
            _texture = serializedObject.FindProperty("_texture");
            _type = serializedObject.FindProperty("m_Type");
            _preserveAspect = serializedObject.FindProperty("m_PreserveAspect");
            _useSpriteMesh = serializedObject.FindProperty("m_UseSpriteMesh");
            _fillCenter = serializedObject.FindProperty("m_FillCenter");
            _fillMethod = serializedObject.FindProperty("m_FillMethod");
            _fillOrigin = serializedObject.FindProperty("m_FillOrigin");
            _fillAmount = serializedObject.FindProperty("m_FillAmount");
            _fillClockwise = serializedObject.FindProperty("m_FillClockwise");
            _pixelsPerUnitMultiplier = serializedObject.FindProperty("m_PixelsPerUnitMultiplier");
            _tileSize = serializedObject.FindProperty("_tileSize");
            _tileSpriteRotation = serializedObject.FindProperty("_tileSpriteRotation");
            _tileGridRotation = serializedObject.FindProperty("_tileGridRotation");
            _tileSpacing = serializedObject.FindProperty("_tileSpacing");
            _tileStagger = serializedObject.FindProperty("_tileStagger");
            _tileOffset = serializedObject.FindProperty("_tileOffset");
            _tilePan = serializedObject.FindProperty("_tilePan");

            _blendMode = serializedObject.FindProperty("_blendMode");
            _colorBlendMode = serializedObject.FindProperty("_colorBlendMode");
            _colorBlendFactor = serializedObject.FindProperty("_colorBlendFactor");
            _blurRadius = serializedObject.FindProperty("_blurRadius");
            _blurQuality = serializedObject.FindProperty("_blurQuality");
            _blurJitter = serializedObject.FindProperty("_blurJitter");
            _gradient = new GradientSection(serializedObject);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var textureChanged = DrawSource();
            EditorGUILayout.PropertyField(m_Color);
            _gradient.Draw();

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(m_Material);

            EditorGUILayout.Space();
            DrawImageType();

            EditorGUILayout.Space();
            Header("Effects");
            EditorGUILayout.PropertyField(_blendMode, new GUIContent("Blend Mode", "How the image composites with what is behind it."));
            EditorGUILayout.PropertyField(_colorBlendMode, new GUIContent("Color Blend", "How the colour (or gradient) combines with the sprite's own colour. Multiply is the stock Image behaviour."));
            using (new EditorGUI.IndentLevelScope())
                EditorGUILayout.PropertyField(_colorBlendFactor, new GUIContent("Factor", "0 shows the untouched sprite, 1 the fully blended result."));
            DrawBlur();

            EditorGUILayout.Space();
            RaycastControlsGUI();
            MaskableControlsGUI();

            serializedObject.ApplyModifiedProperties();

            // Rebuild the generated sprite here (not in OnValidate) to avoid a DestroyImmediate-in-OnValidate
            // warning; the field is already written by ApplyModifiedProperties above.
            if (textureChanged)
                foreach (var t in targets)
                    ((Img)t).RefreshTexture();
        }

        // Sprite and Texture, stacked. Both show when neither is set; once one is set the empty one hides, so
        // the source in use is the only field. Clear the set one to get both back. Returns whether Texture changed.
        private bool DrawSource()
        {
            var hasSprite = _sprite.hasMultipleDifferentValues || _sprite.objectReferenceValue != null;
            var hasTexture = _texture.hasMultipleDifferentValues || _texture.objectReferenceValue != null;

            if (hasSprite || !hasTexture)
                EditorGUILayout.PropertyField(_sprite, new GUIContent("Sprite"));

            var textureChanged = false;
            if (hasTexture || !hasSprite)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(_texture, new GUIContent("Texture", "Show a plain texture instead of a Sprite, like RawImage."));
                textureChanged = EditorGUI.EndChangeCheck();
                if (textureChanged)
                    OfferSpriteInstead();
            }

            if (hasSprite && hasTexture)
                EditorGUILayout.HelpBox("Texture overrides Sprite while both are set.", MessageType.None);

            return textureChanged;
        }

        // A texture imported as a single Sprite is usually meant to be used as one (atlas packing, borders,
        // pixels per unit), so offer that instead of wrapping the texture in a generated sprite.
        private void OfferSpriteInstead()
        {
            if (_texture.hasMultipleDifferentValues || _texture.objectReferenceValue is not Texture2D texture)
                return;

            var path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path))
                return;

            Sprite single = null;
            var count = 0;
            foreach (var asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
            {
                if (asset is not Sprite s) continue;
                single = s;
                count++;
            }

            if (count != 1)
                return;

            var useSprite = EditorUtility.DisplayDialog(
                "Use the Sprite instead?",
                $"'{texture.name}' is imported as a Sprite. Assign that Sprite rather than wrapping the texture, so its import settings (atlas, border, pixels per unit) apply?",
                "Use Sprite",
                "Keep Texture");
            if (!useSprite)
                return;

            _texture.objectReferenceValue = null;
            _sprite.objectReferenceValue = single;
            serializedObject.ApplyModifiedProperties();
            foreach (var t in targets)
                ((Img)t).RefreshTexture();

            // The modal dialog interrupted this GUI pass; end it cleanly rather than drawing the rest.
            GUIUtility.ExitGUI();
        }

        private void DrawImageType()
        {
            if (_sprite.objectReferenceValue == null && _texture.objectReferenceValue == null)
                return;

            EditorGUILayout.PropertyField(_type, new GUIContent("Image Type"));
            if (_type.hasMultipleDifferentValues)
                return;

            var type = (Image.Type)_type.enumValueIndex;
            using (new EditorGUI.IndentLevelScope())
            {
                switch (type)
                {
                    case Image.Type.Simple:
                        EditorGUILayout.PropertyField(_useSpriteMesh, new GUIContent("Use Sprite Mesh"));
                        EditorGUILayout.PropertyField(_preserveAspect, new GUIContent("Preserve Aspect"));
                        break;

                    case Image.Type.Sliced:
                        EditorGUILayout.PropertyField(_fillCenter, new GUIContent("Fill Center"));
                        EditorGUILayout.PropertyField(_pixelsPerUnitMultiplier, new GUIContent("Pixels Per Unit Multiplier"));
                        break;

                    case Image.Type.Tiled:
                        EditorGUILayout.LabelField("Sprite");
                        using (new EditorGUI.IndentLevelScope())
                        {
                            EditorGUILayout.PropertyField(_tileSize, new GUIContent("Size", "Width of each stamped sprite in canvas units; the height follows the sprite's aspect. 0 uses the sprite's native size."));
                            EditorGUILayout.PropertyField(_tileSpriteRotation, new GUIContent("Rotation", "Degrees, counter-clockwise, each stamp about its placement point; independent of the grid's rotation. Stamps are cut off at their cell's edge."));
                        }
                        EditorGUILayout.LabelField("Grid");
                        using (new EditorGUI.IndentLevelScope())
                        {
                            EditorGUILayout.PropertyField(_tileGridRotation, new GUIContent("Rotation", "Degrees, counter-clockwise about the rect centre. Moves the placement points only; the stamps keep their own rotation."));
                            EditorGUILayout.PropertyField(_tileSpacing, new GUIContent("Spacing", "Distance between placement points per axis, in canvas units (at most 8× the sprite size). 0 = the sprite size, edge to edge. Less than the sprite size cuts each stamp off at its cell."));
                            EditorGUILayout.PropertyField(_tileStagger, new GUIContent("Stagger", "X shifts each successive row along x, Y each successive column along y, as a fraction of the spacing. (0.5, 0) is a brick pattern."));
                            EditorGUILayout.PropertyField(_tileOffset, new GUIContent("Offset", "Shifts the grid along its own axes, in canvas units; wraps every cell."));
                            EditorGUILayout.PropertyField(_tilePan, new GUIContent("Pan", "Scrolls the grid along its own axes, in canvas units per second, on shader time (no mesh rebuild). Changing the rate re-phases the pattern; animate Offset for a controlled slide."));
                        }
                        if (target is Img { hasBorder: true })
                            EditorGUILayout.HelpBox("Tiled ignores the sprite border and tiles the whole sprite. Use Sliced for a 9-slice frame.", MessageType.None);
                        break;

                    case Image.Type.Filled:
                        EditorGUILayout.PropertyField(_fillMethod, new GUIContent("Fill Method"));
                        DrawFillOrigin();
                        EditorGUILayout.PropertyField(_fillAmount, new GUIContent("Fill Amount"));
                        if ((Image.FillMethod)_fillMethod.enumValueIndex != Image.FillMethod.Horizontal &&
                            (Image.FillMethod)_fillMethod.enumValueIndex != Image.FillMethod.Vertical)
                            EditorGUILayout.PropertyField(_fillClockwise, new GUIContent("Clockwise"));
                        EditorGUILayout.PropertyField(_preserveAspect, new GUIContent("Preserve Aspect"));
                        break;
                }
            }
        }

        private void DrawFillOrigin()
        {
            var labels = (Image.FillMethod)_fillMethod.enumValueIndex switch
            {
                Image.FillMethod.Horizontal => s_originHorizontal,
                Image.FillMethod.Vertical => s_originVertical,
                Image.FillMethod.Radial90 => s_origin90,
                Image.FillMethod.Radial180 => s_origin180,
                _ => s_origin360,
            };

            EditorGUI.showMixedValue = _fillOrigin.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            var origin = EditorGUILayout.Popup("Fill Origin", Mathf.Clamp(_fillOrigin.intValue, 0, labels.Length - 1), labels);
            if (EditorGUI.EndChangeCheck())
                _fillOrigin.intValue = origin;
            EditorGUI.showMixedValue = false;
        }

        private void DrawBlur()
        {
            EditorGUILayout.PropertyField(_blurRadius, new GUIContent("Blur", "Radius in canvas units. Simple sprites grow their quad so the blur spills past the edge."));
            if (!_blurRadius.hasMultipleDifferentValues && _blurRadius.floatValue <= 0f)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(_blurQuality, new GUIContent("Quality", "Tap count. Cost scales with quality, not radius."));
                EditorGUILayout.PropertyField(_blurJitter, new GUIContent("Jitter", "Break the coarse-mip block grid of a wide blur into fine grain. On by default."));
            }
            DrawNoMipmapWarning();
        }

        // The blur needs mipmaps; without them the taps spread over level 0 and wide radii read as ghost
        // copies. This is the one blur caveat with no shader-side workaround, so it is the only warning.
        private void DrawNoMipmapWarning()
        {
            var texture = (target as Img)?.mainTexture;
            if (texture == null || texture.mipmapCount > 1) return;

            EditorGUILayout.HelpBox(
                "The source texture has no mipmaps, so radii beyond a few pixels will look ghosted. " +
                "Enable Generate Mip Maps on the texture or its Sprite Atlas.",
                MessageType.Warning);
        }

        private static void Header(string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
    }
}
