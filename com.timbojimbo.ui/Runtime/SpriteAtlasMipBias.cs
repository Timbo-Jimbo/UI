using UnityEngine;
using UnityEngine.U2D;

namespace TimboJimbo.UI
{
    /// <summary>
    /// Applies a negative mip bias to every Sprite Atlas texture so icons drawn well below their source size
    /// stay crisp. A Sprite Atlas can generate mipmaps but exposes neither a mip bias nor Kaiser filtering, and
    /// its box-filtered mips read soft; a small negative bias selects a sharper mip level without bringing back
    /// aliasing. The bias is a sampler state rather than an import setting, so it cannot be baked into the atlas:
    /// it is applied to each atlas texture as the atlas registers at runtime, and re-applied by the editor after
    /// import so the Scene and Game views preview the same result.
    /// </summary>
    public static class SpriteAtlasMipBias
    {
        /// <summary>
        /// Mip bias applied to every Sprite Atlas texture. Negative values pick sharper mip levels. The default
        /// of -0.5 counters the softness of box-filtered atlas mips for typical icon minification (roughly 4x)
        /// without reintroducing shimmer. Override from code at startup, before any atlas loads, if a project
        /// needs a different value.
        /// </summary>
        public static float Bias = -0.5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            SpriteAtlasManager.atlasRegistered -= Apply;
            SpriteAtlasManager.atlasRegistered += Apply;
        }

        /// <summary>Applies <see cref="Bias"/> to every texture page of the given atlas.</summary>
        public static void Apply(SpriteAtlas atlas)
        {
            if (atlas == null || atlas.spriteCount == 0) return;

            // A Sprite Atlas exposes its textures only through its sprites, and GetSprites hands back clones,
            // so read the textures off them and release the clones.
            var sprites = new Sprite[atlas.spriteCount];
            atlas.GetSprites(sprites);
            for (var i = 0; i < sprites.Length; i++)
            {
                var sprite = sprites[i];
                if (sprite == null) continue;
                if (sprite.texture != null) sprite.texture.mipMapBias = Bias;
                if (Application.isPlaying) Object.Destroy(sprite); else Object.DestroyImmediate(sprite);
            }
        }
    }
}
