namespace TimboJimbo.UI
{
    /// <summary>
    /// Named stops along the corner curvature family used by <see cref="Box"/>.
    /// The serialized truth is the continuous <see cref="Box.CornerCurvature"/> float;
    /// these presets only map to points on it, ordered from roundest to squarest.
    /// </summary>
    public enum CornerShape
    {
        Circle = 0,
        Parabola = 1,
        Cosine = 2,
        Cubic = 3
    }

    public static class CornerShapeExtensions
    {
        /// <summary>Number of preset stops minus one, i.e. the curvature scale used by the shader.</summary>
        public const int LastShapeIndex = 3;

        /// <summary>Maps a preset to its position on the normalised 0..1 curvature range.</summary>
        public static float ToCurvature(this CornerShape shape)
        {
            return (int)shape / (float)LastShapeIndex;
        }
    }
}
