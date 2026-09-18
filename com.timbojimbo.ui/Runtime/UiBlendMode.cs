namespace TimboJimbo.UI
{
    /// <summary>
    /// How a <see cref="Box"/> or <see cref="Img"/> composites with what is behind it. Each mode has its own
    /// shared material (with different blend factors), so elements of the same component and mode batch
    /// together while different modes are separate draw calls.
    /// </summary>
    public enum UiBlendMode
    {
        /// <summary>Standard alpha "over" compositing.</summary>
        Normal,
        /// <summary>Adds to the background; good for glows and light.</summary>
        Additive,
        /// <summary>Multiplies with the background; always darkens.</summary>
        Multiply,
        /// <summary>Inverse-multiply; always lightens.</summary>
        Screen
    }

    public static class UiBlendModeExtensions
    {
        /// <summary>Suffix appended to a component's shared material name for this mode; empty for Normal.</summary>
        public static string MaterialSuffix(this UiBlendMode mode)
        {
            return mode == UiBlendMode.Normal ? "" : mode.ToString();
        }
    }
}
