namespace TimboJimbo.UI
{
    /// <summary>
    /// How an <see cref="Img"/>'s colour (flat or gradient) combines with the sprite's own colour. Alpha is
    /// always sprite alpha times colour alpha, so the colour's alpha stays an opacity control in every mode.
    /// </summary>
    public enum ColorBlendMode
    {
        /// <summary>Sprite × colour; the stock Image behaviour.</summary>
        Multiply,
        /// <summary>The colour replaces the sprite's RGB, keeping its alpha: a silhouette recolour.</summary>
        Replace,
        /// <summary>Sprite + colour, clamped.</summary>
        Add,
        /// <summary>Inverse multiply; always lightens.</summary>
        Screen,
        /// <summary>Per-channel minimum.</summary>
        Darken,
        /// <summary>Per-channel maximum.</summary>
        Lighten,
        /// <summary>Multiply in the sprite's dark areas, screen in its light areas.</summary>
        Overlay,
        /// <summary>Absolute difference.</summary>
        Difference
    }
}
