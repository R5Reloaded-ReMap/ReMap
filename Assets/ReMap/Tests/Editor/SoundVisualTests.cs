using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace ReMap.Standalone.Tests
{
    public sealed class SoundVisualTests
    {
        private static WorldView CreateWorld() => new WorldView(
            Shader.Find("Universal Render Pipeline/Lit"),
            Shader.Find("ReMap/WorkspaceGrid"),
            Shader.Find("Universal Render Pipeline/Unlit"));

        [Test]
        public void SoundRadiusUsesApexScaleAndOnlyDrawsWireframe()
        {
            var world = CreateWorld();
            try
            {
                var sound = new MapObject {
                    assetId = "custom:sound", displayName = "Sound",
                    isGroup = true, customType = "sound", soundRadius = 1200f
                };
                var document = new MapDocument();
                document.objects.Add(sound);
                world.Sync(document, null);

                var instancesField = typeof(WorldView).GetField("instances",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var instances = (Dictionary<string, GameObject>)
                    instancesField.GetValue(world);
                var range = instances[sound.id].transform
                    .Find("__remap_sound_marker").gameObject;
                var renderers = range.GetComponentsInChildren<Renderer>(true);

                Assert.That(renderers.Length, Is.EqualTo(3));
                Assert.That(renderers.All(renderer => renderer is LineRenderer),
                    Is.True);
                Assert.That(range.GetComponent<MeshRenderer>(), Is.Null);
                var selection = range.GetComponent<SphereCollider>();
                Assert.That(selection, Is.Not.Null);
                Assert.That(selection.isTrigger, Is.True);
                Assert.That(selection.radius, Is.LessThan(1f));

                var horizontal = range.transform.Find("horizontal")
                    .GetComponent<LineRenderer>();
                Assert.That(horizontal.positionCount, Is.EqualTo(49));
                Assert.That(horizontal.GetPosition(0).x, Is.EqualTo(
                    1200f * ApexCoordinates.MetersPerUnit).Within(.0001f));
            }
            finally { Dispose(world); }
        }

        [Test]
        public void SoundRadiusUpdatesWithoutCreatingAnOpaqueVolume()
        {
            var world = CreateWorld();
            try
            {
                var sound = new MapObject {
                    assetId = "custom:sound", displayName = "Sound",
                    isGroup = true, customType = "sound", soundRadius = 100f
                };
                var document = new MapDocument();
                document.objects.Add(sound);
                world.Sync(document, null);
                sound.soundRadius = 800f;
                world.Sync(document, null);

                var instancesField = typeof(WorldView).GetField("instances",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var instances = (Dictionary<string, GameObject>)
                    instancesField.GetValue(world);
                var range = instances[sound.id].transform
                    .Find("__remap_sound_marker").gameObject;
                var horizontal = range.transform.Find("horizontal")
                    .GetComponent<LineRenderer>();
                Assert.That(horizontal.GetPosition(0).x, Is.EqualTo(
                    800f * ApexCoordinates.MetersPerUnit).Within(.0001f));
                Assert.That(range.GetComponentsInChildren<MeshRenderer>(true),
                    Is.Empty);
            }
            finally { Dispose(world); }
        }

        [Test]
        public void SoundPlacementPreviewIsWireframeAndHasNoCollider()
        {
            var world = CreateWorld();
            try
            {
                var entry = new CatalogEntry("custom:sound", "Sound", "Custom",
                    Vector3.one) { CustomType = "sound" };
                world.Preview(entry, Vector3.zero);
                var ghostField = typeof(WorldView).GetField("ghost",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var ghost = (GameObject)ghostField.GetValue(world);
                Assert.That(ghost.GetComponentsInChildren<LineRenderer>(true).Length,
                    Is.EqualTo(3));
                Assert.That(ghost.GetComponentsInChildren<MeshRenderer>(true),
                    Is.Empty);
                Assert.That(ghost.GetComponentsInChildren<Collider>(true),
                    Is.Empty);
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
