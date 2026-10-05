using UnityEngine;
using UnityEngine.UI;

namespace TimboJimbo.UI
{
    /// <summary>
    /// Draws a rounded box as a signed distance field, in up to three layers drawn back to front: a
    /// <see cref="Shadow"/>, a <see cref="Fill"/> and a <see cref="Border"/>. The box owns the shape (corner radii and
    /// curvature, concentric corners, inset) and each layer paints it (see <see cref="BoxLayer"/>), so a card with a
    /// drop shadow and a gradient border is one object. <see cref="Graphic.color"/> tints every layer. Every parameter
    /// travels in vertex data and all boxes share one material per blend mode, so a box drawing all three layers is
    /// still one draw call, and boxes batch with each other. Where more than one of a layer is needed, stack boxes as
    /// separate objects.
    /// </summary>
    [AddComponentMenu("Timbo Jimbo/UI/Box")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class Box : MaskableGraphic
    {
        /// <summary>Base name of the shared materials in the package Resources folder; the blend mode adds a suffix.</summary>
        public const string MaterialName = "Box";

        /// <summary>Extra quad padding so crisp edges have room for their anti-aliasing ramp.</summary>
        private const float AntiAliasPadding = 1f;

        private const AdditionalCanvasShaderChannels RequiredChannels =
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.TexCoord2 |
            AdditionalCanvasShaderChannels.TexCoord3;

        // What a MaskOnly box draws in place of its layers: the plain shape, crisp and opaque white, so it writes a clean
        // stencil.
        private static readonly BoxLayer MaskShape = new(Color.white);

        [SerializeField] private UiBlendMode _blendMode = UiBlendMode.Normal;

        // Drives a Mask component on this object: None has none, DrawAndMask draws the box and masks children,
        // MaskOnly masks children without drawing the box. In MaskOnly the box draws its plain shape as an opaque white
        // crisp quad instead of its layers, so it writes a clean stencil.
        [SerializeField] private MaskMode _masking = MaskMode.None;

        // CSS order: x = top-left, y = top-right, z = bottom-right, w = bottom-left.
        // Kept as one Vector4 so property bindings see four animatable channels.
        [SerializeField] private Vector4 _cornerRadii = new(8f, 8f, 8f, 8f);
        [SerializeField, Range(0f, 1f)] private float _cornerCurvature;

        // When true, this box ignores its own radii and curvature and derives them, per corner, from the
        // nearest ancestor Box so its rounded rect stays concentric with it. Resolves recursively, so a chain
        // of concentric boxes all match the outermost plain one.
        [SerializeField] private bool _concentric;

        // Offsets the box's shape within its RectTransform, per side (x = left, y = right, z = top, w = bottom),
        // in canvas units. Positive shrinks it inward; negative grows it outward past the RectTransform. Only
        // the generated mesh changes: the corners stay concentric with the full rect, and a Mask on the box
        // therefore stencils to the offset shape. The RectTransform is untouched.
        [SerializeField] private Vector4 _inset;

        // The layers, drawn in this order. Each is named for what it is usually for; all three are the same kind of
        // layer, so any can be any effect.
        [SerializeField] private BoxLayer _shadow = new(new Color(0f, 0f, 0f, 0.5f)) { Enabled = false, Blur = 12f, Offset = new Vector2(0f, -4f) };
        [SerializeField] private BoxLayer _fill = new(Color.white);
        [SerializeField] private BoxLayer _border = new(Color.black) { Enabled = false, Stroke = 1f };

        /// <summary>Corner radii in CSS order: top-left, top-right, bottom-right, bottom-left. Clamped by the shader so radii sharing an edge fit within it. Ignored while <see cref="Concentric"/> is on.</summary>
        public Vector4 CornerRadii
        {
            get => _cornerRadii;
            set
            {
                if (_cornerRadii == value) return;
                _cornerRadii = value;
                SetGeometryDirty();
            }
        }

        /// <summary>
        /// Corner shape from 0 (circular) to 1 (squarest, cubic). Intermediate values blend between the
        /// adjacent presets in <see cref="CornerShape"/>. As a corner's radius nears the size limit the
        /// shader lowers this toward 0 so small rects become true pills and circles. Ignored while
        /// <see cref="Concentric"/> is on.
        /// </summary>
        public float CornerCurvature
        {
            get => _cornerCurvature;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(_cornerCurvature, value)) return;
                _cornerCurvature = value;
                SetGeometryDirty();
            }
        }

        /// <summary>
        /// When true the box takes its corner radii and curvature from the nearest ancestor Box, matching each
        /// corner to the parent's so the rounded rects stay concentric across the inset between them. It
        /// resolves recursively, so a concentric box under another concentric box still matches the outermost
        /// plain box. Its own <see cref="CornerRadii"/> and <see cref="CornerCurvature"/> are ignored. It matches the
        /// parent's shape, not the parent's layers.
        /// </summary>
        public bool Concentric
        {
            get => _concentric;
            set
            {
                if (_concentric == value) return;
                _concentric = value;
                SetGeometryDirty();
            }
        }

        /// <summary>
        /// Offsets the box's shape within its RectTransform, per side (x = left, y = right, z = top, w = bottom),
        /// in canvas units. Positive shrinks it inward; negative grows it outward past the RectTransform. Every layer
        /// is drawn from the shape, and the corners stay concentric with the full rect; because a Mask stencils from
        /// the drawn mesh this offsets the mask too. Only the mesh changes; the RectTransform and layout are untouched.
        /// </summary>
        public Vector4 Inset
        {
            get => _inset;
            set
            {
                if (_inset == value) return;
                _inset = value;
                SetGeometryDirty();
            }
        }

        /// <summary>The layer drawn first, under the others; usually a drop shadow. Off by default.</summary>
        public BoxLayer Shadow
        {
            get => _shadow;
            set => SetLayer(ref _shadow, value);
        }

        /// <summary>The layer drawn second; usually the box's fill. On, in white, by default, so <see cref="Graphic.color"/> alone colours a plain box.</summary>
        public BoxLayer Fill
        {
            get => _fill;
            set => SetLayer(ref _fill, value);
        }

        /// <summary>The layer drawn last, over the others; usually a border ring. Off by default.</summary>
        public BoxLayer Border
        {
            get => _border;
            set => SetLayer(ref _border, value);
        }

        /// <summary>
        /// Whether this box masks its children, and whether it also draws itself. Setting it adds, configures or
        /// removes a <see cref="Mask"/> component on this object to match. A Mask stencils from everything the box
        /// draws, so in <see cref="MaskMode.DrawAndMask"/> a shadow, a blur or a ring outside the edge widens the mask
        /// too; a box that casts a shadow and clips its content clips with a MaskOnly child. In
        /// <see cref="MaskMode.MaskOnly"/> the box is not drawn: it draws its plain shape as a crisp white quad instead
        /// of its layers, which writes a clean stencil.
        /// </summary>
        public MaskMode Masking
        {
            get => _masking;
            set
            {
                if (_masking == value) return;
                _masking = value;
                SyncMaskComponent();
                SetAllDirty();
            }
        }

        /// <summary>
        /// How the box composites with what is behind it. Each mode resolves to its own shared material, so
        /// boxes of the same blend mode still batch together; different modes are separate draw calls.
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

        /// <summary>The shared material for this box's blend mode, loaded from the package's Resources folder.</summary>
        public override Material defaultMaterial => SharedMaterials.For(MaterialName, _blendMode, base.defaultMaterial);

        private Box()
        {
            useLegacyMeshGeneration = false;
        }

        /// <summary>Sets all four corners to the same radius.</summary>
        public void SetCornerRadius(float radius)
        {
            CornerRadii = new Vector4(radius, radius, radius, radius);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            CanvasChannels.Ensure(this, RequiredChannels);
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
            // A concentric box resolves against its ancestor, so re-parenting changes what it inherits.
            if (_concentric)
                SetVerticesDirty();
        }

        // The base rebuilds this box on its own size change; also rebuild any concentric descendants, since
        // their inset from this box (and therefore their radii) moved even though their own size did not.
        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            DirtyConcentricDescendants();
        }

        // A box is most often decoration (a panel, a shadow, a ring), so opt in to raycasts rather than out.
        #if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();
            raycastTarget = false;
        }
        #endif

        /// <summary>
        /// Adds, configures or removes the <see cref="Mask"/> component so it matches <see cref="Masking"/>. The
        /// mask is marked non-editable, since the box owns it; None removes it. Called by the setter and the
        /// editor when the mode changes; never from OnValidate, where adding/removing components is disallowed.
        /// </summary>
        public void SyncMaskComponent()
        {
            var mask = GetComponent<Mask>();

            if (_masking == MaskMode.None)
            {
                if (mask != null)
                {
                    mask.hideFlags = HideFlags.None;
                    if (Application.isPlaying) Destroy(mask); else DestroyImmediate(mask);
                }
                return;
            }

            if (mask == null)
                mask = gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = _masking == MaskMode.DrawAndMask;
            mask.hideFlags = HideFlags.NotEditable;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            // The shape is the rect offset by the inset; every layer is drawn from it, and any Mask stencil taken from
            // the mesh follows it while the RectTransform stays put. Its effective radii (CSS order) and curvature are
            // inherited from the parent when concentric, otherwise this box's own reduced by its inset, then CSS-clamped
            // exactly as the shader would.
            var shape = InsetRect(GetPixelAdjustedRect(), _inset);
            ResolveGeometry(shape, out var radiiCss, out var curvature01);

            // MaskOnly draws nothing to colour (the Mask hides the graphic); it exists only to write a stencil.
            if (_masking == MaskMode.MaskOnly)
            {
                AddLayer(vh, MaskShape, shape, radiiCss, curvature01, Color.white);
                return;
            }

            // Graphic.color tints every layer, and picks up the CanvasGroup alpha the renderer applies to the colour
            // channel.
            var tint = color;
            AddLayer(vh, _shadow, shape, radiiCss, curvature01, tint);
            AddLayer(vh, _fill, shape, radiiCss, curvature01, tint);
            AddLayer(vh, _border, shape, radiiCss, curvature01, tint);
        }

        // Adds one layer as a quad: the shape grown by the layer's spread, moved by its offset and padded for its blur and
        // any ring outside the edge.
        private static void AddLayer(VertexHelper vh, BoxLayer layer, Rect shape, Vector4 shapeRadii, float curvature01, Color tint)
        {
            if (!layer.Enabled) return;

            // Spread offsets every side, with the corners kept concentric, as a negative inset does.
            var spread = new Vector4(-layer.Spread, -layer.Spread, -layer.Spread, -layer.Spread);
            var rect = InsetRect(shape, spread);
            var radiiCss = ClampRadiiCss(InsetRadii(shapeRadii, spread), rect.size);

            var halfSize = rect.size * 0.5f;
            var center = rect.center;
            var blur = Mathf.Max(0f, layer.Blur);
            // A negative stroke draws its ring outside the edge, so the quad needs that much extra room too.
            var padding = blur + Mathf.Max(0f, -layer.Stroke) + AntiAliasPadding;

            // Everything travels in texcoords, which the canvas passes through as written (it transforms a normal
            // or tangent by the box's rotation and scale). The radii go as fractions of the short side, which no
            // clamped radius exceeds, two to a float in shader order (TR, BR) and (TL, BL); the gradient stops fill
            // the rest (see UiPacking.PackStopsForTexcoords).
            var shortSide = Mathf.Min(rect.width, rect.height);
            var perShortSide = shortSide > 0f ? 1f / shortSide : 0f;
            var stops = UiPacking.PackStopsForTexcoords(layer.GradientFrom, layer.GradientVia, layer.GradientTo);
            var radiiAndStops = new Vector4(
                UiPacking.PackPair12(radiiCss.y * perShortSide, radiiCss.z * perShortSide),
                UiPacking.PackPair12(radiiCss.x * perShortSide, radiiCss.w * perShortSide),
                stops.z,
                stops.w);

            var parameters = new Vector4(
                blur,
                layer.Stroke,
                UiPacking.PackPair12(curvature01, layer.GradientViaPosition),
                UiPacking.PackGradientHeader(layer.GradientEnabled, layer.GradientMode, layer.GradientUseVia, layer.GradientAngle));

            // The layer's colour tinted by the box's, on the colour channel: the shader multiplies the gradient by it.
            Color32 vertexColor = layer.Color * tint;

            var first = vh.currentVertCount;
            AddCorner(-1f, -1f);
            AddCorner(-1f, 1f);
            AddCorner(1f, 1f);
            AddCorner(1f, -1f);
            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first + 2, first + 3, first);

            void AddCorner(float signX, float signY)
            {
                // Rect-centred sample position: includes the padding so interpolation yields the true
                // SDF position for every pixel, excludes the offset so the shape moves with the quad.
                var sample = new Vector2(signX * (halfSize.x + padding), signY * (halfSize.y + padding));
                var position = center + sample + layer.Offset;

                vh.AddVert(
                    position,
                    vertexColor,
                    new Vector4((signX + 1f) * 0.5f, (signY + 1f) * 0.5f, stops.x, stops.y),
                    new Vector4(halfSize.x, halfSize.y, sample.x, sample.y),
                    radiiAndStops,
                    parameters,
                    Vector3.back,
                    new Vector4(1f, 0f, 0f, -1f));
            }
        }

        private void SetLayer(ref BoxLayer layer, BoxLayer value)
        {
            if (layer.Equals(value)) return;
            layer = value;
            SetVerticesDirty();
        }

        // ── Concentric / inset geometry ────────────────────────────────────────────

        /// <summary>
        /// The box's shape: its effective radii (CSS order, this box's canvas units) and curvature (0..1).
        /// Concentric boxes recurse into the parent; otherwise the box's own values reduced by its inset are
        /// used. Radii are CSS-clamped to <paramref name="shape"/> exactly as the shader clamps them.
        /// </summary>
        private void ResolveGeometry(Rect shape, out Vector4 radii, out float curvature)
        {
            var parent = _concentric ? NearestAncestorBox() : null;
            if (parent != null)
            {
                parent.GetEffectiveShape(out var pMin, out var pMax, out var pRadii, out var pCurv);
                LocalRectToWorld(shape, out var cMin, out var cMax);

                // Signed gaps between the parent's shape and this one, per side, in this box's canvas units.
                // Positive means this box is inset (inside the parent); negative means it is outset (larger). A
                // corner follows the adjacent side that deviates most from the parent (largest magnitude), so it
                // shrinks when inset and grows when outset. Our circular corners cannot follow CSS's ellipse, so
                // this is the concentric approximation for uneven offsets.
                var scale = 1f / WorldScale();
                var gaps = new Vector4(
                    (cMin.x - pMin.x) * scale,   // left
                    (pMax.x - cMax.x) * scale,   // right
                    (pMax.y - cMax.y) * scale,   // top
                    (cMin.y - pMin.y) * scale);  // bottom

                radii = InsetRadii(pRadii, gaps);
                curvature = pCurv;
            }
            else
            {
                radii = InsetRadii(_cornerRadii, _inset);
                curvature = _cornerCurvature;
            }

            radii = ClampRadiiCss(radii, shape.size);
        }

        /// <summary>
        /// The world-space axis-aligned box, effective radii and curvature of this box's shape (after its inset and
        /// any concentric resolution). Used by a concentric child to match this box, so it recurses.
        /// </summary>
        private void GetEffectiveShape(out Vector2 worldMin, out Vector2 worldMax, out Vector4 radii, out float curvature)
        {
            var shape = InsetRect(GetPixelAdjustedRect(), _inset);
            LocalRectToWorld(shape, out worldMin, out worldMax);
            ResolveGeometry(shape, out radii, out curvature);
        }

        // The rect offset by a per-side inset (x = left, y = right, z = top, w = bottom). Positive insets shrink it;
        // negative insets grow it past the rect (outset). Collapses to a line rather than inverting if positive insets
        // exceed the size.
        private static Rect InsetRect(Rect r, Vector4 inset)
        {
            var xMin = r.xMin + inset.x;
            var xMax = r.xMax - inset.y;
            var yMax = r.yMax - inset.z;
            var yMin = r.yMin + inset.w;
            if (xMax < xMin) xMin = xMax = 0.5f * (xMin + xMax);
            if (yMax < yMin) yMin = yMax = 0.5f * (yMin + yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        // Offsets each corner radius (CSS order) by a per-side inset (x = left, y = right, z = top, w = bottom) so the
        // offset corner stays concentric with the original: an inset side shrinks its corners, a negative (outset) side
        // grows them. Each corner follows whichever of its two adjacent sides deviates most (largest magnitude).
        private static Vector4 InsetRadii(Vector4 radii, Vector4 inset)
        {
            float left = inset.x, right = inset.y, top = inset.z, bottom = inset.w;
            return new Vector4(
                Mathf.Max(0f, radii.x - SignedMax(left, top)),      // TL
                Mathf.Max(0f, radii.y - SignedMax(right, top)),     // TR
                Mathf.Max(0f, radii.z - SignedMax(right, bottom)),  // BR
                Mathf.Max(0f, radii.w - SignedMax(left, bottom)));  // BL
        }

        // CSS-style clamp, mirroring Box.shader: each edge's two radii may not exceed that edge's length, and
        // all radii scale down by the tightest overflowing edge. Radii are CSS order (TL, TR, BR, BL).
        private static Vector4 ClampRadiiCss(Vector4 radii, Vector2 size)
        {
            var tl = Mathf.Max(0f, radii.x);
            var tr = Mathf.Max(0f, radii.y);
            var br = Mathf.Max(0f, radii.z);
            var bl = Mathf.Max(0f, radii.w);

            var f = 1f;
            var top = tl + tr;
            var bottom = bl + br;
            var leftEdge = tl + bl;
            var rightEdge = tr + br;
            if (top > 0f) f = Mathf.Min(f, size.x / top);
            if (bottom > 0f) f = Mathf.Min(f, size.x / bottom);
            if (leftEdge > 0f) f = Mathf.Min(f, size.y / leftEdge);
            if (rightEdge > 0f) f = Mathf.Min(f, size.y / rightEdge);

            return new Vector4(tl, tr, br, bl) * f;
        }

        // The value with the larger magnitude, keeping its sign: the corner follows whichever adjacent side
        // deviates most, whether inset (positive) or outset (negative).
        private static float SignedMax(float a, float b) => Mathf.Abs(a) >= Mathf.Abs(b) ? a : b;

        private void LocalRectToWorld(Rect r, out Vector2 worldMin, out Vector2 worldMax)
        {
            var t = rectTransform;
            var c0 = (Vector2)t.TransformPoint(new Vector3(r.xMin, r.yMin));
            var c1 = (Vector2)t.TransformPoint(new Vector3(r.xMax, r.yMin));
            var c2 = (Vector2)t.TransformPoint(new Vector3(r.xMax, r.yMax));
            var c3 = (Vector2)t.TransformPoint(new Vector3(r.xMin, r.yMax));
            worldMin = Vector2.Min(Vector2.Min(c0, c1), Vector2.Min(c2, c3));
            worldMax = Vector2.Max(Vector2.Max(c0, c1), Vector2.Max(c2, c3));
        }

        // Assumes the concentric child and its parent share a canvas and axis alignment (the usual case);
        // world gaps convert to canvas units by the lossy scale.
        private float WorldScale()
        {
            var s = Mathf.Abs(rectTransform.lossyScale.x);
            return s > 1e-6f ? s : 1f;
        }

        private Box NearestAncestorBox()
        {
            for (var p = transform.parent; p != null; p = p.parent)
            {
                if (p.TryGetComponent<Box>(out var box))
                    return box;
            }
            return null;
        }

        private void SetGeometryDirty()
        {
            SetVerticesDirty();
            DirtyConcentricDescendants();
        }

        // Rebuilds concentric boxes below this one. Descends through non-Box objects; a plain Box prunes its
        // subtree (those descendants inherit from it, not us), a concentric Box is dirtied and descended into.
        private void DirtyConcentricDescendants()
        {
            var t = transform;
            for (var i = 0; i < t.childCount; i++)
                DirtyConcentricRecurse(t.GetChild(i));
        }

        private static void DirtyConcentricRecurse(Transform t)
        {
            if (t.TryGetComponent<Box>(out var box))
            {
                if (!box._concentric)
                    return;
                box.SetVerticesDirty();
            }

            for (var i = 0; i < t.childCount; i++)
                DirtyConcentricRecurse(t.GetChild(i));
        }
    }
}
