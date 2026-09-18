namespace TimboJimbo.UI
{
    /// <summary>
    /// Tap budget of the single-pass sprite blur on <see cref="Img"/>. Taps are spread across the blur radius
    /// and each samples a mip chosen so they overlap into a smooth result. A small radius uses fewer taps than
    /// the budget; a wide radius uses the whole budget and then softens, so cost is capped by the budget, not
    /// the radius.
    /// </summary>
    public enum BlurQuality
    {
        /// <summary>Up to 3×3 taps.</summary>
        Low = 1,
        /// <summary>Up to 5×5 taps.</summary>
        Medium = 2,
        /// <summary>Up to 7×7 taps.</summary>
        High = 3
    }
}
