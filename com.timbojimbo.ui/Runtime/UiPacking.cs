using TimboJimbo.Core;
using UnityEngine;

namespace TimboJimbo.UI
{
    /// <summary>
    /// Packs component parameters into vertex data for the package shaders; mirrors UiPacking.cginc. Values
    /// are exact at the vertices, but interpolation and perspective correction can deliver them to the fragment
    /// stage a hair off (65279.998 for 65280), so the shader side rounds before treating anything as an integer
    /// and the "integer plus fraction" packing here keeps the fraction in a half range to leave headroom.
    /// </summary>
    internal static class UiPacking
    {
        /// <summary>
        /// Packs a small integer and a 0..1 fraction into one float as integer + fraction × 0.5. The half-range
        /// fraction leaves ±0.25 of headroom, so the integer survives interpolation error and a fraction of
        /// exactly 1 still decodes cleanly.
        /// </summary>
        public static float PackIntAndFraction(int integer, float fraction01)
        {
            return integer + Mathf.Clamp01(fraction01) * 0.5f;
        }

        /// <summary>
        /// Two 0..1 values in one float, 12 bits (4095 steps) each; exact in a float's 24-bit mantissa. For
        /// continuous parameters where a step of 1/4095 of the range is invisible (angles, relative sizes).
        /// </summary>
        public static float PackPair12(float a01, float b01)
        {
            return Mathf.Floor(Mathf.Clamp01(a01) * 4095f + 0.5f) * 4096f + Mathf.Floor(Mathf.Clamp01(b01) * 4095f + 0.5f);
        }

        /// <summary>
        /// Box's gradient header: the interpolation mode (0 = gradient off, else mode + 1), plus 8 when there is a via
        /// stop, with the wrapped angle as the fraction. Mirrors unpackGradientStops in UiGradient.cginc.
        /// </summary>
        public static float PackGradientHeader(bool enabled, ColorInterpolationMode mode, bool useVia, float angleDegrees)
        {
            return PackIntAndFraction((enabled ? (int)mode + 1 : 0) + (useVia ? 8 : 0), Mathf.Repeat(angleDegrees / 360f, 1f));
        }

        /// <summary>
        /// Packs the gradient stops for texcoords: each stop's RGB as three 8-bit values in one float and the three
        /// alphas (from, via, to) in a fourth (<see cref="PackTriple8"/>). Texcoords reach the shader as they were
        /// written; a normal or tangent does not, since the canvas transforms them by the graphic's rotation and
        /// scale when it batches, which scrambles anything packed into them.
        /// </summary>
        public static Vector4 PackStopsForTexcoords(Color from, Color via, Color to)
        {
            return new Vector4(
                PackTriple8(from.r, from.g, from.b),
                PackTriple8(via.r, via.g, via.b),
                PackTriple8(to.r, to.g, to.b),
                PackTriple8(from.a, via.a, to.a));
        }

        /// <summary>
        /// Three 0..1 values in one float, 8 bits (255 steps) each; exact in a float's 24-bit mantissa. For
        /// parameters where colour-like precision is enough.
        /// </summary>
        public static float PackTriple8(float a01, float b01, float c01)
        {
            return Mathf.Floor(Mathf.Clamp01(a01) * 255f + 0.5f) * 65536f
                   + Mathf.Floor(Mathf.Clamp01(b01) * 255f + 0.5f) * 256f
                   + Mathf.Floor(Mathf.Clamp01(c01) * 255f + 0.5f);
        }

        /// <summary>
        /// Img's header float: the gradient mode (as above), the blur tap count, the jitter and tiled flags
        /// and the colour blend mode folded into one small integer (11 bits), with the wrapped gradient angle
        /// as the fraction. Mirrors unpackImgHeader in Img.shader.
        /// </summary>
        public static float PackImgHeader(bool gradientEnabled, ColorInterpolationMode mode, int blurTaps, bool jitter, bool tiled, ColorBlendMode colorBlend, float angleDegrees)
        {
            var header = (gradientEnabled ? (int)mode + 1 : 0)
                         + Mathf.Clamp(blurTaps, 0, 7) * 8
                         + (jitter ? 64 : 0)
                         + (tiled ? 128 : 0)
                         + Mathf.Clamp((int)colorBlend, 0, 7) * 256;
            return PackIntAndFraction(header, Mathf.Repeat(angleDegrees / 360f, 1f));
        }

        /// <summary>Smallest and largest magnitude <see cref="PackLogPair12"/> spans, as powers of two. Mirrors UiPacking.cginc.</summary>
        private const float LogPairMin = -24f, LogPairMax = 8f;

        /// <summary>
        /// Two non-negative values in one float, each a 12-bit step on a log2 scale from 2^-24 to 2^8 (steps about
        /// 0.5% apart), with 0 kept exact. For a scale that spans orders of magnitude, such as a blur step in uv.
        /// Mirrors unpackLogPair12 in UiPacking.cginc.
        /// </summary>
        public static float PackLogPair12(float a, float b)
        {
            return LogCode(a) * 4096f + LogCode(b);
        }

        // 0 for zero, else 1..4095 along the log scale.
        private static float LogCode(float value)
        {
            if (value <= 0f) return 0f;
            var t = (Mathf.Log(value, 2f) - LogPairMin) / (LogPairMax - LogPairMin);
            return 1f + Mathf.Round(Mathf.Clamp01(t) * 4094f);
        }
    }
}
