using System;
using System.Collections.Generic;
using TimboJimbo.UI.Layout;
using TimboJimbo.UI.Motion;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

namespace TimboJimbo.UI.Variants
{
    /// <summary>
    /// The variants of a prefab, kept inside it. As prefab variants, each is a set of values (colours, sprites, text,
    /// whether an object is active, any serialized field) on objects in its hierarchy, but one instance switches between
    /// them in place rather than being swapped for another prefab. Variants come in groups that are set independently (a
    /// toast's Type: Success, Warning or Error, and its Size: Compact), each on Default (the prefab as it is) or one of its
    /// variants; where two groups set the same value, the later group wins. A group on Inherit (as new groups are) shows
    /// what the nearest set above it with a group of the same name shows, as SwiftUI's environment passes down: a badge
    /// authored on its own shows its toast's Type.
    /// In the editor the selection is previewed and never saved: scenes and prefabs keep their default values, and the
    /// inspector records a variant by editing the objects while it is selected. In play mode a variant set applies its
    /// selection when it wakes and whenever <see cref="Set(string, string)"/> changes it. It notes each value's default the
    /// first time it applies, and puts that back when nothing selected sets it. A group switches inside
    /// <see cref="MotionSystem.Animate(MotionAnimation, Action, string[])"/> on its <see cref="VariantGroup.Animation"/>
    /// wherever it is switched from, as SwiftUI's .animation(_:value:), so what the switch changes moves (see
    /// <see cref="VariantMotion"/>); any switch made inside a change joins it. <see cref="VariantStates"/> and
    /// <see cref="VariantBreakpoints"/> switch a group from the pointer and from the size of what they are on.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Timbo Jimbo/Variants/Variant Set")]
    public sealed class VariantSet : MonoBehaviour
    {
        [SerializeField] private List<VariantGroup> _groups = new();

        // What it has set at runtime, built the first time it applies: a slot for each object and property any variant
        // sets, with its default (read then) and what was last written; and, per group and variant, the slot each entry
        // sets (-1 for one that sets nothing it can reach).
        [NonSerialized] private Slot[] _slots;
        [NonSerialized] private int[][][] _entrySlots;
        [NonSerialized] private VariantValue[] _resolved;

        // What each group showed when it last applied (null until it has, when each showed Default as far as its objects
        // know), to tell when what it inherits has changed.
        [NonSerialized] private string[] _applied;

        private struct Slot
        {
            public Object Target;
            public string Property;
            public VariantAccessor Accessor;
            public VariantValue Default;
            public VariantValue Last;

            // Whether it moves on a spring with a change, and the layout node whose spring it moves on.
            public bool Springs;
            public LayoutNode Node;
        }

        /// <summary>Raised once its selection has changed; in play mode, once the values it gives are applied.</summary>
        public event Action<VariantSet> Changed;

        public IReadOnlyList<VariantGroup> Groups => _groups;
        internal List<VariantGroup> GroupList => _groups;

        /// <summary>
        /// The variant <paramref name="group"/> shows: its name, or empty for Default (and for no such group). It is the
        /// group's own selection, or while it <see cref="VariantGroup.Inherits"/>, what the nearest set above it with a
        /// group of that name shows; a name it inherits that it has no variant of shows Default.
        /// </summary>
        public string Get(string group)
        {
            int index = IndexOfGroup(group);
            if (index < 0) return "";
            string shown = Shown(index);
            return _groups[index].IndexOf(shown) >= 0 ? shown : "";
        }

        /// <summary>
        /// Selects <paramref name="variant"/> in <paramref name="group"/> (null or empty for Default) as its own: it stops
        /// inheriting, and what it shows passes down to the sets under it that inherit it.
        /// </summary>
        public void Set(string group, string variant) => Select(group, variant, Application.isPlaying);

        /// <summary>
        /// Selects <paramref name="variant"/> in whichever group has a variant of that name (the first that does), as
        /// toast.Set("Error"); a button's click can call it with the name. Warns when no group has one.
        /// </summary>
        public void Set(string variant)
        {
            for (int g = 0; g < _groups.Count; g++)
            {
                if (_groups[g].IndexOf(variant) < 0) continue;
                Select(g, variant, Application.isPlaying);
                return;
            }
            Debug.LogWarning($"{name} has no variant named '{variant}'.", this);
        }

        /// <summary>
        /// Puts <paramref name="group"/> back on Inherit: it shows what the nearest set above it with a group of that name
        /// shows, and Default where nothing above has the group.
        /// </summary>
        public void Clear(string group) => Select(group, null, Application.isPlaying, inherit: true);

        /// <summary>
        /// Selects <paramref name="variant"/> in whichever group has it, or puts that group back on Inherit when it shows
        /// it already: a switch turning on and off, a card opening and closing. Warns when no group has one.
        /// </summary>
        public void Toggle(string variant)
        {
            for (int g = 0; g < _groups.Count; g++)
            {
                if (_groups[g].IndexOf(variant) < 0) continue;
                bool shown = Shown(g) == variant;
                Select(g, shown ? null : variant, Application.isPlaying, inherit: shown);
                return;
            }
            Debug.LogWarning($"{name} has no variant named '{variant}'.", this);
        }

        /// <summary>
        /// Puts every group back on Inherit, on the animation of the first group whose look it changes.
        /// </summary>
        public void ClearAll()
        {
            bool changed = false;
            VariantGroup moved = null;
            for (int g = 0; g < _groups.Count; g++)
            {
                var group = _groups[g];
                if (group.Inherits) continue;
                changed = true;
                string shown = Shown(g);
                group.Inherits = true;
                group.Selected = "";
                if (moved == null && Shown(g) != shown)
                    moved = group;
            }
            if (!changed) return;
            if (moved != null && Application.isPlaying)
                Switch(moved);
            Notify();
        }

        /// <summary>Adds <paramref name="group"/> after its groups, for building a set in code (an editor script making a prefab).</summary>
        public void AddGroup(VariantGroup group)
        {
            _groups.Add(group);
            _slots = null;
        }

        public int IndexOfGroup(string group)
        {
            for (int g = 0; g < _groups.Count; g++)
                if (_groups[g].Name == group) return g;
            return -1;
        }

        internal void Select(string group, string variant, bool apply, bool inherit = false)
        {
            int index = IndexOfGroup(group);
            if (index < 0)
            {
                Debug.LogWarning($"{name} has no variant group named '{group}'.", this);
                return;
            }
            Select(index, variant, apply, inherit);
        }

        // Gives a group a selection of its own (`variant`, empty for Default), or with `inherit` puts it back on Inherit;
        // with `apply`, shows it, and what it now shows passes down.
        internal void Select(int group, string variant, bool apply, bool inherit = false)
        {
            variant = inherit ? "" : variant ?? "";
            var target = _groups[group];
            if (variant.Length > 0 && target.IndexOf(variant) < 0)
            {
                Debug.LogWarning($"{name}'s {target.Name} has no variant named '{variant}'.", this);
                return;
            }
            if (target.Inherits == inherit && target.Selected == variant) return;

            string shown = Shown(group);
            target.Inherits = inherit;
            target.Selected = variant;
            if (apply && Shown(group) != shown)
                Switch(target);
            Notify();
        }

        /// <summary>
        /// What <paramref name="group"/> shows: its own selection, or while it inherits, what the nearest set above it with
        /// a group of that name shows (that one's own, or what it inherits in turn); empty for Default, and with nothing
        /// above. A name it has no variant of shows Default here, and still passes down.
        /// </summary>
        internal string Shown(int group)
        {
            var target = _groups[group];
            if (!target.Inherits) return target.Selected;
            var above = Above(target.Name, out int index);
            return above != null ? above.Shown(index) : "";
        }

        /// <summary>The nearest set above this one with a group named <paramref name="group"/>, and that group's index.</summary>
        internal VariantSet Above(string group, out int index)
        {
            for (var parent = transform.parent; parent != null; parent = parent.parent)
            {
                if (!parent.TryGetComponent(out VariantSet set)) continue;
                index = set.IndexOfGroup(group);
                if (index >= 0) return set;
            }
            index = -1;
            return null;
        }

        // Applies a change of `group`'s selection, and passes it down to the sets under it that inherit it, inside
        // MotionSystem.Animate on the group's animation, so what it changes moves, theirs too (at once on None); a switch
        // made inside a change joins that one.
        private void Switch(VariantGroup group) =>
            MotionSystem.Animate(group.Animation ?? MotionAnimation.Default, ApplyAndPassDown);

        // The sets under it take up what they inherit first (each reads it from the selections above it, not from what
        // was applied), so where one of them sets a value this one sets too, this one's lands last and wins, as an outer
        // prefab's overrides do and as the editor shows it.
        private void ApplyAndPassDown()
        {
            using (ListPool<VariantSet>.Get(out var sets))
            {
                GetComponentsInChildren(true, sets);
                foreach (var below in sets)
                {
                    if (below != this)
                        below.Refresh();
                }
            }
            Apply();
        }

        // Shows what it inherits now (in play mode): it applies again when any group shows other than it did when it last
        // applied, and says so.
        private void Refresh()
        {
            for (int g = 0; g < _groups.Count; g++)
            {
                string applied = _applied != null && g < _applied.Length ? _applied[g] : "";
                if (Shown(g) == applied) continue;
                Apply();
                Notify();
                return;
            }
        }

        // Moved under another parent in play mode, it shows what it inherits there (as do the sets under it, told too).
        private void OnTransformParentChanged()
        {
            if (Application.isPlaying)
                Refresh();
        }

        // Tells the editor's preview, which shows every change outside play mode, then anyone listening.
        private void Notify()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                s_editorChanged?.Invoke(this);
#endif
            Changed?.Invoke(this);
        }

        // Shows what it selects and inherits as it wakes (a badge made under a toast shows the toast's Type).
        private void Awake()
        {
            for (int g = 0; g < _groups.Count; g++)
            {
                if (Shown(g).Length == 0) continue;
                Apply();
                return;
            }
        }

        /// <summary>
        /// Writes every value any variant sets as the groups show: the shown variant's value from the last group whose
        /// variant sets it, else its default. Only what differs from what it last wrote is written.
        /// </summary>
        internal void Apply()
        {
            Build();
            for (int s = 0; s < _slots.Length; s++)
                _resolved[s] = _slots[s].Default;

            for (int g = 0; g < _groups.Count; g++)
            {
                string shown = _applied[g] = Shown(g);
                int v = _groups[g].IndexOf(shown);
                if (v < 0) continue;
                var entries = _groups[g].VariantList[v].EntryList;
                var slots = _entrySlots[g][v];
                for (int e = 0; e < slots.Length; e++)
                    if (slots[e] >= 0) _resolved[slots[e]] = entries[e].Value;
            }

            for (int s = 0; s < _slots.Length; s++)
            {
                ref var slot = ref _slots[s];
                if (slot.Target == null || slot.Last.Equals(_resolved[s])) continue;
                Put(slot, _resolved[s]);
                slot.Last = _resolved[s];
            }
        }

        private static void Put(in Slot slot, VariantValue value)
        {
            if (slot.Springs)
                VariantMotion.Move(slot.Target, slot.Property, slot.Accessor, slot.Node, value);
            else
                slot.Accessor.Write(slot.Target, value);
        }

        // Finds a slot for every object and property its variants set, reading each one's default as it is now.
        private void Build()
        {
            if (_slots != null) return;

            var slots = new List<Slot>();
            var found = new Dictionary<(Object, string), int>();
            _entrySlots = new int[_groups.Count][][];
            for (int g = 0; g < _groups.Count; g++)
            {
                var variants = _groups[g].VariantList;
                _entrySlots[g] = new int[variants.Count][];
                for (int v = 0; v < variants.Count; v++)
                {
                    var entries = variants[v].EntryList;
                    var map = _entrySlots[g][v] = new int[entries.Count];
                    for (int e = 0; e < entries.Count; e++)
                    {
                        map[e] = -1;
                        var entry = entries[e];
                        if (entry.Target == null) continue;

                        var key = (entry.Target, entry.Property);
                        if (!found.TryGetValue(key, out int s))
                        {
                            var accessor = VariantAccessor.For(entry.Target, entry.Property);
                            if (accessor == null || accessor.Kind != entry.Value.Kind) continue;
                            var current = accessor.Read(entry.Target);
                            s = slots.Count;
                            slots.Add(new Slot
                            {
                                Target = entry.Target,
                                Property = entry.Property,
                                Accessor = accessor,
                                Default = current,
                                Last = current,
                                Springs = VariantMotion.Springs(entry.Target, accessor.Kind),
                                Node = VariantMotion.NodeOf(entry.Target),
                            });
                            found.Add(key, s);
                        }
                        map[e] = s;
                    }
                }
            }
            _slots = slots.ToArray();
            _resolved = new VariantValue[_slots.Length];
            _applied = new string[_groups.Count];
        }

        // Its variants changed while it is applied (edited in the inspector in play mode): put back every default it has
        // changed, forget them, and apply again from what the variants now say.
        private void Rebuild()
        {
            if (_slots == null) return;
            foreach (var slot in _slots)
            {
                if (slot.Target != null && !slot.Last.Equals(slot.Default))
                    Put(slot, slot.Default);
            }
            _slots = null;
            Apply();
        }

#if UNITY_EDITOR
        // The editor's preview listens here, to show a change to its variants or selection outside play mode.
        internal static Action<VariantSet> s_editorChanged;

        private void OnValidate()
        {
            if (Application.isPlaying)
                Rebuild();
            else
                s_editorChanged?.Invoke(this);
        }
#endif
    }
}
