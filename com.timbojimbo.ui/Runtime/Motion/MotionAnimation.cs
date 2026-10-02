using System;
using UnityEngine;

namespace TimboJimbo.UI.Motion
{
    /// <summary>Ready-made springs for <see cref="MotionAnimation.Use"/>, as SwiftUI's smooth, snappy and bouncy.</summary>
    public enum MotionAnimationPreset
    {
        /// <summary>Settles without overshooting.</summary>
        Smooth,

        /// <summary>A small overshoot, quick to settle.</summary>
        Snappy,

        /// <summary>A lively overshoot and swing back.</summary>
        Bouncy,

        /// <summary>Settles without overshooting, bowing out sideways on the way.</summary>
        Arc,

        /// <summary>
        /// No animation, a duration of 0: what it moves is there at once, once its delay is up, as UIKit's zero-duration
        /// animation. Last, so the presets saved as numbers before it keep theirs.
        /// </summary>
        None,
    }

    /// <summary>
    /// How what a change made in <see cref="MotionSystem.Animate(MotionAnimation, Action, string[])"/> moves gets
    /// where it is going, as SwiftUI's Animation: on a spring, from where it is drawn and at the velocity it has, given as
    /// SwiftUI gives one (a perceptual duration and a bounce), after <see cref="Delay"/>; a move across the screen bows
    /// out sideways by <see cref="Curvature"/>. A change carries one, and what it moves can be given its own instead (a
    /// layout node's Animation). A duration of 0 is no animation (<see cref="MotionAnimationPreset.None"/>): it is there
    /// at once, after its delay.
    /// </summary>
    [Serializable]
    public struct MotionAnimation
    {
        [Tooltip("Seconds: the spring's perceptual duration, as SwiftUI's. Close to how long it takes to settle with little bounce, about how long a swing takes with a lot. 0: no animation, there at once (after its delay).")]
        [Min(0f)] public float Duration;

        [Tooltip("How far it overshoots and swings back: 0 none, more the livelier.")]
        [Range(0f, 0.9f)] public float Bounce;

        [Tooltip("How far a move across the screen bows out sideways: it sets off with a sideways velocity that the spring pulls back in, bowing towards the corner of the L that takes the shorter axis first (on a level or upright move, to the left of the way it goes). 0 goes straight. Only a layout node's place bows.")]
        [Range(0f, 1f)] public float Curvature;

        [Tooltip("Seconds to wait before setting off.")]
        [Min(0f)] public float Delay;

        public MotionAnimation(float duration, float bounce = 0f, float curvature = 0f, float delay = 0f)
        {
            Duration = duration;
            Bounce = bounce;
            Curvature = curvature;
            Delay = delay;
        }

        /// <summary>A smooth spring of about a third of a second: what a change carries unless it is given another.</summary>
        public static MotionAnimation Default => new(0.35f);

        /// <summary>
        /// This animation with <paramref name="preset"/>'s bounce and curvature, its duration and delay kept. None sets
        /// only its duration, to 0; another preset given to an animation with none gives it the default duration back.
        /// </summary>
        public MotionAnimation Use(MotionAnimationPreset preset)
        {
            var animation = this;
            switch (preset)
            {
                case MotionAnimationPreset.None: animation.Duration = 0f; return animation;
                case MotionAnimationPreset.Smooth: animation.Bounce = 0f; animation.Curvature = 0f; break;
                case MotionAnimationPreset.Snappy: animation.Bounce = 0.15f; animation.Curvature = 0f; break;
                case MotionAnimationPreset.Bouncy: animation.Bounce = 0.3f; animation.Curvature = 0f; break;
                case MotionAnimationPreset.Arc: animation.Bounce = 0f; animation.Curvature = 0.6f; break;
                default: throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
            if (animation.Duration <= 0f)
                animation.Duration = Default.Duration;
            return animation;
        }

        /// <summary>
        /// Whether it is <paramref name="preset"/>: None with a duration of 0, whatever else it has; any other preset by
        /// its bounce and curvature, and only with a duration.
        /// </summary>
        public bool Matches(MotionAnimationPreset preset)
        {
            if (preset == MotionAnimationPreset.None) return Duration <= 0f;
            if (Duration <= 0f) return false;
            var used = Use(preset);
            return Mathf.Abs(used.Bounce - Bounce) < 1e-3f && Mathf.Abs(used.Curvature - Curvature) < 1e-3f;
        }

        // No duration and no delay: what it moves is put where it is going in the change's own pass, held by nothing.
        internal bool AtOnce => Duration <= 0f && Delay <= 0f;
    }
}
