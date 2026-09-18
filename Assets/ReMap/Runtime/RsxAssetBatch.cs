using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
namespace ReMap.Standalone
{
    public sealed class AssetBatchResult {
        public readonly Dictionary<string,string> Paths=new Dictionary<string,string>();
        public readonly Dictionary<string,string> Errors=new Dictionary<string,string>();
    }
    public sealed partial class RsxAssetLibrary
    {
        public string OriginArchive(GameAssetRecord record,string[] targets) => record.origins.First(o=>o.mapId==""||targets.Contains(o.mapId)).archive;
        public async Task<AssetBatchResult> ExtractBatchAsync(GameAssetRecord[] entries,string[] targets,CancellationToken cancellation=default) {
            if(entries.Length==0||entries.Length>8)throw new ArgumentException(L.T("#BATCH_1_8_MODELS_REQUIRED"));
            if(CacheRoot==null||entries.Any(e=>!e.Supports(targets)))throw new InvalidOperationException(L.T("#INDEX_COMPATIBLE_MAPS_EXTRACTION"));
            var result=new AssetBatchResult();
            if(!ContinuousPreviewsSupported&&!UsesForkFeatures) {
                foreach(var entry in entries)try {result.Paths[entry.Id]=await ExtractAsync(entry,targets,cancellation);}catch(Exception ex)when(!(ex is OperationCanceledException)){result.Errors[entry.Id]=ex.Message;}
                return result;
            }
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token,cancellation);
            await worker.WaitAsync(linked.Token);
            try {
                var pending=entries.Where(e=> {var path=CachedModel(e);if(path==null)return true;result.Paths[e.Id]=path;return false;}).ToArray();
                if(pending.Length==0)return result;
                if(ContinuousPreviewsSupported)
                {
                    string origin=OriginArchive(pending[0],targets);
                    if(pending.Any(e=>OriginArchive(e,targets)!=origin)||pending.Select(e=>e.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=pending.Length)throw new ArgumentException(L.T("#BATCH_SHARE_ARCHIVE_HAVE_DISTINCT"));
                    linked.Token.ThrowIfCancellationRequested();
                    return await Task.Run(()=>ExtractContinuousBatch(pending,targets,result),shutdown.Token);
                }
                string archive=OriginArchive(pending[0],targets);
                if(pending.Any(e=>OriginArchive(e,targets)!=archive)||pending.Select(e=>e.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=pending.Length)throw new ArgumentException(L.T("#BATCH_SHARE_ARCHIVE_HAVE_DISTINCT"));
                return await Task.Run(()=> {
                    string batch=Path.Combine(CacheDirectory,"Worker","b"+Guid.NewGuid().ToString("N").Substring(0,12));Directory.CreateDirectory(batch);
                    void Export(GameAssetRecord[] models,bool geometry) {
                        string filter=Path.Combine(batch,geometry?"geometry-guids.txt":"guids.txt");File.WriteAllLines(filter,models.Select(e=>e.guid));
                        string output=Path.Combine(batch,geometry?"geometry":"textured");Directory.CreateDirectory(output);
                        var args=new List<string>{"-export","--exporttypes","mdl_","--exportdir",output,"--exportguids",filter};
                        if(!geometry){args.Add("-matltextures");args.Add("--texturemaxsize");args.Add(SharedTextureCache.PreviewMaximumSize.ToString());}
                        foreach(string dependency in geometry?new[]{"common_early.rpak"}:Common)if(dependency!=archive&&File.Exists(Path.Combine(PakDirectory,dependency)))args.Add(Path.Combine(PakDirectory,dependency));
                        args.Add(Path.Combine(PakDirectory,archive));string failure=null;
                        try {RunRsx(args,batch,geometry,linked.Token);}catch(Exception ex)when(ex is IOException||ex is TimeoutException){failure=ex.Message;}
                        foreach(var entry in models) {
                            var matches=Directory.GetFiles(output,"*_LOD0.cast",SearchOption.AllDirectories).Where(p=>string.Equals(Path.GetFileName(p),entry.Name+"_LOD0.cast",StringComparison.OrdinalIgnoreCase)).ToArray();
                            if(matches.Length!=1){result.Errors[entry.Id]=failure??L.T("#RSX_DID_EXPORT_MODEL_LOG")+Path.Combine(batch,geometry?"rsx-geometry.log":"rsx-worker.log");continue;}
                            try {
                                CastReader.Read(matches[0]);
                                string source=Path.GetFullPath(Path.GetDirectoryName(matches[0]));string modelRoot=ModelDirectory(entry);
                                string destination=Path.GetFullPath(Path.Combine(modelRoot,"e"+Guid.NewGuid().ToString("N").Substring(0,8)));
                                if(!source.StartsWith(Path.GetFullPath(batch)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!destination.StartsWith(Path.GetFullPath(modelRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException(L.T("#EXPORT_PATH_OUTSIDE_CACHE"));
                                Directory.CreateDirectory(Path.GetDirectoryName(destination));Directory.Move(source,destination);
                                string cast=Path.Combine(destination,Path.GetFileName(matches[0]));
                                File.WriteAllText(Path.Combine(modelRoot,"complete.txt"),Path.GetRelativePath(modelRoot,cast)+"\n"+archive+"\n"+(geometry?"geometry":"textured")+"\n"+ActiveCacheGeneration());
                                result.Paths[entry.Id]=cast;result.Errors.Remove(entry.Id);
                            }catch(Exception ex)when(ex is IOException||ex is ArgumentException){result.Errors[entry.Id]=ex.Message;}
                        }
                    }
                    try {
                        Export(pending,false);var missing=pending.Where(e=>!result.Paths.ContainsKey(e.Id)).ToArray();
                        if(missing.Length>0)Export(missing,true);
                    } catch(OperationCanceledException) {
                        // Only discard this operation's incomplete exports, inside its verified cache directory.
                        string parent=Path.GetFullPath(Path.Combine(CacheDirectory,"Worker"))+Path.DirectorySeparatorChar;
                        if(Path.GetFullPath(batch).StartsWith(parent,StringComparison.OrdinalIgnoreCase))Directory.Delete(batch,true);
                        throw;
                    }
                    return result;
                },linked.Token);
            }finally {worker.Release();}
        }
    }
}
