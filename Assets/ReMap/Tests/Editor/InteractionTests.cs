using NUnit.Framework;
using UnityEngine;
namespace ReMap.Standalone.Tests
{
    public sealed class InteractionTests
    {
        [Test] public void NormalizedWheelStepsMoveNoticeablyAndRemainReversible() {
            float back=WorldView.WheelDistance(20,-1);
            Assert.That(back,Is.GreaterThan(23));Assert.That(back,Is.LessThan(25));
            Assert.That(WorldView.WheelDistance(back,1),Is.EqualTo(20).Within(.001f));
            Assert.That(WorldView.WheelDistance(20,-1,true),Is.GreaterThan(back));
            Assert.That(WorldView.WheelDistance(.3f,20),Is.EqualTo(.3f));
            Assert.That(WorldView.WheelDistance(1500,-20),Is.EqualTo(1500));
        }
        [Test] public void RetreatWhileLookingDownRisesAndStrafeKeepsHeight()
        {
            var retreat = WorldView.FlyDirection(35, 55, Vector3.back);
            Assert.That(retreat.y, Is.GreaterThan(.7f));
            Assert.That(WorldView.FlyDirection(35, 55, Vector3.right).y, Is.EqualTo(0).Within(.001f));
            Assert.That(WorldView.FlyDirection(35, -55, Vector3.forward).y, Is.GreaterThan(.7f));
        }
        [Test] public void FrameSelectionFitsLargeBoundsWithoutTripleDiagonalRetreat()
        {
            var bounds=new Bounds(Vector3.zero,new Vector3(100,20,100));
            var rotation=Quaternion.Euler(35,35,0);
            float distance=WorldView.FrameDistance(bounds,rotation,50,16f/9);
            Assert.That(distance,Is.LessThan(bounds.size.magnitude*1.5f));
            Assert.That(distance,Is.GreaterThan(bounds.extents.magnitude));
            AssertCornersFit(bounds,rotation,50,16f/9,distance);
        }
        [Test] public void FrameSelectionUsesTheViewportAspectRatio()
        {
            var bounds=new Bounds(Vector3.zero,new Vector3(100,10,10));
            float wide=WorldView.FrameDistance(bounds,Quaternion.identity,50,16f/9);
            float square=WorldView.FrameDistance(bounds,Quaternion.identity,50,1);
            Assert.That(wide,Is.LessThan(square));
            AssertCornersFit(bounds,Quaternion.identity,50,16f/9,wide);
        }
        private static void AssertCornersFit(Bounds bounds,Quaternion rotation,float fov,float aspect,float distance)
        {
            var inverse=Quaternion.Inverse(rotation);float tanV=Mathf.Tan(fov*Mathf.Deg2Rad*.5f),tanH=tanV*aspect;var e=bounds.extents;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2){var p=inverse*Vector3.Scale(e,new Vector3(x,y,z));float depth=distance+p.z;Assert.That(Mathf.Abs(p.x),Is.LessThanOrEqualTo(depth*tanH));Assert.That(Mathf.Abs(p.y),Is.LessThanOrEqualTo(depth*tanV));}
        }
        [Test] public void GizmoHitDistanceClampsToVisibleSegment()
        {
            Assert.That(GizmoOverlay.SegmentDistance(new Vector2(5, 3), Vector2.zero, new Vector2(10, 0)), Is.EqualTo(3));
            Assert.That(GizmoOverlay.SegmentDistance(new Vector2(15, 0), Vector2.zero, new Vector2(10, 0)), Is.EqualTo(5));
            Assert.That(GizmoOverlay.SegmentDistance(Vector2.one, Vector2.zero, Vector2.zero), Is.EqualTo(Mathf.Sqrt(2)).Within(.001f));
        }
        [Test] public void GizmoCenterAndPivotModesUseTheirOwnAnchors()
        {
            var center = new Vector3(10, 20, 30);
            var activePivot = new Vector3(1, 2, 3);
            var customPivot = new Vector3(4, 5, 6);
            Assert.That(GizmoPlacement.Pivot(true, center, activePivot, customPivot), Is.EqualTo(center));
            Assert.That(GizmoPlacement.Pivot(false, center, activePivot), Is.EqualTo(activePivot));
            Assert.That(GizmoPlacement.Pivot(false, center, activePivot, customPivot), Is.EqualTo(customPivot));
        }
        [Test] public void LocalGizmoUsesTheSelectedFolderOrientation()
        {
            var folderOrientation = Quaternion.Euler(20, 70, -15);
            Assert.That(Quaternion.Angle(GizmoPlacement.Basis(true, false, folderOrientation), folderOrientation), Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(GizmoPlacement.Basis(false, false, folderOrientation), Quaternion.identity), Is.LessThan(.001f));
        }
        [Test] public void ZiplineGizmoUsesTheStartEndpointOrientation()
        {
            var start = new ReMap.Standalone.Core.MapObject { customType = "zipline-endpoint" };
            var zipline = new ReMap.Standalone.Core.MapObject { customType = "zipline", ziplineStartId = start.id };
            Assert.That(GizmoPlacement.OrientationId(zipline), Is.EqualTo(start.id));
            Assert.That(GizmoPlacement.OrientationId(start), Is.EqualTo(start.id));
        }
        [TestCase(-15f, -15f)]
        [TestCase(-375f, -15f)]
        [TestCase(0f, 0f)]
        [TestCase(360f, 0f)]
        [TestCase(375f, 15f)]
        [TestCase(725f, 5f)]
        public void InspectorAnglesStayWithinOneTurnAndPreserveDirection(float input, float expected)
        {
            Assert.That(GizmoPlacement.NormalizeAngle(input), Is.EqualTo(expected).Within(.001f));
        }
        [Test] public void ClickingRotationAxisUsesTheConfiguredStepAndWraps()
        {
            Assert.That(GizmoPlacement.StepAngle(350f, 15f), Is.EqualTo(5f).Within(.001f));
        }
        [Test] public void SharedMultiSelectionHeightUsesAbsoluteValueAndAppliesOnlyTheDifference()
        {
            var positions = new[] {
                new Vector3(10f, 29.8f, 5f),
                new Vector3(12f, 29.8f, 8f)
            };

            var reference = GizmoPlacement.SharedPosition(positions, out var sharedAxes);
            var delta = GizmoPlacement.PositionDelta(new Vector3(2f, 30f, -3f), reference, sharedAxes);

            Assert.That(sharedAxes, Is.EqualTo(new Vector3Int(0, 1, 0)));
            Assert.That(reference.y, Is.EqualTo(29.8f).Within(.001f));
            Assert.That(delta.x, Is.EqualTo(2f).Within(.001f));
            Assert.That(delta.y, Is.EqualTo(.2f).Within(.001f));
            Assert.That(delta.z, Is.EqualTo(-3f).Within(.001f));
        }
        [Test] public void SurfacePlacementHandlesMapToDownUpAndZero()
        {
            Assert.That(GizmoOverlay.PlacementDirection(7), Is.EqualTo(Vector3.down));
            Assert.That(GizmoOverlay.PlacementDirection(8), Is.EqualTo(Vector3.up));
            Assert.That(GizmoOverlay.PlacementDirection(9), Is.EqualTo(Vector3.zero));
        }
        [Test] public void SurfacePlacementHandlesAlwaysHaveRenderableColors()
        {
            Assert.That(GizmoOverlay.StrokeColor(7), Is.Not.EqualTo(Color.white));
            Assert.That(GizmoOverlay.StrokeColor(8), Is.Not.EqualTo(Color.white));
            Assert.That(GizmoOverlay.StrokeColor(9), Is.Not.EqualTo(Color.white));
            Assert.That(GizmoOverlay.StrokeColor(7), Is.Not.EqualTo(GizmoOverlay.StrokeColor(8)));
            Assert.That(GizmoOverlay.StrokeColor(99), Is.EqualTo(Color.white));
        }


    }
}
