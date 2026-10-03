using System.Collections;
using NUnit.Framework;
using TimboJimbo.UI.Motion;
using TimboJimbo.UI.Variants;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TimboJimboTests.UI.PlayMode
{
    /// <summary>
    /// Changes in play mode: one made inside another joins it, a value lands or is skipped there, one taken over does not
    /// complete; and a variant set moved under another shows what it inherits there.
    /// </summary>
    public class MotionPlayModeTests
    {
        // A canvas, so canvases are drawn and Motion's frame runs; what a test makes goes under it.
        private GameObject _canvas;

        [SetUp]
        public void SetUp() => _canvas = new GameObject("Canvas", typeof(Canvas));

        [TearDown]
        public void TearDown() => Object.Destroy(_canvas);

        [Test]
        public void AChangeMadeInsideAnotherJoinsItOnItsAnimation()
        {
            var snappy = MotionAnimation.Default.Use(MotionAnimationPreset.Snappy);
            MotionTransition inner = null;
            MotionAnimation seen = default;
            var outer = MotionSystem.Animate(snappy, () =>
                inner = MotionSystem.Animate(new MotionAnimation(0f), () => seen = MotionSystem.Current.Animation));

            Assert.AreSame(outer, inner);
            Assert.AreEqual(snappy, seen);
        }

        [Test]
        public void AChangeThatMovesNothingFinishesAtOnce()
        {
            var change = MotionSystem.Animate(() => { });

            Assert.IsTrue(change.IsFinished);
            Assert.IsTrue(change.Completed);
        }

        [UnityTest]
        public IEnumerator AValueLandsWhereItWasGoing()
        {
            var drawn = Vector4.zero;
            var change = MotionSystem.Animate(new MotionAnimation(0.1f), () =>
                MotionSystem.AnimateValue(_canvas, "value", Vector4.zero, Vector4.one, MotionSystem.Current.Animation, value => drawn = value));
            Assert.IsFalse(change.IsFinished);

            for (float waited = 0f; !change.IsFinished && waited < 2f; waited += Time.unscaledDeltaTime)
                yield return null;

            Assert.IsTrue(change.Completed);
            Assert.AreEqual(Vector4.one, drawn);
        }

        [UnityTest]
        public IEnumerator SkippingPutsAValueWhereItWasGoing()
        {
            var drawn = Vector4.zero;
            var change = MotionSystem.Animate(new MotionAnimation(1f), () =>
                MotionSystem.AnimateValue(_canvas, "value", Vector4.zero, Vector4.one, MotionSystem.Current.Animation, value => drawn = value));

            change.Skip();
            Assert.IsTrue(change.Completed);

            // Written where it was going in the next frame.
            yield return null;
            Assert.AreEqual(Vector4.one, drawn);
        }

        [Test]
        public void AValueTakenOverOnItsWayDoesNotComplete()
        {
            var first = MotionSystem.Animate(new MotionAnimation(1f), () =>
                MotionSystem.AnimateValue(_canvas, "value", Vector4.zero, Vector4.one, MotionSystem.Current.Animation, _ => { }));
            var second = MotionSystem.Animate(new MotionAnimation(1f), () =>
                MotionSystem.AnimateValue(_canvas, "value", Vector4.zero, 2f * Vector4.one, MotionSystem.Current.Animation, _ => { }));

            Assert.IsTrue(first.IsFinished);
            Assert.IsFalse(first.Completed);
            Assert.IsFalse(second.IsFinished);
            second.Skip();
        }

        [Test]
        public void ASetMovedUnderAnotherShowsWhatItInherits()
        {
            var toastObject = new GameObject("Toast", typeof(RectTransform));
            toastObject.transform.SetParent(_canvas.transform, false);
            var toast = toastObject.AddComponent<VariantSet>();
            toast.AddGroup(new VariantGroup("Type", new Variant("Error")));
            toast.Set("Type", "Error");

            var badgeObject = new GameObject("Badge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            badgeObject.transform.SetParent(_canvas.transform, false);
            var image = badgeObject.GetComponent<Image>();
            image.color = Color.white;
            var badge = badgeObject.AddComponent<VariantSet>();
            badge.AddGroup(new VariantGroup("Type",
                new Variant("Error", new VariantEntry(image, "m_Color", VariantValue.FromColor(Color.magenta)))));
            Assert.AreEqual("", badge.Get("Type"));

            badgeObject.transform.SetParent(toastObject.transform, false);

            Assert.AreEqual("Error", badge.Get("Type"));
            Assert.AreEqual(Color.magenta, image.color);
        }
    }
}
