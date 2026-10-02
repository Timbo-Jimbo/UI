using TimboJimbo.Core.Utility;
using TimboJimbo.UI.Layout;
using TimboJimbo.UI.Motion;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimboJimbo.UI.Variants
{
    /// <summary>
    /// How a variant's values move when its set is switched inside
    /// <see cref="MotionSystem.Animate(MotionAnimation, System.Action, string[])"/>. A value drawn as it is (a colour, a
    /// corner radius, a size of something that is not a layout node) moves with the change on the spring of the layout
    /// node it is drawn in, the nearest at or above it (<see cref="LayoutSystem.AnimationOf"/>), so it keeps time with that node;
    /// a colour moves in OkLab, in linear light, so a red turning green passes through no brown. A layout node's own values
    /// are the layout's to move: they go at once, and the nodes they give somewhere new move. What is not a number (text, a
    /// sprite, whether an object is active) goes at once.
    /// </summary>
    internal static class VariantMotion
    {
        public static bool Springs(Object target, VariantValueKind kind) => target is not LayoutNode
            && kind is VariantValueKind.Float or VariantValueKind.Color or VariantValueKind.Vector2 or VariantValueKind.Vector3
                or VariantValueKind.Vector4 or VariantValueKind.Rect;

        /// <summary>The layout node <paramref name="target"/> is drawn in: on its own object, or the nearest above it.</summary>
        public static LayoutNode NodeOf(Object target) => target switch
        {
            GameObject gameObject => gameObject.GetComponentInParent<LayoutNode>(true),
            Component component => component.GetComponentInParent<LayoutNode>(true),
            _ => null,
        };

        /// <summary>
        /// Moves <paramref name="target"/>'s <paramref name="property"/> to <paramref name="to"/>: with the change being
        /// made, from where it is drawn, on <paramref name="node"/>'s spring for it (the change's own without a node);
        /// outside a change, at once.
        /// </summary>
        public static void Move(Object target, string property, VariantAccessor accessor, LayoutNode node, VariantValue to)
        {
            var animation = node != null ? LayoutSystem.AnimationOf(node) : MotionSystem.Current?.Animation ?? MotionAnimation.Default;
            var kind = to.Kind;
            var end = Encode(to);
            // Where it lands (or goes at once) it is written as given, not as it comes back from OkLab.
            MotionSystem.AnimateValue(target, property, Encode(accessor.Read(target)), end, animation,
                drawn => accessor.Write(target, drawn.Equals(end) ? to : Decode(kind, drawn)));
        }

        private static Vector4 Encode(VariantValue value)
        {
            if (value.Kind != VariantValueKind.Color) return value.VectorValue;
            var colour = value.ColorValue;
            ColorExtra.RGBToOkLab(colour, out float l, out float a, out float b);
            return new Vector4(l, a, b, colour.a);
        }

        // An alpha that overshoots on a bouncy spring is held to 0 to 1, as an opacity is.
        private static VariantValue Decode(VariantValueKind kind, Vector4 drawn) => kind == VariantValueKind.Color
            ? VariantValue.FromColor(ColorExtra.OkLabToRGB(drawn.x, drawn.y, drawn.z, Mathf.Clamp01(drawn.w)))
            : VariantValue.FromVector(kind, drawn);
    }
}
