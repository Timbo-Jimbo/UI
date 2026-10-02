using System;
using UnityEngine;

namespace TimboJimbo.UI.Motion
{
    /// <summary>
    /// An animation that may be left unset, to be inherited (a layout node's Animation: unset, it moves on that of the
    /// nearest node above it with one, or else on the change's; a variant group's: unset, it switches on the default),
    /// kept as whether it is set and the animation. Read and written as a <see cref="MotionAnimation"/>? through
    /// <see cref="Value"/>; the inspector draws it as a row of presets with Inherit first.
    /// </summary>
    [Serializable]
    public struct OptionalMotionAnimation
    {
        [SerializeField] private bool _set;
        [SerializeField] private MotionAnimation _animation;

        /// <summary>Set to <paramref name="animation"/>, or unset with null (its animation then the default, for when it is set).</summary>
        public OptionalMotionAnimation(MotionAnimation? animation)
        {
            _set = animation.HasValue;
            _animation = animation ?? MotionAnimation.Default;
        }

        /// <summary>The animation, or null while it is unset (Inherit).</summary>
        public MotionAnimation? Value => _set ? _animation : null;
    }
}
