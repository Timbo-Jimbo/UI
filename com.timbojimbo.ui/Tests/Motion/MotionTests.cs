using NUnit.Framework;
using TimboJimbo.UI.Motion;
using UnityEngine;

namespace TimboJimboTests.UI.Motion
{
    /// <summary>Springs stepped in closed form, the presets, and changes made outside play mode.</summary>
    public class MotionTests
    {
        // A spring at 0 headed for 10 on `animation`.
        private static Spring Headed(MotionAnimation animation)
        {
            var spring = new Spring(0.01f) { Target = new Vector2(10f, 0f) };
            Spring.Parameters(animation, out spring.Omega, out spring.Zeta);
            spring.Delay = animation.Delay;
            return spring;
        }

        [Test]
        public void ASpringComesToRestWhereItIsGoing()
        {
            var spring = Headed(MotionAnimation.Default.Use(MotionAnimationPreset.Bouncy));
            bool there = false;
            for (int frame = 0; frame < 300 && !there; frame++)
                there = spring.Step(1f / 60f);

            Assert.IsTrue(there);
            Assert.AreEqual(10f, spring.Value.x, 0.01f);
        }

        [Test]
        public void ASpringPlaysTheSameAtAnyFrameRate()
        {
            var slow = Headed(MotionAnimation.Default.Use(MotionAnimationPreset.Snappy));
            var fast = Headed(MotionAnimation.Default.Use(MotionAnimationPreset.Snappy));
            for (int frame = 0; frame < 6; frame++) slow.Step(1f / 30f);
            for (int frame = 0; frame < 24; frame++) fast.Step(1f / 120f);

            Assert.AreEqual(slow.Value.x, fast.Value.x, 1e-3f);
            Assert.AreEqual(slow.Velocity.x, fast.Velocity.x, 1e-2f);
        }

        [Test]
        public void NoDurationIsThereOnceTheDelayIsUp()
        {
            var spring = Headed(new MotionAnimation(0f, delay: 0.1f));

            Assert.IsFalse(spring.Step(0.05f));
            Assert.AreEqual(0f, spring.Value.x);
            Assert.IsTrue(spring.Step(0.06f));
            Assert.AreEqual(10f, spring.Value.x);
        }

        [Test]
        public void NoneIsLitByNoDurationAndAnotherPresetGivesItBack()
        {
            var none = MotionAnimation.Default.Use(MotionAnimationPreset.Bouncy).Use(MotionAnimationPreset.None);
            Assert.AreEqual(0f, none.Duration);
            Assert.IsTrue(none.Matches(MotionAnimationPreset.None));
            Assert.IsFalse(none.Matches(MotionAnimationPreset.Bouncy));

            var snappy = none.Use(MotionAnimationPreset.Snappy);
            Assert.AreEqual(MotionAnimation.Default.Duration, snappy.Duration);
            Assert.IsTrue(snappy.Matches(MotionAnimationPreset.Snappy));
        }

        [Test]
        public void OutsidePlayModeAChangeIsMadeAtOnce()
        {
            bool made = false;
            var transition = MotionSystem.Animate(() => made = true);

            Assert.IsTrue(made);
            Assert.IsTrue(transition.IsFinished);
            Assert.IsTrue(transition.Completed);
        }

        [Test]
        public void OutsidePlayModeAValueGoesThereAtOnce()
        {
            var owner = new GameObject("Owner");
            try
            {
                Vector4 drawn = Vector4.zero;
                MotionSystem.AnimateValue(owner, "value", Vector4.zero, Vector4.one, MotionAnimation.Default, value => drawn = value);
                Assert.AreEqual(Vector4.one, drawn);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }
    }
}
