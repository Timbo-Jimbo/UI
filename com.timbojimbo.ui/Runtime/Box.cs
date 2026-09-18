using TimboJimbo.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TimboJimbo.UI
{
    /// <summary>
    /// Draws a rounded box as a signed distance field. One instance is one visual effect: corner radii alone
    /// give a panel, a blur radius with a dark colour gives a drop shadow, and a stroke width gives a border.
    /// Compose them as sibling objects sharing the same anchors. Every parameter travels in vertex data and
    /// all instances share one material, so a shadow, panel and border still render as a single draw call.
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
            AdditionalCanvasShaderChannels.TexCoord3 |
            AdditionalCanvasShaderChannels.Normal |
            AdditionalCanvasShaderChannels.Tangent;

        [SerializeField] private UiBlendMode _blendMode = UiBlendMode.Normal;

        // Drives a Mask component on this object: None has none, DrawAndMask draws the box and masks children,
        // MaskOnly masks children without drawing the box. In MaskOnly the box renders as an opaque white crisp
        // shape (colour/gradient/blur ignored) so it writes a clean stencil.
        [SerializeField] private MaskMode _masking = MaskMode.None;

        // CSS order: x = top-left, y = top-right, z = bottom-right, w = bottom-left.
        // Kept as one Vector4 so property bindings see four animatable channels.
        [SerializeField] private Vector4 _cornerRadii = new(8f, 8f, 8f, 8f);
        [SerializeField, Range(0f, 1f)] private float _cornerCurvature;
        [SerializeField] private float _strokeWidth;
        [SerializeField, Min(0f)] private float _blurRadius;
        [SerializeField] private Vector2 _offset;

        // When true, this box ignores its own radii and curvature and derives them, per corner, from the
        // nearest ancestor Box so its rounded rect stays concentric with it. Resolves recursively, so a chain
        // of concentric boxes all match the outermost plain one.
        [SerializeField] private bool _concentric;

        // Offsets the drawn box within its RectTransform, per side (x = left, y = right, z = top, w = bottom),
        // in canvas units. Positive shrinks it inward; negative grows it outward past the RectTransform. Only
        // the generated mesh changes: the corners stay concentric with the full rect, and a Mask on the box
        // therefore stencils to the offset shape. The RectTransform is untouched.
        [SerializeField] private Vector4 _inset;

        [SerializeField] private bool _gradientEnabled;
        [SerializeField] private ColorInterpolationMode _gradientMode = ColorInterpolationMode.OkLab;
        [SerializeField] private bool _gradientUseVia;
        [SerializeField] private Color _gradientFrom = Color.white;
        [SerializeField] private Color _gradientVia = new(0.5f, 0.5f, 0.5f, 1f);
        [SerializeField] private Color _gradientTo = Color.black;
        [SerializeField, Range(0f, 1f)] private float _gradientViaPosition = 0.5f;
        [SerializeField] private float _gradientAngle;

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
        /// plain box. Its own <see cref="CornerRadii"/> and <see cref="CornerCurvature"/> are ignored.
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
        /// Offsets the drawn box within its RectTransform, per side (x = left, y = right, z = top, w = bottom),
        /// in canvas units. Positive shrinks it inward; negative grows it outward past the RectTransform. The
        /// corners stay concentric with the full rect, and because a Mask stencils from the drawn mesh this
        /// offsets the mask too. Only the mesh changes; the RectTransform and layout are untouched.
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

        /// <summary>
        /// Width of a border ring. 0 draws a filled box. A positive width draws the ring inside the edge (CSS
        /// border), a negative width draws it outside the edge, spilling past the box; the quad grows to fit.
        /// </summary>
        public float StrokeWidth
        {
            get => _strokeWidth;
            set
            {
                if (Mathf.Approximately(_strokeWidth, value)) return;
                _strokeWidth = value;
                SetVerticesDirty();
            }
        }

        /// <summary>Softness of the edge in canvas units. 0 is a crisp anti-aliased edge; larger values give a soft shadow.</summary>
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

        /// <summary>Shifts the drawn box without moving the RectTransform, so a shadow can share the panel's anchors.</summary>
        public Vector2 Offset
        {
            get => _offset;
            set
            {
                if (_offset == value) return;
                _offset = value;
                SetVerticesDirty();
            }
        }

        /// <summary>
        /// Whether this box masks its children, and whether it also draws itself. Setting it adds, configures or
        /// removes a <see cref="Mask"/> component on this object to match. In <see cref="MaskMode.MaskOnly"/> the
        /// box is not drawn, so its colour, gradient and blur are ignored and it renders as a crisp white shape
        /// that writes a clean stencil.
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

        /// <summary>
        /// When true the box is filled with a three-stop gradient (from → via → to). The stops carry their
        /// own colour and alpha, and <see cref="Graphic.color"/> multiplies the whole gradient as a tint, so
        /// colour changes and CanvasGroup fades apply to every stop.
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

        // Two of the three roles (shadow, border) are decorative, so opt in to raycasts rather than out.
        protected override void Reset()
        {
            base.Reset();
            raycastTarget = false;
        }

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
            // MaskOnly draws nothing to colour (the Mask hides the graphic); it exists only to write a stencil,
            // so it renders as a crisp opaque white shape with the colour, gradient and blur ignored.
            var maskOnly = _masking == MaskMode.MaskOnly;
            var blur = maskOnly ? 0f : _blurRadius;
            var gradientEnabled = _gradientEnabled && !maskOnly;

            // The drawn box is the rect shrunk by the inset; the mesh, and any Mask stencil taken from it,
            // follow this smaller rounded rect while the RectTransform stays put.
            var drawn = InsetRect(GetPixelAdjustedRect());
            var halfSize = drawn.size * 0.5f;
            var center = drawn.center;
            // A negative stroke draws its ring outside the edge, so the quad needs that much extra room too.
            var padding = Mathf.Max(0f, blur) + Mathf.Max(0f, -_strokeWidth) + AntiAliasPadding;

            // Effective radii (CSS order) and curvature: inherited from the parent when concentric, otherwise
            // this box's own reduced by its inset, then CSS-clamped exactly as the shader would.
            ResolveGeometry(drawn, out var radiiCss, out var curvature01);

            // Shader order: (TR, BR, TL, BL).
            var radii = new Vector4(radiiCss.y, radiiCss.z, radiiCss.x, radiiCss.w);

            var parameters = new Vector4(
                blur,
                _strokeWidth,
                curvature01 * CornerShapeExtensions.LastShapeIndex,
                UiPacking.PackModeAndAngle(gradientEnabled, _gradientMode, _gradientAngle));

            // Graphic.color is the tint on the colour channel: it multiplies the whole gradient and picks up
            // the CanvasGroup alpha the renderer applies there. The stops travel packed in normal/tangent.
            Color32 tint = maskOnly ? (Color32)Color.white : (Color32)color;
            UiPacking.PackStops(_gradientFrom, _gradientVia, _gradientTo, _gradientUseVia, _gradientViaPosition, out var normal, out var tangent);

            vh.Clear();
            AddCorner(-1f, -1f);
            AddCorner(-1f, 1f);
            AddCorner(1f, 1f);
            AddCorner(1f, -1f);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);

            void AddCorner(float signX, float signY)
            {
                // Rect-centred sample position: includes the padding so interpolation yields the true
                // SDF position for every pixel, excludes the offset so the shape moves with the quad.
                var sample = new Vector2(signX * (halfSize.x + padding), signY * (halfSize.y + padding));
                var position = center + sample + _offset;

                vh.AddVert(
                    position,
                    tint,
                    new Vector4((signX + 1f) * 0.5f, (signY + 1f) * 0.5f, 0f, 0f),
                    new Vector4(halfSize.x, halfSize.y, sample.x, sample.y),
                    radii,
                    parameters,
                    normal,
                    tangent);
            }
        }

        // ── Concentric / inset geometry ────────────────────────────────────────────

        /// <summary>
        /// The box's drawn rounded rect: its effective radii (CSS order, this box's canvas units) and curvature
        /// (0..1). Concentric boxes recurse into the parent; otherwise the box's own values reduced by its inset
        /// are used. Radii are CSS-clamped to <paramref name="drawn"/> exactly as the shader clamps them.
        /// </summary>
        private void ResolveGeometry(Rect drawn, out Vector4 radii, out float curvature)
        {
            var parent = _concentric ? NearestAncestorBox() : null;
            if (parent != null)
            {
                parent.GetEffectiveDrawnBox(out var pMin, out var pMax, out var pRadii, out var pCurv);
                LocalRectToWorld(drawn, out var cMin, out var cMax);

                // Signed gaps between the parent's drawn box and this one, per side, in this box's canvas units.
                // Positive means this box is inset (inside the parent); negative means it is outset (larger). A
                // corner follows the adjacent side that deviates most from the parent (largest magnitude), so it
                // shrinks when inset and grows when outset. Our circular corners cannot follow CSS's ellipse, so
                // this is the concentric approximation for uneven offsets.
                var scale = 1f / WorldScale();
                var gl = (cMin.x - pMin.x) * scale;
                var gr = (pMax.x - cMax.x) * scale;
                var gt = (pMax.y - cMax.y) * scale;
                var gb = (cMin.y - pMin.y) * scale;

                radii = new Vector4(
                    Mathf.Max(0f, pRadii.x - SignedMax(gl, gt)),   // TL
                    Mathf.Max(0f, pRadii.y - SignedMax(gr, gt)),   // TR
                    Mathf.Max(0f, pRadii.z - SignedMax(gr, gb)),   // BR
                    Mathf.Max(0f, pRadii.w - SignedMax(gl, gb)));  // BL
                curvature = pCurv;
            }
            else
            {
                radii = ReduceRadiiByInset(_cornerRadii);
                curvature = _cornerCurvature;
            }

            radii = ClampRadiiCss(radii, drawn.size);
        }

        /// <summary>
        /// The world-space axis-aligned box, effective radii and curvature of this box as drawn (after its
        /// inset and any concentric resolution). Used by a concentric child to match this box, so it recurses.
        /// </summary>
        private void GetEffectiveDrawnBox(out Vector2 worldMin, out Vector2 worldMax, out Vector4 radii, out float curvature)
        {
            var drawn = InsetRect(GetPixelAdjustedRect());
            LocalRectToWorld(drawn, out worldMin, out worldMax);
            ResolveGeometry(drawn, out radii, out curvature);
        }

        // The rect offset by the per-side inset (x = left, y = right, z = top, w = bottom). Positive insets
        // shrink it; negative insets grow it past the rect (outset). Collapses to a line rather than inverting
        // if positive insets exceed the size.
        private Rect InsetRect(Rect r)
        {
            var xMin = r.xMin + _inset.x;
            var xMax = r.xMax - _inset.y;
            var yMax = r.yMax - _inset.z;
            var yMin = r.yMin + _inset.w;
            if (xMax < xMin) xMin = xMax = 0.5f * (xMin + xMax);
            if (yMax < yMin) yMin = yMax = 0.5f * (yMin + yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        // Offsets each corner radius (CSS order) so the drawn corner stays concentric with the full-rect corner:
        // an inset side shrinks its corners, a negative (outset) side grows them. Each corner follows whichever
        // of its two adjacent sides deviates most from the rect (largest magnitude).
        private Vector4 ReduceRadiiByInset(Vector4 radii)
        {
            float left = _inset.x, right = _inset.y, top = _inset.z, bottom = _inset.w;
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
        // deviates most from the parent, whether inset (positive) or outset (negative).
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
