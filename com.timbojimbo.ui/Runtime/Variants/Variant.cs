using System;
using System.Collections.Generic;
using TimboJimbo.UI.Motion;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimboJimbo.UI.Variants
{
    /// <summary>One value a variant sets: a property of an object in its variant set's hierarchy, and what it is set to.</summary>
    [Serializable]
    public struct VariantEntry
    {
        [Tooltip("The GameObject or component the value is set on.")]
        [SerializeField] private Object _target;

        [Tooltip("The property set, by its serialized path (m_Color, _cornerRadii, m_IsActive).")]
        [SerializeField] private string _property;

        [SerializeField] private VariantValue _value;

        public VariantEntry(Object target, string property, VariantValue value)
        {
            _target = target;
            _property = property;
            _value = value;
        }

        public Object Target => _target;
        public string Property => _property;
        public VariantValue Value => _value;

        public bool Sets(Object target, string property) => _target == target && _property == property;
    }

    /// <summary>One variant of a group: a name, and the values it sets.</summary>
    [Serializable]
    public sealed class Variant
    {
        [SerializeField] private string _name;
        [SerializeField] private List<VariantEntry> _entries = new();

        public Variant() : this("Variant") { }

        public Variant(string name) => _name = name;

        /// <summary>A variant named <paramref name="name"/> setting <paramref name="entries"/>, for building one in code.</summary>
        public Variant(string name, params VariantEntry[] entries)
        {
            _name = name;
            _entries.AddRange(entries);
        }

        public string Name { get => _name; internal set => _name = value; }
        public IReadOnlyList<VariantEntry> Entries => _entries;
        internal List<VariantEntry> EntryList => _entries;

        /// <summary>The index of the entry setting <paramref name="property"/> of <paramref name="target"/>, or -1.</summary>
        public int IndexOf(Object target, string property)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Sets(target, property)) return i;
            return -1;
        }
    }

    /// <summary>
    /// Variants that exclude one another (a toast's Type: Success, Warning, Error), and which of them is shown: one it
    /// selects itself by name, or none (empty), which is Default, the prefab as it is; or, while it
    /// <see cref="Inherits"/>, the one the nearest set above it with a group of the same name shows, as SwiftUI's
    /// environment and UIKit's traits pass down the hierarchy.
    /// </summary>
    [Serializable]
    public sealed class VariantGroup
    {
        [SerializeField] private string _name;
        [SerializeField] private string _selected = "";

        [Tooltip("Shows what the nearest variant set above it with a group of this name shows (Default with none), rather than a selection of its own.")]
        [SerializeField] private bool _inherit;

        [Tooltip("Switching it animates, wherever it is switched from (code, a button, a breakpoint), as SwiftUI's .animation(value:): the layout it changes springs, and its values move on the springs of the layout nodes they are drawn in. A switch made inside MotionSystem.Animate animates either way.")]
        [SerializeField] private bool _animated = true;

        [Tooltip("The animation an animated switch is made on, as SwiftUI's .animation(_:value:): what it moves goes on this, unless a layout node it moves (or one above it) has an Animation of its own. Inherit: the default.")]
        [SerializeField] private OptionalMotionAnimation _animation = new(null);

        [SerializeField] private List<Variant> _variants = new();

        // What Unity makes a saved group with: one saved before groups could inherit keeps the selection it had.
        public VariantGroup() => _name = "Group";

        /// <summary>A group named <paramref name="name"/>, on Inherit, for building one in code.</summary>
        public VariantGroup(string name)
        {
            _name = name;
            _inherit = true;
        }

        /// <summary>A group named <paramref name="name"/> of <paramref name="variants"/>, on Inherit, for building one in code.</summary>
        public VariantGroup(string name, params Variant[] variants) : this(name) => _variants.AddRange(variants);

        public string Name { get => _name; internal set => _name = value; }

        /// <summary>
        /// The variant it selects itself: its name, or empty for Default. Read only while it does not
        /// <see cref="Inherits"/>; <see cref="VariantSet.Get"/> says what it shows either way.
        /// </summary>
        public string Selected { get => _selected ?? ""; internal set => _selected = value ?? ""; }

        /// <summary>
        /// Whether it shows what the nearest set above it with a group of the same name shows, rather than a selection of
        /// its own: that set's variant of the same name, or Default where it has none or nothing above has the group.
        /// New groups do. <see cref="VariantSet.Set(string, string)"/> gives it a selection of its own, and
        /// <see cref="VariantSet.Clear"/> puts it back.
        /// </summary>
        public bool Inherits { get => _inherit; internal set => _inherit = value; }

        /// <summary>
        /// Whether switching it animates, wherever it is switched from, as SwiftUI's <c>.animation(value:)</c>: the
        /// switch is made inside <see cref="MotionSystem.Animate(MotionAnimation, Action, string[])"/>, on
        /// <see cref="Animation"/>. One made inside a change joins it, either way, on that change's animation.
        /// </summary>
        public bool Animated { get => _animated; set => _animated = value; }

        /// <summary>
        /// The animation an <see cref="Animated"/> switch is made on, as SwiftUI's <c>.animation(_:value:)</c>: a
        /// hover's quick spring, say. What the switch moves goes on it, unless a layout node it moves (or one above
        /// that) has an Animation of its own. Null (the default) switches on <see cref="MotionAnimation.Default"/>.
        /// </summary>
        public MotionAnimation? Animation { get => _animation.Value; set => _animation = new OptionalMotionAnimation(value); }

        public IReadOnlyList<Variant> Variants => _variants;
        internal List<Variant> VariantList => _variants;

        /// <summary>The variant named <paramref name="name"/>; null for none, or for Default (empty).</summary>
        public Variant Find(string name)
        {
            int index = IndexOf(name);
            return index < 0 ? null : _variants[index];
        }

        /// <summary>The index of the variant named <paramref name="name"/>; -1 for none, or for Default (empty).</summary>
        public int IndexOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < _variants.Count; i++)
                if (_variants[i].Name == name) return i;
            return -1;
        }
    }
}
