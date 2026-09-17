using System;
using System.Linq;
using NUnit.Framework;
using ReMap.Standalone.Core;
using UnityEngine;
namespace ReMap.Standalone.Tests
{
    public sealed class ConstructionTests
    {
        [Test] public void ClonedBranchKeepsHierarchyDisabledFlagsAndIndependentMetadata()
        {
            var doc = new MapDocument();
            var parent = new MapObject { isGroup = true };
            var group = new MapObject { isGroup = true, parentId = parent.id };
            var nested = new MapObject { isGroup = true, parentId = group.id };
            var child = new MapObject { parentId = nested.id, disabled = true, gameModelPath = "mdl/test.rmdl", commonAsset = true, rotation = new Float3(10,20,30), scale = new Float3(2,3,4) };
            child.availableMaps.Add("test"); doc.objects.AddRange(new[] { parent, group, nested, child });
            string id = ConstructionTools.CloneBranch(doc, group.id, parent.id, new Vector3(4,5,6)); doc.Validate();
            var ids = MapHierarchy.Subtree(doc, id); Assert.That(ids.Count, Is.EqualTo(3));
            var clone = doc.objects.Single(o => ids.Contains(o.id) && !o.isGroup);
            Assert.That(clone.disabled, Is.True); Assert.That(clone.gameModelPath, Is.EqualTo(child.gameModelPath));
            Assert.That(clone.rotation.y, Is.EqualTo(20)); Assert.That(clone.scale.z, Is.EqualTo(4));
            clone.availableMaps.Clear(); Assert.That(child.availableMaps.Count, Is.EqualTo(1));
            Assert.That(doc.objects.Single(o => o.id == id).position.y, Is.EqualTo(5));
        }
        [Test] public void RecursiveTargetsSkipDisabledSubtreesAndDoNotTransformParentsTwice()
        {
            var doc = new MapDocument(); var group = new MapObject { isGroup = true };
            var nested = new MapObject { isGroup = true, parentId = group.id };
            var leaf = new MapObject { parentId = nested.id }; var hidden = new MapObject { parentId = group.id, disabled = true };
            doc.objects.AddRange(new[] { group, nested, leaf, hidden });
            Assert.That(ConstructionTools.Targets(doc, group.id, true), Is.EqualTo(new[] { leaf.id }));
            Assert.That(ConstructionTools.Targets(doc, group.id, false), Is.EqualTo(new[] { group.id }));
            nested.disabled = true; Assert.That(ConstructionTools.Targets(doc, group.id, true), Is.Empty);
            Assert.Throws<ArgumentException>(() => ConstructionTools.Targets(doc, hidden.id, false));
        }
        [Test] public void LimitsRejectOversizedOperationsBeforeEditing()
        {
            var doc = new MapDocument(); doc.objects.Add(new MapObject());
            Assert.Throws<ArgumentException>(() => ConstructionTools.Budget(doc, 1001));
            Assert.Throws<ArgumentException>(() => ConstructionTools.Grid(doc, null, doc.objects[0].id, 100, 100, Vector3.right, Vector3.forward));
            Assert.That(doc.objects.Count, Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => ConstructionTools.Grid(doc, null, doc.objects[0].id, 2, 2, Vector3.zero, Vector3.forward));
            Assert.Throws<ArgumentException>(() => ConstructionTools.Range(float.NaN, 0, 10, "test"));
        }
        [Test] public void FolderPivotTargetsUseBottomCenterCenterAndTopCenter()
        {
            var bounds = new Bounds(new Vector3(4, 5, 6), new Vector3(8, 10, 12));
            Assert.That(ConstructionTools.PivotTarget(bounds, GroupPivotAnchor.BottomCenter), Is.EqualTo(new Vector3(4, 0, 6)));
            Assert.That(ConstructionTools.PivotTarget(bounds, GroupPivotAnchor.Center), Is.EqualTo(new Vector3(4, 5, 6)));
            Assert.That(ConstructionTools.PivotTarget(bounds, GroupPivotAnchor.TopCenter), Is.EqualTo(new Vector3(4, 10, 6)));
        }
        [Test] public void FolderPivotReferencesKeepOnlySelectedDescendants()
        {
            var doc = new MapDocument();
            var folder = new MapObject { isGroup = true };
            var nested = new MapObject { isGroup = true, parentId = folder.id };
            var first = new MapObject { parentId = folder.id };
            var second = new MapObject { parentId = nested.id };
            var outside = new MapObject();
            doc.objects.AddRange(new[] { folder, nested, first, second, outside });
            Assert.That(ConstructionTools.PivotReferences(doc, folder.id, new[] { first.id, second.id, outside.id }), Is.EqualTo(new[] { first.id, second.id }));
        }
        [Test] public void ConstructionBoundsIgnoreZiplineCableAndEndpointMarker()
        {
            var model = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var cable = new GameObject("cable");
            try
            {
                marker.name = "__remap_zipline_endpoint";
                var line = cable.AddComponent<LineRenderer>();

                Assert.That(WorldView.IsConstructionGeometryRenderer(model.GetComponent<Renderer>()), Is.True);
                Assert.That(WorldView.IsConstructionGeometryRenderer(marker.GetComponent<Renderer>()), Is.False);
                Assert.That(WorldView.IsConstructionGeometryRenderer(line), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(model);
                UnityEngine.Object.DestroyImmediate(marker);
                UnityEngine.Object.DestroyImmediate(cable);
            }
        }

        [Test] public void BranchCopyIsOneUndoAndRoundTripsThroughMapSave()
        {
            var session = new MapSession(); var source = new MapObject { isGroup = true }; var leaf = new MapObject { parentId = source.id };
            session.Edit(d => d.objects.AddRange(new[] { source, leaf }));
            session.Edit(d => ConstructionTools.CloneBranch(d, source.id, "", Vector3.one));
            var codec = new UnityMapCodec(); string encoded = codec.Encode(session.Snapshot());
            var restored = codec.Decode(encoded); restored.Validate(); Assert.That(restored.objects.Count, Is.EqualTo(4));
            session.Undo(); Assert.That(session.Snapshot().objects.Count, Is.EqualTo(2));
            session.Redo(); Assert.That(codec.Encode(session.Snapshot()), Is.EqualTo(encoded));
        }
    }
}
