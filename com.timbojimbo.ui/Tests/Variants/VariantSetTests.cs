using System;
using System.Collections.Generic;
using NUnit.Framework;
using TimboJimbo.UI.Variants;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TimboJimboTests.UI.Variants
{
    /// <summary>A variant set applies its selection over the defaults it found, and puts them back when nothing sets them.</summary>
    public class VariantSetTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var created in _created)
                if (created != null) Object.DestroyImmediate(created);
            _created.Clear();
        }

        private GameObject Make(string name, Transform parent = null, params Type[] components)
        {
            var gameObject = new GameObject(name, components);
            if (parent != null) gameObject.transform.SetParent(parent, false);
            else _created.Add(gameObject);
            return gameObject;
        }

        // A toast: a set on its root, an image and an icon under it, and Type and Size groups.
        private (VariantSet Set, Image Background, GameObject Icon) Toast()
        {
            var root = Make("Toast", null, typeof(RectTransform));
            var background = Make("Background", root.transform, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            background.color = Color.white;
            var icon = Make("Icon", root.transform, typeof(RectTransform));
            icon.SetActive(false);

            var set = root.AddComponent<VariantSet>();
            var type = new VariantGroup("Type");
            var success = new Variant("Success");
            success.EntryList.Add(new VariantEntry(background, "m_Color", VariantValue.FromColor(Color.green)));
            var error = new Variant("Error");
            error.EntryList.Add(new VariantEntry(background, "m_Color", VariantValue.FromColor(Color.red)));
            error.EntryList.Add(new VariantEntry(icon, "m_IsActive", VariantValue.FromBool(true)));
            type.VariantList.Add(success);
            type.VariantList.Add(error);

            var size = new VariantGroup("Size");
            var compact = new Variant("Compact");
            compact.EntryList.Add(new VariantEntry(background, "m_Color", VariantValue.FromColor(Color.gray)));
            size.VariantList.Add(compact);

            set.GroupList.Add(type);
            set.GroupList.Add(size);
            return (set, background, icon);
        }

        [Test]
        public void SelectingAVariantAppliesItsValues()
        {
            var (set, background, icon) = Toast();
            set.Select("Type", "Error", apply: true);

            Assert.AreEqual(Color.red, background.color);
            Assert.IsTrue(icon.activeSelf);
            Assert.AreEqual("Error", set.Get("Type"));
        }

        [Test]
        public void SwitchingPutsBackWhatTheNewVariantDoesNotSet()
        {
            var (set, background, icon) = Toast();
            set.Select("Type", "Error", apply: true);
            set.Select("Type", "Success", apply: true);

            Assert.AreEqual(Color.green, background.color);
            Assert.IsFalse(icon.activeSelf, "Success does not set the icon, so it goes back to its default");
        }

        [Test]
        public void DefaultPutsBackEverything()
        {
            var (set, background, icon) = Toast();
            set.Select("Type", "Error", apply: true);
            set.Select("Type", "", apply: true);

            Assert.AreEqual(Color.white, background.color);
            Assert.IsFalse(icon.activeSelf);
        }

        [Test]
        public void ALaterGroupWinsWhereBothSetAValue()
        {
            var (set, background, icon) = Toast();
            set.Select("Type", "Error", apply: true);
            set.Select("Size", "Compact", apply: true);

            Assert.AreEqual(Color.gray, background.color);
            Assert.IsTrue(icon.activeSelf, "Compact does not set the icon, so Error's value stands");

            set.Select("Size", "", apply: true);
            Assert.AreEqual(Color.red, background.color);
        }

        [Test]
        public void SetFindsTheGroupByTheVariantsName()
        {
            var (set, _, _) = Toast();
            set.Set("Compact");
            Assert.AreEqual("Compact", set.Get("Size"));
        }

        [Test]
        public void AccessorsGoThroughTheMatchingProperty()
        {
            var gameObject = Make("Thing", null, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            var image = gameObject.GetComponent<Image>();
            var rect = (RectTransform)gameObject.transform;

            VariantAccessor.For(image, "m_Color").Write(image, VariantValue.FromColor(Color.blue));
            Assert.AreEqual(Color.blue, image.color);

            VariantAccessor.For(image, "m_Type").Write(image, VariantValue.FromInt((long)Image.Type.Filled));
            Assert.AreEqual(Image.Type.Filled, image.type);

            // Unity's own components keep their data natively; their properties are found by name.
            VariantAccessor.For(rect, "m_SizeDelta").Write(rect, VariantValue.FromVector(VariantValueKind.Vector2, new Vector2(10f, 20f)));
            Assert.AreEqual(new Vector2(10f, 20f), rect.sizeDelta);

            var group = gameObject.GetComponent<CanvasGroup>();
            VariantAccessor.For(group, "m_Alpha").Write(group, VariantValue.FromFloat(0.25f));
            Assert.AreEqual(0.25f, group.alpha);

            Assert.IsNull(VariantAccessor.For(image, "m_OnCullStateChanged.m_PersistentCalls.m_Calls.Array.data[0]"));
        }

        [Test]
        public void AccessorsReachFieldsInsideStructs()
        {
            var gameObject = Make("Button", null, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var button = gameObject.GetComponent<Button>();
            var accessor = VariantAccessor.For(button, "m_Colors.m_NormalColor");

            Assert.IsNotNull(accessor);
            Assert.AreEqual(VariantValueKind.Color, accessor.Kind);
            accessor.Write(button, VariantValue.FromColor(Color.yellow));
            Assert.AreEqual(Color.yellow, button.colors.normalColor);
            Assert.AreEqual(Color.yellow, accessor.Read(button).ColorValue);
        }
    }
}
