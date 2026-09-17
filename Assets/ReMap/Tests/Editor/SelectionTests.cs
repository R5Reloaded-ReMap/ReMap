using System;
using System.Linq;
using NUnit.Framework;
using ReMap.Standalone.Core;
namespace ReMap.Standalone.Tests
{
    public sealed class SelectionTests
    {
        [Test] public void RootsRemoveNestedSelectionWithoutDroppingUnrelatedBranches() {
            var a=new MapObject{isGroup=true};var b=new MapObject{isGroup=true,parentId=a.id};var c=new MapObject{parentId=b.id};var d=new MapObject();
            var doc=new MapDocument();doc.objects.AddRange(new[]{c,d,b,a});doc.Validate();
            Assert.That(MapSelection.Roots(doc,new[]{a.id,c.id,b.id,d.id,"missing"}),Is.EquivalentTo(new[]{a.id,d.id}));
            Assert.That(MapSelection.Branches(doc,new[]{a.id}),Is.EquivalentTo(new[]{a.id,b.id,c.id}));
        }
        [Test] public void AssemblyInstancesHaveIndependentIdsParentsAndMetadata() {
            var a=new MapObject{isGroup=true};var b=new MapObject{isGroup=true,parentId=a.id};var c=new MapObject{parentId=b.id,disabled=true,gameModelPath="mdl/test.rmdl",commonAsset=true};c.availableMaps.Add("map-a");
            var assembly=new MapDocument();assembly.objects.AddRange(new[]{a,b,c});var doc=new MapDocument();
            string first=MapAssembly.Insert(doc,assembly,new Float3(3,4,5));string second=MapAssembly.Insert(doc,assembly,new Float3(-3,0,0));doc.Validate();
            Assert.That(doc.objects.Select(o=>o.id).Distinct().Count(),Is.EqualTo(6));Assert.That(MapHierarchy.Subtree(doc,first).Count,Is.EqualTo(3));
            Assert.That(MapHierarchy.Subtree(doc,first).Overlaps(MapHierarchy.Subtree(doc,second)),Is.False);
            var copy=doc.objects.First(o=>!o.isGroup);Assert.That(copy.disabled,Is.True);Assert.That(copy.gameModelPath,Is.EqualTo(c.gameModelPath));Assert.That(copy.commonAsset,Is.True);
            copy.availableMaps.Clear();Assert.That(c.availableMaps,Has.Count.EqualTo(1));Assert.That(doc.objects.Single(o=>o.id==first).position.y,Is.EqualTo(4));
        }
        [Test] public void InvalidAssemblyCannotPartiallyModifyDestination() {
            var doc=new MapDocument();doc.objects.Add(new MapObject());var bad=new MapDocument();bad.objects.Add(new MapObject());
            Assert.Throws<ArgumentException>(()=>MapAssembly.Insert(doc,bad,default));Assert.That(doc.objects,Has.Count.EqualTo(1));
            bad.objects[0].isGroup=true;Assert.Throws<ArgumentException>(()=>MapAssembly.Insert(doc,bad,new Float3(float.NaN,0,0)));Assert.That(doc.objects,Has.Count.EqualTo(1));
        }
        [Test] public void PasteParentUsesFocusedFolderAndOtherwiseRestoresExistingParent() {
            var originalParent=new MapObject();var folder=new MapObject{isGroup=true};var objectRow=new MapObject();var doc=new MapDocument();doc.objects.AddRange(new[]{originalParent,folder,objectRow});
            Assert.That(MapSelection.PasteParent(doc,folder.id,originalParent.id),Is.EqualTo(folder.id));
            Assert.That(MapSelection.PasteParent(doc,objectRow.id,originalParent.id),Is.EqualTo(originalParent.id));
            Assert.That(MapSelection.PasteParent(doc,null,"missing"),Is.Empty);
        }
    }
}
