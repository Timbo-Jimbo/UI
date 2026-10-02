using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimboJimbo.UI.Variants
{
    /// <summary>The kinds of value a variant sets. Enums, flags and layer masks are kept as <see cref="Int"/>.</summary>
    public enum VariantValueKind
    {
        Bool,
        Int,
        Float,
        Color,
        Vector2,
        Vector3,
        Vector4,
        Rect,
        Quaternion,
        Vector2Int,
        Vector3Int,
        String,
        Object,
    }

    /// <summary>
    /// One value a variant sets, of any kind a serialized field can be: a number, a colour, a vector, a string or a
    /// reference. It is kept in a few plain fields rather than polymorphically, so it serializes, diffs and is overridden
    /// in prefab variants as any other field is.
    /// </summary>
    [Serializable]
    public struct VariantValue : IEquatable<VariantValue>
    {
        [SerializeField] private VariantValueKind _kind;

        // A float, and every vector, colour, rect and rotation, by component (a rect as x, y, width, height).
        [SerializeField] private Vector4 _vector;

        // A bool (0 or 1), an integer, an enum or a layer mask.
        [SerializeField] private long _integer;

        [SerializeField] private string _string;
        [SerializeField] private Object _object;

        public VariantValueKind Kind => _kind;
        public bool BoolValue => _integer != 0;
        public long IntValue => _integer;
        public float FloatValue => _vector.x;
        public Color ColorValue => _vector;
        public Vector4 VectorValue => _vector;
        public Rect RectValue => new(_vector.x, _vector.y, _vector.z, _vector.w);
        public Quaternion QuaternionValue => new(_vector.x, _vector.y, _vector.z, _vector.w);
        public Vector2Int Vector2IntValue => new(Mathf.RoundToInt(_vector.x), Mathf.RoundToInt(_vector.y));
        public Vector3Int Vector3IntValue => new(Mathf.RoundToInt(_vector.x), Mathf.RoundToInt(_vector.y), Mathf.RoundToInt(_vector.z));
        public string StringValue => _string ?? "";
        public Object ObjectValue => _object;

        public static VariantValue FromBool(bool value) => new() { _kind = VariantValueKind.Bool, _integer = value ? 1 : 0 };
        public static VariantValue FromInt(long value) => new() { _kind = VariantValueKind.Int, _integer = value };
        public static VariantValue FromFloat(float value) => new() { _kind = VariantValueKind.Float, _vector = new Vector4(value, 0f, 0f, 0f) };
        public static VariantValue FromColor(Color value) => new() { _kind = VariantValueKind.Color, _vector = value };
        public static VariantValue FromString(string value) => new() { _kind = VariantValueKind.String, _string = value ?? "" };
        public static VariantValue FromObject(Object value) => new() { _kind = VariantValueKind.Object, _object = value };

        /// <summary>A vector-like value (a vector, rect, rotation or integer vector) from its components.</summary>
        public static VariantValue FromVector(VariantValueKind kind, Vector4 value) => new() { _kind = kind, _vector = value };

        /// <summary>The kind of value a field or property of <paramref name="type"/> holds; false for one a variant cannot set.</summary>
        public static bool TryGetKind(Type type, out VariantValueKind kind)
        {
            kind = default;
            if (type == typeof(bool)) kind = VariantValueKind.Bool;
            else if (type.IsEnum || type == typeof(LayerMask) || type == typeof(int) || type == typeof(long) || type == typeof(short)
                     || type == typeof(byte) || type == typeof(uint) || type == typeof(ushort) || type == typeof(sbyte) || type == typeof(char))
                kind = VariantValueKind.Int;
            else if (type == typeof(float) || type == typeof(double)) kind = VariantValueKind.Float;
            else if (type == typeof(Color) || type == typeof(Color32)) kind = VariantValueKind.Color;
            else if (type == typeof(Vector2)) kind = VariantValueKind.Vector2;
            else if (type == typeof(Vector3)) kind = VariantValueKind.Vector3;
            else if (type == typeof(Vector4)) kind = VariantValueKind.Vector4;
            else if (type == typeof(Rect)) kind = VariantValueKind.Rect;
            else if (type == typeof(Quaternion)) kind = VariantValueKind.Quaternion;
            else if (type == typeof(Vector2Int)) kind = VariantValueKind.Vector2Int;
            else if (type == typeof(Vector3Int)) kind = VariantValueKind.Vector3Int;
            else if (type == typeof(string)) kind = VariantValueKind.String;
            else if (typeof(Object).IsAssignableFrom(type)) kind = VariantValueKind.Object;
            else return false;
            return true;
        }

        /// <summary>The value of <paramref name="kind"/> that <paramref name="value"/> (as read from a field or property) holds.</summary>
        public static VariantValue FromBoxed(object value, VariantValueKind kind)
        {
            switch (kind)
            {
                case VariantValueKind.Bool: return FromBool(value is true);
                case VariantValueKind.Int:
                    return FromInt(value switch
                    {
                        LayerMask mask => mask.value,
                        char character => character,
                        null => 0,
                        _ => Convert.ToInt64(value),
                    });
                case VariantValueKind.Float: return FromFloat(value == null ? 0f : Convert.ToSingle(value));
                case VariantValueKind.Color: return FromColor(value is Color32 color32 ? (Color)color32 : value is Color color ? color : default(Color));
                case VariantValueKind.Vector2: return FromVector(kind, value is Vector2 v2 ? v2 : default);
                case VariantValueKind.Vector3: return FromVector(kind, value is Vector3 v3 ? v3 : default);
                case VariantValueKind.Vector4: return FromVector(kind, value is Vector4 v4 ? v4 : default);
                case VariantValueKind.Rect:
                    var rect = value is Rect r ? r : default;
                    return FromVector(kind, new Vector4(rect.x, rect.y, rect.width, rect.height));
                case VariantValueKind.Quaternion:
                    var rotation = value is Quaternion q ? q : Quaternion.identity;
                    return FromVector(kind, new Vector4(rotation.x, rotation.y, rotation.z, rotation.w));
                case VariantValueKind.Vector2Int:
                    var i2 = value is Vector2Int a ? a : default;
                    return FromVector(kind, new Vector4(i2.x, i2.y, 0f, 0f));
                case VariantValueKind.Vector3Int:
                    var i3 = value is Vector3Int b ? b : default;
                    return FromVector(kind, new Vector4(i3.x, i3.y, i3.z, 0f));
                case VariantValueKind.String: return FromString(value as string);
                case VariantValueKind.Object: return FromObject(value as Object);
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        /// <summary>This value as a field or property of <paramref name="type"/> takes it: an enum, a Color32, a double.</summary>
        public object ToBoxed(Type type)
        {
            switch (_kind)
            {
                case VariantValueKind.Bool: return BoolValue;
                case VariantValueKind.Int:
                    if (type.IsEnum) return Enum.ToObject(type, _integer);
                    if (type == typeof(LayerMask)) return (LayerMask)(int)_integer;
                    if (type == typeof(char)) return (char)_integer;
                    return Convert.ChangeType(_integer, type);
                case VariantValueKind.Float: return type == typeof(double) ? (object)(double)_vector.x : _vector.x;
                case VariantValueKind.Color: return type == typeof(Color32) ? (object)(Color32)ColorValue : ColorValue;
                case VariantValueKind.Vector2: return (Vector2)_vector;
                case VariantValueKind.Vector3: return (Vector3)_vector;
                case VariantValueKind.Vector4: return _vector;
                case VariantValueKind.Rect: return RectValue;
                case VariantValueKind.Quaternion: return QuaternionValue;
                case VariantValueKind.Vector2Int: return Vector2IntValue;
                case VariantValueKind.Vector3Int: return Vector3IntValue;
                case VariantValueKind.String: return StringValue;
                case VariantValueKind.Object: return _object ? _object : null;
                default: throw new ArgumentOutOfRangeException();
            }
        }

        public bool Equals(VariantValue other)
        {
            if (_kind != other._kind) return false;
            return _kind switch
            {
                VariantValueKind.Bool or VariantValueKind.Int => _integer == other._integer,
                VariantValueKind.String => StringValue == other.StringValue,
                VariantValueKind.Object => _object == other._object,
                _ => _vector.Equals(other._vector),
            };
        }

        public override bool Equals(object obj) => obj is VariantValue other && Equals(other);

        public override int GetHashCode() => _kind switch
        {
            VariantValueKind.Bool or VariantValueKind.Int => HashCode.Combine(_kind, _integer),
            VariantValueKind.String => HashCode.Combine(_kind, StringValue),
            VariantValueKind.Object => HashCode.Combine(_kind, _object ? _object.GetHashCode() : 0),
            _ => HashCode.Combine(_kind, _vector),
        };

        public override string ToString() => _kind switch
        {
            VariantValueKind.Bool => BoolValue.ToString(),
            VariantValueKind.Int => _integer.ToString(),
            VariantValueKind.Float => _vector.x.ToString("0.###"),
            VariantValueKind.Color => "#" + ColorUtility.ToHtmlStringRGBA(ColorValue),
            VariantValueKind.String => $"\"{StringValue}\"",
            VariantValueKind.Object => _object ? _object.name : "None",
            _ => _vector.ToString(),
        };
    }
}
