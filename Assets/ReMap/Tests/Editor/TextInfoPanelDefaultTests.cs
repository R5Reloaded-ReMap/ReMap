using NUnit.Framework;
using ReMap.Standalone.Core;
using System.Reflection;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class TextInfoPanelDefaultTests
    {
        [Test]
        public void NewTextInfoPanelsHideThePinByDefault()
        {
            Assert.That(new MapObject().textInfoPanelShowPin, Is.False);

            MethodInfo factory = typeof(ReMapApp).GetMethod("CreateTextInfoPanel",
                BindingFlags.NonPublic | BindingFlags.Static);
            var panel = (MapObject)factory.Invoke(null, new object[] { Vector3.zero, "" });

            Assert.That(panel.textInfoPanelShowPin, Is.False);
        }
    }
}
