using System;
using System.Linq;
using NUnit.Framework;
using ReMap.Standalone.Core;
namespace ReMap.Tests
{
    public sealed class HierarchyTests
    {
        [Test] public void DisabledAncestorExcludesDescendantsButSaveKeepsThem()
        {
            var group = new MapObject { isGroup = true, disabled = true }; var child = new MapObject { parentId = group.id };
            var doc = new MapDocument(); doc.objects.Add(group); doc.objects.Add(child);
            Assert.That(MapHierarchy.GenerationObjects(doc), Is.Empty);
            Assert.That(doc.Copy().objects.Count, Is.EqualTo(2));
            group.disabled = false; Assert.That(MapHierarchy.GenerationObjects(doc).Single().id, Is.EqualTo(child.id));
            child.disabled = true; Assert.That(MapHierarchy.GenerationObjects(doc), Is.Empty);
        }
        [Test] public void ReparentRejectsCyclesAndRestoresPreviousParent()
        {
            var a = new MapObject { isGroup = true }; var b = new MapObject { isGroup = true, parentId = a.id };
            var doc = new MapDocument(); doc.objects.Add(a); doc.objects.Add(b);
            Assert.Throws<ArgumentException>(() => MapHierarchy.Reparent(doc, a.id, b.id)); Assert.That(a.parentId, Is.Empty); doc.Validate();
        }
        [Test] public void SelectionContainersKeepOnlySelectedBranchesOpen()
        {
            var left=new MapObject{isGroup=true};var nested=new MapObject{isGroup=true,parentId=left.id};var leaf=new MapObject{parentId=nested.id};
            var right=new MapObject{isGroup=true};var other=new MapObject{parentId=right.id};var doc=new MapDocument();doc.objects.AddRange(new[]{left,nested,leaf,right,other});
            Assert.That(MapHierarchy.SelectionContainers(doc,new[]{leaf.id}),Is.EquivalentTo(new[]{left.id,nested.id}));
            Assert.That(MapHierarchy.SelectionContainers(doc,new[]{nested.id}),Is.EquivalentTo(new[]{left.id,nested.id}));
            Assert.That(MapHierarchy.SelectionContainers(doc,new[]{leaf.id,other.id}),Is.EquivalentTo(new[]{left.id,nested.id,right.id}));
            Assert.That(MapHierarchy.SelectionContainers(doc,Array.Empty<string>()),Is.Empty);
        }
        [Test] public void DuplicateRemapsNestedParentsAndCopiesEnableState()
        {
            var a = new MapObject { isGroup = true }; var b = new MapObject { isGroup = true, parentId = a.id }; var c = new MapObject { parentId = b.id, disabled = true };
            var doc = new MapDocument(); doc.objects.AddRange(new[] { a, b, c });
            string copy = MapHierarchy.Duplicate(doc, a.id); doc.Validate();
            Assert.That(doc.objects.Count, Is.EqualTo(6));
            var copied = doc.objects.Where(o => MapHierarchy.Subtree(doc, copy).Contains(o.id)).ToArray();
            Assert.That(copied.Length, Is.EqualTo(3)); Assert.That(copied.Single(o => !o.isGroup).disabled, Is.True);
            Assert.That(copied.Single(o => !o.isGroup).parentId, Is.Not.EqualTo(b.id));
        }
        [Test] public void DuplicateUsesIncrementingNumericSuffixes()
        {
            var original = new MapObject { displayName = "Crate" }; var doc = new MapDocument(); doc.objects.Add(original);
            string first = MapHierarchy.Duplicate(doc, original.id);
            string second = MapHierarchy.Duplicate(doc, original.id);
            string third = MapHierarchy.Duplicate(doc, first);
            Assert.That(doc.objects.Single(item => item.id == first).displayName, Is.EqualTo("Crate_01"));
            Assert.That(doc.objects.Single(item => item.id == second).displayName, Is.EqualTo("Crate_02"));
            Assert.That(doc.objects.Single(item => item.id == third).displayName, Is.EqualTo("Crate_03"));
        }
        [Test] public void DuplicatePreservesAnOriginalNumericSuffix()
        {
            var original = new MapObject { displayName = "Wall_01" }; var doc = new MapDocument(); doc.objects.Add(original);
            string copy = MapHierarchy.Duplicate(doc, original.id);
            Assert.That(doc.objects.Single(item => item.id == copy).displayName, Is.EqualTo("Wall_01_01"));
        }
        [Test] public void ModelsCanContainChildrenAndRemainGenerated()
        {
            var a = new MapObject(); var b = new MapObject { parentId = a.id }; var doc = new MapDocument(); doc.objects.Add(a);
            Assert.That(MapHierarchy.GenerationObjects(doc).Count(), Is.EqualTo(1)); doc.objects.Add(b); doc.Validate();
            Assert.That(MapHierarchy.GenerationObjects(doc).Select(o => o.id), Is.EquivalentTo(new[] { a.id, b.id }));
            a.disabled = true; Assert.That(MapHierarchy.GenerationObjects(doc), Is.Empty);
        }
        [Test] public void ReorderMovesBetweenSiblingsAndCanReparent()
        {
            var folder = new MapObject { isGroup = true }; var a = new MapObject(); var b = new MapObject(); var c = new MapObject();
            var doc = new MapDocument(); doc.objects.AddRange(new[] { folder, a, b, c });
            MapHierarchy.Reorder(doc, c.id, "", a.id);
            Assert.That(doc.objects.Where(o => o.parentId == "").Select(o => o.id), Is.EqualTo(new[] { folder.id, c.id, a.id, b.id }));
            MapHierarchy.Reorder(doc, a.id, folder.id);
            Assert.That(a.parentId, Is.EqualTo(folder.id));
            Assert.That(doc.objects.Where(o => o.parentId == folder.id).Single().id, Is.EqualTo(a.id));
            doc.Validate();
        }
        [Test] public void ReorderRejectsCyclesAndRestoresOrderAndParent()
        {
            var parent = new MapObject { isGroup = true }; var child = new MapObject { parentId = parent.id };
            var sibling = new MapObject(); var doc = new MapDocument(); doc.objects.AddRange(new[] { parent, child, sibling });
            Assert.Throws<ArgumentException>(() => MapHierarchy.Reorder(doc, parent.id, child.id));
            Assert.That(parent.parentId, Is.Empty);
            Assert.That(doc.objects.Select(o => o.id), Is.EqualTo(new[] { parent.id, child.id, sibling.id }));
        }
    }
}
