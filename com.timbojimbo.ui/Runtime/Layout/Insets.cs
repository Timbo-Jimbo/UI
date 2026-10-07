using System;

namespace TimboJimbo.UI.Layout
{
    /// <summary>Space on each side of a node's content: its padding.</summary>
    [Serializable]
    public struct Insets : IEquatable<Insets>
    {
        public float Left;
        public float Right;
        public float Top;
        public float Bottom;

        public Insets(float left, float right, float top, float bottom)
        {
            Left = left;
            Right = right;
            Top = top;
            Bottom = bottom;
        }

        /// <summary>The same on every side.</summary>
        public static Insets All(float inset) => new(inset, inset, inset, inset);

        /// <summary><paramref name="horizontal"/> left and right, <paramref name="vertical"/> top and bottom.</summary>
        public static Insets Symmetric(float horizontal, float vertical) => new(horizontal, horizontal, vertical, vertical);

        /// <summary>Left plus right.</summary>
        public float Horizontal => Left + Right;

        /// <summary>Top plus bottom.</summary>
        public float Vertical => Top + Bottom;

        /// <summary>Each side of one plus the same side of the other.</summary>
        public static Insets operator +(Insets a, Insets b) =>
            new(a.Left + b.Left, a.Right + b.Right, a.Top + b.Top, a.Bottom + b.Bottom);

        public bool Equals(Insets other) => Left == other.Left && Right == other.Right && Top == other.Top && Bottom == other.Bottom;

        public override bool Equals(object obj) => obj is Insets other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Left, Right, Top, Bottom);

        public static bool operator ==(Insets a, Insets b) => a.Equals(b);

        public static bool operator !=(Insets a, Insets b) => !a.Equals(b);
    }
}
