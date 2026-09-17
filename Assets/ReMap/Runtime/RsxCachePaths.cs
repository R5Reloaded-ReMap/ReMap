using ReMap.Standalone.Core;
using System;
using System.IO;
using System.Linq;
using UnityEngine;
namespace ReMap.Standalone {
    public sealed partial class RsxAssetLibrary {
        private const string CacheGenerationMarker = "active-generation.txt";

        public static string ActivateCacheGeneration(string cacheDirectory,string fingerprint) {
            if(string.IsNullOrWhiteSpace(fingerprint)||fingerprint.Length!=24||fingerprint.Any(c=>!Uri.IsHexDigit(c)))throw new ArgumentException(L.T("#INVALID_CACHE_GENERATION"));
            string root=Path.GetFullPath(cacheDirectory);Directory.CreateDirectory(root);
            string marker=Path.Combine(root,CacheGenerationMarker);
            string active=File.Exists(marker)?File.ReadAllText(marker).Trim():"";
            bool hasActiveIndex=Directory.Exists(Path.Combine(root,"index"));
            if(!string.Equals(active,fingerprint,StringComparison.OrdinalIgnoreCase)) {
                if(hasActiveIndex)ArchiveActiveGeneration(root,active);
                string legacy=Path.Combine(root,fingerprint),version=Path.Combine(root,"Versions",fingerprint);
                if(Directory.Exists(legacy))RestoreGeneration(legacy,root);
                else if(Directory.Exists(version))RestoreGeneration(version,root);
                File.WriteAllText(marker,fingerprint);
            }
            ArchiveLegacyDirectories(root);
            PromoteArchivedModels(root);
            return root;
        }

        private static void ArchiveLegacyDirectories(string root) {
            string versions=Path.Combine(root,"Versions");
            foreach(string source in Directory.GetDirectories(root).Where(path=>Path.GetFileName(path).Length==24&&Path.GetFileName(path).All(Uri.IsHexDigit)).ToArray()) {
                string destination=Path.Combine(versions,Path.GetFileName(source));
                if(Directory.Exists(destination)) {
                    bool canMerge=!Directory.Exists(Path.Combine(destination,"Models"))&&!Directory.Exists(Path.Combine(destination,"models"))&&!Directory.Exists(Path.Combine(destination,"index"));
                    if(canMerge) {foreach(string item in Directory.GetFileSystemEntries(source)) {string target=Path.Combine(destination,Path.GetFileName(item));if(File.Exists(target)||Directory.Exists(target)){canMerge=false;break;}}}
                    if(canMerge) {foreach(string item in Directory.GetFileSystemEntries(source)) {string target=Path.Combine(destination,Path.GetFileName(item));if(Directory.Exists(item))Directory.Move(item,target);else File.Move(item,target);}Directory.Delete(source);continue;}
                    destination+="-"+Guid.NewGuid().ToString("N").Substring(0,8);
                }
                Directory.CreateDirectory(versions);Directory.Move(source,destination);
            }
        }

        private static void ArchiveActiveGeneration(string root,string fingerprint) {
            string name=fingerprint.Length==24&&fingerprint.All(Uri.IsHexDigit)?fingerprint:"legacy-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string destination=Path.Combine(root,"Versions",name);
            if(Directory.Exists(Path.Combine(destination,"index")))destination+="-"+Guid.NewGuid().ToString("N").Substring(0,8);
            Directory.CreateDirectory(destination);MoveCachePart(root,destination,"index");
        }

        private static void RestoreGeneration(string source,string root) {
            PromoteModelDirectories(source,root);MoveCachePart(source,root,"index");
        }

        private static void PromoteArchivedModels(string root) {
            string versions=Path.Combine(root,"Versions");
            if(!Directory.Exists(versions))return;
            foreach(string generation in Directory.GetDirectories(versions))PromoteModelDirectories(generation,root);
        }

        private static void PromoteModelDirectories(string sourceRoot,string destinationRoot) {
            string shared=Path.Combine(destinationRoot,"Models");
            foreach(string name in new[]{"Models","models"}) {
                string source=Path.Combine(sourceRoot,name);if(!Directory.Exists(source))continue;
                foreach(string model in Directory.GetDirectories(source)) {
                    string destination=Path.Combine(shared,Path.GetFileName(model));
                    if(Directory.Exists(destination))continue;
                    Directory.CreateDirectory(shared);Directory.Move(model,destination);
                }
                if(!Directory.EnumerateFileSystemEntries(source).Any())Directory.Delete(source);
            }
        }

        private static void MoveCachePart(string sourceRoot,string destinationRoot,string sourceName,string destinationName=null) {
            string source=Path.Combine(sourceRoot,sourceName);if(!Directory.Exists(source))return;
            string destination=Path.Combine(destinationRoot,destinationName??sourceName);if(Directory.Exists(destination))throw new IOException(L.T("#CACHE_DESTINATION_ALREADY_EXISTS")+destination);
            Directory.CreateDirectory(destinationRoot);Directory.Move(source,destination);
        }

        public static string FindExistingModelDirectory(string localRoot,string guid) {
            string cache=Path.Combine(Path.GetFullPath(localRoot),"AssetCache");
            foreach(string candidate in new[]{Path.Combine(cache,"Models",guid),Path.Combine(cache,"models",guid)})if(File.Exists(Path.Combine(candidate,"complete.txt")))return candidate;
            if(Directory.Exists(cache))foreach(string generation in Directory.GetDirectories(cache).Where(path=>Path.GetFileName(path).Length==24))foreach(string folder in new[]{"Models","models"}) {string candidate=Path.Combine(generation,folder,guid);if(File.Exists(Path.Combine(candidate,"complete.txt")))return candidate;}
            string versions=Path.Combine(cache,"Versions");
            if(Directory.Exists(versions))foreach(string generation in Directory.GetDirectories(versions))foreach(string folder in new[]{"Models","models"}) {string candidate=Path.Combine(generation,folder,guid);if(File.Exists(Path.Combine(candidate,"complete.txt")))return candidate;}
            return null;
        }

        // Older batches repeated the model name and a 32-character batch ID in every texture path.
        // Moving the containing directory preserves CAST-relative texture references, including files over MAX_PATH.
        private static string CompactCachedModel(string modelRoot,string cast) {
            string source=Path.GetDirectoryName(cast);
            string prefix=Path.GetRelativePath(modelRoot,source).Replace('\\','/');
            if(!prefix.StartsWith("batch-",StringComparison.Ordinal))return cast;
            string destination=Path.GetFullPath(Path.Combine(modelRoot,"e"+Guid.NewGuid().ToString("N").Substring(0,8)));
            string boundary=Path.GetFullPath(modelRoot)+Path.DirectorySeparatorChar;
            if(!source.StartsWith(boundary,StringComparison.OrdinalIgnoreCase)||!destination.StartsWith(boundary,StringComparison.OrdinalIgnoreCase))throw new IOException(L.T("#INVALID_CACHE_PATH"));
            string marker=Path.Combine(modelRoot,"complete.txt"),manifestPath=Path.Combine(modelRoot,"textures.manifest.json");
            string oldMarker=File.ReadAllText(marker),oldManifest=File.Exists(manifestPath)?File.ReadAllText(manifestPath):null;
            string updated=Path.Combine(destination,Path.GetFileName(cast));
            Directory.Move(source,destination);
            try {
                if(oldManifest!=null) {
                    var manifest=JsonUtility.FromJson<SharedTextureCache.Manifest>(oldManifest);
                    foreach(var entry in manifest.entries)if(entry.source.StartsWith(prefix+"/",StringComparison.Ordinal))entry.source=Path.GetFileName(destination)+entry.source.Substring(prefix.Length);
                    File.WriteAllText(manifestPath,JsonUtility.ToJson(manifest,true));
                }
                string[] lines=oldMarker.Split('\n');lines[0]=Path.GetRelativePath(modelRoot,updated);File.WriteAllText(marker,string.Join("\n",lines));
                return updated;
            } catch {
                Directory.Move(destination,source);
                if(oldManifest!=null)File.WriteAllText(manifestPath,oldManifest);
                File.WriteAllText(marker,oldMarker);throw;
            }
        }
    }
}
