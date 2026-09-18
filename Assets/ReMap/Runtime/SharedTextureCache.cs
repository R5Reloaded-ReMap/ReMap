using ReMap.Standalone.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ReMap.Standalone
{
    // Manifests retain CAST-relative names; content-addressed PNGs are shared across all models.
    public static class SharedTextureCache
    {
        public const int PreviewMaximumSize = 512;
        [Serializable] public sealed class Entry { public string source, hash; }
        [Serializable] public sealed class Manifest
        {
            public int maximumSize;
            public List<Entry> entries = new List<Entry>();
        }
        private sealed class Resident { public Texture2D texture; public int references; }
        private static readonly Dictionary<string, Resident> resident = new Dictionary<string, Resident>(StringComparer.OrdinalIgnoreCase);
        public static int ResidentCount => resident.Count;
        public static string RootFor(string modelRoot)
        {
            for (var directory = new DirectoryInfo(Path.GetFullPath(modelRoot)); directory != null; directory = directory.Parent)
                if (string.Equals(directory.Name, "AssetCache", StringComparison.OrdinalIgnoreCase)) return Path.Combine(directory.FullName, "Textures");
            var generation = Directory.GetParent(modelRoot)?.Parent?.Parent;
            if (generation != null) return Path.Combine(generation.FullName, "Textures");
            throw new InvalidDataException(L.T("#INVALID_MODEL_CACHE_PATH"));
        }
        private static readonly Dictionary<string, SemaphoreSlim> normalizers = new Dictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
        public static async Task Normalize(string modelRoot, int limit,
            CancellationToken cancellation = default)
        {
            modelRoot = Path.GetFullPath(modelRoot);
            if (!normalizers.TryGetValue(modelRoot, out var gate)) normalizers.Add(modelRoot, gate = new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellation);
            try { await NormalizeCore(modelRoot, limit, cancellation); }
            finally { gate.Release(); }
        }
        private static async Task NormalizeCore(string modelRoot, int limit, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            limit = Mathf.Clamp(limit <= 0 ? PreviewMaximumSize : limit, 256, 2048);
            string shared = RootFor(modelRoot); Directory.CreateDirectory(shared);
            string manifestPath = Path.Combine(modelRoot, "textures.manifest.json");
            var manifest = File.Exists(manifestPath) ? JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath)) : new Manifest();
            manifest = manifest ?? new Manifest();
            manifest.entries = manifest.entries ?? new List<Entry>();
            if (manifest.maximumSize != limit)
            {
                // Older manifests point at already-normalized shared PNGs. Resize those directly so changing
                // the preview policy does not force RSX to extract every model again.
                foreach (var entry in manifest.entries)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (entry == null || string.IsNullOrEmpty(entry.hash) || entry.hash.Length != 64 ||
                        entry.hash.Any(c => !Uri.IsHexDigit(c))) continue;
                    string existing = Path.Combine(shared, entry.hash + ".png");
                    if (!File.Exists(existing)) continue;
                    byte[] normalized = NormalizePng(File.ReadAllBytes(existing), limit);
                    string hash = Store(shared, normalized);
                    if (hash == entry.hash) continue;
                    entry.hash = hash;
                    SaveManifest(manifestPath, manifest);
                    await Task.Yield();
                }
                manifest.maximumSize = limit;
                SaveManifest(manifestPath, manifest);
            }
            var rawTextures=Directory.EnumerateFiles(modelRoot, "*.png", SearchOption.AllDirectories).Where(p=>Path.GetDirectoryName(p)!=modelRoot).ToArray();
            var albedos=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if(rawTextures.Length>0)foreach(string cast in Directory.EnumerateFiles(modelRoot,"*.cast",SearchOption.AllDirectories))
                foreach(var material in CastReader.Read(cast).SelectMany(n=>n.Descendants(CastReader.Material)))
                    foreach(string relative in CastAlbedo.Candidates(material,CastAlbedo.ModelName(cast)))
                    {
                        string candidate=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(cast),relative.Replace('/',Path.DirectorySeparatorChar)));
                        if(!candidate.StartsWith(modelRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException(L.T("#TEXTURE_OUTSIDE_MODEL_CACHE"));
                        albedos.Add(candidate);
                    }
            foreach (string path in rawTextures)
            {
                cancellation.ThrowIfCancellationRequested();
                // Discard only this model's generated non-color exports. Existing shared cache entries remain intact.
                if(albedos.Count>0&&!albedos.Contains(Path.GetFullPath(path))&&!CastAlbedo.IsColorTextureName(path)){File.Delete(path);continue;}
                byte[] normalized = NormalizePng(File.ReadAllBytes(path), limit);
                string hash = Store(shared, normalized);
                string relative = Path.GetRelativePath(modelRoot, path).Replace('\\', '/');
                manifest.entries.RemoveAll(e => e.source == relative); manifest.entries.Add(new Entry { source = relative, hash = hash });
                manifest.maximumSize = limit;
                SaveManifest(manifestPath, manifest);
                // Delete only the processed cache file, after both shared content and its mapping are durable.
                File.Delete(path);
                await Task.Yield();
                cancellation.ThrowIfCancellationRequested();
            }
        }
        private static byte[] NormalizePng(byte[] bytes, int limit)
        {
            ValidatePng(bytes);
            Texture2D source = null, output = null; RenderTexture rt = null; var previous = RenderTexture.active;
            try
            {
                source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(source, bytes)) throw new InvalidDataException(L.T("#UNREADABLE_PNG"));
                if (Math.Max(source.width, source.height) <= limit) return bytes;
                float ratio = (float)limit / Math.Max(source.width, source.height);
                int width = Math.Max(1, Mathf.RoundToInt(source.width * ratio));
                int height = Math.Max(1, Mathf.RoundToInt(source.height * ratio));
                rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(source, rt); RenderTexture.active = rt;
                output = new Texture2D(width, height, TextureFormat.RGBA32, false);
                output.ReadPixels(new Rect(0, 0, width, height), 0, 0); output.Apply();
                return output.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                DestroyTexture(output);
                DestroyTexture(source);
            }
        }
        private static string Store(string shared, byte[] normalized)
        {
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(normalized)).Replace("-", "").ToLowerInvariant();
            string destination = Path.Combine(shared, hash + ".png");
            if (!File.Exists(destination)) File.WriteAllBytes(destination, normalized);
            return hash;
        }
        private static void SaveManifest(string manifestPath, Manifest manifest)
        {
            string temporary = manifestPath + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(manifest, true));
            if (File.Exists(manifestPath)) File.Replace(temporary, manifestPath, null); else File.Move(temporary, manifestPath);
        }
        public static string Resolve(string castPath, string relative)
        {
            string folder = Path.GetFullPath(Path.GetDirectoryName(castPath));
            string original = Path.GetFullPath(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!original.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L.T("#TEXTURE_OUTSIDE_MODEL_CACHE"));
            if (File.Exists(original)) return original;
            var current = new DirectoryInfo(folder);
            for (int i = 0; current != null && i < 8; i++, current = current.Parent)
            {
                string manifestPath = Path.Combine(current.FullName, "textures.manifest.json");
                if (!File.Exists(manifestPath)) continue;
                var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
                string key = Path.GetRelativePath(current.FullName, original).Replace('\\', '/');
                var entry = manifest.entries.FirstOrDefault(e => e.source == key);
                if (entry == null || entry.hash.Length != 64 || entry.hash.Any(c => !Uri.IsHexDigit(c))) return null;
                string shared = Path.Combine(RootFor(current.FullName), entry.hash + ".png");
                return File.Exists(shared) ? shared : null;
            }
            return null;
        }
        public static string ResolveAlbedo(string castPath,CastNode material)
        {
            foreach(string relative in CastAlbedo.Candidates(material,CastAlbedo.ModelName(castPath)))
            {
                string resolved=Resolve(castPath,relative);
                if(resolved!=null&&File.Exists(resolved)&&Path.GetExtension(resolved).Equals(".png",StringComparison.OrdinalIgnoreCase))return resolved;
            }
            return null;
        }
        public static Texture2D Acquire(string path)
        {
            if (!resident.TryGetValue(path, out var item))
            {
                byte[] bytes = File.ReadAllBytes(path); ValidatePng(bytes);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (!ImageConversion.LoadImage(texture, bytes, true)) { DestroyTexture(texture); throw new InvalidDataException(L.T("#UNREADABLE_PNG")); }
                item = new Resident { texture = texture }; resident.Add(path, item);
            }
            item.references++; return item.texture;
        }
        public static void Release(string path)
        {
            if (!resident.TryGetValue(path, out var item)) return;
            if (--item.references == 0) { DestroyTexture(item.texture); resident.Remove(path); }
        }
        private static void DestroyTexture(Texture texture)
        {
            if (texture == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }
        private static void ValidatePng(byte[] bytes)
        {
            uint Big(int offset) => ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
            if (bytes.Length < 24 || bytes.Length > 64 * 1024 * 1024 || bytes[0] != 137 || bytes[1] != 80) throw new InvalidDataException(L.T("#INVALID_PNG"));
            if (Big(16) > 8192 || Big(20) > 8192) throw new InvalidDataException(L.T("#TEXTURE_EXCEEDS_8192_PX"));
        }
    }
}
