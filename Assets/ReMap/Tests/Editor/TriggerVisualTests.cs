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

        [TestCase("button", "button-teleport-target", "__remap_button_teleport_target_marker")]
        [TestCase("trigger", "trigger-teleport-target", "__remap_trigger_teleport_target_marker")]
        public void TeleportTargetsShowPlayerFacingDirection(string sourceType, string targetType, string markerName)
        {
            var world = CreateWorld();
            try
            {
                var source = new MapObject {
                    assetId = "custom:" + sourceType, displayName = "Source", customType = sourceType,
                    isGroup = true, buttonTeleportEnabled = true, triggerTeleportEnabled = true
                };
                var target = new MapObject {
                    assetId = "custom:" + targetType, displayName = "Destination", customType = targetType,
                    customRole = "destination", parentId = source.id, isGroup = true,
                    rotation = WorldView.ToData(new Vector3(0f, 35f, 0f))
                };
                var document = new MapDocument();
                document.objects.Add(source); document.objects.Add(target);
                world.Sync(document, null);

                var instancesField = typeof(WorldView).GetField("instances", BindingFlags.Instance | BindingFlags.NonPublic);
                var instances = (Dictionary<string, GameObject>)instancesField.GetValue(world);
                GameObject targetInstance = instances[target.id];
                var arrow = targetInstance.transform.Find(markerName + "/direction").GetComponent<LineRenderer>();
                Vector3 direction = arrow.GetPosition(1) - arrow.GetPosition(0);

                Assert.That(arrow.positionCount, Is.EqualTo(5));
                Assert.That(direction.magnitude, Is.EqualTo(64f * ApexCoordinates.MetersPerUnit).Within(.0001f));
                Assert.That(Vector3.Dot(direction.normalized, targetInstance.transform.right), Is.GreaterThan(.999f));
            }
            finally { Dispose(world); }
        }

        [TestCase("button", "button-teleport-target")]
        [TestCase("trigger", "trigger-teleport-target")]
        public void LockedTeleportTargetStaysAtItsWorldPositionWhenOwnerMoves(string sourceType, string targetType)
        {
            var world = CreateWorld();
            try
            {
                var source = new MapObject {
                    assetId = "custom:" + sourceType, displayName = "Source", customType = sourceType,
                    isGroup = true, buttonTeleportEnabled = true, triggerTeleportEnabled = true
                };
                var target = new MapObject {
                    assetId = "custom:" + targetType, displayName = "Destination", customType = targetType,
                    customRole = "destination", parentId = source.id, isGroup = true, positionLocked = true,
                    position = new Float3(2f, 0f, 0f)
                };
                var document = new MapDocument(); document.objects.Add(source); document.objects.Add(target);
                world.Sync(document, null);
                var lockedIdsMethod = typeof(ReMapApp).GetMethod("LockedPositionIds",
                    BindingFlags.Static | BindingFlags.NonPublic);
                var lockedIds = (string[])lockedIdsMethod.Invoke(null, new object[] { document, new[] { source.id } });
                CollectionAssert.AreEqual(new[] { target.id }, lockedIds);
                var originals = world.CaptureSelection(new[] { source.id });
                var lockedPositions = world.CaptureSelection(lockedIds);
                Vector3 before = WorldView.ToVector(world.WorldPose(target.id).position);

                Assert.That(world.PreviewSelection(originals, Vector3.zero, Quaternion.identity,
                    Vector3.right * 3f, Quaternion.identity, Vector3.one, lockedPositions), Is.True);
                Assert.That(Vector3.Distance(WorldView.ToVector(world.WorldPose(target.id).position), before),
                    Is.LessThan(.0001f));

                var poses = originals.Concat(lockedPositions).ToDictionary(
                    original => original.Local.id, original => world.LocalPose(original.Local.id));
                foreach (var item in document.objects) if (poses.TryGetValue(item.id, out var pose))
                { item.position = pose.position; item.rotation = pose.rotation; item.scale = pose.scale; }
                world.Sync(document, null);
                Assert.That(Vector3.Distance(WorldView.ToVector(world.WorldPose(target.id).position), before),
                    Is.LessThan(.0001f));
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
