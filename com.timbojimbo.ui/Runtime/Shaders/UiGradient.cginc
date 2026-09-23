// Three-stop gradient evaluation shared by the package's UI shaders (Box, Img).
//
// Vertex contract (written by TimboJimbo.UI.UiPacking), always in texcoords the component places: the
// canvas passes those through as written, while it transforms a normal or tangent by the graphic's rotation
// and scale when it batches, which would scramble anything packed there. A stop's RGB travels as three 8-bit
// integers in one float (PackTriple8), its alpha as a byte of another.
//   Box (unpackGradientStops): three stops as four floats, from, via and to RGB then the three alphas
//     (PackStopsForTexcoords); a header, interpMode (integer, 0 = off, else mode+1) plus 8 with a via stop,
//     and the normalised angle as a half-range fraction (PackGradientHeader); and the via position.
//   Img (unpackGradientTwoStops): two stops, from and to RGB and their alphas, with the mode and angle taken
//     from its own header.
//
// Usage: in the vertex shader unpack into the three v2f fields, then in the fragment shader call
// gradientBaseColor with the rect-centred pixel position and the rect's half size.
#ifndef TIMBOJIMBO_UI_GRADIENT_INCLUDED
#define TIMBOJIMBO_UI_GRADIENT_INCLUDED

#include "UnityCG.cginc"
#include "UiPacking.cginc"

// Gradient interpolation modes, matching TimboJimbo.Core.ColorInterpolationMode (offset by +1;
// 0 means the gradient is disabled).
#define GRAD_OFF 0
#define GRAD_RGB 1
#define GRAD_HSV 2
#define GRAD_OKLAB 3
#define GRAD_OKLCH 4

#ifndef PI
#define PI 3.14159265358979
#endif
#define TWO_PI 6.28318530717959

// ---- Colour interpolation (ported from TimboJimbo.Core.ColorExtra) -------------------------------------
// Stops arrive in nominal sRGB space. RGB and HSV interpolate there directly; OkLab and OkLCh convert to
// linear light first (matching ColorExtra) and back to nominal afterwards. Whatever a mode returns is
// nominal, and gradientBaseColor linearises it once for the framebuffer.
float3 rgb2hsv(float3 c)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

float3 hsv2rgb(float3 c)
{
    float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
}

float3 linearToLMS(float3 c)
{
    return float3(
        0.4122214708 * c.r + 0.5363325363 * c.g + 0.0514459929 * c.b,
        0.2119034982 * c.r + 0.6806995451 * c.g + 0.1073969566 * c.b,
        0.0883024619 * c.r + 0.2817188376 * c.g + 0.6299787005 * c.b);
}

float3 lmsToOkLab(float3 lms)
{
    float3 c = pow(max(lms, 0.0), 1.0 / 3.0);
    return float3(
        0.2104542553 * c.x + 0.7936177850 * c.y - 0.0040720468 * c.z,
        1.9779984951 * c.x - 2.4285922050 * c.y + 0.4505937099 * c.z,
        0.0259040371 * c.x + 0.7827717662 * c.y - 0.8086757660 * c.z);
}

float3 okLabToLMS(float3 lab)
{
    float3 c = float3(
        lab.x + 0.3963377774 * lab.y + 0.2158037573 * lab.z,
        lab.x - 0.1055613458 * lab.y - 0.0638541728 * lab.z,
        lab.x - 0.0894841775 * lab.y - 1.2914855480 * lab.z);
    return c * c * c;
}

float3 lmsToLinear(float3 lms)
{
    return float3(
         4.0767416621 * lms.x - 3.3077115913 * lms.y + 0.2309699292 * lms.z,
        -1.2684380046 * lms.x + 2.6097574011 * lms.y - 0.3413193965 * lms.z,
        -0.0041960863 * lms.x - 0.7034186147 * lms.y + 1.7076147010 * lms.z);
}

float3 rgbToOkLab(float3 c) { return lmsToOkLab(linearToLMS(GammaToLinearSpace(c))); }
float3 okLabToRGB(float3 lab) { return LinearToGammaSpace(lmsToLinear(okLabToLMS(lab))); }

float4 lerpRGB(float4 a, float4 b, float t) { return lerp(a, b, t); }

float4 lerpHSV(float4 a, float4 b, float t)
{
    float3 ah = rgb2hsv(a.rgb);
    float3 bh = rgb2hsv(b.rgb);
    if (abs(bh.x - ah.x) > 0.5)
    {
        if (bh.x > ah.x) ah.x += 1.0; else bh.x += 1.0;
    }
    float3 hsv = lerp(ah, bh, t);
    hsv.x = frac(hsv.x);
    return float4(hsv2rgb(hsv), lerp(a.a, b.a, t));
}

float4 lerpOkLab(float4 a, float4 b, float t)
{
    float3 lab = lerp(rgbToOkLab(a.rgb), rgbToOkLab(b.rgb), t);
    return float4(okLabToRGB(lab), lerp(a.a, b.a, t));
}

float4 lerpOkLCh(float4 a, float4 b, float t)
{
    float3 la = rgbToOkLab(a.rgb);
    float3 lb = rgbToOkLab(b.rgb);
    float2 ca = float2(length(la.yz), atan2(la.z, la.y)); // chroma, hue
    float2 cb = float2(length(lb.yz), atan2(lb.z, lb.y));
    float ha = ca.y, hb = cb.y;
    if (abs(hb - ha) > PI)
    {
        if (hb > ha) ha += TWO_PI; else hb += TWO_PI;
    }
    float l = lerp(la.x, lb.x, t);
    float c = lerp(ca.x, cb.x, t);
    float h = lerp(ha, hb, t);
    if (h > PI) h -= TWO_PI; else if (h < -PI) h += TWO_PI;
    float3 lab = float3(l, c * cos(h), c * sin(h));
    return float4(okLabToRGB(lab), lerp(a.a, b.a, t));
}

// ---- Gradient ----------------------------------------------------------------------------------------
// Fills the v2f fields gradientBaseColor reads from stop channels as 0..255 integers: stops0 = from and via,
// stops1 = to, the via position (< 0 for none) and the mode (0 = off), each stop repacked two channels to a
// float (r*256+g, b*256+a); dir = the gradient axis. Runs in the vertex stage on exact attributes.
void gradientFields(float3 from, float3 via, float3 to, float3 alpha, float viaPosition, float mode, float angle01, out float4 stops0, out float4 stops1, out float2 dir)
{
    stops0 = float4(from.x * 256.0 + from.y, from.z * 256.0 + alpha.x, via.x * 256.0 + via.y, via.z * 256.0 + alpha.y);
    stops1 = float4(to.x * 256.0 + to.y, to.z * 256.0 + alpha.z, viaPosition, mode);
    float angle = angle01 * TWO_PI;
    dir = float2(cos(angle), sin(angle));
}

// Box's three stops (UiPacking.PackStopsForTexcoords: from, via and to RGB, then the three alphas) with its
// header (UiPacking.PackGradientHeader) and via position.
void unpackGradientStops(float4 packedStops, float header, float viaPosition, out float4 stops0, out float4 stops1, out float2 dir)
{
    float code, angle01;
    unpackIntAndFraction(header, code, angle01);
    float hasVia = code > 7.5 ? 1.0 : 0.0;
    gradientFields(unpackBytes(packedStops.x), unpackBytes(packedStops.y), unpackBytes(packedStops.z), unpackBytes(packedStops.w),
        hasVia > 0.5 ? viaPosition : -1.0, code - hasVia * 8.0, angle01, stops0, stops1, dir);
}

// Img's two stops: from and to RGB (UiPacking.PackTriple8) and their alphas as 0..255 integers, with the mode
// and angle from its own header.
void unpackGradientTwoStops(float fromRgb, float toRgb, float2 alphas, float mode, float angle01, out float4 stops0, out float4 stops1, out float2 dir)
{
    gradientFields(unpackBytes(fromRgb), 0.0, unpackBytes(toRgb), float3(alphas.x, 0.0, alphas.y), -1.0, mode, angle01, stops0, stops1, dir);
}

float4 evalGradient(float4 from, float4 via, float4 to, float viaPos, int mode, float t)
{
    float4 a, b;
    float lt;
    if (viaPos < 0.0)
    {
        a = from; b = to; lt = t;
    }
    else if (t <= viaPos)
    {
        a = from; b = via; lt = viaPos > 1e-5 ? t / viaPos : 0.0;
    }
    else
    {
        a = via; b = to; lt = viaPos < 1.0 - 1e-5 ? (t - viaPos) / (1.0 - viaPos) : 1.0;
    }
    lt = saturate(lt);

    if (mode == GRAD_RGB) return lerpRGB(a, b, lt);
    if (mode == GRAD_HSV) return lerpHSV(a, b, lt);
    if (mode == GRAD_OKLAB) return lerpOkLab(a, b, lt);
    return lerpOkLCh(a, b, lt);
}

// The pixel's base colour: the solid tint, or the gradient multiplied by it. The tint is Graphic.color in
// framebuffer space and carries the CanvasGroup alpha, so multiplying the gradient by it makes colour and
// canvas fades apply to every stop, not just one. position is rect-centred; the gradient runs from the
// rect's edge at -dir to its edge at +dir. Runs in the fragment stage, so the mode (an integer at the
// vertices) is rounded before use.
float4 gradientBaseColor(float4 tint, float4 stops0, float4 stops1, float2 dir, float2 position, float2 halfSize)
{
    float mode = unpackInt(stops1.w);
    if (mode < 0.5) return tint;

    float extent = abs(halfSize.x * dir.x) + abs(halfSize.y * dir.y);
    float t = extent > 1e-5 ? saturate(dot(position, dir) / (2.0 * extent) + 0.5) : 0.5;

    float4 from = unpackColor(stops0.xy);
    float4 via  = unpackColor(stops0.zw);
    float4 to   = unpackColor(stops1.xy);
    float4 grad = evalGradient(from, via, to, stops1.z, (int)mode, t);

    // Stops are authored (nominal) sRGB; convert the interpolated result to framebuffer space to match
    // the tint, then tint it.
    if (!IsGammaSpace())
        grad.rgb = GammaToLinearSpace(grad.rgb);
    return grad * tint;
}

#endif
