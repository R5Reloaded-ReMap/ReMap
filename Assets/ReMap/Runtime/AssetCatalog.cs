using ReMap.Standalone.Core;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReMap.Standalone
{
    public sealed class CatalogEntry
    {
        public readonly string Id, Name, Category;
        public readonly Vector3 Size;
        public ReMap.Standalone.Core.GameAssetRecord GameAsset;
        public string CustomType;
        public string[] SupportedGameTargets = { GameTargets.R5Reloaded, GameTargets.R5Flowstate };
        public float PlacementLift;
        public CatalogEntry(string id, string name, string category, Vector3 size)
        { Id = id; Name = name; Category = category; Size = size; }
        public bool SupportsGame(string target) =>
            SupportedGameTargets == null || Array.IndexOf(SupportedGameTargets, GameTargets.Normalize(target)) >= 0;
    }

    // Catalog metadata is separate from loaded models. An RSX-backed catalog can replace this one.
    public interface IAssetCatalog { IReadOnlyList<CatalogEntry> Entries { get; } }
    public interface IModelProvider : IDisposable
    {
        GameObject Create(string assetId);
        void Release(string assetId, GameObject instance);
    }

    public sealed class DemoCatalog : IAssetCatalog
    {
        public IReadOnlyList<CatalogEntry> Entries { get; } = Array.AsReadOnly(new[] {
            new CatalogEntry("demo:cube", L.T("#BLOCK"), L.T("#CONSTRUCTION"), Vector3.one),
            new CatalogEntry("demo:floor", L.T("#PLATFORM"), L.T("#CONSTRUCTION"), new Vector3(4, .25f, 4)),
            new CatalogEntry("demo:wall", L.T("#WALL"), L.T("#CONSTRUCTION"), new Vector3(4, 3, .3f)),
            new CatalogEntry("demo:cylinder", L.T("#PILLAR"), L.T("#CONSTRUCTION"), new Vector3(1, 2, 1))
        });
    }

    public sealed class DemoModelProvider : IModelProvider
    {
        private sealed class Loaded { public Material Material; public int References; }
        private readonly Dictionary<string, Loaded> loaded = new Dictionary<string, Loaded>();
        private readonly Shader shader;
        public int LoadedModelCount => loaded.Count;

        public DemoModelProvider(Shader shader) { this.shader = shader; }

        public GameObject Create(string assetId) => Create(assetId, true);
        public GameObject Create(string assetId, bool collision)
        {
            if (!loaded.TryGetValue(assetId, out var asset))
            {
                // Created only on first placement. Duplicate instances share material and primitive mesh.
                asset = new Loaded { Material = new Material(shader) };
                var known = assetId.StartsWith("demo:", StringComparison.Ordinal);
                asset.Material.color = known ? new Color(.35f, .68f, .73f) : new Color(.85f, .3f, .6f);
                loaded.Add(assetId, asset);
            }
            asset.References++;
            var instance = GameObject.CreatePrimitive(assetId == "demo:cylinder" ? PrimitiveType.Cylinder : PrimitiveType.Cube);
            var primitiveCollider = instance.GetComponent<Collider>();
            if (!collision || assetId == "demo:cylinder") primitiveCollider.enabled = false;
            if (collision && assetId == "demo:cylinder") {
                UnityEngine.Object.Destroy(primitiveCollider);
                ModelCollision.Attach(instance, instance.GetComponent<MeshFilter>().sharedMesh);
            }
            instance.GetComponent<Renderer>().sharedMaterial = asset.Material;
            return instance;
        }

        public void Release(string assetId, GameObject instance)
        {
            instance.SetActive(false);
            UnityEngine.Object.Destroy(instance);
            if (loaded.TryGetValue(assetId, out var asset) && --asset.References == 0)
            {
                UnityEngine.Object.Destroy(asset.Material);
                loaded.Remove(assetId);
            }
        }

        public void Dispose()
        {
            foreach (var asset in loaded.Values) UnityEngine.Object.Destroy(asset.Material);
            loaded.Clear();
        }
    }
}
