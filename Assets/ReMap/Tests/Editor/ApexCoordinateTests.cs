using System;
using NUnit.Framework;
using ReMap.Standalone.Core;
using UnityEngine;
namespace ReMap.Standalone.Tests {
    public sealed class ApexCoordinateTests {
        [Test] public void SourceUnitsAndVerticalAxisRoundTrip() {
            var p=ApexCoordinates.ToUnity(new Float3(256,128,64));
            Assert.That(p.x,Is.EqualTo(6.5024f).Within(.00001f));Assert.That(p.y,Is.EqualTo(1.6256f).Within(.00001f));Assert.That(p.z,Is.EqualTo(3.2512f).Within(.00001f));
            var a=ApexCoordinates.ToApex(p);Assert.That(a.x,Is.EqualTo(256).Within(.001f));Assert.That(a.y,Is.EqualTo(128).Within(.001f));Assert.That(a.z,Is.EqualTo(64).Within(.001f));
            Assert.That(ApexCoordinates.CoordRange,Is.EqualTo(131071));
        }
        [TestCase(0,1)][TestCase(0,-1)][TestCase(1,1)][TestCase(1,-1)][TestCase(2,1)][TestCase(2,-1)]
        public void InclusiveWorldEdgeAndRejectedOutsideAreTransactional(int axis,int sign) {
            var session=new MapSession();var p=Vector3.zero;p[axis]=sign*ApexCoordinates.MaxUnityCoord;
            session.Edit(d=>d.objects.Add(new MapObject{position=WorldView.ToData(p)}));long revision=session.Revision;
            Assert.Throws<ArgumentException>(()=>session.Edit(d=>{var next=p;next[axis]+=sign*ApexCoordinates.MetersPerUnit;d.objects[0].position=WorldView.ToData(next);}));
            Assert.That(session.Revision,Is.EqualTo(revision));Assert.That(WorldView.ToVector(session.Snapshot().objects[0].position),Is.EqualTo(p));
        }
        [Test] public void RotatedScaledNestedParentsUseWorldCoordinates() {
            var group=new MapObject{isGroup=true,position=new Float3(ApexCoordinates.MaxUnityCoord-3,0,0),rotation=new Float3(0,90,0),scale=new Float3(2,3,2)};
            var nested=new MapObject{isGroup=true,parentId=group.id};var child=new MapObject{parentId=nested.id,position=new Float3(0,0,1)};
            var doc=new MapDocument();doc.objects.AddRange(new[]{group,nested,child});Assert.DoesNotThrow(doc.Validate);
            child.position.z=2;Assert.Throws<ArgumentException>(doc.Validate);child.disabled=true;Assert.Throws<ArgumentException>(doc.Validate);
        }
        [Test] public void SceneOriginOffsetUsesTheFinalGameWorldLimits() {
            var doc=new MapDocument {originOffset=ApexCoordinates.ToUnity(new Float3(ApexCoordinates.MaxWorldCoord,0,0))};
            doc.objects.Add(new MapObject());Assert.DoesNotThrow(doc.Validate);
            doc.objects[0].position=ApexCoordinates.ToUnity(new Float3(1,0,0));Assert.Throws<ArgumentException>(doc.Validate);
            doc.objects[0].position=new Float3();doc.originOffset.x=float.NaN;Assert.Throws<ArgumentException>(doc.Validate);
        }
        [Test] public void DisplayedGameWorldPositionIncludesSceneOriginOffset() {
            var editorWorld=ApexDisplay.UnityPosition(new Vector3(12,-34,56));
            var originOffset=WorldView.ToData(ApexDisplay.UnityPosition(new Vector3(1000,2000,-3000)));
            Assert.That(Vector3.Distance(ApexDisplay.GamePosition(editorWorld,originOffset),new Vector3(1012,1966,-2944)),Is.LessThan(.001f));
        }
        [Test] public void WorldMatrixMatchesUnityForMixedRotationsAndNonUniformScales() {
            var root=new GameObject("Coordinate test");var child=new GameObject("Child");child.transform.SetParent(root.transform,false);
            try {
                root.transform.SetPositionAndRotation(new Vector3(100,200,-300),Quaternion.Euler(23,71,-19));root.transform.localScale=new Vector3(2,3,.5f);
                child.transform.localPosition=new Vector3(8,19,-3);var expected=child.transform.position;
                var parent=new MapObject{isGroup=true,position=WorldView.ToData(root.transform.position),rotation=new Float3(23,71,-19),scale=new Float3(2,3,.5f)};
                var item=new MapObject{parentId=parent.id,position=WorldView.ToData(child.transform.localPosition)};var doc=new MapDocument();doc.objects.AddRange(new[]{parent,item});
                parent.position.x+=ApexCoordinates.MaxUnityCoord-expected.x-.02f;Assert.DoesNotThrow(doc.Validate);
                parent.position.x+=.04f;Assert.Throws<ArgumentException>(doc.Validate);
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [TestCase(0,0,0)][TestCase(10,30,20)][TestCase(-30,120,-45)][TestCase(90,40,0)][TestCase(-90,-20,35)]
        public void SourceAnglesRoundTripAsOrientation(float pitch,float yaw,float roll) {
            var a=new Vector3(pitch,yaw,roll);var u=ApexDisplay.UnityAngles(a);var round=ApexDisplay.UnityAngles(ApexDisplay.Angles(u));
            Assert.That(Quaternion.Angle(Quaternion.Euler(u),Quaternion.Euler(round)),Is.LessThan(.08f));
        }
        [Test] public void PositiveSourceYawTurnsSourceXIntoSourceY() {
            var rotation=Quaternion.Euler(ApexDisplay.UnityAngles(new Vector3(0,90,0)));
            Assert.That(Vector3.Distance(rotation*Vector3.right,Vector3.forward),Is.LessThan(.0001f));
        }
        [Test] public void KnownModelOrientationCorrectionMatchesApexAndLeavesOtherModelsUntouched() {
            Assert.That(System.IO.File.Exists(ApexModelOrientation.MetadataPath),Is.True);
            var expected=Quaternion.Euler(ApexDisplay.UnityAngles(new Vector3(0,0,-90)));
            var cast=ApexModelOrientation.VisualCorrection(@"cache\charge_pylon_01_cells_LOD0.cast");
            var model=ApexModelOrientation.VisualCorrection(@"mdl\props\charge_pylon\charge_pylon_01_cells.rmdl");
            Assert.That(Quaternion.Angle(cast,expected),Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(model,expected),Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(ApexModelOrientation.VisualCorrection("ordinary_LOD0.cast"),Quaternion.identity),Is.LessThan(.001f));
        }
        [Test] public void EditableModelOrientationMetadataAcceptsPathsAndRejectsInvalidEntries() {
            const string json="{\"corrections\":["+
                "{\"model\":\"mdl/props/custom/custom_model.rmdl\",\"pitch\":10,\"yaw\":20,\"roll\":30},"+
                "{\"model\":\"\",\"pitch\":1,\"yaw\":2,\"roll\":3}]}";
            var corrections=ApexModelOrientation.ReadCorrections(json);
            Assert.That(corrections.Count,Is.EqualTo(1));
            Assert.That(corrections.ContainsKey("custom_model"),Is.True);
            Assert.That(corrections["custom_model"],Is.EqualTo(new Vector3(10,20,30)));
        }
        [Test] public void OldSavePreservesPositionRotationAndModelScale() {
            var doc=new MapDocument();var item=new MapObject{position=new Float3(3,2,1),rotation=new Float3(20,30,40)};doc.objects.Add(item);
            var codec=new UnityMapCodec();var restored=codec.Decode(codec.Encode(doc));restored.Validate();
            Assert.That(restored.coordinateSystem,Is.EqualTo("unity-y-up-meters"));Assert.That(restored.objects[0].position.x,Is.EqualTo(3));Assert.That(restored.objects[0].scale.x,Is.EqualTo(1));
        }
    }
}
