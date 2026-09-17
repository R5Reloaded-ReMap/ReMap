using NUnit.Framework;
using UnityEngine;
namespace ReMap.Standalone.Tests
{
    public sealed class CollisionTests
    {
        private Mesh mesh; private GameObject first, second;
        [TearDown] public void Cleanup() { Object.DestroyImmediate(first); Object.DestroyImmediate(second); Object.DestroyImmediate(mesh); }
        private MeshCollider Surface(Vector3[] vertices, int[] triangles)
        {
            mesh = new Mesh { vertices = vertices, triangles = triangles }; mesh.RecalculateBounds();
            ModelCollision.Bake(mesh); first = new GameObject("Collision test"); return ModelCollision.Attach(first, mesh);
        }
        [Test] public void ConcaveMeshLeavesTheOpeningEmpty()
        {
            // Two disconnected surfaces must not be filled in by a convex hull or bounding box.
            var collider = Surface(new[] { new Vector3(-3,0,-2), new Vector3(-3,0,2), new Vector3(-1,0,2), new Vector3(-1,0,-2),
                new Vector3(1,0,-2), new Vector3(1,0,2), new Vector3(3,0,2), new Vector3(3,0,-2) }, new[] {0,1,2,0,2,3,4,5,6,4,6,7});
            Physics.SyncTransforms();
            Assert.That(collider.convex, Is.False);
            Assert.That(collider.Raycast(new Ray(new Vector3(0,5,0), Vector3.down), out _, 10), Is.False);
            Assert.That(collider.Raycast(new Ray(new Vector3(2,5,0), Vector3.down), out var hit, 10), Is.True);
            Assert.That(hit.point.y, Is.EqualTo(0).Within(.001f));
        }
        [Test] public void SlopedSurfaceReturnsTriangleHeightAndNormal()
        {
            var collider = Surface(new[] { new Vector3(-2,0,-2), new Vector3(-2,0,2), new Vector3(2,2,2), new Vector3(2,2,-2) }, new[] {0,1,2,0,2,3});
            Physics.SyncTransforms();
            Assert.That(collider.Raycast(new Ray(new Vector3(0,5,0), Vector3.down), out var hit, 10), Is.True);
            Assert.That(hit.point.y, Is.EqualTo(1).Within(.001f));
            Assert.That(Vector3.Angle(hit.normal, new Vector3(-.5f,1,0)), Is.LessThan(.01f));
        }
        [Test] public void CopiesShareMeshAndRespectRotatedScaledParent()
        {
            var collider = Surface(new[] { new Vector3(-2,0,-2), new Vector3(-2,0,2), new Vector3(2,0,2), new Vector3(2,0,-2) }, new[] {0,1,2,0,2,3});
            second = new GameObject("Copy"); var copy = ModelCollision.Attach(second, mesh);
            second.transform.SetParent(first.transform, false); second.transform.localPosition = new Vector3(0,2,0);
            first.transform.SetPositionAndRotation(new Vector3(10,3,5), Quaternion.Euler(0,32,0)); first.transform.localScale = new Vector3(2,3,1);
            Physics.SyncTransforms();
            Assert.That(copy.sharedMesh, Is.SameAs(collider.sharedMesh));
            Assert.That(copy.Raycast(new Ray(new Vector3(10,15,5), Vector3.down), out var hit, 20), Is.True);
            Assert.That(hit.point.y, Is.EqualTo(9).Within(.001f));
        }
    }
}
