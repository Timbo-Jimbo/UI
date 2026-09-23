// UI/Default with a per-pixel three-stop gradient tint, a choice of how that colour blends with the sprite,
// a single-pass sprite blur, and material-driven blend factors, for the Img component. Every effect
// parameter arrives in vertex data so all Img instances share one material per blend mode and batch.
//
// Vertex contract (written by TimboJimbo.UI.Img on top of Image's own geometry; gradient fields per
// UiGradient.cginc). Everything is in texcoords, which the canvas passes through as written; it transforms a
// normal or tangent by the image's rotation and scale, which would scramble anything packed there.
//   COLOR     = Graphic.color, the tint (8-bit; the renderer folds CanvasGroup alpha into it)
//   TEXCOORD0 = (sprite uv.x, sprite uv.y, blur tap spacing in uv (UiPacking.PackLogPair12), 0)
//               Tiled: (tile width, blur tap spacing, offPair, staggerPair), the first two in canvas units;
//               the sprite uv is derived per pixel
//   TEXCOORD1 = (halfSize.x, halfSize.y, rectCentredPos.x, rectCentredPos.y)
//               Tiled: (rectCentredPos.x, rectCentredPos.y, rotPair, spacingPair); the quad is the rect, so
//               each corner sits at ± the half size and the half size is its magnitude
//   TEXCOORD2 = (header, gradient from RGB, gradient to RGB, alphasAndFactor)
//               header (UiPacking.PackImgHeader) = gradient mode + blurTaps*8 + jitter*64 + tiled*128 +
//               colourBlendMode*256, with the gradient angle as the fraction; the RGBs and alphasAndFactor
//               (from alpha, to alpha, colour blend factor) are UiPacking.PackTriple8; the pairs are
//               UiPacking.PackPair12 of (grid rotation, sprite rotation) as turns, (spacing.x, .y) as a
//               fraction of TILE_SPACING_RANGE sprite sizes, (offset.x, .y) as a fraction of the spacing and
//               (stagger.x, .y) as a fraction of the spacing
//   TEXCOORD3 = sprite uv bounds (minU, minV, maxU, maxV); samples stay inside, taps outside are transparent
Shader "TimboJimbo/UI/Img"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
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
            // Amount, in mip texels, the wide-blur taps are dithered by to break the coarse-mip block grid into
            // grain. The per-component Img.BlurJitter flag (in vertex data) scales this to 0 when it is off.
            #define BLUR_JITTER_TEXELS 1.0
            // Offset applied to the blur's mip level. 0 matches the mip texel to the tap spacing, so each tap's
            // bilinear footprint tiles edge-to-edge with its neighbours and the result is smooth. Going finer
            // (negative) sharpens each tap but lets the tap grid show through as a lattice, so it stays at 0.
            #define BLUR_MIP_BIAS 0.0
            // Ranges of the packed tile layout; mirrored by TimboJimbo.UI.Img.
            #define TILE_SPACING_RANGE 8.0   // spacing, in sprite sizes

            // Colour blend modes, matching TimboJimbo.UI.ColorBlendMode.
            #define CBLEND_MULTIPLY 0
            #define CBLEND_REPLACE 1
            #define CBLEND_ADD 2
            #define CBLEND_SCREEN 3
            #define CBLEND_DARKEN 4
            #define CBLEND_LIGHTEN 5
            #define CBLEND_OVERLAY 6
            #define CBLEND_DIFFERENCE 7

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float4 texcoord : TEXCOORD0;
                float4 box      : TEXCOORD1;
                float4 params   : TEXCOORD2;
                float4 bounds   : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float4 tint     : COLOR;       // Graphic.color * CanvasGroup alpha, framebuffer space
                float4 texcoord : TEXCOORD0;   // sprite uv.xy, blur tap spacing.zw (Tiled: tile params, see above)
                float4 mask     : TEXCOORD1;
                float4 box      : TEXCOORD2;   // halfSize.xy, rect-centred position.zw
                float4 gStops0  : TEXCOORD3;   // packed from.xy, packed via.xy
                float4 gStops1  : TEXCOORD4;   // packed to.xy, viaPosition, gradient mode (0 = off)
                float2 gDir     : TEXCOORD5;   // gradient axis direction
                float4 effects  : TEXCOORD6;   // header (raw), colour blend factor, staggerPair, 0
                float4 bounds   : TEXCOORD7;   // sprite uv bounds
                float4 tile     : TEXCOORD8;   // sprite width, rotPair, spacingPair, offPair
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Splits the header integer packed by UiPacking.PackImgHeader.
            void unpackImgHeader(float header, out float gradientMode, out float blurTaps, out float jitter, out float tiled, out float colorBlendMode)
            {
                gradientMode = fmod(header, 8.0);
                header = floor(header / 8.0);
                blurTaps = fmod(header, 8.0);
                header = floor(header / 8.0);
                jitter = fmod(header, 2.0);
                header = floor(header / 2.0);
                tiled = fmod(header, 2.0);
                colorBlendMode = floor(header / 2.0);
            }

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            // Isotropic hash (Dave Hoskins, hash22): two decorrelated values in [0,1) with no directional
            // structure, unlike interleaved-gradient noise whose diagonal weave shows at large radii.
            float2 hash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            // Bilinear filtering reads half a texel past the sample point, which at the sprite's edge in an
            // atlas is the neighbouring sprite. Keep every sample that margin inside the sprite rect, scaled
            // to the mip being read. Tile-edge pixels that land on the rect edge would otherwise bleed.
            float2 clampToSprite(float2 uv, float4 bounds, float lod)
            {
                float2 margin = 0.5 * _MainTex_TexelSize.xy * exp2(lod);
                float2 lo = bounds.xy + margin;
                float2 hi = bounds.zw - margin;
                return clamp(uv, min(lo, hi), max(lo, hi));
            }

            // One blur tap, premultiplied so transparent texels do not drag their (undefined) colour into the
            // average. Outside the sprite's bounds the tap is transparent rather than a neighbouring sprite.
            float4 sampleSpritePremultiplied(float2 uv, float4 bounds, float lod)
            {
                if (any(uv < bounds.xy) || any(uv > bounds.zw)) return 0;
                uv = clampToSprite(uv, bounds, lod);
                float4 c = tex2Dlod(_MainTex, float4(uv, 0, lod)) + _TextureSampleAdd;
                return float4(c.rgb * c.a, c.a);
            }

            // Tile grid for Img's Tiled type. The quad is the rect. The grid only places stamps: a lattice of
            // points laid out from the rect-centred position with its origin at the rect's bottom-left when
            // unrotated (as Image tiles), rotated about the rect centre, staggered row by row and column by
            // column, and shifted by the offset and the time-driven pan. The sprite is what gets stamped at
            // each point: its own size and its own rotation (in canvas orientation, independent of the
            // grid's), cut off at the edge of its cell so stamps never overlap. Working in canvas units keeps
            // the blur correct too: its taps step across stamps and gaps like they would across a real mesh.
            struct Tiling
            {
                float2 halfSize;
                float4 bounds;
                float2 spriteSize;
                float2 spacing;    // distance between placement points, per axis
                float2 stagger;    // row shift along x, column shift along y, as a fraction of the spacing
                float2 shift;      // offset + pan * time, canvas units along the grid axes
                float gridSin, gridCos;
                float stampSin, stampCos;   // sprite rotation relative to the grid axes
            };

            // Maps a vector into the frame of something rotated counter-clockwise by the angle (sin, cos).
            float2 intoRotated(float2 v, float s, float c)
            {
                return float2(c * v.x + s * v.y, -s * v.x + c * v.y);
            }

            // Continuous grid coordinate (canvas units) for a rect-centred position. Continuous, so its screen
            // derivatives give the right mip; the per-stamp uv below jumps at every cell edge.
            float2 tileSpace(float2 p, Tiling t)
            {
                return intoRotated(p, t.gridSin, t.gridCos) + t.halfSize - t.shift;
            }

            // Sprite uv for a grid coordinate, and whether it lands on the stamp rather than beside it. Cell
            // (i, j) is centred at ((i + j * stagger.x) * spacing.x, (j + i * stagger.y) * spacing.y) plus half a
            // spacing; the point belongs to the nearest cell in lattice coordinates, so cells are the
            // lattice's own parallelograms (rectangles without stagger) and stamps are cut at their edges.
            float2 tileUv(float2 q, Tiling t, out bool inTile)
            {
                float2 u = q / t.spacing - 0.5;
                float det = max(1.0 - t.stagger.x * t.stagger.y, 1e-3);
                float2 ij = floor(float2(u.x - t.stagger.x * u.y, u.y - t.stagger.y * u.x) / det + 0.5);
                float2 centre = (float2(ij.x + ij.y * t.stagger.x, ij.y + ij.x * t.stagger.y) + 0.5) * t.spacing;
                float2 rel = intoRotated(q - centre, t.stampSin, t.stampCos);
                inTile = all(abs(rel) <= t.spriteSize * 0.5);
                return t.bounds.xy + (rel / t.spriteSize + 0.5) * (t.bounds.zw - t.bounds.xy);
            }

            // One premultiplied sample at a point: the sprite uv, or in tiled mode the grid coordinate.
            float4 sampleAt(float2 at, float lod, bool tiled, Tiling t)
            {
                float2 uv = at;
                bool inTile = true;
                if (tiled)
                    uv = tileUv(at, t, inTile);
                return inTile ? sampleSpritePremultiplied(uv, t.bounds, lod) : 0;
            }

            // Gaussian-weighted (2n+1)^2 taps spread across ±radius. The component precomputes the tap
            // spacing (uv, or canvas units when tiled) and the mip whose texel pitch matches it, so a wide
            // radius costs the same as a narrow one and stays smooth as long as the texture has mipmaps.
            // Returns straight alpha so the colour blend sees the sprite's real colour.
            float4 blurSprite(float2 origin, float2 spacing, float lod, int n, float2 seed, float jitterTexels, bool tiled, Tiling t)
            {
                float4 sum = 0;
                float weightSum = 0;
                float invN = 1.0 / n;
                [loop] for (int y = -n; y <= n; y++)
                {
                    float fy = y * invN;
                    float wy = exp(-2.0 * fy * fy);
                    [loop] for (int x = -n; x <= n; x++)
                    {
                        float fx = x * invN;
                        float w = wy * exp(-2.0 * fx * fx);
                        // Independent jitter per tap and per screen pixel (0 when disabled), so the coarse mip's
                        // facet grid is sampled incoherently and averages into smooth grain rather than blocks.
                        float2 offset = float2(x, y) + (hash22(seed + float2(x, y) * 37.0) - 0.5) * jitterTexels;
                        sum += w * sampleAt(origin + offset * spacing, lod, tiled, t);
                        weightSum += w;
                    }
                }
                sum /= weightSum;
                return float4(sum.a > 1e-5 ? sum.rgb / sum.a : 0.0, sum.a);
            }

            float3 blendColor(float3 s, float3 c, int mode)
            {
                if (mode == CBLEND_REPLACE) return c;
                if (mode == CBLEND_ADD) return saturate(s + c);
                if (mode == CBLEND_SCREEN) return 1.0 - (1.0 - s) * (1.0 - c);
                if (mode == CBLEND_DARKEN) return min(s, c);
                if (mode == CBLEND_LIGHTEN) return max(s, c);
                if (mode == CBLEND_OVERLAY) return s < 0.5 ? 2.0 * s * c : 1.0 - 2.0 * (1.0 - s) * (1.0 - c);
                if (mode == CBLEND_DIFFERENCE) return abs(s - c);
                return s * c;
            }

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

                float header, angle01;
                unpackIntAndFraction(v.params.x, header, angle01);
                float gradientMode, blurTaps, jitter, tiled, colorBlendMode;
                unpackImgHeader(header, gradientMode, blurTaps, jitter, tiled, colorBlendMode);
                bool isTiled = tiled > 0.5;

                // A tiled quad is the rect: each corner carries its rect-centred position, which is ± the half
                // size, and uv0 carries the tile data, so there is no sprite uv to transform.
                float2 halfSize = isTiled ? abs(v.box.xy) : v.box.xy;
                float2 position = isTiled ? v.box.xy : v.box.zw;
                OUT.box = float4(halfSize, position);
                OUT.texcoord = isTiled
                    ? float4(0.0, 0.0, v.texcoord.y, v.texcoord.y)
                    : float4(TRANSFORM_TEX(v.texcoord.xy, _MainTex), unpackLogPair12(v.texcoord.z));
                OUT.tile = float4(v.texcoord.x, v.box.z, v.box.w, v.texcoord.z);
                OUT.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));
                OUT.bounds = v.bounds;

                // The stops' alphas share a float with the colour blend factor.
                float3 alphasAndFactor = unpackBytes(v.params.w);
                OUT.effects = float4(v.params.x, alphasAndFactor.z / 255.0, isTiled ? v.texcoord.w : 0.0, 0.0);

                // Graphic.color is the tint; it stays in framebuffer space and is applied after the gradient
                // is converted. Honour the project option that keeps UI vertex colours in gamma.
                OUT.tint = v.color;
                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                    OUT.tint.rgb = UIGammaToLinear(OUT.tint.rgb);

                unpackGradientTwoStops(v.params.y, v.params.z, alphasAndFactor.xy, gradientMode, angle01, OUT.gStops0, OUT.gStops1, OUT.gDir);

                return OUT;
            }

            float4 frag(v2f IN) : SV_Target
            {
                // Interpolated integers are rounded before use (see UiPacking.cginc).
                float header, angle01;
                unpackIntAndFraction(IN.effects.x, header, angle01);
                float gradientMode, tapsF, jitterF, tiledF, colorBlendMode;
                unpackImgHeader(header, gradientMode, tapsF, jitterF, tiledF, colorBlendMode);
                int blurTaps = (int)tapsF;
                bool tiled = tiledF > 0.5;
                float jitterTexels = jitterF > 0.5 ? BLUR_JITTER_TEXELS : 0.0;
                float blendFactor = IN.effects.y;
                float staggerX, staggerY;
                unpackPair12(IN.effects.z, staggerX, staggerY);

                Tiling t;
                t.halfSize = IN.box.xy;
                t.bounds = IN.bounds;
                float2 spritePixels = (IN.bounds.zw - IN.bounds.xy) * _MainTex_TexelSize.zw;
                t.spriteSize = max(float2(IN.tile.x, IN.tile.x * spritePixels.y / max(spritePixels.x, 1e-4)), 1e-4);
                float gridTurns, spriteTurns, spacingX, spacingY, offX, offY;
                unpackPair12(IN.tile.y, gridTurns, spriteTurns);
                unpackPair12(IN.tile.z, spacingX, spacingY);
                unpackPair12(IN.tile.w, offX, offY);
                sincos(gridTurns * TWO_PI, t.gridSin, t.gridCos);
                // The sprite's rotation is in canvas orientation; inside grid space that is relative to the grid.
                sincos((spriteTurns - gridTurns) * TWO_PI, t.stampSin, t.stampCos);
                t.spacing = max(t.spriteSize * float2(spacingX, spacingY) * TILE_SPACING_RANGE, 1e-4);
                t.stagger = float2(staggerX, staggerY);
                t.shift = float2(offX, offY) * t.spacing;

                // The sample coordinate: the sprite uv, or the continuous grid coordinate when tiled. Its
                // derivatives are taken here, before any branch, and unclamped so the edge clamp and the
                // per-tile uv jumps do not disturb mip selection.
                float2 at = tiled ? tileSpace(IN.box.zw, t) : IN.texcoord.xy;
                float2 spacing = IN.texcoord.zw;
                float2 dx = ddx(at);
                float2 dy = ddy(at);

                // The blur reads the mip whose texel pitch matches the tap spacing, so the taps' bilinear
                // footprints tile into a smooth result and cost stays flat as the radius grows.
                float2 spacingTexels = tiled ? spacing * spritePixels / t.spriteSize : spacing * _MainTex_TexelSize.zw;
                float lod = max(0.0, log2(max(max(spacingTexels.x, spacingTexels.y), 1e-6)) + BLUR_MIP_BIAS);

                // Sprite colour, straight alpha, blurred or sharp.
                float4 sprite;
                if (blurTaps > 0)
                {
                    // IN.vertex carries the screen-space pixel position in the fragment stage; it seeds the
                    // per-tap jitter so the pattern is stable per pixel but different for every tap.
                    sprite = blurSprite(at, spacing, lod, blurTaps, IN.vertex.xy, jitterTexels, tiled, t);
                }
                else if (tiled)
                {
                    bool inTile;
                    float2 uv = tileUv(at, t, inTile);
                    // Derivatives follow the stamp's rotation and its uv scale.
                    float2 uvPerUnit = (IN.bounds.zw - IN.bounds.xy) / t.spriteSize;
                    float2 dxUv = intoRotated(dx, t.stampSin, t.stampCos) * uvPerUnit;
                    float2 dyUv = intoRotated(dy, t.stampSin, t.stampCos) * uvPerUnit;
                    sprite = inTile
                        ? tex2Dgrad(_MainTex, clampToSprite(uv, IN.bounds, 0), dxUv, dyUv) + _TextureSampleAdd
                        : 0;
                }
                else
                {
                    sprite = tex2Dgrad(_MainTex, clampToSprite(at, IN.bounds, 0), dx, dy) + _TextureSampleAdd;
                }

                // The component colour (flat tint or gradient), then its blend with the sprite. Alpha is
                // always sprite alpha times colour alpha so the colour's alpha stays an opacity control.
                float4 baseColor = gradientBaseColor(IN.tint, IN.gStops0, IN.gStops1, IN.gDir, IN.box.zw, IN.box.xy);
                float3 blended = blendColor(sprite.rgb, baseColor.rgb, (int)colorBlendMode);
                float4 color = float4(lerp(sprite.rgb, blended, blendFactor), sprite.a * baseColor.a);

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
