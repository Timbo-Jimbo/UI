using System.Collections.Generic;
using NUnit.Framework;
using TimboJimbo.UI.Variants;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TimboJimboTests.UI.Variants
{
    /// <summary>
    /// A group on Inherit shows what the nearest set above it with a group of its name shows, and a switch passes down to
    /// the sets under it.
    /// </summary>
    public class VariantInheritanceTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var created in _created)
                if (created != null) Object.DestroyImmediate(created);
            _created.Clear();
        }

        // A set on a new object under `parent` (a root without one), with a Type group of `variants`, each colouring an
        // image on that object, which starts white. The group inherits, as a group made in code does.
        private (VariantSet Set, Image Image) TypeSet(string name, Transform parent, params (string Name, Color Colour)[] variants)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (parent != null) gameObject.transform.SetParent(parent, false);
            else _created.Add(gameObject);
            var image = gameObject.GetComponent<Image>();
            image.color = Color.white;

            var set = gameObject.AddComponent<VariantSet>();
            var type = new VariantGroup("Type");
            foreach (var (variant, colour) in variants)
                type.VariantList.Add(new Variant(variant, new VariantEntry(image, "m_Color", VariantValue.FromColor(colour))));
            set.GroupList.Add(type);
            return (set, image);
        }

        [Test]
        public void AGroupMadeInCodeInherits()
        {
            var (set, _) = TypeSet("Toast", null, ("Error", Color.red));
            Assert.IsTrue(set.Groups[0].Inherits);
            Assert.AreEqual("", set.Get("Type"));
        }

        [Test]
        public void AnInheritingGroupShowsTheSetAbove()
        {
            var (toast, _) = TypeSet("Toast", null, ("Error", Color.red));
            var (badge, badgeImage) = TypeSet("Badge", toast.transform, ("Error", Color.magenta));

            toast.Select("Type", "Error", apply: true);

            Assert.AreEqual("Error", badge.Get("Type"));
            Assert.AreEqual(Color.magenta, badgeImage.color);
            Assert.IsTrue(badge.Groups[0].Inherits);
        }

        [Test]
        public void ASelectionOfItsOwnOverridesInherit()
        {
            var (toast, _) = TypeSet("Toast", null, ("Success", Color.green), ("Error", Color.red));
            var (badge, badgeImage) = TypeSet("Badge", toast.transform, ("Success", Color.cyan), ("Error", Color.magenta));

            badge.Select("Type", "Success", apply: true);
            toast.Select("Type", "Error", apply: true);

            Assert.AreEqual("Success", badge.Get("Type"));
            Assert.AreEqual(Color.cyan, badgeImage.color);
        }

        [Test]
        public void ClearingPutsItBackOnInherit()
        {
            var (toast, _) = TypeSet("Toast", null, ("Success", Color.green), ("Error", Color.red));
            var (badge, badgeImage) = TypeSet("Badge", toast.transform, ("Success", Color.cyan), ("Error", Color.magenta));
            badge.Select("Type", "Success", apply: true);
            toast.Select("Type", "Error", apply: true);

            badge.Select("Type", null, apply: true, inherit: true);

            Assert.IsTrue(badge.Groups[0].Inherits);
            Assert.AreEqual("Error", badge.Get("Type"));
            Assert.AreEqual(Color.magenta, badgeImage.color);
        }

        [Test]
        public void ANameItHasNoVariantOfShowsDefaultAndStillPassesDown()
        {
            var (toast, _) = TypeSet("Toast", null, ("Error", Color.red));
            var (card, cardImage) = TypeSet("Card", toast.transform, ("Success", Color.green));
            var (badge, badgeImage) = TypeSet("Badge", card.transform, ("Error", Color.magenta));

            toast.Select("Type", "Error", apply: true);

            Assert.AreEqual("", card.Get("Type"));
            Assert.AreEqual(Color.white, cardImage.color);
            Assert.AreEqual("Error", badge.Get("Type"));
            Assert.AreEqual(Color.magenta, badgeImage.color);
        }

        [Test]
        public void AnExplicitDefaultPassesDefaultDown()
        {
            var (toast, _) = TypeSet("Toast", null, ("Error", Color.red));
            var (card, _) = TypeSet("Card", toast.transform, ("Error", Color.yellow));
            var (badge, badgeImage) = TypeSet("Badge", card.transform, ("Error", Color.magenta));

            card.Select("Type", "", apply: true);
            toast.Select("Type", "Error", apply: true);

            Assert.AreEqual("", badge.Get("Type"));
            Assert.AreEqual(Color.white, badgeImage.color);
        }

        [Test]
        public void TheOuterSetWinsWhereBothSetAValue()
        {
            var (toast, _) = TypeSet("Toast", null, ("Error", Color.red));
            var (badge, badgeImage) = TypeSet("Badge", toast.transform, ("Error", Color.magenta));
            toast.GroupList[0].VariantList[0].EntryList.Add(new VariantEntry(badgeImage, "m_Color", VariantValue.FromColor(Color.blue)));

            toast.Select("Type", "Error", apply: true);

            Assert.AreEqual(Color.blue, badgeImage.color);
        }
    }
}
