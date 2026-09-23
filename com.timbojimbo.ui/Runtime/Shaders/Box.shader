// Signed-distance rounded box for UGUI. Every parameter (rect size, per-corner radii, corner curvature,
// stroke width, blur radius, and a three-stop gradient) arrives in vertex data so all Box instances share
// one material and batch. All of it is in texcoords, which the canvas passes through as written; it
// transforms a normal or tangent by the box's rotation and scale, which would scramble anything packed there.
//
// Vertex contract (written by TimboJimbo.UI.Box; gradient fields per UiGradient.cginc):
//   COLOR     = Graphic.color, the tint (8-bit; the renderer folds CanvasGroup alpha into it)
//   TEXCOORD0 = (uv.x, uv.y, from RGB, via RGB)   a stop's RGB as three 8-bit integers in one float
//   TEXCOORD1 = (halfSize.x, halfSize.y, samplePos.x, samplePos.y)
//   TEXCOORD2 = (radii TR|BR, radii TL|BL, to RGB, alphas from|via|to)   radii as 12-bit fractions of the
//               short side, two to a float (UiPacking.PackPair12)
//   TEXCOORD3 = (blurRadius, strokeWidth, curvature 0..1|via position (PackPair12), gradient header)
Shader "TimboJimbo/UI/Box"
{
    Properties
    {
        [PerRendererData] _MainTex ("Main Texture", 2D) = "white" {}
        [HideInInspector] _BlendSrc ("Blend Src", Float) = 1
        [HideInInspector] _BlendDst ("Blend Dst", Float) = 10
        [HideInInspector] _BlendOp ("Blend Op", Float) = 0
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        // Fragment output is premultiplied; the blend factors (set per blend mode via the material) decide
        // how it combines with the destination. Default is premultiplied "over" (Normal).
        Blend [_BlendSrc] [_BlendDst]
        BlendOp [_BlendOp]
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "UiGradient.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            // Distance (in canvas units) from the radius limit over which a corner slides toward a circle.
            #define BLEND_TO_CIRCLE_DISTANCE 16.0
            #define CORNER_CIRCLE 0
            #define CORNER_PARABOLA 1
            #define CORNER_COSINE 2
            #define CORNER_CUBIC 3

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float4 texcoord : TEXCOORD0;
                float4 box      : TEXCOORD1;
                float4 radii    : TEXCOORD2;
                float4 params   : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex    : SV_POSITION;
                float4 tint      : COLOR;       // Graphic.color * CanvasGroup alpha, framebuffer space
                float2 texcoord  : TEXCOORD0;
                float4 mask      : TEXCOORD1;
                float4 box       : TEXCOORD2;   // halfSize.xy, samplePos.zw
                float4 radii     : TEXCOORD3;   // clamped, (TR, BR, TL, BL)
                float4 curvature : TEXCOORD4;   // effective per-corner curvature, same order as radii
                float2 sdfParams : TEXCOORD5;   // blur, stroke
                float4 gStops0   : TEXCOORD6;   // packed from.xy, packed via.xy
                float4 gStops1   : TEXCOORD7;   // packed to.xy, viaPosition, gradient mode (0 = off)
                float2 gDir      : TEXCOORD8;   // gradient axis direction
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            // Coverage 1..0 as the signed distance crosses the transition band of half-width h.
            // Smootherstep (Perlin's quintic) eases in and out with zero first and second derivatives at the
            // band edges, so a soft/blurred edge reads like a Gaussian blur while costing only a polynomial,
            // not the per-pixel erf (exp + sqrt) a true Gaussian falloff would need.
            float edgeCoverage(float d, float h)
            {
                float t = saturate((d + h) / (2.0 * h));
                return 1.0 - t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
            }

            // ---- Corner curves (canonical corner space) --------------------------------------------------
            float sdCornerCircle(in float2 p)
            {
                return length(p - float2(0.0, -1.0)) - sqrt(2.0);
            }

            float sdCornerParabola(in float2 p)
            {
                float y = (0.5 + p.y) * (2.0 / 3.0);
                float h = p.x * p.x + y * y * y;
                float w = pow(p.x + sqrt(abs(h)), 1.0 / 3.0);
                float x = w - y / w;
                float2 q = float2(x, 0.5 * (1.0 - x * x));
                return length(p - q) * sign(p.y - q.y);
            }

            float sdCornerCosine(in float2 uv)
            {
                uv *= (TWO_PI / 4.0);
                float ta = 0.0, tb = TWO_PI / 4.0;
                for (int i = 0; i < 8; i++)
                {
                    float t = 0.5 * (ta + tb);
                    float y = t - uv.x + (uv.y - cos(t)) * sin(t);
                    if (y < 0.0) ta = t; else tb = t;
                }
                float2 qa = float2(ta, cos(ta)), qb = float2(tb, cos(tb));
                float2 pa = uv - qa, di = qb - qa;
                float h = clamp(dot(pa, di) / dot(di, di), 0.0, 1.0);
                return length(pa - di * h) * sign(pa.y * di.x - pa.x * di.y) * (4.0 / TWO_PI);
            }

            float sdCornerCubic(in float2 uv)
            {
                float ta = 0.0, tb = 1.0;
                for (int i = 0; i < 12; i++)
                {
                    float t = 0.5 * (ta + tb);
                    float c = (t * t * (t - 3.0) + 2.0) / 3.0;
                    float dc = t * (t - 2.0);
                    float y = (uv.x - t) + (uv.y - c) * dc;
                    if (y > 0.0) ta = t; else tb = t;
                }
                float2 qa = float2(ta, (ta * ta * (ta - 3.0) + 2.0) / 3.0);
                float2 qb = float2(tb, (tb * tb * (tb - 3.0) + 2.0) / 3.0);
                float2 pa = uv - qa, di = qb - qa;
                float h = clamp(dot(pa, di) / dot(di, di), 0.0, 1.0);
                return length(pa - di * h) * sign(pa.y * di.x - pa.x * di.y);
            }

            float sdCorner(in float2 uv, in int type)
            {
                if (type == CORNER_CIRCLE) return sdCornerCircle(uv);
                if (type == CORNER_PARABOLA) return sdCornerParabola(uv);
                if (type == CORNER_COSINE) return sdCornerCosine(uv);
                return sdCornerCubic(uv);
            }

            // Rounds one corner within its own zone, measured from the arc centre (halfSize - radius from the
            // box centre, in the corner's direction) rather than the box centre. Because the zone is defined
            // around the arc centre, a corner radius may exceed half the box without folding into the opposite
            // corner, so a full-height top radius with a square bottom renders without a seam. Zones do not
            // overlap once the radii are edge-clamped. Returns the incoming distance unchanged outside the zone.
            float sdCornerZone(float d, in float2 position, in float2 halfSize, in float radius, in float curvature, in float2 s)
            {
                if (radius <= 1e-4) return d;
                float2 k = s * (halfSize - radius);     // arc centre
                float2 v = (position - k) * s;          // offset from the centre, positive toward the corner
                if (v.x <= 0.0 || v.y <= 0.0) return d; // this pixel belongs to an edge or another corner

                float2 uv = float2(abs(v.x - v.y), v.x + v.y - radius) / radius;
                int t0 = (int)floor(curvature);
                float f = frac(curvature);
                float cd = sdCorner(uv, t0);
                if (f > 0.0)
                    cd = lerp(cd, sdCorner(uv, min(t0 + 1, CORNER_CUBIC)), f);

                // The rounded corner is the rectangle intersected with the arc constraint, so combine with
                // max: whichever feature is nearest (the arc or an adjacent edge) defines the distance. This
                // keeps the field continuous where a large corner's zone runs alongside another edge.
                return max(d, cd * radius * sqrt(0.5));
            }

            // radii/curvature are packed per corner as (TR, BR, TL, BL).
            float sdRoundBox(in float2 position, in float2 halfSize, in float4 radii, in float4 curvature)
            {
                // Default: the sharp rectangle, covering the edges and any straight gaps between corner arcs.
                float2 e = abs(position) - halfSize;
                float d = min(max(e.x, e.y), 0.0) + length(max(e, 0.0));

                d = sdCornerZone(d, position, halfSize, radii.z, curvature.z, float2(-1.0,  1.0)); // TL
                d = sdCornerZone(d, position, halfSize, radii.x, curvature.x, float2( 1.0,  1.0)); // TR
                d = sdCornerZone(d, position, halfSize, radii.y, curvature.y, float2( 1.0, -1.0)); // BR
                d = sdCornerZone(d, position, halfSize, radii.w, curvature.w, float2(-1.0, -1.0)); // BL
                return d;
            }

            // ----------------------------------------------------------------------------------------------
            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float4 vPosition = UnityObjectToClipPos(v.vertex);
                OUT.vertex = vPosition;

                float2 pixelSize = vPosition.w;
                pixelSize /= float2(1, 1) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
                OUT.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));

                // Per-quad SDF work. Clamp radii CSS-style: the limit on each corner comes from the two radii
                // sharing an edge, not from half the box, so e.g. a full-height top radius is allowed when the
                // bottom radius is 0. Scale all radii by the tightest edge that overflows. radii = (TR,BR,TL,BL).
                float2 halfSize = v.box.xy;
                float2 fullSize = halfSize * 2.0;
                // Radii arrive as fractions of the short side, (TR, BR) and (TL, BL).
                float4 rIn;
                unpackPair12(v.radii.x, rIn.x, rIn.y);
                unpackPair12(v.radii.y, rIn.z, rIn.w);
                rIn *= max(min(fullSize.x, fullSize.y), 0.0);
                float topSum = rIn.z + rIn.x;    // TL + TR span the top edge (width)
                float botSum = rIn.w + rIn.y;    // BL + BR span the bottom edge (width)
                float leftSum = rIn.z + rIn.w;   // TL + BL span the left edge (height)
                float rightSum = rIn.x + rIn.y;  // TR + BR span the right edge (height)
                float f = 1.0;
                f = min(f, topSum   > 0.0 ? fullSize.x / topSum   : 1.0);
                f = min(f, botSum   > 0.0 ? fullSize.x / botSum   : 1.0);
                f = min(f, leftSum  > 0.0 ? fullSize.y / leftSum  : 1.0);
                f = min(f, rightSum > 0.0 ? fullSize.y / rightSum : 1.0);
                float4 radii = rIn * f;

                // Slide a corner's curvature toward a circle as it approaches the short-side limit, so small
                // rects become clean pills and circles.
                float maxRadius = min(halfSize.x, halfSize.y);
                float4 headroom = maxRadius - radii;
                float4 toCircle = saturate((BLEND_TO_CIRCLE_DISTANCE - headroom) / BLEND_TO_CIRCLE_DISTANCE);

                float curvature01, viaPosition;
                unpackPair12(v.params.z, curvature01, viaPosition);

                OUT.box = v.box;
                OUT.radii = radii;
                OUT.curvature = curvature01 * CORNER_CUBIC * (1.0 - toCircle);
                OUT.sdfParams = v.params.xy;

                // Graphic.color is the tint; it stays in framebuffer space and is applied after the gradient
                // is converted. Honour the project option that keeps UI vertex colours in gamma.
                OUT.tint = v.color;
                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                    OUT.tint.rgb = UIGammaToLinear(OUT.tint.rgb);

                unpackGradientStops(float4(v.texcoord.zw, v.radii.zw), v.params.w, viaPosition, OUT.gStops0, OUT.gStops1, OUT.gDir);

                return OUT;
            }

            float4 frag(v2f IN) : SV_Target
            {
                float2 position = IN.box.zw;
                float2 halfSize = IN.box.xy;

                float4 baseColor = gradientBaseColor(IN.tint, IN.gStops0, IN.gStops1, IN.gDir, position, halfSize);

                float dist = sdRoundBox(position, halfSize, IN.radii, IN.curvature);

                // A ring of width |stroke|, centred half a width off the edge: inside it for a positive stroke
                // (CSS border), outside it for a negative one (spills past the box).
                float stroke = IN.sdfParams.y;
                if (abs(stroke) > 1e-5)
                    dist = abs(dist + 0.5 * stroke) - 0.5 * abs(stroke);

                // Coverage falls off across the transition band, one pixel wide for a crisp edge (fwidth) or
                // the blur radius for a soft one, eased by edgeCoverage so it reads like a real soft edge.
                float blur = IN.sdfParams.x;
                float halfBand = max(0.5 * fwidth(dist), blur);

                float4 color = baseColor * tex2D(_MainTex, IN.texcoord);
                color.a *= edgeCoverage(dist, halfBand);

                #ifdef UNITY_UI_CLIP_RECT
                half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
                color.a *= m.x * m.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                color.rgb *= color.a;
                return color;
            }
        ENDCG
        }
    }

    CustomEditor "TimboJimboEditor.UI.BlendModeShaderGUI"
}
