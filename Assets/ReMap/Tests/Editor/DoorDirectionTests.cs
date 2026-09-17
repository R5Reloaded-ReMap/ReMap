using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace ReMap.Standalone.Tests
{
    public sealed class DoorDirectionTests
    {
        [TestCase("single", true)]
        [TestCase("double", true)]
        [TestCase("vertical", false)]
        [TestCase("horizontal", false)]
        public void SwingDoorsShowTheirOpeningDirection(string type, bool visible)
        {
            var world = new WorldView(Shader.Find("Universal Render Pipeline/Lit"),
                Shader.Find("ReMap/WorkspaceGrid"),
                Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                var door = new MapObject {
                    assetId = "custom:door", displayName = "Door", isGroup = true,
                    customType = "door", doorType = type,
                    rotation = WorldView.ToData(new Vector3(0f, 37f, 0f))
                };
                var document = new MapDocument();
                document.objects.Add(door);
                world.Sync(document, null);

                var field = typeof(WorldView).GetField("instances",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var instances = (Dictionary<string, GameObject>)field.GetValue(world);
                GameObject instance = instances[door.id];
                var arrow = instance.transform.Find("__remap_door_opening_direction")
                    .GetComponent<LineRenderer>();

                Assert.That(arrow.enabled, Is.EqualTo(visible));
                if (!visible) return;
                Vector3 direction = (arrow.GetPosition(1) - arrow.GetPosition(0)).normalized;
                Assert.That(Vector3.Dot(direction, instance.transform.forward),
                    Is.GreaterThan(.999f));
                Assert.That(arrow.positionCount, Is.EqualTo(5));
            }
            finally
            {
                bool previous = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;
                try { world.Dispose(); }
                finally { LogAssert.ignoreFailingMessages = previous; }
            }
        }
    }
}
