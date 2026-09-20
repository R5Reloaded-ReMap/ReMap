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
        public sealed class AlbedoInspection
        {
            public readonly HashSet<ulong> materialHashes = new HashSet<ulong>();
            public int missing, suspicious;
            public bool NeedsFallback => materialHashes.Count > 0;
        }
        private sealed class Resident { public Texture2D texture; public int references; }
        private static readonly Dictionary<string, Resident> resident = new Dictionary<string, Resident>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, bool> suspiciousPngs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
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
        public static bool ManifestMatchesMaximum(string modelRoot,int limit)
        {
            try
            {
                string path=Path.Combine(modelRoot,"textures.manifest.json");
                if(!File.Exists(path))return false;
                var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
                return manifest!=null&&manifest.maximumSize==Mathf.Clamp(limit<=0?PreviewMaximumSize:limit,256,2048);
            }
            catch{return false;}
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
            var normalizedSources=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                normalizedSources.Add(relative);
                manifest.entries.RemoveAll(e => e.source == relative); manifest.entries.Add(new Entry { source = relative, hash = hash });
                manifest.maximumSize = limit;
                SaveManifest(manifestPath, manifest);
                // Delete only the processed cache file, after both shared content and its mapping are durable.
                File.Delete(path);
                await Task.Yield();
                cancellation.ThrowIfCancellationRequested();
            }
            if(rawTextures.Length>0&&manifest.entries.RemoveAll(e=>e==null||!normalizedSources.Contains(e.source))>0)
                SaveManifest(manifestPath,manifest);
        }

        public static int CollectGarbage(string cacheRoot)
        {
            if(string.IsNullOrWhiteSpace(cacheRoot))return 0;
            string root=Path.GetFullPath(cacheRoot),models=Path.Combine(root,"Models"),textures=Path.Combine(root,"Textures");
            if(!Directory.Exists(models)||!Directory.Exists(textures))return 0;
            var referenced=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(string path in Directory.EnumerateFiles(models,"textures.manifest.json",SearchOption.AllDirectories))
            {
                try
                {
                    var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
                    // Legacy manifests may reference corrupted or oversized PNGs. Do not keep those files
                    // alive: the model cache rejects the manifest and RSX will recreate it on demand.
                    if(manifest?.entries==null||manifest.maximumSize!=PreviewMaximumSize)continue;
                    foreach(var entry in manifest.entries)
                        if(entry!=null&&!string.IsNullOrEmpty(entry.hash)&&entry.hash.Length==64&&entry.hash.All(Uri.IsHexDigit))referenced.Add(entry.hash);
                }
                catch(IOException){}
            }
            int removed=0;
            foreach(string path in Directory.EnumerateFiles(textures,"*.png",SearchOption.TopDirectoryOnly))
            {
                string hash=Path.GetFileNameWithoutExtension(path);
                if(hash.Length!=64||hash.Any(c=>!Uri.IsHexDigit(c))||referenced.Contains(hash))continue;
                try{File.Delete(path);removed++;}catch(IOException){}catch(UnauthorizedAccessException){}
            }
            return removed;
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
        public static AlbedoInspection InspectAlbedos(string castPath)
        {
            var result=new AlbedoInspection();
            foreach(var material in CastReader.Read(castPath).SelectMany(root=>root.Descendants(CastReader.Material)).GroupBy(node=>node.Hash).Select(group=>group.First()))
            {
                string path=ResolveAlbedo(castPath,material);
                if(path==null)
                {
                    result.missing++;
                    if(material.Hash!=0)result.materialHashes.Add(material.Hash);
                    continue;
                }
                if(!suspiciousPngs.TryGetValue(path,out bool suspicious))
                    suspiciousPngs[path]=suspicious=LooksLikeRandomCorruption(path);
                if(suspicious)
                {
                    result.suspicious++;
                    if(material.Hash!=0)result.materialHashes.Add(material.Hash);
                }
            }
            return result;
        }
        private static bool LooksLikeRandomCorruption(string path)
        {
            byte[] bytes=File.ReadAllBytes(path);ValidatePng(bytes);Texture2D texture=null;
            try
            {
                texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                if(!ImageConversion.LoadImage(texture,bytes,false))return false;
                return LooksLikeRandomCorruption(texture.GetPixels32(),texture.width,texture.height,bytes.Length);
            }
            finally{DestroyTexture(texture);}
        }
        public static bool LooksLikeRandomCorruption(Color32[] pixels,int width,int height,int encodedLength)
        {
            if(pixels==null||width<32||height<32||pixels.Length<width*height)return false;
            int stride=Math.Max(1,Math.Max(width,height)/256),count=0,high=0;
            double sx=0,sy=0,sxx=0,syy=0,sxy=0,difference=0;
            for(int y=0;y<height;y+=stride)for(int x=0;x+stride<width;x+=stride)
            {
                Color32 a=pixels[y*width+x],b=pixels[y*width+x+stride];
                double la=(a.r*54+a.g*183+a.b*19)/256.0,lb=(b.r*54+b.g*183+b.b*19)/256.0;
                double d=(Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b))/(3.0*255.0);
                sx+=la;sy+=lb;sxx+=la*la;syy+=lb*lb;sxy+=la*lb;difference+=d;if(d>.35)high++;count++;
            }
            if(count<128)return false;
            double covariance=count*sxy-sx*sy,varianceX=count*sxx-sx*sx,varianceY=count*syy-sy*sy;
            double correlation=varianceX>0&&varianceY>0?covariance/Math.Sqrt(varianceX*varianceY):1;
            double encodedRatio=encodedLength/(double)(width*height*4);
            return correlation<.08&&difference/count>.27&&high/(double)count>.20&&encodedRatio>.45;
        }
        public static int ReplaceAlbedosFromOfficial(string modelRoot,string legacyCast,string officialCast,string officialRoot,IEnumerable<ulong> requested)
        {
            var wanted=new HashSet<ulong>(requested??Array.Empty<ulong>());if(wanted.Count==0)return 0;
            modelRoot=Path.GetFullPath(modelRoot);officialRoot=Path.GetFullPath(officialRoot);
            string manifestPath=Path.Combine(modelRoot,"textures.manifest.json"),shared=RootFor(modelRoot);Directory.CreateDirectory(shared);
            var manifest=File.Exists(manifestPath)?JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath)):new Manifest();
            manifest=manifest??new Manifest();manifest.entries=manifest.entries??new List<Entry>();
            var legacy=CastReader.Read(legacyCast).SelectMany(root=>root.Descendants(CastReader.Material)).Where(node=>wanted.Contains(node.Hash)).GroupBy(node=>node.Hash).ToDictionary(group=>group.Key,group=>group.First());
            var official=CastReader.Read(officialCast).SelectMany(root=>root.Descendants(CastReader.Material)).Where(node=>wanted.Contains(node.Hash)).GroupBy(node=>node.Hash).ToDictionary(group=>group.Key,group=>group.First());
            int replaced=0;
            foreach(var pair in legacy)
            {
                if(pair.Key==0||!official.TryGetValue(pair.Key,out var officialMaterial))continue;
                string officialPng=null;
                foreach(string relative in CastAlbedo.Candidates(officialMaterial,CastAlbedo.ModelName(officialCast)))
                {
                    string candidate=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(officialCast),relative.Replace('/',Path.DirectorySeparatorChar)));
                    if(candidate.StartsWith(officialRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&File.Exists(candidate)){officialPng=candidate;break;}
                }
                if(officialPng==null)continue;
                string source=null;
                foreach(string relative in CastAlbedo.Candidates(pair.Value,CastAlbedo.ModelName(legacyCast)))
                {
                    string candidate=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(legacyCast),relative.Replace('/',Path.DirectorySeparatorChar)));
                    if(!candidate.StartsWith(modelRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))continue;
                    string key=Path.GetRelativePath(modelRoot,candidate).Replace('\\','/');
                    if(source==null)source=key;
                    if(manifest.entries.Any(entry=>entry!=null&&string.Equals(entry.source,key,StringComparison.OrdinalIgnoreCase))){source=key;break;}
                }
                if(source==null)continue;
                string hash=Store(shared,NormalizePng(File.ReadAllBytes(officialPng),PreviewMaximumSize));
                manifest.entries.RemoveAll(entry=>entry!=null&&string.Equals(entry.source,source,StringComparison.OrdinalIgnoreCase));
                manifest.entries.Add(new Entry{source=source,hash=hash});replaced++;
            }
            if(replaced>0){manifest.maximumSize=PreviewMaximumSize;SaveManifest(manifestPath,manifest);}
            return replaced;
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
