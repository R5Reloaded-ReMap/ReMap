using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.TestTools;

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

        [Test] public void InsertedPointUsesTheFollowingSpanMidpoint()
        {
            var points = Create("CreateDefaultZiprailObjects")
                .Where(item => item.customType == "ziprail-point").ToArray();
            var method = typeof(ReMapApp).GetMethod("InsertedControlPointPosition",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Vector3 position = (Vector3)method.Invoke(null, new object[] { points, 0 });
            Assert.That(position, Is.EqualTo(Vector3.Lerp(
                WorldView.ToVector(points[0].position), WorldView.ToVector(points[1].position), .5f)));
        }

        [Test] public void InsertedPointAfterTheEndContinuesTheLastSpan()
        {
            var points = Create("CreateDefaultCurvedZiplineObjects")
                .Where(item => item.customType == "curved-zipline-point").ToArray();
            var method = typeof(ReMapApp).GetMethod("InsertedControlPointPosition",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Vector3 last = WorldView.ToVector(points[2].position);
            Vector3 previous = WorldView.ToVector(points[1].position);
            Vector3 position = (Vector3)method.Invoke(null, new object[] { points, 2 });
            Assert.That(position, Is.EqualTo(last + last - previous));
        }

        [Test] public void ZiprailPreviewCreatesTriggerHitboxesForPointsAndCable()
        {
            var world = new WorldView(Shader.Find("Universal Render Pipeline/Lit"),
                Shader.Find("ReMap/WorkspaceGrid"),
                Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                var objects = Create("CreateDefaultZiprailObjects");
                var document = new MapDocument { gameTarget = GameTargets.R5Flowstate };
                document.objects.AddRange(objects);
                world.Sync(document, null);
                Physics.SyncTransforms();

                var field = typeof(WorldView).GetField("instances",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var instances = (Dictionary<string, GameObject>)field.GetValue(world);
                var point = objects.Single(item => item.customType == "ziprail-point" &&
                    item.customRole == "1");
                var marker = instances[point.id].transform.Find("__remap_zipline_endpoint");
                var markerCollider = marker.GetComponent<SphereCollider>();
                Assert.That(markerCollider.enabled && markerCollider.isTrigger, Is.True);

                var ziprail = objects.Single(item => item.customType == "ziprail");
                var cable = instances[ziprail.id].transform.Find("__remap_zipline_cable_selection");
                Assert.That(cable, Is.Not.Null);
                Assert.That(cable.GetComponentsInChildren<CapsuleCollider>()
                    .All(collider => collider.enabled && collider.isTrigger), Is.True);
                Assert.That(cable.GetComponentsInChildren<CapsuleCollider>().Length, Is.GreaterThan(0));
            }
            finally
            {
                bool previous = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;
                try { world.Dispose(); }
                finally { LogAssert.ignoreFailingMessages = previous; }
            }
        }

        [TestCase("support", "0", 0f, 0f, -320f)]
        [TestCase("arm", "2", 0f, 0f, 0f)]
        [TestCase("building-claw-02", "1", 0f, 0f, 0f)]
        [TestCase("wall", "1", -1024f, 0f, 0f)]
        [TestCase("none", "1", 0f, 0f, 0f)]
        public void ZiprailGizmoUsesTheVisualMountBase(string profile, string role,
            float apexX, float apexY, float apexZ)
        {
            var world = new WorldView(Shader.Find("Universal Render Pipeline/Lit"),
                Shader.Find("ReMap/WorkspaceGrid"),
                Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                var objects = Create("CreateDefaultZiprailObjects");
                var point = objects.Single(item => item.customType == "ziprail-point" &&
                    item.customRole == role);
                point.customProfile = profile;
                var document = new MapDocument { gameTarget = GameTargets.R5Flowstate };
                document.objects.AddRange(objects);
                world.Sync(document, null);

                var field = typeof(WorldView).GetField("instances",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var instances = (Dictionary<string, GameObject>)field.GetValue(world);
                Vector3 expected = instances[point.id].transform.TransformPoint(
                    ApexDisplay.UnityPosition(new Vector3(apexX, apexY, apexZ)));

                Assert.That(Vector3.Distance(world.ZiprailGizmoPivot(point.id), expected),
                    Is.LessThan(.0001f));
                if (role == "0")
                {
                    var ziprail = objects.Single(item => item.customType == "ziprail");
                    Assert.That(Vector3.Distance(world.ZiprailGizmoPivot(ziprail.id), expected),
                        Is.LessThan(.0001f));
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

        [TestCase("curved-zipline-component", "support", true)]
        [TestCase("curved-zipline-component", "support-post", true)]
        [TestCase("curved-zipline-component", "arm", false)]
        [TestCase("ziprail-component", "arm", true)]
        [TestCase("ziprail-component", "wall", true)]
        [TestCase("ziprail-component", "ground-claw", true)]
        [TestCase("ziprail-component", "cord-end", false)]
        public void SupportModelsKeepSolidColliders(string customType, string role, bool solid)
        {
            var instance = new GameObject("component");
            try
            {
                var physical = instance.AddComponent<BoxCollider>();
                physical.isTrigger = true;
                var selection = new GameObject("__remap_zipline_model_selection");
                selection.transform.SetParent(instance.transform, false);
                var selectionCollider = selection.AddComponent<BoxCollider>();
                selectionCollider.isTrigger = true;

                var method = typeof(WorldView).GetMethod("ConfigureZiplineComponentColliders",
                    BindingFlags.Static | BindingFlags.NonPublic);
                method.Invoke(null, new object[] {
                    instance, new MapObject { customType = customType, customRole = role }
                });

                Assert.That(physical.enabled, Is.EqualTo(solid));
                if (solid) Assert.That(physical.isTrigger, Is.False);
                Assert.That(selectionCollider.enabled, Is.True);
                Assert.That(selectionCollider.isTrigger, Is.True);
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}
