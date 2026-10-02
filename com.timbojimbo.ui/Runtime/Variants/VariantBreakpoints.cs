using System;
using System.Collections.Generic;
using UnityEngine;

namespace TimboJimbo.UI.Variants
{
    /// <summary>What a <see cref="VariantBreakpoints"/> measures of its rect.</summary>
    public enum BreakpointMeasure
    {
        /// <summary>How wide it is drawn.</summary>
        Width,

        /// <summary>How tall it is drawn.</summary>
        Height,

        /// <summary>Its width over its height: 1 and over is landscape (or square), under 1 portrait.</summary>
        AspectRatio,
    }

    /// <summary>
    /// Selects a variant of a group by the size its object is drawn at, as CSS's container queries and Tailwind's
    /// breakpoints do, mobile first: Default below every breakpoint, else the variant of the largest one it reaches. It
    /// measures its own RectTransform, so it goes on the object whose size decides (a screen, a card, a panel);
    /// orientation is <see cref="BreakpointMeasure.AspectRatio"/> with a breakpoint at 1. It switches through
    /// <see cref="VariantSet.Set(string, string)"/>, so the switch animates as the group does, and while what it is on is
    /// resized on a spring it switches as it crosses. It reads the size drawn in the last frame, before the frame is laid
    /// out, as a switch made while a frame is laid out would not animate. The editor previews the variant for the size it
    /// is drawn at, without saving it.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Timbo Jimbo/Variants/Variant Breakpoints")]
    public sealed class VariantBreakpoints : MonoBehaviour
    {
        [Serializable]
        public struct Breakpoint
        {
            [Tooltip("The least it measures for this breakpoint's variant to be selected.")]
            public float Min;

            [Tooltip("The variant selected from there up; empty for Default.")]
            public string Variant;

            public Breakpoint(float min, string variant)
            {
                Min = min;
                Variant = variant;
            }
        }

        [Tooltip("The variant set it selects in: the one on this object or above it, when none is given.")]
        [SerializeField] private VariantSet _target;

        [Tooltip("The group it selects in.")]
        [SerializeField] private string _group;

        [Tooltip("What it measures of its own rect: its width, its height, or its width over its height.")]
        [SerializeField] private BreakpointMeasure _measure;

        [Tooltip("In any order. Below every one of them, the group is on Default.")]
        [SerializeField] private Breakpoint[] _breakpoints = Array.Empty<Breakpoint>();

        /// <summary>The variant set it selects in: the one given, or else the one on this object or above it.</summary>
        public VariantSet Target => _target != null ? _target : GetComponentInParent<VariantSet>(true);

        public string Group => _group;
        public BreakpointMeasure Measure => _measure;
        public IReadOnlyList<Breakpoint> Breakpoints => _breakpoints;

        /// <summary>Selects in <paramref name="target"/>'s <paramref name="group"/> by <paramref name="measure"/> at <paramref name="breakpoints"/>.</summary>
        public void Setup(VariantSet target, string group, BreakpointMeasure measure, params Breakpoint[] breakpoints)
        {
            _target = target;
            _group = group;
            _measure = measure;
            _breakpoints = breakpoints;
        }

        /// <summary>What it measures of a rect of <paramref name="size"/>.</summary>
        public float MeasureOf(Vector2 size) => _measure switch
        {
            BreakpointMeasure.Width => size.x,
            BreakpointMeasure.Height => size.y,
            _ => size.y > 0f ? size.x / size.y : 0f,
        };

        /// <summary>The variant a rect of <paramref name="size"/> gets: the largest breakpoint's it reaches, or empty for Default.</summary>
        public string VariantFor(Vector2 size)
        {
            float value = MeasureOf(size);
            string variant = "";
            float reached = float.NegativeInfinity;
            foreach (var breakpoint in _breakpoints)
            {
                if (value < breakpoint.Min || breakpoint.Min < reached) continue;
                reached = breakpoint.Min;
                variant = breakpoint.Variant ?? "";
            }
            return variant;
        }

        /// <summary>The size its rect is drawn at now.</summary>
        public Vector2 DrawnSize => ((RectTransform)transform).rect.size;

        // Selects the variant for the size it is drawn at as the group's own (Default included), so the group passes it
        // down to the sets under it that inherit it. A group or variant it names that its set does not have is left
        // alone, rather than warned about every frame: the inspector shows it missing.
        private void Update()
        {
            var target = Target;
            if (target == null) return;
            int group = target.IndexOfGroup(_group);
            if (group < 0) return;
            string variant = VariantFor(DrawnSize);
            if (variant.Length > 0 && target.Groups[group].IndexOf(variant) < 0) return;
            if (target.Groups[group].Inherits || target.Groups[group].Selected != variant)
                target.Set(_group, variant);
        }

#if UNITY_EDITOR
        // The editor's preview shows what its breakpoints select, so a change to them shows at once.
        private void OnValidate()
        {
            if (!Application.isPlaying)
                VariantSet.s_editorChanged?.Invoke(Target);
        }
#endif
    }
}
