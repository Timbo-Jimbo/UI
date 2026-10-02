using TimboJimbo.UI.Variants;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UI.Variants
{
    /// <summary>
    /// Right-clicking a property of anything under a variant set offers, for each variant selected there, to add the
    /// property's current value to it, or to take the property out of it.
    /// </summary>
    [InitializeOnLoad]
    internal static class VariantPropertyMenu
    {
        static VariantPropertyMenu() => EditorApplication.contextualPropertyMenu += Fill;

        private static void Fill(GenericMenu menu, SerializedProperty property)
        {
            var serialized = property.serializedObject;
            if (serialized.isEditingMultipleObjects) return;

            var target = serialized.targetObject;
            if (target is VariantSet || EditorUtility.IsPersistent(target)) return;
            var gameObject = target switch
            {
                GameObject go => go,
                Component component => component.gameObject,
                _ => null,
            };
            if (gameObject == null) return;

            string path = VariantCapture.EntryPath(serialized, property.propertyPath);
            if (path == null) return;
            var accessor = VariantAccessor.For(target, path);
            if (accessor == null) return;

            bool separated = false;
            foreach (var set in gameObject.GetComponentsInParent<VariantSet>(true))
            {
                for (int g = 0; g < set.Groups.Count; g++)
                {
                    var group = set.Groups[g];
                    var variant = group.Find(group.Selected);
                    if (variant == null) continue;

                    if (!separated)
                    {
                        menu.AddSeparator("");
                        separated = true;
                    }

                    var owner = set;
                    int groupIndex = g;
                    string variantName = variant.Name;
                    string label = $"Variants/{Safe(set.name)} · {Safe(group.Name)}: {Safe(variantName)}";
                    if (variant.IndexOf(target, path) >= 0)
                        menu.AddItem(new GUIContent($"{label}/Remove from Variant"), false,
                            () => VariantEditing.RemoveEntry(owner, groupIndex, variantName, target, path, $"Remove from {variantName}"));
                    else
                        menu.AddItem(new GUIContent($"{label}/Add to Variant"), false,
                            () => VariantEditing.SetEntry(owner, groupIndex, variantName, target, path, accessor.Read(target), $"Add to {variantName}"));
                }
            }
        }

        // A slash in a menu path would open a submenu.
        private static string Safe(string name) => name.Replace('/', '∕');
    }
}
