using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ReMap.Standalone.Core;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class ZiplinePointEditingTests
    {
        private static MapObject[] Create(string method)
        {
            var factory = typeof(ReMapApp).GetMethod(method,
                BindingFlags.Static | BindingFlags.NonPublic);
            return (MapObject[])factory.Invoke(null, new object[] { Vector3.zero, "" });
        }

        [TestCase("CreateDefaultCurvedZiplineObjects")]
        [TestCase("CreateDefaultZiprailObjects")]
        public void FirstControlPointIsVisibleAndSelectable(string factory)
        {
            var first = Create(factory).Single(item => item.customRole == "0");
            var hidden = typeof(ReMapApp).GetMethod("HiddenHierarchyObject",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That((bool)hidden.Invoke(null, new object[] { first }), Is.False);
            Assert.That(first.customProfile, Is.Not.Null);
        }

        [Test] public void PointPositionLockSurvivesCopyAndSerialization()
        {
            var point = Create("CreateDefaultZiprailObjects")
                .Single(item => item.customRole == "1");
            point.positionLocked = true;
            Assert.That(point.Copy().positionLocked, Is.True);

            var document = new MapDocument();
            document.objects.Add(point);
            var restored = JsonUtility.FromJson<MapDocument>(JsonUtility.ToJson(document));
            Assert.That(restored.objects.Single().positionLocked, Is.True);
        }
    }
}
