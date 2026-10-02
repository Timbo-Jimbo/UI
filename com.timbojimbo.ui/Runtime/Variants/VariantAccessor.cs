using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TimboJimbo.UI.Variants
{
    /// <summary>
    /// How a variant reads and writes one property of one type of object, named as Unity serializes it: m_Color,
    /// _cornerRadii, m_IsActive, or a field inside one as m_Colors.m_NormalColor. It goes through the object's public
    /// property where one matches the serialized field (m_Color to color, _cornerRadii to CornerRadii), so whatever that
    /// property does on a change (dirtying a graphic, laying out again) still happens. A field with no property is written
    /// as it is, and a graphic redrawn. Found once per type and property, then kept.
    /// </summary>
    public abstract class VariantAccessor
    {
        private static readonly Dictionary<(Type, string), VariantAccessor> s_found = new();

        protected VariantAccessor(VariantValueKind kind) => Kind = kind;

        /// <summary>The kind of value the property holds.</summary>
        public VariantValueKind Kind { get; }

        /// <summary>The type of the property's value, as a field of it is declared (an enum, a Sprite).</summary>
        public abstract Type ValueType { get; }

        public abstract VariantValue Read(Object target);
        public abstract void Write(Object target, VariantValue value);

        /// <summary>The accessor for <paramref name="property"/> of <paramref name="target"/>; null for one a variant cannot set.</summary>
        public static VariantAccessor For(Object target, string property)
        {
            if (target == null || string.IsNullOrEmpty(property)) return null;
            return For(target.GetType(), property);
        }

        /// <summary>The accessor for <paramref name="property"/> of objects of <paramref name="type"/>; null for one a variant cannot set.</summary>
        public static VariantAccessor For(Type type, string property)
        {
            var key = (type, property);
            if (!s_found.TryGetValue(key, out var accessor))
            {
                accessor = Create(type, property);
                s_found[key] = accessor;
            }
            return accessor;
        }

        private static VariantAccessor Create(Type type, string property)
        {
            if (type == typeof(GameObject))
                return property == "m_IsActive" ? ActiveAccessor.Instance : null;

            // A set inside another shows what it selects or inherits: a variant does not set it.
            if (typeof(VariantSet).IsAssignableFrom(type)) return null;

            if (property.Contains(".Array.")) return null;

            // TextMesh Pro keeps its colour twice (m_fontColor, and m_fontColor32 made from it) and sets both through color.
            if (Inherits(type, "TMPro.TMP_Text"))
            {
                if (property == "m_fontColor") return MemberAccessor.Create(type, property, "color");
                if (property == "m_fontColor32") return null;
            }

            return MemberAccessor.Create(type, property, null);
        }

        private static bool Inherits(Type type, string fullName)
        {
            for (; type != null; type = type.BaseType)
                if (type.FullName == fullName) return true;
            return false;
        }
    }

    /// <summary>Whether a GameObject is active.</summary>
    internal sealed class ActiveAccessor : VariantAccessor
    {
        public static readonly ActiveAccessor Instance = new();

        private ActiveAccessor() : base(VariantValueKind.Bool) { }

        public override Type ValueType => typeof(bool);
        public override VariantValue Read(Object target) => VariantValue.FromBool(((GameObject)target).activeSelf);
        public override void Write(Object target, VariantValue value) => ((GameObject)target).SetActive(value.BoolValue);
    }

    /// <summary>A serialized field, through the property that matches it where there is one, and any fields inside it.</summary>
    internal sealed class MemberAccessor : VariantAccessor
    {
        // The outermost member: its property, or with none its field.
        private readonly PropertyInfo _property;
        private readonly FieldInfo _field;

        // The fields inside that member's value, down to the one set; empty when it is the member itself.
        private readonly FieldInfo[] _path;

        private readonly Type _valueType;

        private MemberAccessor(VariantValueKind kind, PropertyInfo property, FieldInfo field, FieldInfo[] path, Type valueType)
            : base(kind)
        {
            _property = property;
            _field = field;
            _path = path;
            _valueType = valueType;
        }

        public override Type ValueType => _valueType;

        /// <summary>
        /// The accessor for <paramref name="serializedPath"/> on <paramref name="type"/>, through the property named
        /// <paramref name="propertyName"/> or, when that is null, the one named as the serialized field is without its
        /// m_ or _ (any case). Unity's own components keep their data natively, with no field to find; their properties
        /// are matched by name alone.
        /// </summary>
        public static MemberAccessor Create(Type type, string serializedPath, string propertyName)
        {
            var names = serializedPath.Split('.');
            var field = FindField(type, names[0]);
            var property = FindProperty(type, propertyName ?? Unprefixed(names[0]), field?.FieldType);
            if (property == null && field == null) return null;

            var valueType = property != null ? property.PropertyType : field.FieldType;
            var path = new FieldInfo[names.Length - 1];
            for (int i = 1; i < names.Length; i++)
            {
                var inner = FindField(valueType, names[i]);
                if (inner == null) return null;
                path[i - 1] = inner;
                valueType = inner.FieldType;
            }

            if (!VariantValue.TryGetKind(valueType, out var kind)) return null;
            return new MemberAccessor(kind, property, property == null ? field : null, path, valueType);
        }

        public override VariantValue Read(Object target)
        {
            object value = GetRoot(target);
            foreach (var inner in _path)
            {
                if (value == null) return VariantValue.FromBoxed(null, Kind);
                value = inner.GetValue(value);
            }
            return VariantValue.FromBoxed(value, Kind);
        }

        public override void Write(Object target, VariantValue value)
        {
            object boxed = value.ToBoxed(_valueType);
            if (_path.Length == 0)
            {
                SetRoot(target, boxed);
            }
            else
            {
                // Each struct on the way down is a boxed copy: set the field on the innermost, put each back into the one
                // above it, and the outermost back through the root (a class on the way is changed where it is).
                var containers = new object[_path.Length];
                containers[0] = GetRoot(target);
                if (containers[0] == null) return;
                for (int i = 1; i < _path.Length; i++)
                {
                    containers[i] = _path[i - 1].GetValue(containers[i - 1]);
                    if (containers[i] == null) return;
                }
                _path[^1].SetValue(containers[^1], boxed);
                for (int i = _path.Length - 1; i > 0; i--)
                    _path[i - 1].SetValue(containers[i - 1], containers[i]);
                SetRoot(target, containers[0]);
            }

            // Written behind its back: a graphic is told to draw again, as its property would have.
            if (_property == null && target is Graphic graphic)
                graphic.SetAllDirty();
        }

        private object GetRoot(Object target) => _property != null ? _property.GetValue(target) : _field.GetValue(target);

        private void SetRoot(Object target, object value)
        {
            if (_property != null)
                _property.SetValue(target, value);
            else
                _field.SetValue(target, value);
        }

        private static string Unprefixed(string name)
        {
            if (name.StartsWith("m_", StringComparison.Ordinal)) return name.Substring(2);
            if (name.StartsWith("_", StringComparison.Ordinal)) return name.Substring(1);
            return name;
        }

        private static FieldInfo FindField(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (; type != null && type != typeof(object); type = type.BaseType)
            {
                var field = type.GetField(name, flags);
                if (field != null) return field;
            }
            return null;
        }

        // The public, settable property of that name (any case): of the field's own type when there is a field, else of
        // any type a variant can set; the most derived one where a subclass hides its parent's.
        private static PropertyInfo FindProperty(Type type, string name, Type fieldType)
        {
            PropertyInfo found = null;
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (!property.CanRead || property.SetMethod == null || !property.SetMethod.IsPublic || property.GetIndexParameters().Length > 0) continue;

                bool fits = fieldType != null ? property.PropertyType == fieldType : VariantValue.TryGetKind(property.PropertyType, out _);
                if (fits && (found == null || property.DeclaringType.IsSubclassOf(found.DeclaringType)))
                    found = property;
            }
            return found;
        }
    }
}
