using System.Collections.Generic;
using UnityEngine;

namespace TimboJimbo.UI
{
    /// <summary>
    /// Resolves the shared per-blend-mode materials that ship in the package's Resources folder. Every
    /// instance of a component with the same blend mode gets the same material object, so they batch.
    /// </summary>
    internal static class SharedMaterials
    {
        /// <summary>Resources folder holding the shared materials.</summary>
        public const string ResourceFolder = "TimboJimbo/UI/";

        private static readonly Dictionary<(string, UiBlendMode), Material> s_cache = new();

        /// <summary>
        /// The shared material named <paramref name="baseName"/> plus the mode's suffix (e.g. "Box",
        /// "BoxAdditive"), or <paramref name="fallback"/> when it is missing.
        /// </summary>
        public static Material For(string baseName, UiBlendMode mode, Material fallback)
        {
            var key = (baseName, mode);
            if (!s_cache.TryGetValue(key, out var mat) || mat == null)
            {
                var path = ResourceFolder + baseName + mode.MaterialSuffix();
                mat = Resources.Load<Material>(path);
                if (mat == null)
                    Debug.LogError($"{baseName}: shared material missing at Resources/{path}. Falling back to the default UI material.");
                s_cache[key] = mat;
            }

            return mat != null ? mat : fallback;
        }
    }
}
