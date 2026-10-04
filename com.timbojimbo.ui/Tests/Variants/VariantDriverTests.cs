using System.Collections.Generic;
using NUnit.Framework;
using TimboJimbo.UI.Focus;
using TimboJimbo.UI.Variants;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace TimboJimboTests.UI.Variants
{
    /// <summary>What VariantStates and VariantBreakpoints pick: a state by precedence, a breakpoint by size.</summary>
    public class VariantDriverTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var created in _created)
                if (created != null) Object.DestroyImmediate(created);
            _created.Clear();
            FocusSystem.SetVisible(false);
        }

        private VariantSet SetWith(string group, params string[] variants)
        {
            var gameObject = new GameObject("Thing", typeof(RectTransform));
            _created.Add(gameObject);
            var set = gameObject.AddComponent<VariantSet>();
            var added = new VariantGroup(group);
            foreach (var variant in variants)
                added.VariantList.Add(new Variant(variant));
            set.GroupList.Add(added);
            return set;
        }

        [Test]
        public void StatesGoByPrecedence()
        {
            var set = SetWith("State", "Hover", "Pressed", "Focused", "Disabled");
            var group = set.Groups[0];
            var states = set.gameObject.AddComponent<VariantStates>();
            states.Setup(set, "State", "Hover", "Pressed", "Focused", "Disabled");
            var pointer = new PointerEventData(null);

            Assert.AreEqual("", states.Pick(group));
            states.OnPointerEnter(pointer);
            Assert.AreEqual("Hover", states.Pick(group));
            FocusSystem.SetVisible(true);
            states.OnSelect(new BaseEventData(null));
            Assert.AreEqual("Focused", states.Pick(group));
            states.OnPointerDown(pointer);
            Assert.AreEqual("Pressed", states.Pick(group));
            // Dragged out, the press no longer shows, as UIKit's highlight drops.
            states.OnPointerExit(pointer);
            Assert.AreEqual("Focused", states.Pick(group));
            states.Interactable = false;
            Assert.AreEqual("Disabled", states.Pick(group));
        }

        [Test]
        public void FocusedShowsOnlyWhileFocusShows()
        {
            var set = SetWith("State", "Hover", "Focused");
            var group = set.Groups[0];
            var states = set.gameObject.AddComponent<VariantStates>();
            states.Setup(set, "State", "Hover", "", "Focused", "");
            var pointer = new PointerEventData(null);

            // Selected by a click: no ring.
            states.OnPointerEnter(pointer);
            states.OnSelect(pointer);
            Assert.AreEqual("Hover", states.Pick(group));
            // Then a key: the ring.
            FocusSystem.SetVisible(true);
            Assert.AreEqual("Focused", states.Pick(group));
        }

        [Test]
        public void AStateWithNoVariantFallsThrough()
        {
            var set = SetWith("State", "Hover");
            var group = set.Groups[0];
            var states = set.gameObject.AddComponent<VariantStates>();
            states.Setup(set, "State", "Hover", "", "", "");
            var pointer = new PointerEventData(null);

            states.OnPointerEnter(pointer);
            states.OnPointerDown(pointer);
            Assert.AreEqual("Hover", states.Pick(group));
        }

        [Test]
        public void BreakpointsPickTheLargestReached()
        {
            var set = SetWith("Width", "Wide", "Huge");
            var breakpoints = set.gameObject.AddComponent<VariantBreakpoints>();
            // Out of order: the largest reached wins whatever the order.
            breakpoints.Setup(set, "Width", BreakpointMeasure.Width,
                new VariantBreakpoints.Breakpoint(800f, "Huge"), new VariantBreakpoints.Breakpoint(420f, "Wide"));

            Assert.AreEqual("", breakpoints.VariantFor(new Vector2(300f, 900f)));
            Assert.AreEqual("Wide", breakpoints.VariantFor(new Vector2(420f, 100f)));
            Assert.AreEqual("Wide", breakpoints.VariantFor(new Vector2(799f, 100f)));
            Assert.AreEqual("Huge", breakpoints.VariantFor(new Vector2(800f, 100f)));
        }

        [Test]
        public void AnAspectBreakpointAtOneIsOrientation()
        {
            var set = SetWith("Orientation", "Landscape");
            var breakpoints = set.gameObject.AddComponent<VariantBreakpoints>();
            breakpoints.Setup(set, "Orientation", BreakpointMeasure.AspectRatio, new VariantBreakpoints.Breakpoint(1f, "Landscape"));

            Assert.AreEqual("", breakpoints.VariantFor(new Vector2(300f, 500f)));
            Assert.AreEqual("Landscape", breakpoints.VariantFor(new Vector2(500f, 300f)));
        }
    }
}
