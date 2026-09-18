using TimboJimbo.UI;
using UnityEditor;
using UnityEngine.U2D;

namespace TimboJimboEditor.UI
{
    /// <summary>
    /// Keeps the editor preview in step with <see cref="SpriteAtlasMipBias"/>. The bias is a sampler state that
    /// an import resets, so it is re-applied whenever an atlas is imported and once after each domain reload.
    /// </summary>
    internal sealed class SpriteAtlasMipBiasEditorHooks : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (var path in importedAssets)
            {
                if (IsAtlas(path))
                    SpriteAtlasMipBias.Apply(AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path));
            }
        }

        [InitializeOnLoadMethod]
        private static void ApplyAfterDomainReload()
        {
            // Defer so this never runs in the middle of an import.
            EditorApplication.delayCall += () =>
            {
                foreach (var guid in AssetDatabase.FindAssets("t:SpriteAtlas"))
                    SpriteAtlasMipBias.Apply(AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AssetDatabase.GUIDToAssetPath(guid)));
            };

            // Hook into regeneration of sprite atlases to re-apply the mip bias.
            SpriteAtlasManager.atlasRegistered -= SpriteAtlasMipBias.Apply;
            SpriteAtlasManager.atlasRegistered += SpriteAtlasMipBias.Apply;
        }

        private static bool IsAtlas(string path)
        {
            return path.EndsWith(".spriteatlas") || path.EndsWith(".spriteatlasv2");
        }
    }
}
