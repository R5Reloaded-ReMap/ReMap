using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace ReMap.Standalone.Tests
{
    public sealed class TriggerVisualTests
    {
        private static WorldView CreateWorld() => new WorldView(
            Shader.Find("Universal Render Pipeline/Lit"),
            Shader.Find("ReMap/WorkspaceGrid"),
            Shader.Find("Universal Render Pipeline/Unlit"));

        [Test]
        public void LargeTriggerUsesClickableWireframeWithoutOpaqueSurface()
        {
            var world = CreateWorld();
            try
            {
                var trigger = new MapObject {
                    assetId = "custom:trigger", displayName = "Large trigger",
                    isGroup = true, customType = "trigger",
                    triggerRadius = 2000f, triggerHalfHeight = 1000f
                };
                var document = new MapDocument();
                document.objects.Add(trigger);
                world.Sync(document, null);

                var instancesField = typeof(WorldView).GetField("instances",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var instances = (Dictionary<string, GameObject>)
                    instancesField.GetValue(world);
                var volume = instances[trigger.id].transform
                    .Find("__remap_trigger_volume").gameObject;
                var renderers = volume.GetComponentsInChildren<Renderer>(true);

                Assert.That(renderers.Length, Is.EqualTo(10));
                Assert.That(renderers.All(renderer => renderer is LineRenderer),
                    Is.True);
                Assert.That(volume.GetComponent<MeshRenderer>(), Is.Null);
                var selection = volume.GetComponent<SphereCollider>();
                Assert.That(selection, Is.Not.Null);
                Assert.That(selection.enabled && selection.isTrigger, Is.True);

                var top = volume.transform.Find("top").GetComponent<LineRenderer>();
                Assert.That(top.positionCount, Is.EqualTo(41));
                Assert.That(top.GetPosition(0).y, Is.EqualTo(
                    1000f * ApexCoordinates.MetersPerUnit).Within(.0001f));
                Assert.That(top.GetPosition(0).x, Is.EqualTo(
                    2000f * ApexCoordinates.MetersPerUnit).Within(.0001f));
            }
            finally { Dispose(world); }
        }

        [Test]
        public void TriggerPlacementPreviewIsAlsoWireframe()
        {
            var world = CreateWorld();
            try
            {
                var entry = new CatalogEntry("custom:trigger", "Trigger", "Custom",
                    new Vector3(5.08f, 2.54f, 5.08f)) {
                    CustomType = "trigger"
                };
                world.Preview(entry, Vector3.zero);
                var ghostField = typeof(WorldView).GetField("ghost",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var ghost = (GameObject)ghostField.GetValue(world);
                Assert.That(ghost.GetComponentsInChildren<LineRenderer>(true).Length,
                    Is.EqualTo(10));
                Assert.That(ghost.GetComponentsInChildren<MeshRenderer>(true).Length,
                    Is.EqualTo(0));
                Assert.That(ghost.GetComponentsInChildren<Collider>(true).Length,
                    Is.EqualTo(0));
            }
            finally { Dispose(world); }
        }

        private static void Dispose(WorldView world)
        {
            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try { world.Dispose(); }
            finally { LogAssert.ignoreFailingMessages = previous; }
        }
    }
}
