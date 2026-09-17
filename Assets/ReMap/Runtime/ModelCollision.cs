using ReMap.Standalone.Core;
using System;
using UnityEngine;
namespace ReMap.Standalone
{
    // Concave, static editor geometry: do not add a dynamic Rigidbody or a convex hull.
    public static class ModelCollision
    {
        public const MeshColliderCookingOptions Cooking = MeshColliderCookingOptions.CookForFasterSimulation |
            MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices |
            MeshColliderCookingOptions.UseFastMidphase;
        public static void Bake(Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount < 3) throw new ArgumentException(L.T("#COLLISION_GEOMETRY_MISSING"));
            Physics.BakeMesh(mesh.GetEntityId(), false, Cooking);
        }
        public static MeshCollider Attach(GameObject instance, Mesh mesh)
        {
            var collider = instance.AddComponent<MeshCollider>();
            collider.convex = false; collider.cookingOptions = Cooking; collider.sharedMesh = mesh;
            return collider;
        }
    }
}
