using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace ReMap.Standalone
{
    public sealed class MapReferenceModel
    {
        public string AssetId;
        public MprtPlacement Placement;
    }

    public sealed class MprtReferenceOptions
    {
        public float Distance = 750f;
        public int MaxActive = 15000;
        public float MinimumSize = .5f;
        public int DensityPercent = 100;

        public static MprtReferenceOptions From(AssetSourceSettings settings) => new MprtReferenceOptions {
            Distance = Mathf.Clamp(settings?.mprtDistance ?? 750f, 50f, 5000f),
            MaxActive = Mathf.Clamp(settings?.mprtMaxActive ?? 15000, 100, 50000),
            MinimumSize = Mathf.Clamp(settings?.mprtMinimumSize ?? .5f, 0f, 100f),
            DensityPercent = Mathf.Clamp(settings?.mprtDensityPercent ?? 100, 1, 100)
        };
    }

    public static class MprtReferenceProfiles
    {
        public static void Apply(AssetSourceSettings settings, int preset)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.mprtPreset = Mathf.Clamp(preset, 0, 3);
            if (settings.mprtPreset == 0)
            { settings.mprtDistance = 300; settings.mprtMaxActive = 5000; settings.mprtMinimumSize = 1; settings.mprtDensityPercent = 50; }
            else if (settings.mprtPreset == 1)
            { settings.mprtDistance = 750; settings.mprtMaxActive = 15000; settings.mprtMinimumSize = .5f; settings.mprtDensityPercent = 100; }
            else if (settings.mprtPreset == 2)
            { settings.mprtDistance = 1500; settings.mprtMaxActive = 30000; settings.mprtMinimumSize = .25f; settings.mprtDensityPercent = 100; }
        }
    }

    public sealed partial class WorldView
    {
        private GameObject mapReferenceRoot, bspReferenceRoot, mprtReferenceRoot;
        private readonly List<Mesh> mapReferenceMeshes = new List<Mesh>();
        private readonly List<Vector3> bspQueryVertices = new List<Vector3>();
        private readonly List<int> bspQueryIndices = new List<int>();
        private readonly List<Material> mapReferenceMaterials = new List<Material>();
        private readonly List<Texture2D> mapReferenceTextures = new List<Texture2D>();
        private readonly List<KeyValuePair<string, GameObject>> mapReferenceModels = new List<KeyValuePair<string, GameObject>>();
        private sealed class MprtStreamEntry
        {
            public MapReferenceModel Reference;
            public Vector3 Position;
            public int DensityRank;
            public GameObject Instance;
            public bool RejectedBySize;
        }
        private sealed class BspMeshChunkData
        {
            public Vector3[] Positions;
            public Vector3[] Normals;
            public Vector2[] TextureCoordinates;
            public int[] Indices;
        }
        private const float MprtCellSize = 200f;
        private const int MprtPoolLimit = 2048;
        // Unity/PhysX rejects collision triangles with an edge over 500 Unity units.
        // Those faces bridge unrelated BSP regions and appear as giant walls in the map view.
        private const float MaximumBspTriangleEdgeSquared = 250000f;
        private readonly List<MprtStreamEntry> mprtStreamEntries = new List<MprtStreamEntry>();
        private readonly Dictionary<long, List<int>> mprtCells = new Dictionary<long, List<int>>();
        private readonly HashSet<int> mprtActive = new HashSet<int>();
        private readonly HashSet<int> mprtTarget = new HashSet<int>();
        private readonly Queue<int> mprtCreateQueue = new Queue<int>();
        private readonly Queue<int> mprtRemoveQueue = new Queue<int>();
        private readonly Dictionary<string, float> mprtModelSizes = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, Stack<GameObject>> mprtPool = new Dictionary<string, Stack<GameObject>>(StringComparer.Ordinal);
        private MprtReferenceOptions mprtOptions = new MprtReferenceOptions();
        private float mprtRefreshTimer;
        private Vector3 mprtLastCameraPosition = new Vector3(float.PositiveInfinity, 0, 0);
        private int mprtPoolCount;

        public void LoadBspReference(ApexBspMap map, string extractionRoot, Float3 originOffset)
        {
            ClearBspReference();
            EnsureMapReferenceRoot(originOffset);
            bspReferenceRoot = new GameObject("Main BSP (locked)");
            bspReferenceRoot.transform.SetParent(mapReferenceRoot.transform, false);
            var textureIndex = BuildTextureIndex(extractionRoot);
            int surfaceIndex = 0;
            foreach (var surface in map.Surfaces)
            {
                Material material = CreateMapMaterial(surface.MaterialPath, textureIndex);
                foreach (var chunk in MeshChunks(surface, 180000))
                {
                    var gameObject = new GameObject("BSP " + (++surfaceIndex) + " · " + surface.MaterialPath,
                        typeof(MeshFilter), typeof(MeshRenderer));
                    gameObject.transform.SetParent(bspReferenceRoot.transform, false);
                    gameObject.GetComponent<MeshFilter>().sharedMesh = chunk;
                    gameObject.GetComponent<MeshRenderer>().sharedMaterial = material;
                }
            }
        }

        public async Task LoadBspReferenceAsync(ApexBspMap map, string extractionRoot, Float3 originOffset,
            IProgress<float> progress, CancellationToken cancellation)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            ClearBspReference();
            EnsureMapReferenceRoot(originOffset);
            bspReferenceRoot = new GameObject("Main BSP (locked)");
            bspReferenceRoot.transform.SetParent(mapReferenceRoot.transform, false);
            // Keep a partially constructed terrain hidden. The caller makes the completed
            // reference visible, or clears it if cancellation/another error interrupts us.
            bspReferenceRoot.SetActive(false);
            var textureIndex = await Task.Run(() => BuildTextureIndex(extractionRoot), cancellation);
            cancellation.ThrowIfCancellationRequested();

            // Large 64k maps can contain tens of millions of collision indices. Preparing
            // tiny chunks on Unity's main thread used to take minutes and created hundreds
            // of GameObjects. Remap a larger bounded chunk on a worker, then only upload the
            // finished arrays on the main thread. This keeps Cancel/UI responsive while also
            // reducing Unity object and draw-call overhead by roughly an order of magnitude.
            const int maximumIndicesPerChunk = 600000;
            int totalIndices = Math.Max(1, map.Surfaces.Sum(surface => surface.Indices?.Length ?? 0));
            int completedIndices = 0, surfaceIndex = 0;
            foreach (var surface in map.Surfaces)
            {
                cancellation.ThrowIfCancellationRequested();
                Material material = CreateMapMaterial(surface.MaterialPath, textureIndex);
                for (int first = 0; first < surface.Indices.Length; first += maximumIndicesPerChunk)
                {
                    cancellation.ThrowIfCancellationRequested();
                    int count = Math.Min(maximumIndicesPerChunk, surface.Indices.Length - first);
                    count -= count % 3;
                    if (count == 0) continue;
                    int chunkFirst = first, chunkCount = count;
                    BspMeshChunkData data = await Task.Run(
                        () => BuildMeshChunkData(surface, chunkFirst, chunkCount, cancellation), cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    completedIndices += count;
                    progress?.Report(Mathf.Clamp01((float)completedIndices / totalIndices));
                    if (data.Indices.Length > 0)
                    {
                        Mesh chunk = CreateMesh(data, surface.MaterialPath);
                        mapReferenceMeshes.Add(chunk);
                        var gameObject = new GameObject("BSP " + (++surfaceIndex) + " · " + surface.MaterialPath,
                            typeof(MeshFilter), typeof(MeshRenderer));
                        gameObject.transform.SetParent(bspReferenceRoot.transform, false);
                        gameObject.GetComponent<MeshFilter>().sharedMesh = chunk;
                        gameObject.GetComponent<MeshRenderer>().sharedMaterial = material;
                    }
                    // Unity object creation remains on the main thread. Yield after each upload
                    // so progress and input are presented before preparing the next chunk.
                    await Task.Yield();
                }
            }
            cancellation.ThrowIfCancellationRequested();
            progress?.Report(1);
        }

        public void BeginMprtReference(Float3 originOffset)
        {
            ClearMprtReference();
            EnsureMapReferenceRoot(originOffset);
            mprtReferenceRoot = new GameObject("MPRT models (locked)");
            mprtReferenceRoot.transform.SetParent(mapReferenceRoot.transform, false);
        }

        public void AddMprtReference(MapReferenceModel reference)
        {
            if (reference?.Placement == null || string.IsNullOrEmpty(reference.AssetId) ||
                mprtReferenceRoot == null || !models.IsPrepared(reference.AssetId)) return;
            var instance = CreateMprtInstance(reference);
            mapReferenceModels.Add(new KeyValuePair<string, GameObject>(reference.AssetId, instance));
        }

        public void ConfigureMprtReference(IEnumerable<MapReferenceModel> references, MprtReferenceOptions options,
            Float3 originOffset)
        {
            BeginMprtReference(originOffset);
            mprtOptions = options ?? new MprtReferenceOptions();
            foreach (MapReferenceModel reference in references ?? Enumerable.Empty<MapReferenceModel>())
                AddMprtStreamEntry(reference);
            SetMprtReferenceOptions(options);
        }

        public async Task ConfigureMprtReferenceAsync(IReadOnlyList<MapReferenceModel> references,
            MprtReferenceOptions options, Float3 originOffset, IProgress<float> progress,
            CancellationToken cancellation)
        {
            BeginMprtReference(originOffset);
            mprtOptions = options ?? new MprtReferenceOptions();
            mprtReferenceRoot.SetActive(false);
            int total = references?.Count ?? 0;
            for (int i = 0; i < total; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                AddMprtStreamEntry(references[i]);
                if ((i & 4095) == 4095)
                {
                    progress?.Report((float)(i + 1) / Math.Max(1, total));
                    await Task.Yield();
                }
            }
            cancellation.ThrowIfCancellationRequested();
            progress?.Report(1);
            SetMprtReferenceOptions(options);
        }

        private void AddMprtStreamEntry(MapReferenceModel reference)
        {
            if (reference?.Placement == null || string.IsNullOrEmpty(reference.AssetId) ||
                !models.IsPrepared(reference.AssetId)) return;
            Vector3 position = ToVector(ApexCoordinates.ToUnity(reference.Placement.Position));
            int index = mprtStreamEntries.Count;
            var entry = new MprtStreamEntry { Reference = reference, Position = position,
                DensityRank = StableDensityRank(reference) };
            mprtStreamEntries.Add(entry);
            long cell = MprtCell(position);
            if (!mprtCells.TryGetValue(cell, out var indices)) mprtCells.Add(cell, indices = new List<int>());
            indices.Add(index);
        }

        public void SetMprtReferenceOptions(MprtReferenceOptions options)
        {
            mprtOptions = options ?? new MprtReferenceOptions();
            foreach (var entry in mprtStreamEntries) entry.RejectedBySize = false;
            mprtRefreshTimer = 0;
            mprtLastCameraPosition = new Vector3(float.PositiveInfinity, 0, 0);
        }

        public int MprtReferenceActiveCount => mprtActive.Count;
        public int MprtReferenceAvailableCount => mprtStreamEntries.Count;

        public void TickMprtReference(float deltaTime)
        {
            if (mprtReferenceRoot == null || !mprtReferenceRoot.activeInHierarchy || mprtStreamEntries.Count == 0) return;
            // Stream around the point being inspected rather than the physical camera position.
            // A framed overview can place the camera kilometres above the terrain while its focus
            // remains exactly where the user expects nearby props to appear.
            Vector3 cameraLocal = mprtReferenceRoot.transform.InverseTransformPoint(focus);
            mprtRefreshTimer -= Mathf.Max(0, deltaTime);
            float threshold = Mathf.Max(10f, mprtOptions.Distance * .08f);
            if (mprtRefreshTimer <= 0 || (cameraLocal - mprtLastCameraPosition).sqrMagnitude > threshold * threshold)
            {
                RebuildMprtTarget(cameraLocal);
                mprtLastCameraPosition = cameraLocal;
                mprtRefreshTimer = .35f;
            }
            for (int i = 0; i < 96 && mprtRemoveQueue.Count > 0; i++) ReleaseMprtEntry(mprtRemoveQueue.Dequeue());
            for (int i = 0; i < 64 && mprtCreateQueue.Count > 0; i++) ActivateMprtEntry(mprtCreateQueue.Dequeue());
        }

        private void RebuildMprtTarget(Vector3 cameraLocal)
        {
            var candidates = new List<KeyValuePair<float, int>>();
            int cellRadius = Mathf.CeilToInt(mprtOptions.Distance / MprtCellSize);
            int cx = Mathf.FloorToInt(cameraLocal.x / MprtCellSize), cz = Mathf.FloorToInt(cameraLocal.z / MprtCellSize);
            float maximumDistanceSq = mprtOptions.Distance * mprtOptions.Distance;
            for (int x = cx - cellRadius; x <= cx + cellRadius; x++)
                for (int z = cz - cellRadius; z <= cz + cellRadius; z++)
                    if (mprtCells.TryGetValue(MprtCell(x, z), out var indices))
                        foreach (int index in indices)
                        {
                            MprtStreamEntry entry = mprtStreamEntries[index];
                            if (entry.DensityRank >= mprtOptions.DensityPercent || entry.RejectedBySize) continue;
                            if (mprtModelSizes.TryGetValue(entry.Reference.AssetId, out float size) &&
                                size * entry.Reference.Placement.Scale < mprtOptions.MinimumSize) continue;
                            float distanceSq = (entry.Position - cameraLocal).sqrMagnitude;
                            if (distanceSq <= maximumDistanceSq) candidates.Add(new KeyValuePair<float, int>(distanceSq, index));
                        }
            candidates.Sort((left, right) => left.Key.CompareTo(right.Key));
            mprtTarget.Clear();
            for (int i = 0; i < candidates.Count && i < mprtOptions.MaxActive; i++) mprtTarget.Add(candidates[i].Value);
            mprtCreateQueue.Clear(); mprtRemoveQueue.Clear();
            foreach (int index in mprtActive) if (!mprtTarget.Contains(index)) mprtRemoveQueue.Enqueue(index);
            foreach (int index in mprtTarget) if (!mprtActive.Contains(index)) mprtCreateQueue.Enqueue(index);
        }

        private void ActivateMprtEntry(int index)
        {
            if (!mprtTarget.Contains(index) || mprtActive.Contains(index)) return;
            MprtStreamEntry entry = mprtStreamEntries[index];
            if (entry.RejectedBySize) return;
            GameObject instance = AcquireMprtInstance(entry.Reference);
            if (instance == null) { entry.RejectedBySize = true; return; }
            var mesh = instance.GetComponent<MeshFilter>()?.sharedMesh;
            float modelSize = mesh == null ? 0 : Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z);
            mprtModelSizes[entry.Reference.AssetId] = modelSize;
            if (modelSize * entry.Reference.Placement.Scale < mprtOptions.MinimumSize)
            {
                entry.RejectedBySize = true;
                models.Release(entry.Reference.AssetId, instance);
                return;
            }
            entry.Instance = instance; mprtActive.Add(index);
        }

        private void ReleaseMprtEntry(int index)
        {
            if (!mprtActive.Remove(index)) return;
            MprtStreamEntry entry = mprtStreamEntries[index];
            GameObject instance = entry.Instance; entry.Instance = null;
            if (instance == null) return;
            if (mprtPoolCount < MprtPoolLimit)
            {
                instance.SetActive(false);
                if (!mprtPool.TryGetValue(entry.Reference.AssetId, out var pool))
                    mprtPool.Add(entry.Reference.AssetId, pool = new Stack<GameObject>());
                pool.Push(instance); mprtPoolCount++;
            }
            else models.Release(entry.Reference.AssetId, instance);
        }

        private GameObject AcquireMprtInstance(MapReferenceModel reference)
        {
            if (!models.IsPrepared(reference.AssetId)) return null;
            GameObject instance = null;
            if (mprtPool.TryGetValue(reference.AssetId, out var pool) && pool.Count > 0)
            { instance = pool.Pop(); mprtPoolCount--; instance.SetActive(true); }
            return PlaceMprtInstance(instance ?? models.Create(reference.AssetId, false), reference);
        }

        private GameObject CreateMprtInstance(MapReferenceModel reference) =>
            PlaceMprtInstance(models.Create(reference.AssetId, false), reference);

        private GameObject PlaceMprtInstance(GameObject instance, MapReferenceModel reference)
        {
            instance.name = Path.GetFileNameWithoutExtension(reference.Placement.ModelPath) + " (locked)";
            instance.transform.SetParent(mprtReferenceRoot.transform, false);
            instance.transform.localPosition = ToVector(ApexCoordinates.ToUnity(reference.Placement.Position));
            instance.transform.localRotation = Quaternion.Euler(ApexDisplay.UnityAngles(ToVector(reference.Placement.Angles)));
            instance.transform.localScale = Vector3.one * reference.Placement.Scale;
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            return instance;
        }

        private static int StableDensityRank(MapReferenceModel reference)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char character in reference.AssetId) { hash ^= character; hash *= 16777619; }
                hash = MixDensityHash(hash, Mathf.RoundToInt(reference.Placement.Position.x * 16f));
                hash = MixDensityHash(hash, Mathf.RoundToInt(reference.Placement.Position.y * 16f));
                hash = MixDensityHash(hash, Mathf.RoundToInt(reference.Placement.Position.z * 16f));
                return (int)(hash % 100);
            }
        }

        private static uint MixDensityHash(uint hash, int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                return hash * 16777619;
            }
        }

        private static long MprtCell(Vector3 position) => MprtCell(Mathf.FloorToInt(position.x / MprtCellSize),
            Mathf.FloorToInt(position.z / MprtCellSize));
        private static long MprtCell(int x, int z) => ((long)x << 32) ^ (uint)z;

        public void SetMapReferenceOrigin(Float3 originOffset)
        {
            if (mapReferenceRoot != null) mapReferenceRoot.transform.position = -ToVector(originOffset);
        }

        public void SetMapReferenceVisibility(bool showBsp, bool showMprt)
        {
            if (bspReferenceRoot != null) bspReferenceRoot.SetActive(showBsp);
            if (mprtReferenceRoot != null) mprtReferenceRoot.SetActive(showMprt);
        }

        public int BspReferenceMeshCount => mapReferenceMeshes.Count;
        public int BspReferenceTriangleCount => mapReferenceMeshes.Sum(mesh =>
            mesh == null || mesh.subMeshCount == 0 ? 0 : (int)(mesh.GetIndexCount(0) / 3));
        public int BspReferenceColliderCount => bspReferenceRoot == null ? 0 :
            bspReferenceRoot.GetComponentsInChildren<MeshCollider>(true).Length;

        public bool TryBspSurfaceBelow(Vector3 worldOrigin, out float worldHeight)
        {
            worldHeight = 0f;
            if (bspReferenceRoot == null || mapReferenceMeshes.Count == 0) return false;
            Vector3 origin = bspReferenceRoot.transform.InverseTransformPoint(worldOrigin);
            float highest = float.NegativeInfinity;
            const float tolerance = .0001f;
            foreach (Mesh mesh in mapReferenceMeshes)
            {
                if (mesh == null) continue;
                Bounds bounds = mesh.bounds;
                if (origin.x < bounds.min.x - tolerance || origin.x > bounds.max.x + tolerance ||
                    origin.z < bounds.min.z - tolerance || origin.z > bounds.max.z + tolerance ||
                    bounds.min.y > origin.y + tolerance)
                    continue;
                bspQueryVertices.Clear(); bspQueryIndices.Clear();
                mesh.GetVertices(bspQueryVertices);
                mesh.GetIndices(bspQueryIndices, 0);
                for (int i = 0; i + 2 < bspQueryIndices.Count; i += 3)
                {
                    Vector3 a = bspQueryVertices[bspQueryIndices[i]];
                    Vector3 b = bspQueryVertices[bspQueryIndices[i + 1]];
                    Vector3 c = bspQueryVertices[bspQueryIndices[i + 2]];
                    if (!TryVerticalTriangleHeight(origin.x, origin.z, a, b, c, out float height) ||
                        height > origin.y + tolerance || height <= highest)
                        continue;
                    highest = height;
                }
            }
            if (float.IsNegativeInfinity(highest)) return false;
            worldHeight = bspReferenceRoot.transform.TransformPoint(
                new Vector3(origin.x, highest, origin.z)).y;
            return true;
        }

        public bool TryBspRaycast(Ray worldRay, float maximumDistance, out Vector3 worldPoint,
            out float worldDistance)
        {
            worldPoint = Vector3.zero; worldDistance = 0f;
            if (bspReferenceRoot == null || mapReferenceMeshes.Count == 0 || maximumDistance <= 0f) return false;
            var ray = new Ray(bspReferenceRoot.transform.InverseTransformPoint(worldRay.origin),
                bspReferenceRoot.transform.InverseTransformDirection(worldRay.direction).normalized);
            float nearest = float.PositiveInfinity;
            foreach (Mesh mesh in mapReferenceMeshes)
            {
                if (mesh == null || !mesh.bounds.IntersectRay(ray, out float boundsDistance) ||
                    boundsDistance > nearest)
                    continue;
                bspQueryVertices.Clear(); bspQueryIndices.Clear();
                mesh.GetVertices(bspQueryVertices);
                mesh.GetIndices(bspQueryIndices, 0);
                for (int i = 0; i + 2 < bspQueryIndices.Count; i += 3)
                {
                    Vector3 a = bspQueryVertices[bspQueryIndices[i]];
                    Vector3 b = bspQueryVertices[bspQueryIndices[i + 1]];
                    Vector3 c = bspQueryVertices[bspQueryIndices[i + 2]];
                    if (TryRayTriangleDistance(ray, a, b, c, out float distance) && distance < nearest)
                        nearest = distance;
                }
            }
            if (float.IsPositiveInfinity(nearest)) return false;
            worldPoint = bspReferenceRoot.transform.TransformPoint(ray.GetPoint(nearest));
            worldDistance = Vector3.Distance(worldRay.origin, worldPoint);
            return worldDistance <= maximumDistance;
        }

        private static bool TryVerticalTriangleHeight(float x, float z, Vector3 a, Vector3 b, Vector3 c,
            out float height)
        {
            height = 0f;
            float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(denominator) < .0000001f) return false;
            float first = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / denominator;
            float second = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / denominator;
            float third = 1f - first - second;
            const float tolerance = .0001f;
            if (first < -tolerance || second < -tolerance || third < -tolerance) return false;
            height = first * a.y + second * b.y + third * c.y;
            return float.IsFinite(height);
        }

        private static bool TryRayTriangleDistance(Ray ray, Vector3 a, Vector3 b, Vector3 c,
            out float distance)
        {
            distance = 0f;
            Vector3 edge1 = b - a, edge2 = c - a;
            Vector3 cross = Vector3.Cross(ray.direction, edge2);
            float determinant = Vector3.Dot(edge1, cross);
            if (Mathf.Abs(determinant) < .0000001f) return false;
            float inverse = 1f / determinant;
            Vector3 offset = ray.origin - a;
            float first = Vector3.Dot(offset, cross) * inverse;
            if (first < -.0001f || first > 1.0001f) return false;
            Vector3 secondCross = Vector3.Cross(offset, edge1);
            float second = Vector3.Dot(ray.direction, secondCross) * inverse;
            if (second < -.0001f || first + second > 1.0001f) return false;
            distance = Vector3.Dot(edge2, secondCross) * inverse;
            return distance >= 0f && float.IsFinite(distance);
        }

        public bool FocusBspReference()
        {
            if (bspReferenceRoot == null) return false;
            var renderers = bspReferenceRoot.GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0) return false;
            Bounds bounds = RobustBspBounds();
            if (bounds.size.sqrMagnitude < .001f)
            {
                bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            }
            focus = bounds.center;
            yaw = 35;
            pitch = 42;
            distance = Mathf.Clamp(bounds.size.magnitude * .72f, 12, 1500);
            orbitPoint = focus;
            UpdateCamera();
            return true;
        }

        private Bounds RobustBspBounds()
        {
            var x = new List<float>(); var y = new List<float>(); var z = new List<float>();
            foreach (var filter in bspReferenceRoot.GetComponentsInChildren<MeshFilter>(false))
            {
                if (filter.sharedMesh == null) continue;
                foreach (Vector3 local in filter.sharedMesh.vertices)
                {
                    Vector3 point = filter.transform.TransformPoint(local);
                    x.Add(point.x); y.Add(point.y); z.Add(point.z);
                }
            }
            if (x.Count < 3) return new Bounds();
            x.Sort(); y.Sort(); z.Sort();
            int low = Mathf.FloorToInt((x.Count - 1) * .02f);
            int high = Mathf.CeilToInt((x.Count - 1) * .98f);
            var minimum = new Vector3(x[low], y[low], z[low]);
            var maximum = new Vector3(x[high], y[high], z[high]);
            var bounds = new Bounds((minimum + maximum) * .5f, maximum - minimum);
            return bounds;
        }

        public void ClearMapReference()
        {
            ClearMprtReference();
            ClearBspReference();
            if (mapReferenceRoot != null) UnityEngine.Object.Destroy(mapReferenceRoot);
            mapReferenceRoot = null;
        }

        private void EnsureMapReferenceRoot(Float3 originOffset)
        {
            if (mapReferenceRoot == null)
            {
                mapReferenceRoot = new GameObject("Map reference (read only)");
                mapReferenceRoot.transform.SetParent(root.transform, false);
            }
            SetMapReferenceOrigin(originOffset);
        }

        public void ClearMprtReference()
        {
            foreach (int index in mprtActive.ToArray())
            {
                var entry = mprtStreamEntries[index];
                if (entry.Instance != null) models.Release(entry.Reference.AssetId, entry.Instance);
            }
            foreach (var pair in mprtPool)
                foreach (var instance in pair.Value) if (instance != null) models.Release(pair.Key, instance);
            foreach (var pair in mapReferenceModels)
                if (pair.Value != null) models.Release(pair.Key, pair.Value);
            mapReferenceModels.Clear();
            mprtStreamEntries.Clear(); mprtCells.Clear(); mprtActive.Clear(); mprtTarget.Clear();
            mprtCreateQueue.Clear(); mprtRemoveQueue.Clear(); mprtModelSizes.Clear(); mprtPool.Clear(); mprtPoolCount = 0;
            if (mprtReferenceRoot != null) UnityEngine.Object.Destroy(mprtReferenceRoot);
            mprtReferenceRoot = null;
        }

        private void ClearBspReference()
        {
            if (bspReferenceRoot != null) UnityEngine.Object.Destroy(bspReferenceRoot);
            bspReferenceRoot = null;
            foreach (var mesh in mapReferenceMeshes) if (mesh != null) UnityEngine.Object.Destroy(mesh);
            foreach (var material in mapReferenceMaterials) if (material != null) UnityEngine.Object.Destroy(material);
            foreach (var texture in mapReferenceTextures) if (texture != null) UnityEngine.Object.Destroy(texture);
            mapReferenceMeshes.Clear(); mapReferenceMaterials.Clear(); mapReferenceTextures.Clear();
        }

        private Material CreateMapMaterial(string materialPath, Dictionary<string, string> textureIndex)
        {
            int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(materialPath ?? "");
            float hue = (hash & 0xffff) / 65535f;
            var material = new Material(objectShader) { name = "BSP · " + materialPath };
            material.SetColor("_BaseColor", Color.HSVToRGB(hue, .18f, .72f));
            material.SetFloat("_Smoothness", .12f);
            string stem = Path.GetFileNameWithoutExtension((materialPath ?? "").Replace('\\', '/'));
            string texturePath = null;
            foreach (string candidate in new[] { stem, stem + "_col", stem + "_albedo", stem + "_albedoTexture" })
                if (textureIndex.TryGetValue(candidate, out texturePath)) break;
            if (texturePath != null)
            {
                try
                {
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                    if (texture.LoadImage(File.ReadAllBytes(texturePath)))
                    {
                        texture.name = stem; texture.wrapMode = TextureWrapMode.Repeat;
                        mapReferenceTextures.Add(texture);
                        material.SetTexture("_BaseMap", texture); material.SetColor("_BaseColor", Color.white);
                    }
                    else UnityEngine.Object.Destroy(texture);
                }
                catch (IOException) { }
            }
            mapReferenceMaterials.Add(material);
            return material;
        }

        private static Dictionary<string, string> BuildTextureIndex(string rootPath)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return result;
            try
            {
                foreach (string path in Directory.EnumerateFiles(rootPath, "*.png", SearchOption.AllDirectories))
                {
                    string stem = Path.GetFileNameWithoutExtension(path);
                    if (!result.ContainsKey(stem)) result.Add(stem, path);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return result;
        }

        private IEnumerable<Mesh> MeshChunks(BspSurface surface, int maximumIndices,
            CancellationToken cancellation = default)
        {
            for (int first = 0; first < surface.Indices.Length; first += maximumIndices)
            {
                cancellation.ThrowIfCancellationRequested();
                int count = Math.Min(maximumIndices, surface.Indices.Length - first);
                count -= count % 3;
                if (count == 0) continue;
                BspMeshChunkData data = BuildMeshChunkData(surface, first, count, cancellation);
                if (data.Indices.Length == 0) continue;
                Mesh mesh = CreateMesh(data, surface.MaterialPath);
                mapReferenceMeshes.Add(mesh);
                yield return mesh;
            }
        }

        private static BspMeshChunkData BuildMeshChunkData(BspSurface surface, int first, int count,
            CancellationToken cancellation)
        {
            int capacity = Math.Min(count, surface.Positions.Length);
            var remap = new Dictionary<int, int>(capacity);
            var positions = new List<Vector3>(capacity);
            var normals = new List<Vector3>(capacity);
            var textureCoordinates = new List<Vector2>(capacity);
            var indices = new List<int>(count);
            for (int i = 0; i < count; i += 3)
            {
                if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                int source0 = surface.Indices[first + i];
                int source1 = surface.Indices[first + i + 1];
                int source2 = surface.Indices[first + i + 2];
                Vector3 position0 = BspPosition(surface.Positions[source0]);
                Vector3 position1 = BspPosition(surface.Positions[source1]);
                Vector3 position2 = BspPosition(surface.Positions[source2]);
                if (!BspTriangleWithinEdgeLimit(position0, position1, position2)) continue;

                int destination0 = RemapBspVertex(surface, source0, position0, remap,
                    positions, normals, textureCoordinates);
                int destination1 = RemapBspVertex(surface, source1, position1, remap,
                    positions, normals, textureCoordinates);
                int destination2 = RemapBspVertex(surface, source2, position2, remap,
                    positions, normals, textureCoordinates);
                // The Source-to-Unity Y/Z swap changes handedness.
                indices.Add(destination0); indices.Add(destination2); indices.Add(destination1);
            }
            return new BspMeshChunkData {
                Positions = positions.ToArray(), Normals = normals.ToArray(),
                TextureCoordinates = textureCoordinates.ToArray(), Indices = indices.ToArray()
            };
        }

        private static int RemapBspVertex(BspSurface surface, int source, Vector3 position,
            Dictionary<int, int> remap, List<Vector3> positions, List<Vector3> normals,
            List<Vector2> textureCoordinates)
        {
            if (remap.TryGetValue(source, out int destination)) return destination;
            destination = positions.Count;
            remap.Add(source, destination);
            positions.Add(position);
            Float3 normal = surface.Normals[source];
            normals.Add(new Vector3(normal.x, normal.z, normal.y).normalized);
            BspVector2 textureCoordinate = surface.TextureCoordinates[source];
            textureCoordinates.Add(new Vector2(textureCoordinate.x, 1 - textureCoordinate.y));
            return destination;
        }

        private static Vector3 BspPosition(Float3 source)
        {
            Float3 position = ApexCoordinates.ToUnity(source);
            return new Vector3(position.x, position.y, position.z);
        }

        private static bool BspTriangleWithinEdgeLimit(Vector3 a, Vector3 b, Vector3 c) =>
            BspSquaredDistance(a, b) <= MaximumBspTriangleEdgeSquared &&
            BspSquaredDistance(b, c) <= MaximumBspTriangleEdgeSquared &&
            BspSquaredDistance(c, a) <= MaximumBspTriangleEdgeSquared;

        private static float BspSquaredDistance(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, y = a.y - b.y, z = a.z - b.z;
            return x * x + y * y + z * z;
        }

        private static Mesh CreateMesh(BspMeshChunkData data, string materialPath)
        {
            var mesh = new Mesh { name = "BSP · " + materialPath, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(data.Positions); mesh.SetNormals(data.Normals);
            mesh.SetUVs(0, data.TextureCoordinates);
            mesh.SetTriangles(data.Indices, 0, false); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
