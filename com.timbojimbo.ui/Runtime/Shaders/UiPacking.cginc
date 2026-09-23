// Vertex-data packing shared by the package's UI shaders; mirrors TimboJimbo.UI.UiPacking (C#).
//
// Packed values are exact integers at the vertices, but interpolation and perspective correction can deliver
// them to the fragment stage a hair off (65279.998 for 65280). Splitting such a value with floor carries the
// error into the next field: red decoded as yellow, alpha 0 as 255, and which way it tipped depended on the
// viewport size. Every decode here therefore rounds before treating anything as an integer, and the
// "integer plus fraction" packing keeps the fraction in a half range so the integer has ±0.25 of headroom.
//
// Rounding is round(), not round(v): packings fill up to all 24 bits of a float's mantissa, and above
// 2^23 a float has no halves, so v + 0.5 lands exactly on a tie that rounds to even. An odd value then tips up
// by one and carries into the next field; three alphas of 1 (16777215) decoded as the first alpha 0.
#ifndef TIMBOJIMBO_UI_PACKING_INCLUDED
#define TIMBOJIMBO_UI_PACKING_INCLUDED

// Nearest integer of an interpolated value that was an exact integer at the vertices.
float unpackInt(float v)
{
    return round(v);
}

// Splits a value packed by UiPacking.PackIntAndFraction (integer + fraction * 0.5). The half-range fraction
// leaves ±0.25 of headroom, so the integer survives interpolation error and a fraction of exactly 1.
void unpackIntAndFraction(float packed, out float integer, out float fraction)
{
    integer = floor(packed + 0.25);
    fraction = saturate((packed - integer) * 2.0);
}

// Splits two 0..1 values packed by UiPacking.PackPair12 (12 bits each, 4095 steps, exact in a float's 24-bit
// mantissa). Rounded first; an interpolation error of a whole step is 1/4095 of the range, so this suits
// continuous parameters (angles, relative sizes), not colours.
void unpackPair12(float packed, out float a, out float b)
{
    packed = round(packed);
    float hi = floor(packed / 4096.0);
    a = hi / 4095.0;
    b = (packed - hi * 4096.0) / 4095.0;
}

// Splits two non-negative values packed by UiPacking.PackLogPair12: 12-bit steps on a log2 scale from
// 2^-24 to 2^8, 0 meaning zero.
#define LOG_PAIR_MIN -24.0
#define LOG_PAIR_MAX 8.0
float2 unpackLogPair12(float packed)
{
    packed = round(packed);
    float hi = floor(packed / 4096.0);
    float2 code = float2(hi, packed - hi * 4096.0);
    float2 value = exp2(LOG_PAIR_MIN + (code - 1.0) / 4094.0 * (LOG_PAIR_MAX - LOG_PAIR_MIN));
    return code > 0.5 ? value : 0.0;
}

// The three 8-bit integers (0..255) UiPacking.PackTriple8 packed into one float, first to last.
float3 unpackBytes(float packed)
{
    packed = round(packed);
    float hi = floor(packed / 65536.0);
    float rest = packed - hi * 65536.0;
    float mid = floor(rest / 256.0);
    return float3(hi, mid, rest - mid * 256.0);
}

// Splits three 0..1 values packed by UiPacking.PackTriple8 (8 bits each, 255 steps).
void unpackTriple8(float packed, out float a, out float b, out float c)
{
    float3 bytes = unpackBytes(packed) / 255.0;
    a = bytes.x;
    b = bytes.y;
    c = bytes.z;
}

// Unpacks a colour packed as two floats (two 8-bit channels each) by UiPacking.PackColor.
float4 unpackColor(float2 p)
{
    p = round(p);
    float r = floor(p.x / 256.0);
    float g = p.x - r * 256.0;
    float b = floor(p.y / 256.0);
    float a = p.y - b * 256.0;
    return float4(r, g, b, a) * (1.0 / 255.0);
}

#endif
