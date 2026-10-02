using System;
using System.Collections.Generic;
using System.Linq;
using TimboJimbo.UI.Variants;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimboJimboEditor.UI.Variants
{
    /// <summary>
    /// Inspector for <see cref="VariantSet"/>: each group as a row of tabs (Default and its variants), selecting one
    /// previews it in the scene or prefab; under the selected variant, its name, a Record button, and the values it sets,
    /// by object, each editable or removable. While recording, edits to anything under the set go into the variant. In
    /// play mode the tabs switch variants as code would.
    /// </summary>
    [CustomEditor(typeof(VariantSet))]
    public sealed class VariantSetEditor : UnityEditor.Editor
    {
        private const string DefaultLabel = "Default";
        private const string InheritLabel = "Inherit";

        private static readonly GUIContent s_inherit = new(InheritLabel,
            "Show what the nearest variant set above with a group of this name shows (Default with none), as its parts follow a toast's Type.");
        private static readonly GUIContent s_addVariant = new("+", "Add a variant to this group.");
        private static readonly GUIContent s_animated = new("Animated",
            "Switching this group animates, wherever it is switched from: its layout springs, and its values move on the springs of the layout nodes they are drawn in.");
        private static readonly GUIContent s_animation = new("Animation",
            "What an animated switch is made on, unless a layout node it moves (or one above it) has an Animation of its own. Inherit: the default.");
        private static readonly GUIContent s_menu = new("⋮", "More");
        private static readonly GUIContent s_remove = new("×", "Take this value out of the variant: it keeps its default here.");
        private static readonly Color s_recordColor = new(1f, 0.45f, 0.45f);

        private static GUIStyle s_groupName;

        private VariantSet Set => (VariantSet)target;

        private void OnEnable() => VariantPreview.Sampled += Repaint;
        private void OnDisable() => VariantPreview.Sampled -= Repaint;

        public override void OnInspectorGUI()
        {
            s_groupName ??= new GUIStyle(EditorStyles.textField) { fontStyle = FontStyle.Bold };
            serializedObject.Update();
            var groups = serializedObject.FindProperty("_groups");

            if (EditorUtility.IsPersistent(Set))
                EditorGUILayout.HelpBox("Open the prefab to preview and record its variants. A variant selected here is the one its instances start as.", MessageType.Info);
            else if (VariantPreview.Paused)
                EditorGUILayout.HelpBox("The preview waits while the Animation window or Timeline previews.", MessageType.Info);
            if (groups.arraySize == 0)
                EditorGUILayout.HelpBox("Add a group of variants that exclude one another: a Type of Success, Warning and Error, say.", MessageType.None);

            for (int g = 0; g < groups.arraySize; g++)
                if (!Group(groups, g)) break;

            EditorGUILayout.Space(2);
            if (GUILayout.Button("Add Group"))
                AddGroup(groups);

            serializedObject.ApplyModifiedProperties();
        }

        // One group: its name, its tabs, and its selected variant. False once its structure has changed, which ends the
        // inspector for this event.
        private bool Group(SerializedProperty groups, int g)
        {
            var group = groups.GetArrayElementAtIndex(g);
            var name = group.FindPropertyRelative("_name");
            var selected = group.FindPropertyRelative("_selected");
            var inherit = group.FindPropertyRelative("_inherit");
            var variants = group.FindPropertyRelative("_variants");

            EditorGUILayout.Space(2);
            using var box = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string renamed = EditorGUILayout.DelayedTextField(name.stringValue, s_groupName);
                if (EditorGUI.EndChangeCheck() && renamed.Trim().Length > 0)
                    name.stringValue = renamed.Trim();
                var animated = group.FindPropertyRelative("_animated");
                animated.boolValue = EditorGUILayout.ToggleLeft(s_animated, animated.boolValue, GUILayout.Width(76));
                if (GUILayout.Button(s_menu, EditorStyles.miniButton, GUILayout.Width(22)))
                    GroupMenu(g, groups.arraySize);
            }
            // What an animated switch is made on: Inherit for the default, or a preset of its own.
            if (group.FindPropertyRelative("_animated").boolValue)
                EditorGUILayout.PropertyField(group.FindPropertyRelative("_animation"), s_animation);

            Shown(g);

            using (new EditorGUILayout.HorizontalScope())
            {
                Tab(InheritLabel, null, selected, inherit, g, EditorStyles.miniButtonLeft);
                Tab(DefaultLabel, "", selected, inherit, g, EditorStyles.miniButtonMid);
                for (int v = 0; v < variants.arraySize; v++)
                {
                    string variantName = variants.GetArrayElementAtIndex(v).FindPropertyRelative("_name").stringValue;
                    Tab(variantName, variantName, selected, inherit, g, EditorStyles.miniButtonMid);
                }
                if (GUILayout.Button(s_addVariant, EditorStyles.miniButtonRight, GUILayout.Width(24)))
                {
                    AddVariant(variants, selected, inherit);
                    return false;
                }
            }

            // Inheriting, it has no variant of its own to show here: the line above says what it shows.
            int index = inherit.boolValue ? -1 : IndexOf(variants, selected.stringValue);
            if (index < 0)
            {
                if (variants.arraySize == 0)
                    EditorGUILayout.LabelField("Add a variant with +.", EditorStyles.miniLabel);
                return true;
            }

            var variant = variants.GetArrayElementAtIndex(index);
            var variantNameProperty = variant.FindPropertyRelative("_name");
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string renamed = EditorGUILayout.DelayedTextField("Name", variantNameProperty.stringValue);
                if (EditorGUI.EndChangeCheck())
                    Rename(variants, selected, variantNameProperty, renamed);
                if (GUILayout.Button(s_menu, EditorStyles.miniButton, GUILayout.Width(22)))
                    VariantMenu(g, index, variants.arraySize);
            }

            Record(g, variantNameProperty.stringValue);
            Entries(variant.FindPropertyRelative("_entries"));
            return true;
        }

        // Where what a group shows comes from, when it is not its own selection: a breakpoint, or (inheriting) the
        // nearest set above with a group of its name. In play mode breakpoints select for real, so only an inherited one
        // is said, from the sets as they are.
        private void Shown(int g)
        {
            var group = Set.Groups[g];
            string variant, by;
            if (Application.isPlaying)
            {
                if (!group.Inherits || Set.Above(group.Name, out _) is not { } above) return;
                variant = Set.Get(group.Name);
                by = $"{above.name}'s {group.Name}";
            }
            else if (!VariantPreview.TryGetDriven(Set, g, out variant, out by))
            {
                return;
            }
            string line = variant.Length == 0 || group.IndexOf(variant) >= 0
                ? $"Shown as {Label(variant)}, which {by} selects."
                : $"Shown as {DefaultLabel}: {by} selects {variant}, which this group has no variant of.";
            EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
        }

        // A tab: Inherit (`variant` null), Default (empty) or a variant, lit while the group is on it.
        private void Tab(string label, string variant, SerializedProperty selected, SerializedProperty inherit, int g, GUIStyle style)
        {
            bool on = variant == null ? inherit.boolValue : !inherit.boolValue && selected.stringValue == variant;
            if (on && !string.IsNullOrEmpty(variant) && VariantPreview.IsRecording(Set, g))
                label = "● " + label;
            if (GUILayout.Toggle(on, variant == null ? s_inherit : new GUIContent(label), style) && !on)
            {
                if (Application.isPlaying)
                {
                    if (variant == null)
                        Set.Clear(Set.Groups[g].Name);
                    else
                        Set.Set(Set.Groups[g].Name, variant);
                }
                else
                {
                    inherit.boolValue = variant == null;
                    selected.stringValue = variant ?? "";
                }
            }
        }

        private void Record(int g, string variant)
        {
            bool recording = VariantPreview.IsRecording(Set, g);
            using (new EditorGUI.DisabledScope(Application.isPlaying || EditorUtility.IsPersistent(Set)))
            {
                var background = GUI.backgroundColor;
                if (recording) GUI.backgroundColor = s_recordColor;
                bool clicked = GUILayout.Button(recording ? "■  Stop Recording" : "●  Record", GUILayout.Height(22));
                GUI.backgroundColor = background;
                if (clicked)
                {
                    serializedObject.ApplyModifiedProperties();
                    if (recording)
                        VariantPreview.StopRecording();
                    else
                        VariantPreview.StartRecording(Set, g);
                    GUIUtility.ExitGUI();
                }
            }

            if (recording)
            {
                string where = PrefabStageUtility.GetCurrentPrefabStage() != null ? "prefab" : "scene";
                EditorGUILayout.HelpBox($"Recording {variant}: edits to anything under {Set.name} go into this variant, not the {where}.", MessageType.None);
            }
        }

        // The values a variant sets, by object in hierarchy order, each editable in place.
        private void Entries(SerializedProperty entries)
        {
            if (entries.arraySize == 0)
            {
                EditorGUILayout.LabelField($"Sets nothing yet. Record, then edit anything under {Set.name}.", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            var order = Enumerable.Range(0, entries.arraySize)
                .Select(i => (Index: i, Key: OrderKey(entries.GetArrayElementAtIndex(i).FindPropertyRelative("_target").objectReferenceValue)))
                .OrderBy(e => e.Key, StringComparer.Ordinal)
                .ThenBy(e => e.Index)
                .Select(e => e.Index)
                .ToList();

            GameObject shown = null;
            bool first = true;
            int removed = -1;
            foreach (int i in order)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                var entryTarget = entry.FindPropertyRelative("_target").objectReferenceValue;
                string property = entry.FindPropertyRelative("_property").stringValue;
                var gameObject = GameObjectOf(entryTarget);
                if (first || gameObject != shown)
                {
                    ObjectHeader(gameObject);
                    shown = gameObject;
                    first = false;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(14);
                    Value(entry.FindPropertyRelative("_value"), entryTarget, property);
                    if (GUILayout.Button(s_remove, EditorStyles.miniButton, GUILayout.Width(20)))
                        removed = i;
                }
            }

            if (removed >= 0)
                entries.DeleteArrayElementAtIndex(removed);
        }

        private static void ObjectHeader(GameObject gameObject)
        {
            var rect = EditorGUILayout.GetControlRect();
            if (gameObject == null)
            {
                EditorGUI.LabelField(rect, "Missing object", EditorStyles.boldLabel);
                return;
            }
            var content = new GUIContent(gameObject.name, EditorGUIUtility.IconContent("GameObject Icon").image, "Click to find it in the hierarchy.");
            if (GUI.Button(rect, content, EditorStyles.boldLabel))
                EditorGUIUtility.PingObject(gameObject);
        }

        private static void Value(SerializedProperty value, Object entryTarget, string property)
        {
            var accessor = entryTarget != null ? VariantAccessor.For(entryTarget, property) : null;
            var label = new GUIContent(entryTarget != null ? VariantCapture.Describe(entryTarget, property) : property, property);
            if (accessor == null)
            {
                EditorGUILayout.LabelField(label, new GUIContent(entryTarget == null ? "Missing" : "Can't be set"));
                return;
            }
            var kind = (VariantValueKind)value.FindPropertyRelative("_kind").intValue;
            if (kind != accessor.Kind)
            {
                EditorGUILayout.LabelField(label, new GUIContent("Its type has changed: record it again"));
                return;
            }
            VariantFields.Draw(label, value, accessor);
        }

        private void Rename(SerializedProperty variants, SerializedProperty selected, SerializedProperty name, string renamed)
        {
            renamed = renamed.Trim();
            if (renamed.Length == 0 || renamed == name.stringValue || renamed == DefaultLabel) return;
            if (IndexOf(variants, renamed) >= 0)
            {
                Debug.LogWarning($"{Set.name} already has a variant named '{renamed}' in that group.", Set);
                return;
            }
            name.stringValue = renamed;
            selected.stringValue = renamed;
        }

        private void AddGroup(SerializedProperty groups)
        {
            var names = Enumerable.Range(0, groups.arraySize).Select(g => groups.GetArrayElementAtIndex(g).FindPropertyRelative("_name").stringValue);
            groups.arraySize++;
            var group = groups.GetArrayElementAtIndex(groups.arraySize - 1);
            group.FindPropertyRelative("_name").stringValue = Unique("Group", names.ToList());
            group.FindPropertyRelative("_selected").stringValue = "";
            group.FindPropertyRelative("_inherit").boolValue = true;
            group.FindPropertyRelative("_animated").boolValue = true;
            group.FindPropertyRelative("_animation").FindPropertyRelative("_set").boolValue = false;
            group.FindPropertyRelative("_variants").ClearArray();
        }

        private static void AddVariant(SerializedProperty variants, SerializedProperty selected, SerializedProperty inherit)
        {
            var names = Names(variants);
            variants.arraySize++;
            var variant = variants.GetArrayElementAtIndex(variants.arraySize - 1);
            string name = Unique("Variant", names);
            variant.FindPropertyRelative("_name").stringValue = name;
            variant.FindPropertyRelative("_entries").ClearArray();
            selected.stringValue = name;
            inherit.boolValue = false;
        }

        private void GroupMenu(int g, int count)
        {
            var menu = new GenericMenu();
            AddItem(menu, "Move Up", g > 0, groups => groups.MoveArrayElement(g, g - 1));
            AddItem(menu, "Move Down", g < count - 1, groups => groups.MoveArrayElement(g, g + 1));
            menu.AddSeparator("");
            AddItem(menu, "Remove Group", true, groups => groups.DeleteArrayElementAtIndex(g));
            menu.ShowAsContext();
        }

        private void VariantMenu(int g, int v, int count)
        {
            var menu = new GenericMenu();
            AddItem(menu, "Duplicate", true, groups =>
            {
                var group = groups.GetArrayElementAtIndex(g);
                var variants = group.FindPropertyRelative("_variants");
                var names = Names(variants);
                variants.InsertArrayElementAtIndex(v);
                var copy = variants.GetArrayElementAtIndex(v + 1).FindPropertyRelative("_name");
                copy.stringValue = Unique(copy.stringValue + " Copy", names);
                group.FindPropertyRelative("_selected").stringValue = copy.stringValue;
                group.FindPropertyRelative("_inherit").boolValue = false;
            });
            AddItem(menu, "Move Left", v > 0, groups => groups.GetArrayElementAtIndex(g).FindPropertyRelative("_variants").MoveArrayElement(v, v - 1));
            AddItem(menu, "Move Right", v < count - 1, groups => groups.GetArrayElementAtIndex(g).FindPropertyRelative("_variants").MoveArrayElement(v, v + 1));
            menu.AddSeparator("");
            AddItem(menu, "Remove Variant", true, groups =>
            {
                var group = groups.GetArrayElementAtIndex(g);
                group.FindPropertyRelative("_variants").DeleteArrayElementAtIndex(v);
                group.FindPropertyRelative("_selected").stringValue = "";
            });
            menu.ShowAsContext();
        }

        // A menu item changing the groups, after the inspector event that opened it has ended.
        private void AddItem(GenericMenu menu, string label, bool enabled, Action<SerializedProperty> change)
        {
            if (!enabled)
            {
                menu.AddDisabledItem(new GUIContent(label));
                return;
            }
            menu.AddItem(new GUIContent(label), false, () =>
            {
                serializedObject.Update();
                change(serializedObject.FindProperty("_groups"));
                serializedObject.ApplyModifiedProperties();
            });
        }

        private static int IndexOf(SerializedProperty variants, string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            for (int v = 0; v < variants.arraySize; v++)
                if (variants.GetArrayElementAtIndex(v).FindPropertyRelative("_name").stringValue == name) return v;
            return -1;
        }

        private static List<string> Names(SerializedProperty variants) =>
            Enumerable.Range(0, variants.arraySize).Select(v => variants.GetArrayElementAtIndex(v).FindPropertyRelative("_name").stringValue).ToList();

        private static string Unique(string name, List<string> taken)
        {
            if (!taken.Contains(name) && name != DefaultLabel) return name;
            for (int n = 2; ; n++)
                if (!taken.Contains($"{name} {n}")) return $"{name} {n}";
        }

        private static string Label(string variant) => string.IsNullOrEmpty(variant) ? DefaultLabel : variant;

        private static GameObject GameObjectOf(Object target) => target switch
        {
            GameObject gameObject => gameObject,
            Component component => component.gameObject,
            _ => null,
        };

        // Sorts entries as the hierarchy reads: by each object's sibling indices below the set, then the object's own
        // entries before its components', in the order the components are listed. Missing ones go last.
        private string OrderKey(Object entryTarget)
        {
            var gameObject = GameObjectOf(entryTarget);
            if (gameObject == null) return "~";

            var path = new List<string>();
            for (var transform = gameObject.transform; transform != null && transform != Set.transform; transform = transform.parent)
                path.Add(transform.GetSiblingIndex().ToString("D4"));
            path.Reverse();

            int component = entryTarget is Component c ? Array.IndexOf(gameObject.GetComponents<Component>(), c) : -1;
            return string.Join("/", path) + "#" + (component + 1).ToString("D3");
        }
    }
}
