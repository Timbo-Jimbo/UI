using TimboJimbo.UI.Variants;
using UnityEditor;
using Object = UnityEngine.Object;

namespace TimboJimboEditor.UI.Variants
{
    /// <summary>Changes to a variant set's entries made from outside its inspector, with undo.</summary>
    internal static class VariantEditing
    {
        /// <summary>Sets <paramref name="property"/> of <paramref name="target"/> to <paramref name="value"/> in a variant, adding the entry if it has none.</summary>
        public static void SetEntry(VariantSet set, int group, string variant, Object target, string property, VariantValue value, string undoName)
        {
            var found = set.GroupList[group].Find(variant);
            if (found == null) return;

            int index = found.IndexOf(target, property);
            if (index >= 0 && found.EntryList[index].Value.Equals(value)) return;

            Undo.RecordObject(set, undoName);
            var entry = new VariantEntry(target, property, value);
            if (index >= 0)
                found.EntryList[index] = entry;
            else
                found.EntryList.Add(entry);
            Recorded(set);
        }

        /// <summary>Takes <paramref name="property"/> of <paramref name="target"/> out of a variant, so it keeps its default there.</summary>
        public static void RemoveEntry(VariantSet set, int group, string variant, Object target, string property, string undoName)
        {
            var found = set.GroupList[group].Find(variant);
            int index = found?.IndexOf(target, property) ?? -1;
            if (index < 0) return;

            Undo.RecordObject(set, undoName);
            found.EntryList.RemoveAt(index);
            Recorded(set);
        }

        private static void Recorded(VariantSet set)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(set))
                PrefabUtility.RecordPrefabInstancePropertyModifications(set);
            VariantPreview.MarkDirty();
        }
    }
}
