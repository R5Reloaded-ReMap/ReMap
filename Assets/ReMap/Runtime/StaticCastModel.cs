using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace ReMap.Standalone
{
    [Serializable]
    public sealed class ApexModelOrientationEntry
    {
        public string model;
        public float pitch;
        public float yaw;
        public float roll;
    }

    [Serializable]
    public sealed class ApexModelOrientationCatalog
    {
        public ApexModelOrientationEntry[] corrections = Array.Empty<ApexModelOrientationEntry>();
    }

    public static class ApexModelOrientation
    {
        public const string MetadataFileName = "ModelOrientationCorrections.json";
        private static readonly object Sync = new object();
        private static Dictionary<string, Vector3> cachedCorrections;
        private static string cachedPath;
        private static DateTime cachedWriteTimeUtc;

        public static string MetadataPath => Path.Combine(Application.streamingAssetsPath, MetadataFileName);

        public static Quaternion VisualCorrection(string sourcePath)
        {
            return Corrections().TryGetValue(ModelKey(sourcePath), out Vector3 apexAngles)
                ? Quaternion.Euler(ApexDisplay.UnityAngles(apexAngles))
                : Quaternion.identity;
        }

        public static Dictionary<string, Vector3> ReadCorrections(string json)
        {
            var result = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json)) return result;
            var catalog = JsonUtility.FromJson<ApexModelOrientationCatalog>(json);
            if (catalog?.corrections == null) return result;
            foreach (ApexModelOrientationEntry entry in catalog.corrections)
            {
                if (entry == null) continue;
                string key = ModelKey(entry.model);
                var angles = new Vector3(entry.pitch, entry.yaw, entry.roll);
                if (key.Length == 0 || !float.IsFinite(angles.x) ||
                    !float.IsFinite(angles.y) || !float.IsFinite(angles.z)) continue;
                result[key] = angles;
            }
            return result;
        }

        private static Dictionary<string, Vector3> Corrections()
        {
            string path = MetadataPath;
            DateTime writeTimeUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            lock (Sync)
            {
                if (cachedCorrections != null && cachedPath == path && cachedWriteTimeUtc == writeTimeUtc)
                    return cachedCorrections;
                var next = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
                if (writeTimeUtc != DateTime.MinValue)
                {
                    try { next = ReadCorrections(File.ReadAllText(path)); }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"Could not read {MetadataFileName}: {exception.Message}");
                    }
                }
                cachedPath = path;
                cachedWriteTimeUtc = writeTimeUtc;
                cachedCorrections = next;
                return cachedCorrections;
            }
        }

        private static string ModelKey(string sourcePath)
        {
            string normalized = GameAssetIndex.NormalizeModelPath(sourcePath ?? "");
            int separator = normalized.LastIndexOf('/');
            string name = separator >= 0 ? normalized.Substring(separator + 1) : normalized;
            int extension = name.LastIndexOf('.');
            if (extension >= 0) name = name.Substring(0, extension);
            if (name.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 5);
            return name;
        }
    }

    public sealed class StaticCastModel : IDisposable
    {
        private const long TextureMemoryBudgetBytes=256L*1024*1024;
        public Mesh Mesh;
        public Material[] Materials;
        public readonly List<string> TexturePaths = new List<string>();
        public int MissingAlbedo;
        public readonly List<string> AlbedoWarnings=new List<string>();
        private static long EstimatedTextureMemoryBytes(Texture2D texture)
        {
            long bytes=0;
            int width=texture.width,height=texture.height;
            for(int mip=0;mip<Math.Max(1,texture.mipmapCount);mip++)
            {
                bytes+=(long)width*height*4;
                width=Math.Max(1,width/2);height=Math.Max(1,height/2);
            }
            return bytes;
        }
        public static StaticCastModel Load(string path, Shader shader)
        {
            var result = new StaticCastModel();
            try
            {
                var roots = CastReader.Read(path);
                var nodes = roots.SelectMany(r => r.Descendants(CastReader.Mesh)).ToArray();
                if (nodes.Length == 0 || nodes.Length > 128) throw new InvalidDataException(L.T("#CAST_STATIC_MESHES_MISSING_TOO"));
                var materialNodes = roots.SelectMany(r => r.Descendants(CastReader.Material)).GroupBy(n => n.Hash).ToDictionary(g => g.Key, g => g.First());
                var positions = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>();
                var faces = new List<int[]>(); var materials = new List<Material>();
                Quaternion visualCorrection = ApexModelOrientation.VisualCorrection(path);
                long textureMemoryBytes=0;
                foreach (var node in nodes)
                {
                    var p = node.Floats("vp"); var n = node.Floats("vn"); var u = node.Floats("u0"); var f = node.Integers("f");
                    if (p == null || p.Length % 3 != 0 || f == null || f.Length % 3 != 0) throw new InvalidDataException(L.T("#INVALID_CAST_GEOMETRY"));
                    int vertices = p.Length / 3, offset = positions.Count;
                    if (vertices == 0 || vertices + offset > 1000000 || f.Length > 6000000) throw new InvalidDataException(L.T("#MODEL_TOO_LARGE_PROTOTYPE"));
                    for (int i = 0; i < vertices; i++)
                    {
                        // Preserve the source pivot. Source inches / Z-up -> editor metres / Y-up.
                        var position = visualCorrection *
                            (new Vector3(p[i * 3], p[i * 3 + 2], p[i * 3 + 1]) * ApexCoordinates.MetersPerUnit);
                        if (!float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z))
                            throw new InvalidDataException(L.T("#NON_FINITE_CAST_VERTEX"));
                        positions.Add(position);
                        normals.Add(n != null && n.Length == p.Length
                            ? visualCorrection * new Vector3(n[i * 3], n[i * 3 + 2], n[i * 3 + 1]).normalized
                            : Vector3.zero);
                        uv.Add(u != null && u.Length == vertices * 2 ? new Vector2(u[i * 2], 1 - u[i * 2 + 1]) : Vector2.zero);
                    }
                    var triangles = new int[f.Length];
                    for (int i = 0; i < f.Length; i += 3)
                    {
                        for (int j = 0; j < 3; j++) if (f[i + j] >= (ulong)vertices) throw new InvalidDataException(L.T("#CAST_VERTEX_INDEX_OUT_BOUNDS"));
                        triangles[i] = offset + (int)f[i]; triangles[i + 1] = offset + (int)f[i + 2]; triangles[i + 2] = offset + (int)f[i + 1];
                    }
                    faces.Add(triangles);
                    var material = new Material(shader) { name = "Apex static material" };
                    material.SetColor("_BaseColor", new Color(.65f, .74f, .78f));
                    material.SetFloat("_Smoothness", .2f);
                    materials.Add(material); result.Materials = materials.ToArray();
                    bool textured = false;
                    if (materialNodes.TryGetValue(node.Link("m"), out var source))
                    {
                        material.name = source.Text("n") ?? material.name;
                        string texturePath=SharedTextureCache.ResolveAlbedo(path,source);
                        if(texturePath!=null)
                        {
                            Texture2D texture=null;
                            try{texture=SharedTextureCache.Acquire(texturePath);}
                            catch(IOException ex){result.AlbedoWarnings.Add(material.name+": "+ex.Message);}
                            if(texture!=null)
                            {
                                if(!result.TexturePaths.Contains(texturePath))textureMemoryBytes+=EstimatedTextureMemoryBytes(texture);
                                result.TexturePaths.Add(texturePath);
                                if(textureMemoryBytes>TextureMemoryBudgetBytes)throw new InvalidDataException(L.F("#TEXTURE_MEMORY_BUDGET_EXCEEDED_ARG0",TextureMemoryBudgetBytes/(1024*1024)));
                                material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",Color.white);textured=true;
                            }
                        }
                        else result.AlbedoWarnings.Add(material.name+": "+L.T(source.Link("albedo")==0&&source.Link("diffuse")==0
                            ? "#NO_ALBEDO_BINDING_MATCHING_COLOR" : "#REFERENCED_ALBEDO_FILE_MISSING_CACHE"));
                    }
                    else result.AlbedoWarnings.Add(material.name+": "+L.T("#MATERIAL_DEFINITION_MISSING_MODEL"));
                    if (!textured) result.MissingAlbedo++;
                }
                result.Mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path), indexFormat = IndexFormat.UInt32 };
                result.Mesh.SetVertices(positions); result.Mesh.SetNormals(normals); result.Mesh.SetUVs(0, uv);
                result.Mesh.subMeshCount = faces.Count;
                for (int i = 0; i < faces.Count; i++) result.Mesh.SetTriangles(faces[i], i);
                if (normals.All(n => n == Vector3.zero)) result.Mesh.RecalculateNormals();
                result.Mesh.RecalculateBounds();
                return result;
            }
            catch { result.Dispose(); throw; }
        }
        private static uint BigEndian(byte[] b, int i) => ((uint)b[i] << 24) | ((uint)b[i + 1] << 16) | ((uint)b[i + 2] << 8) | b[i + 3];
        public void Dispose()
        {
            if (Mesh != null) UnityEngine.Object.Destroy(Mesh);
            if (Materials != null) foreach (var item in Materials) UnityEngine.Object.Destroy(item);
            foreach (var path in TexturePaths) SharedTextureCache.Release(path);
        }
    }
    public sealed class WorkspaceModelProvider : IModelProvider
    {
        private sealed class Resource { public StaticCastModel Model; public int References; public bool CollisionReady; }
        private readonly DemoModelProvider demo;
        private readonly Shader shader;
        private readonly Dictionary<string, string> prepared = new Dictionary<string, string>();
        private readonly Dictionary<string, Resource> loaded = new Dictionary<string, Resource>();
        public int LoadedModelCount => demo.LoadedModelCount + loaded.Count;
        public int LoadedGameModelCount => loaded.Count;
        public int CollisionModelCount => loaded.Values.Count(r => r.CollisionReady);
        public bool IsPrepared(string id) => prepared.ContainsKey(id);
        public void ForgetPrepared() { prepared.Clear(); }
        public WorkspaceModelProvider(Shader shader) { this.shader = shader; demo = new DemoModelProvider(shader); }
        public void Prepare(string id, string path) { prepared[id] = path; }
        public GameObject Create(string id) => Create(id, true);
        public GameObject Create(string id, bool collision)
        {
            if (!prepared.TryGetValue(id, out var path)) return demo.Create(id, collision);
            if (!loaded.TryGetValue(id, out var value))
            { value = new Resource { Model = StaticCastModel.Load(path, shader) }; loaded.Add(id, value); }
            if (collision && !value.CollisionReady) { ModelCollision.Bake(value.Model.Mesh); value.CollisionReady = true; }
            value.References++;
            var instance = new GameObject(id, typeof(MeshFilter), typeof(MeshRenderer));
            instance.GetComponent<MeshFilter>().sharedMesh = value.Model.Mesh;
            instance.GetComponent<MeshRenderer>().sharedMaterials = value.Model.Materials;
            if (collision) ModelCollision.Attach(instance, value.Model.Mesh);
            return instance;
        }
        public string AlbedoDiagnostics(string id) => loaded.TryGetValue(id,out var value)?string.Join("\n",value.Model.AlbedoWarnings):"";
        public int MissingAlbedo(string id) => loaded.TryGetValue(id, out var value) ? value.Model.MissingAlbedo : 0;
        public void Release(string id, GameObject instance)
        {
            if (loaded.TryGetValue(id, out var value) && instance.GetComponent<MeshFilter>().sharedMesh == value.Model.Mesh)
            {
                instance.SetActive(false); UnityEngine.Object.Destroy(instance);
                if (--value.References == 0) { value.Model.Dispose(); loaded.Remove(id); }
            }
            else demo.Release(id, instance);
        }
        public void Dispose() { foreach (var value in loaded.Values) value.Model.Dispose(); loaded.Clear(); demo.Dispose(); }
    }
    public static class ModelThumbnail
    {
        private const int Width = 256;
        private const int Height = 192;
        private const int ProbeWidth = 64;
        private const int ProbeHeight = 48;
        private static readonly Quaternion[] ThumbnailViews =
        {
            Quaternion.Euler(20, 145, 0),
            Quaternion.Euler(20, 325, 0),
            Quaternion.Euler(20, 55, 0),
            Quaternion.Euler(20, 235, 0)
        };

        public static Texture2D Render(GameObject model)
        {
            int oldLayer = model.layer; model.layer = 31;
            var go = new GameObject("Asset thumbnail camera", typeof(Camera));
            var camera = go.GetComponent<Camera>(); camera.enabled = false;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var probeRt = new RenderTexture(ProbeWidth, ProbeHeight, 16, RenderTextureFormat.ARGB32);
            var probe = new Texture2D(ProbeWidth, ProbeHeight, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                var bounds = model.GetComponent<Renderer>().bounds;
                float radius = Mathf.Max(.05f, bounds.extents.magnitude);
                camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.075f, .105f, .14f);
                camera.orthographic = true; camera.orthographicSize = radius * 1.12f;
                camera.nearClipPlane = .001f; camera.farClipPlane = radius * 10 + 10;
                camera.transform.rotation = MostVisibleView(camera, bounds, radius, probeRt, probe);
                camera.transform.position = bounds.center - camera.transform.forward * (radius * 3 + 1);
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); texture.Apply(); return texture;
            }
            finally
            {
                model.layer = oldLayer; RenderTexture.active = previous; camera.targetTexture = null;
                rt.Release(); probeRt.Release(); UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(probeRt);
                UnityEngine.Object.Destroy(probe); UnityEngine.Object.Destroy(go);
            }
        }

        private static Quaternion MostVisibleView(Camera camera, Bounds bounds, float radius, RenderTexture target, Texture2D probe)
        {
            int bestScore = -1, bestView = 0;
            for (int i = 0; i < ThumbnailViews.Length; i++)
            {
                camera.transform.rotation = ThumbnailViews[i];
                camera.transform.position = bounds.center - camera.transform.forward * (radius * 3 + 1);
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                probe.ReadPixels(new Rect(0, 0, ProbeWidth, ProbeHeight), 0, 0); probe.Apply(false, false);
                int score = ForegroundPixelCount(probe);
                if (score > bestScore) { bestScore = score; bestView = i; }
            }
            return ThumbnailViews[bestView];
        }

        private static int ForegroundPixelCount(Texture2D probe)
        {
            var pixels = probe.GetRawTextureData<Color32>();
            Color32 background = pixels[0];
            int count = 0;
            for (int i = 1; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                int difference = Math.Abs(pixel.r - background.r) + Math.Abs(pixel.g - background.g) + Math.Abs(pixel.b - background.b);
                if (difference > 18) count++;
            }
            return count;
        }
    }
}
