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

        /// <summary>Interpolation mode (0 = gradient off, else mode + 1) with the wrapped angle as the fraction.</summary>
        public static float PackModeAndAngle(bool enabled, ColorInterpolationMode mode, float angleDegrees)
        {
            return PackIntAndFraction(enabled ? (int)mode + 1 : 0, Mathf.Repeat(angleDegrees / 360f, 1f));
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

        /// <summary>
        /// Packs the gradient stops into the normal and tangent, two 8-bit channels per float, leaving the
        /// colour channel free for <see cref="Graphic.color"/>. A via position below 0 tells the shader there
        /// is no via stop.
        /// </summary>
        public static void PackStops(Color from, Color via, Color to, bool useVia, float viaPosition, out Vector3 normal, out Vector4 tangent)
        {
            var f = PackColor(from);
            var v = PackColor(via);
            var t = PackColor(to);
            normal = new Vector3(f.x, f.y, v.x);
            tangent = new Vector4(v.y, t.x, t.y, useVia ? viaPosition : -1f);
        }

        // Packs an LDR colour into two floats, two 8-bit channels each (r,g then b,a). Integers up to 65535
        // are exact in a 32-bit float, so the stops arrive intact at the vertices.
        private static Vector2 PackColor(Color c)
        {
            var r = Mathf.Floor(Mathf.Clamp01(c.r) * 255f);
            var g = Mathf.Floor(Mathf.Clamp01(c.g) * 255f);
            var b = Mathf.Floor(Mathf.Clamp01(c.b) * 255f);
            var a = Mathf.Floor(Mathf.Clamp01(c.a) * 255f);
            return new Vector2(r * 256f + g, b * 256f + a);
        }
    }
}
