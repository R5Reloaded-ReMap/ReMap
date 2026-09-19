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
                var arrow = instance.transform.Find("__remap_door_opening_direction").GetComponent<LineRenderer>();
                var opposite = instance.transform.Find("__remap_door_opening_direction_opposite").GetComponent<LineRenderer>();

                Assert.That(arrow.enabled, Is.EqualTo(visible));
                Assert.That(opposite.enabled, Is.EqualTo(type == "double"));
                if (!visible) return;
                Assert.That(arrow.positionCount, Is.GreaterThan(12));
                Vector3 primaryHinge = instance.transform.position + instance.transform.right * (type == "double" ? 64f : -64f) * ApexCoordinates.MetersPerUnit;
                Vector3 primaryStart = Vector3.ProjectOnPlane(arrow.GetPosition(1) - primaryHinge, instance.transform.up);
                Vector3 primaryEnd = Vector3.ProjectOnPlane(arrow.GetPosition(arrow.positionCount - 4) - primaryHinge, instance.transform.up);
                Assert.That(primaryStart.magnitude, Is.EqualTo(64f * ApexCoordinates.MetersPerUnit).Within(.0001f));
                Assert.That(primaryEnd.magnitude, Is.EqualTo(64f * ApexCoordinates.MetersPerUnit).Within(.0001f));
                Assert.That(Mathf.Abs(Vector3.Dot(primaryStart.normalized, instance.transform.right)), Is.GreaterThan(.999f));
                Assert.That(Vector3.Dot(primaryEnd.normalized, instance.transform.forward), Is.GreaterThan(.999f));
                if (type == "double")
                {
                    Vector3 oppositeHinge = instance.transform.position - instance.transform.right * 64f * ApexCoordinates.MetersPerUnit;
                    Vector3 oppositeStart = Vector3.ProjectOnPlane(opposite.GetPosition(1) - oppositeHinge, instance.transform.up);
                    Assert.That(Vector3.Dot(primaryStart.normalized, oppositeStart.normalized), Is.LessThan(-.999f));
                }
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
