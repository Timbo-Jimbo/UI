using TimboJimbo.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TimboJimbo.UI
{
    /// <summary>
    /// An <see cref="Image"/> with a gradient tint and a blend mode. Everything Image does (sprite types,
    /// preserve aspect, fill, sprite meshes, raycast hit testing, layout) is inherited unchanged; this class
    /// only rewrites the vertices Image generates so the shader can evaluate the gradient per pixel across the
    /// rect, and swaps the material for the package's shared per-blend-mode one. The gradient multiplies into
    /// the sprite exactly as <see cref="Graphic.color"/> does, and that colour still tints the whole result.
    /// </summary>
    [AddComponentMenu("Timbo Jimbo/UI/Img")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class Img : Image
#if TJ_UI_LAYOUT
        , TimboJimbo.UI.Layout.ILayoutMeasurable
#endif
    {
#if TJ_UI_LAYOUT
        // With the UI Layout package present an Img is a LayoutNode's content directly: its sprite's size.
        Vector2 TimboJimbo.UI.Layout.ILayoutMeasurable.Measure(float availableWidth) => new(preferredWidth, preferredHeight);
        float TimboJimbo.UI.Layout.ILayoutMeasurable.MinWidth => 0f;
        bool TimboJimbo.UI.Layout.ILayoutMeasurable.SizeIsAnimatable => true;
#endif

        /// <summary>Base name of the shared materials in the package Resources folder; the blend mode adds a suffix.</summary>
        public const string MaterialName = "Img";

        private const AdditionalCanvasShaderChannels RequiredChannels =
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.TexCoord2 |
            AdditionalCanvasShaderChannels.TexCoord3 |
            AdditionalCanvasShaderChannels.Normal |
            AdditionalCanvasShaderChannels.Tangent;

        /// <summary>Extra quad padding beyond the blur radius so the soft edge has room for its last tap.</summary>
        private const float BlurPadding = 1f;

        // A plain texture shown instead of a Sprite (like RawImage). It is wrapped in a generated full-rect
        // sprite and pushed through overrideSprite, which takes precedence over Source Image, so only one
        // renders at a time and the serialized sprite is left untouched.
        [SerializeField] private Texture2D _texture;
        [System.NonSerialized] private Sprite _generatedSprite;

        [SerializeField] private UiBlendMode _blendMode = UiBlendMode.Normal;

        [SerializeField] private ColorBlendMode _colorBlendMode = ColorBlendMode.Multiply;
        [SerializeField, Range(0f, 1f)] private float _colorBlendFactor = 1f;

        // The blur is a single pass in the shader (see Img.shader). A multi-pass method could be added later
        // as a separate option alongside these fields; radius and quality would keep their meaning.
        [SerializeField, Min(0f)] private float _blurRadius;
        [SerializeField] private BlurQuality _blurQuality = BlurQuality.Medium;
        [SerializeField] private bool _blurJitter = true;

        // Tiled: Img draws the tile grid in the shader from a single quad (see Img.shader) instead of Image's
        // one-quad-per-tile mesh, which is what makes the sprite and grid controls possible. The grid only
        // places stamps; the sprite settings say what is stamped there. The sprite's border and Fill Center
        // are not used in this type; use Sliced for a 9-slice frame.
        [SerializeField, Min(0f)] private float _tileSize;
        [SerializeField] private float _tileSpriteRotation;
        [SerializeField] private float _tileGridRotation;
        [SerializeField] private Vector2 _tileSpacing;
        [SerializeField] private Vector2 _tileStagger;
        [SerializeField] private Vector2 _tileOffset;
        [SerializeField] private Vector2 _tilePan;

        /// <summary>Largest spacing, as a multiple of the sprite size, the packed vertex data can carry. Mirrors TILE_SPACING_RANGE in Img.shader.</summary>
        private const float TileSpacingRangeSprites = 8f;

        /// <summary>Largest pan rate, in periods per second, the packed vertex data can carry. Mirrors TILE_PAN_RANGE in Img.shader.</summary>
        private const float TilePanRangePeriods = 8f;

        [SerializeField] private bool _gradientEnabled;
        [SerializeField] private ColorInterpolationMode _gradientMode = ColorInterpolationMode.OkLab;
        [SerializeField] private bool _gradientUseVia;
        [SerializeField] private Color _gradientFrom = Color.white;
        [SerializeField] private Color _gradientVia = new(0.5f, 0.5f, 0.5f, 1f);
        [SerializeField] private Color _gradientTo = Color.black;
        [SerializeField, Range(0f, 1f)] private float _gradientViaPosition = 0.5f;
        [SerializeField] private float _gradientAngle;

        /// <summary>
        /// How the image composites with what is behind it. Each mode resolves to its own shared material, so
        /// images of the same blend mode still batch together; different modes are separate draw calls.
        /// </summary>
        public UiBlendMode BlendMode
        {
            get => _blendMode;
            set
            {
                if (_blendMode == value) return;
                _blendMode = value;
                SetMaterialDirty();
            }
        }

        /// <summary>
        /// How the colour (flat <see cref="Graphic.color"/> or the gradient) combines with the sprite's own
        /// colour. Multiply is the stock Image behaviour. Alpha is always sprite alpha times colour alpha.
        /// </summary>
        public ColorBlendMode ColorBlendMode
        {
            get => _colorBlendMode;
            set
            {
                if (_colorBlendMode == value) return;
                _colorBlendMode = value;
                SetVerticesDirty();
            }
        }

        /// <summary>Strength of the colour blend: 0 shows the untouched sprite, 1 the fully blended result.</summary>
        public float ColorBlendFactor
        {
            get => _colorBlendFactor;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(_colorBlendFactor, value)) return;
                _colorBlendFactor = value;
                SetVerticesDirty();
            }
        }

        /// <summary>
        /// Blur radius in canvas units; 0 draws the sprite sharply. Simple sprites grow their quad by the
        /// radius so the blur can spill past the sprite's edge; sliced, tiled, filled and mesh sprites blur
        /// within their own geometry. Large radii need mipmaps on the sprite texture to stay smooth.
        /// </summary>
        public float BlurRadius
        {
            get => _blurRadius;
            set
            {
                value = Mathf.Max(0f, value);
                if (Mathf.Approximately(_blurRadius, value)) return;
                _blurRadius = value;
                SetVerticesDirty();
            }
        }

        /// <summary>Tap count of the blur. Cost scales with quality, not radius.</summary>
        public BlurQuality BlurQuality
        {
            get => _blurQuality;
            set
            {
                if (_blurQuality == value) return;
                _blurQuality = value;
                SetVerticesDirty();
            }
        }

        /// <summary>
        /// When true (the default) a wide blur dithers its taps so the coarse-mip block grid breaks into fine
        /// grain instead of visible blocks. The flag rides in vertex data, so toggling it keeps the shared
        /// material and does not add a draw call.
        /// </summary>
        public bool BlurJitter
        {
            get => _blurJitter;
            set
            {
                if (_blurJitter == value) return;
                _blurJitter = value;
                SetVerticesDirty();
            }
        }

        /// <summary>
        /// Tiled type: tile width in canvas units; the height follows the sprite's aspect. 0 uses the sprite's
        /// native size on the canvas (its pixel width over pixels per unit). 50 in a 100×100 rect is a 2×2 grid.
        /// </summary>
        public float TileSize
        {
            get => _tileSize;
            set
            {
                value = Mathf.Max(0f, value);
                if (Mathf.Approximately(_tileSize, value)) return;
                _tileSize = value;
                SetVerticesDirty();
            }
        }

        /// <summary>Tiled type: rotation of each stamped sprite about its placement point, in degrees counter-clockwise, independent of the grid's rotation. A stamp is cut off at its cell's edge.</summary>
        public float TileSpriteRotation
        {
            get => _tileSpriteRotation;
            set
            {
                if (Mathf.Approximately(_tileSpriteRotation, value)) return;
                _tileSpriteRotation = value;
                SetVerticesDirty();
            }
        }

        /// <summary>Tiled type: rotation of the placement grid in degrees, counter-clockwise about the rect centre. Moves where sprites are stamped, not how they are turned.</summary>
        public float TileGridRotation
        {
            get => _tileGridRotation;
            set
            {
                if (Mathf.Approximately(_tileGridRotation, value)) return;
                _tileGridRotation = value;
                SetVerticesDirty();
            }
        }

        /// <summary>
        /// Tiled type: distance between placement points per axis, in canvas units (at most 8× the sprite
        /// size). 0 on an axis means the sprite size, so sprites sit edge to edge. Spacing below the sprite
        /// size cuts each stamp off at its cell's edge rather than overlapping.
        /// </summary>
        public Vector2 TileSpacing
        {
            get => _tileSpacing;
            set
            {
                value = Vector2.Max(value, Vector2.zero);
                if (_tileSpacing == value) return;
                _tileSpacing = value;
                SetVerticesDirty();
            }
        }

        /// <summary>
        /// Tiled type: shifts each successive row along x (x component) and each successive column along y
        /// (y component), as a fraction of the spacing. (0.5, 0) is a brick pattern.
        /// </summary>
        public Vector2 TileStagger
        {
            get => _tileStagger;
            set
            {
                if (_tileStagger == value) return;
                _tileStagger = value;
                SetVerticesDirty();
            }
        }

        /// <summary>Tiled type: shifts the grid along its own axes, in canvas units; wraps every period.</summary>
        public Vector2 TileOffset
        {
            get => _tileOffset;
            set
            {
                if (_tileOffset == value) return;
                _tileOffset = value;
                SetVerticesDirty();
            }
        }

        /// <summary>
        /// Tiled type: scrolls the grid along its own axes, in canvas units per second, driven by shader time
        /// so no mesh is rebuilt while it moves (at most 8 periods per second). Changing the rate re-phases the
        /// pattern, so animate <see cref="TileOffset"/> instead for a controlled slide.
        /// </summary>
        public Vector2 TilePan
        {
            get => _tilePan;
            set
            {
                if (_tilePan == value) return;
                _tilePan = value;
                SetVerticesDirty();
            }
        }

        /// <summary>
        /// When true the sprite is tinted by a three-stop gradient (from → via → to) across the rect instead
        /// of a flat colour. The stops carry their own colour and alpha, and <see cref="Graphic.color"/>
        /// multiplies the whole gradient as a tint, so colour changes and CanvasGroup fades apply to every stop.
        /// </summary>
        public bool GradientEnabled
        {
            get => _gradientEnabled;
            set
            {
                if (_gradientEnabled == value) return;
                _gradientEnabled = value;
                SetVerticesDirty();
            }
        }

        /// <summary>Colour space the gradient is interpolated in, matching the Property Bindings package's modes.</summary>
        public ColorInterpolationMode GradientMode
        {
            get => _gradientMode;
            set
            {
                if (_gradientMode == value) return;
                _gradientMode = value;
                SetVerticesDirty();
            }
        }

        /// <summary>When false the via stop is ignored and the gradient runs straight from → to.</summary>
        public bool GradientUseVia
        {
            get => _gradientUseVia;
            set
            {
                if (_gradientUseVia == value) return;
                _gradientUseVia = value;
                SetVerticesDirty();
            }
        }

        /// <summary>Start stop of the gradient.</summary>
        public Color GradientFrom
        {
            get => _gradientFrom;
            set
            {
                if (_gradientFrom == value) return;
                _gradientFrom = value;
                SetVerticesDirty();
            }
        }

        /// <summary>Middle stop of the gradient, placed at <see cref="GradientViaPosition"/>.</summary>
        public Color GradientVia
        {
            get => _gradientVia;
            set
            {
                if (_gradientVia == value) return;
                _gradientVia = value;
                SetVerticesDirty();
            }
        }

        /// <summary>End stop of the gradient.</summary>
        public Color GradientTo
        {
            get => _gradientTo;
            set
            {
                if (_gradientTo == value) return;
                _gradientTo = value;
                SetVerticesDirty();
            }
        }

        /// <summary>Position of the via stop along the gradient, 0 at the from end and 1 at the to end.</summary>
        public float GradientViaPosition
        {
            get => _gradientViaPosition;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(_gradientViaPosition, value)) return;
                _gradientViaPosition = value;
                SetVerticesDirty();
            }
        }

        /// <summary>Gradient direction in degrees. 0 runs left → right, 90 runs bottom → top, increasing counter-clockwise.</summary>
        public float GradientAngle
        {
            get => _gradientAngle;
            set
            {
                if (Mathf.Approximately(_gradientAngle, value)) return;
                _gradientAngle = value;
                SetVerticesDirty();
            }
        }

        /// <summary>
        /// The shared material for this image's blend mode. Image bypasses this for sprites packed with an
        /// ETC1 alpha-split texture (it returns its own ETC1 material), which is not a supported combination.
        /// </summary>
        public override Material defaultMaterial => SharedMaterials.For(MaterialName, _blendMode, base.defaultMaterial);

        /// <summary>
        /// A plain texture to display instead of a Sprite, wrapped in a generated full-rect sprite the way
        /// RawImage shows a raw texture. While set it takes precedence over Source Image; clear it to fall back
        /// to the sprite. Only one renders at a time. Must be a <see cref="Texture2D"/> (Sprite.Create needs one).
        /// </summary>
        public Texture2D Texture
        {
            get => _texture;
            set
            {
                if (_texture == value) return;
                _texture = value;
                ApplyTexture();
            }
        }

        /// <summary>Rebuilds the generated sprite from the current <see cref="Texture"/>. The editor calls this
        /// after the field changes; runtime code should assign the <see cref="Texture"/> property instead.</summary>
        public void RefreshTexture() => ApplyTexture();

        private void ApplyTexture()
        {
            if (_generatedSprite != null)
            {
                if (overrideSprite == _generatedSprite) overrideSprite = null;
                DestroyGeneratedSprite();
            }

            if (_texture != null)
            {
                _generatedSprite = Sprite.Create(_texture, new Rect(0f, 0f, _texture.width, _texture.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                _generatedSprite.name = $"Img Generated ({_texture.name})";
                _generatedSprite.hideFlags = HideFlags.HideAndDontSave;
                overrideSprite = _generatedSprite;
            }
        }

        private void DestroyGeneratedSprite()
        {
            if (_generatedSprite == null) return;
            if (Application.isPlaying) Destroy(_generatedSprite); else DestroyImmediate(_generatedSprite);
            _generatedSprite = null;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            CanvasChannels.Ensure(this, RequiredChannels);
            ApplyTexture();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (overrideSprite == _generatedSprite) overrideSprite = null;
            DestroyGeneratedSprite();
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            CanvasChannels.Ensure(this, RequiredChannels);
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            CanvasChannels.Ensure(this, RequiredChannels);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            var rect = GetPixelAdjustedRect();
            var halfSize = rect.size * 0.5f;
            var center = rect.center;

            // The sprite actually being drawn: the texture override if present, otherwise Source Image.
            var drawnSprite = overrideSprite != null ? overrideSprite : sprite;
            var tiled = type == Type.Tiled && drawnSprite != null;

            // Let Image build the sprite geometry, then stamp the effect descriptors onto every vertex. uv0 is
            // the sprite's atlas UV, so the position within the rect is derived from the vertex position
            // instead; it spans the full rect, so a preserved-aspect or filled sprite shows the part of the
            // gradient it covers, like a CSS background behind a clipped image. Tiled is the exception: one
            // quad covers the rect and the shader lays the tiles out from the rect-centred position.
            if (tiled)
                BuildRectQuad(vh, rect);
            else
                base.OnPopulateMesh(vh);

            UiPacking.PackStops(_gradientFrom, _gradientVia, _gradientTo, _gradientUseVia, _gradientViaPosition, out var normal, out var tangent);

            // The shader must not sample neighbouring sprites in an atlas, so it gets the sprite's UV bounds:
            // every sample is kept half a texel inside them and blur taps outside them read as transparent.
            var bounds = drawnSprite != null ? UnityEngine.Sprites.DataUtility.GetOuterUV(drawnSprite) : new Vector4(0f, 0f, 1f, 1f);
            var tileSize = tiled ? TileSizeCanvas(drawnSprite) : Vector2.zero;

            // Blur params travel per vertex too, so blurred and sharp images share the material.
            var texture = mainTexture;
            var texelSize = texture != null ? new Vector2(1f / texture.width, 1f / texture.height) : Vector2.zero;
            var uvPerUnit = tiled
                ? new Vector2((bounds.z - bounds.x) / tileSize.x, (bounds.w - bounds.y) / tileSize.y)
                : UvPerCanvasUnit(vh, texelSize);
            ComputeBlur(uvPerUnit, texelSize, out var blurTaps, out var tapSpacing);

            // The tiled layout is packed relative to the sprite so it is scale-free: spacing as a multiple of
            // the sprite size, offset and pan as a fraction of the spacing (a pan of one spacing per second
            // moves one cell per second), stagger as a fraction of the spacing. The two rotations share a
            // float. See Img.shader for the decode.
            var tilePacked = Vector4.zero;   // rotPair, spacingPair, offPair, panPair
            var stagger = Vector2.zero;
            if (tiled)
            {
                var spacing01 = new Vector2(
                    Mathf.Clamp01((_tileSpacing.x > 0f ? _tileSpacing.x : tileSize.x) / (tileSize.x * TileSpacingRangeSprites)),
                    Mathf.Clamp01((_tileSpacing.y > 0f ? _tileSpacing.y : tileSize.y) / (tileSize.y * TileSpacingRangeSprites)));
                var period = Vector2.Max(Vector2.Scale(spacing01, tileSize) * TileSpacingRangeSprites, new Vector2(1e-3f, 1e-3f));
                stagger = new Vector2(Mathf.Repeat(_tileStagger.x, 1f), Mathf.Repeat(_tileStagger.y, 1f));
                tilePacked = new Vector4(
                    UiPacking.PackPair12(Mathf.Repeat(_tileGridRotation / 360f, 1f), Mathf.Repeat(_tileSpriteRotation / 360f, 1f)),
                    UiPacking.PackPair12(spacing01.x, spacing01.y),
                    UiPacking.PackPair12(Mathf.Repeat(_tileOffset.x / period.x, 1f), Mathf.Repeat(_tileOffset.y / period.y, 1f)),
                    UiPacking.PackPair12(
                        (Mathf.Clamp(_tilePan.x / period.x, -TilePanRangePeriods, TilePanRangePeriods) / TilePanRangePeriods + 1f) * 0.5f,
                        (Mathf.Clamp(_tilePan.y / period.y, -TilePanRangePeriods, TilePanRangePeriods) / TilePanRangePeriods + 1f) * 0.5f));
            }

            // Channel layout (see the contract at the top of Img.shader). Small integers ride with a 0..1
            // fraction in one float (UiPacking.PackIntAndFraction): the header folds the gradient mode, blur
            // taps, flags and colour blend mode around the gradient angle; the blend factor shares a float
            // with the stagger. Tiled quads keep their corner in uv0.xy (the shader derives the position from
            // it) so uv1.zw is free for tile data. No extra vertex channels, so every variation still shares
            // one material.
            var header = UiPacking.PackImgHeader(_gradientEnabled, _gradientMode, blurTaps, _blurJitter, tiled, _colorBlendMode, _gradientAngle);
            var factorAndStagger = UiPacking.PackTriple8(_colorBlendFactor, stagger.x, stagger.y);
            var spacingCanvas = blurTaps > 0 ? _blurRadius / blurTaps : 0f;

            if (blurTaps > 0 && drawnSprite != null && type == Type.Simple && !useSpriteMesh && vh.currentVertCount == 4)
                ExpandSimpleQuad(vh, _blurRadius + BlurPadding, uvPerUnit);

            var vertex = new UIVertex();
            for (var i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                var sample = (Vector2)vertex.position - center;
                vertex.uv0 = tiled
                    ? new Vector4(vertex.uv0.x, vertex.uv0.y, tileSize.x, spacingCanvas)
                    : new Vector4(vertex.uv0.x, vertex.uv0.y, tapSpacing.x, tapSpacing.y);
                vertex.uv1 = tiled
                    ? new Vector4(halfSize.x, halfSize.y, tilePacked.x, tilePacked.y)
                    : new Vector4(halfSize.x, halfSize.y, sample.x, sample.y);
                vertex.uv2 = new Vector4(header, tilePacked.z, tilePacked.w, factorAndStagger);
                vertex.uv3 = bounds;
                vertex.normal = normal;
                vertex.tangent = tangent;
                vh.SetUIVertex(vertex, i);
            }
        }

        // One quad over the rect, in Image's vertex order (BL, TL, TR, BR), with the corner (0/1) in uv0.xy.
        private void BuildRectQuad(VertexHelper vh, Rect rect)
        {
            Color32 tint = color;
            vh.Clear();
            vh.AddVert(new Vector3(rect.xMin, rect.yMin), tint, new Vector4(0f, 0f));
            vh.AddVert(new Vector3(rect.xMin, rect.yMax), tint, new Vector4(0f, 1f));
            vh.AddVert(new Vector3(rect.xMax, rect.yMax), tint, new Vector4(1f, 1f));
            vh.AddVert(new Vector3(rect.xMax, rect.yMin), tint, new Vector4(1f, 0f));
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

        // Tile size in canvas units: the configured width (or the sprite's native width when 0), with the
        // height following the sprite's aspect so tiles are never stretched.
        private Vector2 TileSizeCanvas(Sprite drawnSprite)
        {
            var pixels = drawnSprite.rect.size;
            var width = _tileSize > 0f ? _tileSize : pixels.x / Mathf.Max(pixelsPerUnit, 1e-3f);
            width = Mathf.Max(width, 1e-3f);
            var height = width * pixels.y / Mathf.Max(pixels.x, 1e-3f);
            return new Vector2(width, Mathf.Max(height, 1e-3f));
        }

        /// <summary>
        /// Target tap spacing in source texels. Facets in the blur are this size (in source pixels), so a
        /// couple of texels reads as smooth while keeping the tap count and mip as low as possible.
        /// </summary>
        private const float TargetSpacingTexels = 2f;

        /// <summary>
        /// Chooses the blur's tap count and per-axis tap spacing for the current geometry. The tap count grows
        /// with the radius toward the quality budget so a small blur is cheap; past the budget the spacing
        /// grows instead, so wide blur softens rather than spiking in cost. The shader picks the mip whose
        /// texel pitch matches the spacing (see BLUR_MIP_BIAS in Img.shader), so each tap's bilinear footprint
        /// tiles edge-to-edge with its neighbours and the result is smooth.
        /// </summary>
        private void ComputeBlur(Vector2 uvPerUnit, Vector2 texelSize, out int taps, out Vector2 tapSpacing)
        {
            if (_blurRadius <= 0f)
            {
                taps = 0;
                tapSpacing = Vector2.zero;
                return;
            }

            var radiusTexels = _blurRadius * Mathf.Max(SafeRatio(uvPerUnit.x, texelSize.x), SafeRatio(uvPerUnit.y, texelSize.y));
            // Kept mobile-friendly: 3x3 / 5x5 / 7x7 taps at most. Texture samples are the expensive part on
            // tile GPUs, so wide blur reaches further through coarser mips (and, in future, a multi-pass path)
            // rather than through more taps.
            var maxTaps = (int)_blurQuality;   // Low up to 3x3, Medium 5x5, High 7x7 taps
            taps = Mathf.Clamp(Mathf.RoundToInt(radiusTexels / TargetSpacingTexels), 1, maxTaps);
            tapSpacing = _blurRadius * uvPerUnit / taps;
        }

        private static float SafeRatio(float a, float b) => b > 1e-6f ? a / b : 0f;

        // How much uv one canvas unit covers, per axis, for the geometry Image just built (Tiled is computed
        // from the tile size instead, since its uv is derived in the shader).
        private Vector2 UvPerCanvasUnit(VertexHelper vh, Vector2 texelSize)
        {
            if (type == Type.Sliced)
                return texelSize * multipliedPixelsPerUnit;

            var vertex = new UIVertex();
            var minPos = new Vector2(float.MaxValue, float.MaxValue);
            var maxPos = new Vector2(float.MinValue, float.MinValue);
            var minUv = new Vector2(float.MaxValue, float.MaxValue);
            var maxUv = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                minPos = Vector2.Min(minPos, vertex.position);
                maxPos = Vector2.Max(maxPos, vertex.position);
                minUv = Vector2.Min(minUv, vertex.uv0);
                maxUv = Vector2.Max(maxUv, vertex.uv0);
            }

            var size = maxPos - minPos;
            if (size.x <= 0f || size.y <= 0f) return Vector2.zero;
            return (maxUv - minUv) / size;
        }

        // Grows a simple sprite's quad outward by the padding, extrapolating the UVs at the same rate so the
        // sprite stays where it was and the ring around it samples outside its bounds (transparent), which is
        // where the blur's soft edge lands.
        private static void ExpandSimpleQuad(VertexHelper vh, float padding, Vector2 uvPerUnit)
        {
            var vertex = new UIVertex();
            var mid = Vector2.zero;
            for (var i = 0; i < 4; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                mid += (Vector2)vertex.position * 0.25f;
            }

            for (var i = 0; i < 4; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                var sign = new Vector2(Mathf.Sign(vertex.position.x - mid.x), Mathf.Sign(vertex.position.y - mid.y));
                vertex.position += (Vector3)(sign * padding);
                vertex.uv0 += (Vector4)(sign * padding * uvPerUnit);
                vh.SetUIVertex(vertex, i);
            }
        }
    }
}
