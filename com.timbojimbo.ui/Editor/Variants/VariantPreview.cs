using System;
using System.Collections.Generic;
using TimboJimbo.UI.Variants;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TimboJimboEditor.UI.Variants
{
    /// <summary>
    /// Shows every variant set in the stage being edited (the open scenes, or the prefab open in prefab mode) as its
    /// selection says, without saving it. It goes through AnimationMode, as the Animation window previews a clip: a value
    /// a variant sets is drawn as set, while the scene or prefab keeps (and saves) its default, and it goes back the
    /// moment nothing sets it. While a variant is recorded, an edit to anything under its set goes into the variant rather
    /// than the object; an edit to a value a variant is showing is turned away, as it would never be saved.
    /// </summary>
    [InitializeOnLoad]
    internal static class VariantPreview
    {
        private static AnimationModeDriver s_driver;
        private static bool s_dirty;

        // The set and group being recorded into (its selected variant), if any.
        private static VariantSet s_recording;
        private static int s_recordingGroup = -1;

        // From the last sample: the variant each value shown comes from, and each group shown as something other than its
        // own selection (a breakpoint's, or one it inherits), with what selects it.
        private static readonly Dictionary<(Object, string), string> s_sources = new();
        private static readonly Dictionary<(VariantSet, int), (string Variant, string By)> s_drivenSelections = new();

        // Each breakpoints component in the stage, with the variant it gave at the last sample, to see when it gives another.
        private static readonly List<(VariantBreakpoints Breakpoints, string Variant)> s_breakpoints = new();

        private readonly struct Write
        {
            public readonly Object Target;
            public readonly string Property;
            public readonly VariantAccessor Accessor;
            public readonly VariantValue Value;

            public Write(Object target, string property, VariantAccessor accessor, VariantValue value)
            {
                Target = target;
                Property = property;
                Accessor = accessor;
                Value = value;
            }
        }

        /// <summary>Raised after each sample, for inspectors to draw again.</summary>
        public static event Action Sampled;

        /// <summary>Whether the preview is waiting on another user of AnimationMode (the Animation window, Timeline).</summary>
        public static bool Paused { get; private set; }

        static VariantPreview()
        {
            VariantSet.s_editorChanged += _ => MarkDirty();
            Undo.undoRedoPerformed += MarkDirty;
            Undo.postprocessModifications += Postprocess;
            EditorApplication.hierarchyChanged += MarkDirty;
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += StopAll;
            PrefabStage.prefabStageOpened += _ => MarkDirty();
            PrefabStage.prefabStageClosing += _ =>
            {
                StopAll();
                MarkDirty();
            };
            EditorSceneManager.sceneOpened += (_, _) => MarkDirty();
            EditorSceneManager.sceneClosed += _ => MarkDirty();
            MarkDirty();
        }

        private static AnimationModeDriver Driver
        {
            get
            {
                if (s_driver == null)
                {
                    s_driver = ScriptableObject.CreateInstance<AnimationModeDriver>();
                    s_driver.name = "Variant Preview";
                    s_driver.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_driver;
            }
        }

        private static bool Previewing => s_driver != null && AnimationMode.InAnimationMode(s_driver);

        /// <summary>Samples again on the next editor update.</summary>
        public static void MarkDirty() => s_dirty = true;

        public static bool IsRecording(VariantSet set, int group) => set != null && s_recording == set && s_recordingGroup == group;

        /// <summary>Records edits to anything under <paramref name="set"/> into the variant selected in <paramref name="group"/>.</summary>
        public static void StartRecording(VariantSet set, int group)
        {
            if (AnimationMode.InAnimationMode() && !Previewing)
            {
                Debug.LogWarning("Variants can't record while the Animation window or Timeline is previewing.");
                return;
            }
            s_recording = set;
            s_recordingGroup = group;
            Sample();
        }

        public static void StopRecording()
        {
            s_recording = null;
            s_recordingGroup = -1;
            Sample();
        }

        /// <summary>
        /// Whether <paramref name="group"/> is shown as something other than its own selection, a breakpoint's or one it
        /// inherits: which variant, and what selects it.
        /// </summary>
        public static bool TryGetDriven(VariantSet set, int group, out string variant, out string by)
        {
            bool found = s_drivenSelections.TryGetValue((set, group), out var driven);
            (variant, by) = found ? driven : (null, null);
            return found;
        }

        private static void Update()
        {
            // Whoever held AnimationMode has let go of it.
            if (Paused && !AnimationMode.InAnimationMode())
                s_dirty = true;

            // A canvas or the Game view resizing says nothing: a breakpoint that now gives another variant asks for one.
            if (!s_dirty && !EditorApplication.isPlayingOrWillChangePlaymode && BreakpointsMoved())
                s_dirty = true;

            if (s_dirty)
                Sample();
        }

        private static bool BreakpointsMoved()
        {
            foreach (var (breakpoints, variant) in s_breakpoints)
            {
                if (breakpoints == null || breakpoints.VariantFor(breakpoints.DrawnSize) != variant)
                    return true;
            }
            return false;
        }

        private static void PlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
                StopAll();
            else if (change == PlayModeStateChange.EnteredEditMode)
                MarkDirty();
        }

        private static void StopAll()
        {
            s_recording = null;
            s_recordingGroup = -1;
            Stop();
        }

        private static void Stop()
        {
            if (Previewing)
                AnimationMode.StopAnimationMode(s_driver);
        }

        // Shows every set as its selection says: holds each value a selected variant sets (AnimationMode noting its default
        // the first time) and writes it, and lets go of the rest, which AnimationMode puts back.
        private static void Sample()
        {
            s_dirty = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                StopAll();
                Finish();
                return;
            }

            // A recording ends when its set goes, or its group goes back to Default or to Inherit.
            if (s_recording == null || s_recordingGroup >= s_recording.Groups.Count || s_recording.Groups[s_recordingGroup].Inherits
                || s_recording.Groups[s_recordingGroup].Selected.Length == 0)
            {
                s_recording = null;
                s_recordingGroup = -1;
            }

            var writes = Resolve();
            if (writes.Count == 0 && s_recording == null)
            {
                Stop();
                Paused = false;
                Finish();
                return;
            }

            if (!Previewing)
            {
                if (AnimationMode.InAnimationMode())
                {
                    Paused = true;
                    s_recording = null;
                    s_recordingGroup = -1;
                    Finish();
                    return;
                }
                AnimationMode.StartAnimationMode(Driver);
            }
            Paused = false;

            AnimationMode.BeginSampling();
            foreach (var write in writes)
                Hold(write.Target, write.Property);
            foreach (var write in writes)
            {
                if (!write.Accessor.Read(write.Target).Equals(write.Value))
                    write.Accessor.Write(write.Target, write.Value);
            }
            AnimationMode.EndSampling();
            Finish();
        }

        private static void Finish()
        {
            Sampled?.Invoke();
            InternalEditorUtility.RepaintAllViews();
        }

        // Tells AnimationMode the property is being set, so it is drawn as set but saved (and put back) as it is now. It
        // keeps the first value it is given, so a property already held keeps the default it was first held with.
        private static void Hold(Object target, string property)
        {
            using var serialized = new SerializedObject(target);
            var found = serialized.FindProperty(property);
            if (found == null) return;
            VariantCapture.ForEachLeaf(found, leaf =>
                AnimationMode.AddPropertyModification(VariantCapture.Binding(target, leaf), VariantCapture.Snapshot(target, leaf), true));
        }

        // What every set's selection gives the objects under it. Breakpoints come first, each selecting its group's
        // variant for the size its rect is drawn at now, as it would in play mode. Then sets further out come first, so a
        // group that inherits (and that no breakpoint selects) shows what the nearest set above with a group of its name
        // was found to show, worked out already; and where both set a value, the one further out wins, as an outer
        // prefab's overrides win over a nested one's. Within a set, a later group wins over an earlier one.
        private static List<Write> Resolve()
        {
            s_sources.Clear();
            s_drivenSelections.Clear();

            var sets = FindInStage<VariantSet>();
            sets.Sort((a, b) => Depth(a.transform).CompareTo(Depth(b.transform)));

            // Null for a group that inherits, until it is selected or its turn comes.
            var selections = new Dictionary<VariantSet, string[]>();
            foreach (var set in sets)
            {
                var selected = new string[set.Groups.Count];
                for (int g = 0; g < selected.Length; g++)
                    selected[g] = set.Groups[g].Inherits ? null : set.Groups[g].Selected;
                selections[set] = selected;
            }

            s_breakpoints.Clear();
            foreach (var breakpoints in FindInStage<VariantBreakpoints>())
            {
                var size = breakpoints.DrawnSize;
                string variant = breakpoints.VariantFor(size);
                s_breakpoints.Add((breakpoints, variant));

                var target = breakpoints.Target;
                if (target == null || !selections.TryGetValue(target, out var selected)) continue;
                int group = target.IndexOfGroup(breakpoints.Group);
                if (group < 0 || (variant.Length > 0 && target.Groups[group].IndexOf(variant) < 0)) continue;
                selected[group] = variant;
                s_drivenSelections[(target, group)] = (variant, Describe(breakpoints, size));
            }

            var writes = new List<Write>();
            var taken = new HashSet<(Object, string)>();
            var local = new Dictionary<(Object, string), (VariantValue Value, string Source)>();
            foreach (var set in sets)
            {
                local.Clear();
                var selected = selections[set];
                Inherit(set, selected, selections);
                for (int g = 0; g < set.Groups.Count; g++)
                {
                    var group = set.Groups[g];
                    var variant = group.Find(selected[g]);
                    if (variant == null) continue;

                    string source = $"{set.name} · {group.Name}: {variant.Name}";
                    foreach (var entry in variant.Entries)
                    {
                        if (entry.Target != null && Within(entry.Target, set))
                            local[(entry.Target, entry.Property)] = (entry.Value, source);
                    }
                }

                foreach (var pair in local)
                {
                    if (!taken.Add(pair.Key)) continue;
                    var accessor = VariantAccessor.For(pair.Key.Item1, pair.Key.Item2);
                    if (accessor == null || accessor.Kind != pair.Value.Value.Kind) continue;
                    writes.Add(new Write(pair.Key.Item1, pair.Key.Item2, accessor, pair.Value.Value));
                    s_sources[pair.Key] = pair.Value.Source;
                }
            }
            return writes;
        }

        // Each group of `set` that inherits and that no breakpoint has selected shows what the nearest set above with a
        // group of its name shows, which sets further out were worked out first to find (Default with none). A name it has
        // no variant of shows Default, and still passes down.
        private static void Inherit(VariantSet set, string[] selected, Dictionary<VariantSet, string[]> selections)
        {
            for (int g = 0; g < selected.Length; g++)
            {
                if (selected[g] != null) continue;
                var name = set.Groups[g].Name;
                var above = set.Above(name, out int index);
                selected[g] = above != null && selections.TryGetValue(above, out var theirs) ? theirs[index] ?? "" : "";
                if (above != null)
                    s_drivenSelections[(set, g)] = (selected[g], $"{above.name}'s {name}");
            }
        }

        // What selects a breakpoint's variant, for the inspector's "Shown as ..., which ... selects".
        private static string Describe(VariantBreakpoints breakpoints, Vector2 size)
        {
            float value = breakpoints.MeasureOf(size);
            string at = breakpoints.Measure switch
            {
                BreakpointMeasure.Width => $"{value:0} wide",
                BreakpointMeasure.Height => $"{value:0} tall",
                _ => $"an aspect of {value:0.##}",
            };
            return $"{breakpoints.name}'s breakpoints at {at}";
        }

        // Every component of a kind in the stage being edited, active or not.
        private static List<T> FindInStage<T>() where T : Component
        {
            var found = new List<T>();
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                prefabStage.prefabContentsRoot.GetComponentsInChildren(true, found);
                return found;
            }

            var buffer = new List<T>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    root.GetComponentsInChildren(true, buffer);
                    found.AddRange(buffer);
                }
            }
            return found;
        }

        private static int Depth(Transform transform)
        {
            int depth = 0;
            for (; transform.parent != null; transform = transform.parent) depth++;
            return depth;
        }

        private static GameObject GameObjectOf(Object target) => target switch
        {
            GameObject gameObject => gameObject,
            Component component => component.gameObject,
            _ => null,
        };

        private static bool Within(Object target, VariantSet set)
        {
            var gameObject = GameObjectOf(target);
            return gameObject != null && gameObject.transform.IsChildOf(set.transform);
        }

        // Every edit made with undo (the inspector, the scene view's handles) passes through here while the preview is
        // on: one being recorded goes into the variant instead, one to a value a variant shows is turned away, and the rest
        // go through as they are.
        private static UndoPropertyModification[] Postprocess(UndoPropertyModification[] modifications)
        {
            if (!Previewing) return modifications;

            List<UndoPropertyModification> kept = null;
            for (int i = 0; i < modifications.Length; i++)
            {
                var modification = modifications[i];
                var target = modification.currentValue?.target ?? modification.previousValue?.target;
                var path = modification.currentValue?.propertyPath ?? modification.previousValue?.propertyPath;
                bool handled = target != null && path != null && (Record(modification, target, path) || TurnAway(target, path));
                if (handled)
                {
                    kept ??= new List<UndoPropertyModification>(modifications[..i]);
                    continue;
                }
                kept?.Add(modification);
            }
            return kept?.ToArray() ?? modifications;
        }

        // An edit to something under the set being recorded goes into its selected variant. The object keeps the edited
        // value, held by AnimationMode with the value it had before, which is what is saved and what comes back once
        // nothing sets it.
        private static bool Record(UndoPropertyModification modification, Object target, string leafPath)
        {
            var set = s_recording;
            if (set == null || target == set || modification.previousValue == null) return false;
            var gameObject = GameObjectOf(target);
            if (gameObject == null || !gameObject.transform.IsChildOf(set.transform)) return false;

            var kind = VariantCapture.Classify(target, leafPath, out var property);
            if (kind == CaptureKind.Pass) return false;

            string variant = set.Groups[s_recordingGroup].Selected;
            string undoName = $"Record {variant}";

            using (var serialized = new SerializedObject(target))
            {
                var leaf = serialized.FindProperty(leafPath);
                if (leaf == null) return false;
                AnimationMode.AddPropertyModification(VariantCapture.Binding(target, leaf), modification.previousValue, true);
            }

            if (kind == CaptureKind.Record)
                VariantEditing.SetEntry(set, s_recordingGroup, variant, target, property, VariantAccessor.For(target, property).Read(target), undoName);
            MarkDirty();
            return true;
        }

        // An edit to a value a variant is showing would never be saved (AnimationMode saves the default): it is put back,
        // and the window it was made in says which variant sets it.
        private static bool TurnAway(Object target, string leafPath)
        {
            if (!AnimationMode.IsPropertyAnimated(target, leafPath)) return false;

            MarkDirty();
            string property;
            using (var serialized = new SerializedObject(target))
                property = VariantCapture.EntryPath(serialized, leafPath) ?? leafPath;
            string message = s_sources.TryGetValue((target, property), out var source)
                ? $"{source} sets this.\nRecord that variant to change it, or select Default."
                : "A variant being previewed sets this.";
            (EditorWindow.focusedWindow ?? EditorWindow.mouseOverWindow)?.ShowNotification(new GUIContent(message), 2.5);
            return true;
        }
    }
}
