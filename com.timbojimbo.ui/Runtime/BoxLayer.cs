using System;
using TimboJimbo.Core;
using UnityEngine;

namespace TimboJimbo.UI
{
    /// <summary>
    /// One layer of a <see cref="Box"/>: the Box's shape painted in a colour or a gradient, filled or as a ring
    /// (<see cref="Stroke"/>), crisp or soft (<see cref="Blur"/>), moved (<see cref="Offset"/>) and grown or shrunk
    /// (<see cref="Spread"/>). A Box has three, its <see cref="Box.Shadow"/>, <see cref="Box.Fill"/> and
    /// <see cref="Box.Border"/>, drawn in that order. They are the same kind of layer, so a slot is named for what it is
    /// usually for, not limited to it: a shadow can be a glow ring and a fill can be soft. Each is one quad of the Box's
    /// mesh carrying its values in vertex data, so a Box drawing all three is still one draw call.
    /// </summary>
    [Serializable]
    public struct BoxLayer : IEquatable<BoxLayer>
    {
        [Tooltip("Whether the layer is drawn.")]
        public bool Enabled;

        [Tooltip("The layer's colour. It multiplies the gradient when one is on, and the Box's own Color multiplies every layer.")]
        public Color Color;

        [Tooltip("Width of a ring along the edge. 0 fills the shape; positive draws the ring inside the edge (a CSS border), negative outside it.")]
        public float Stroke;

        [Tooltip("Softness of the edge in canvas units. 0 is a crisp anti-aliased edge; larger gives a soft shadow or glow.")]
        [Min(0f)] public float Blur;

        [Tooltip("Moves the layer without moving the Box, x right and y up, as a shadow's offset.")]
        public Vector2 Offset;

        [Tooltip("Grows the layer past the Box's shape (positive) or shrinks it inside (negative) on every side, in canvas units, with its corners kept concentric, as a shadow's spread.")]
        public float Spread;

        [Tooltip("Fills the layer with a three-stop gradient (from, via, to) multiplied by its Color.")]
        public bool GradientEnabled;

        [Tooltip("Colour space the gradient is interpolated in.")]
        public ColorInterpolationMode GradientMode;

        [Tooltip("When off the via stop is ignored and the gradient runs straight from From to To.")]
        public bool GradientUseVia;

        public Color GradientFrom;
        public Color GradientVia;
        public Color GradientTo;

        [Tooltip("Position of the via stop along the gradient, 0 at the From end and 1 at the To end.")]
        [Range(0f, 1f)] public float GradientViaPosition;

        [Tooltip("Gradient direction in degrees. 0 runs left to right, 90 bottom to top, increasing counter-clockwise.")]
        public float GradientAngle;

        /// <summary>A drawn layer filling the shape in <paramref name="color"/>, with the gradient off and set to white → black.</summary>
        public BoxLayer(Color color)
        {
            Enabled = true;
            Color = color;
            Stroke = 0f;
            Blur = 0f;
            Offset = Vector2.zero;
            Spread = 0f;
            GradientEnabled = false;
            GradientMode = ColorInterpolationMode.OkLab;
            GradientUseVia = false;
            GradientFrom = Color.white;
            GradientVia = new Color(0.5f, 0.5f, 0.5f, 1f);
            GradientTo = Color.black;
            GradientViaPosition = 0.5f;
            GradientAngle = 0f;
        }

        public bool Equals(BoxLayer other)
        {
            // Exact, as the hash is: Unity's == on colours and vectors is approximate.
            return Enabled == other.Enabled
                   && Color.Equals(other.Color)
                   && Stroke == other.Stroke
                   && Blur == other.Blur
                   && Offset.Equals(other.Offset)
                   && Spread == other.Spread
                   && GradientEnabled == other.GradientEnabled
                   && GradientMode == other.GradientMode
                   && GradientUseVia == other.GradientUseVia
                   && GradientFrom.Equals(other.GradientFrom)
                   && GradientVia.Equals(other.GradientVia)
                   && GradientTo.Equals(other.GradientTo)
                   && GradientViaPosition == other.GradientViaPosition
                   && GradientAngle == other.GradientAngle;
        }

        public override bool Equals(object obj) => obj is BoxLayer other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Enabled);
            hash.Add(Color);
            hash.Add(Stroke);
            hash.Add(Blur);
            hash.Add(Offset);
            hash.Add(Spread);
            hash.Add(GradientEnabled);
            hash.Add(GradientMode);
            hash.Add(GradientUseVia);
            hash.Add(GradientFrom);
            hash.Add(GradientVia);
            hash.Add(GradientTo);
            hash.Add(GradientViaPosition);
            hash.Add(GradientAngle);
            return hash.ToHashCode();
        }
    }
}
