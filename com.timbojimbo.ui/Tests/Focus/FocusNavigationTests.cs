using System.Collections.Generic;
using NUnit.Framework;
using TimboJimbo.UI.Focus;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TimboJimboTests.UI.Focus
{
    /// <summary>Where focus goes from a rect: the neighbour in a direction, and reading order for Tab.</summary>
    public class FocusNavigationTests
    {
        // A rect of `size` around (x, y), in screen space (y up).
        private static Rect At(float x, float y, float size = 40f) => new(x - size * 0.5f, y - size * 0.5f, size, size);

        [Test]
        public void ANeighbourInLineComesBeforeANearerOneOffToTheSide()
        {
            var from = At(0f, 0f);
            var rects = new List<Rect> { At(60f, 70f), At(200f, 0f) };

            Assert.AreEqual(1, FocusNavigation.Pick(from, MoveDirection.Right, rects));
        }

        [Test]
        public void TheNearestInLineWins()
        {
            var from = At(0f, 0f);
            var rects = new List<Rect> { At(300f, 0f), At(100f, 5f), At(200f, -5f) };

            Assert.AreEqual(1, FocusNavigation.Pick(from, MoveDirection.Right, rects));
        }

        [Test]
        public void OnlyWhatLiesThatWayCounts()
        {
            var from = At(0f, 0f);
            var rects = new List<Rect> { At(-100f, 0f), At(0f, 100f), At(0f, -100f) };

            Assert.AreEqual(-1, FocusNavigation.Pick(from, MoveDirection.Right, rects));
            Assert.AreEqual(1, FocusNavigation.Pick(from, MoveDirection.Up, rects));
            Assert.AreEqual(2, FocusNavigation.Pick(from, MoveDirection.Down, rects));
            Assert.AreEqual(0, FocusNavigation.Pick(from, MoveDirection.Left, rects));
        }

        [Test]
        public void TheOneMovedFromIsSkipped()
        {
            var rects = new List<Rect> { At(0f, 0f), At(100f, 0f) };

            Assert.AreEqual(1, FocusNavigation.Pick(rects[0], MoveDirection.Right, rects, skip: 0));
            Assert.AreEqual(-1, FocusNavigation.Pick(rects[1], MoveDirection.Right, rects, skip: 1));
        }

        [Test]
        public void ReadingOrderGoesByRowsThenAcross()
        {
            // Two rows, top (y 100) and bottom (y 0), given out of order; the top row's middle one a little lower.
            var rects = new List<Rect> { At(100f, 0f), At(200f, 95f), At(0f, 100f), At(0f, 0f), At(300f, 100f) };

            CollectionAssert.AreEqual(new[] { 2, 1, 4, 3, 0 }, FocusNavigation.ReadingOrder(rects));
        }
    }
}
