using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
namespace ReMap.Standalone
{
    public sealed partial class RsxAssetLibrary
    {
        private RsxPreviewSession previewSession;
        public int PreviewSessionStarts { get; private set; }
        public int PreviewArchiveLoads => previewSession?.ArchiveLoads??0;
        public int PreviewProcessId => previewSession?.ProcessId??0;
        public string PreferredPreviewArchive => previewSession?.LastArchive;
        private string SessionExecutable
        {
            get
            {
                string configured=Settings.rsxExecutable;
                if(string.IsNullOrEmpty(configured))return null;
                return File.Exists(configured)&&File.Exists(configured+".remap-session-v1")?configured:null;
            }
        }
        public bool ContinuousPreviewsSupported => SessionExecutable!=null;
        public bool BatchPreviewsSupported => ContinuousPreviewsSupported&&File.Exists(SessionExecutable+".remap-session-v2");
        public bool GeometryPreviewsSupported => ContinuousPreviewsSupported&&File.Exists(SessionExecutable+".remap-session-v3");
        private void EnsurePreviewSession(bool geometryOnly=false)
        {
            if(previewSession!=null&&previewSession.Alive&&previewSession.GeometryOnly==geometryOnly)return;
            ResetPreviewSession();
            string workerRoot=Path.Combine(CacheDirectory,"Worker");
            previewSession=new RsxPreviewSession(SessionExecutable,Path.Combine(workerRoot,"s"+Guid.NewGuid().ToString("N").Substring(0,8)),workerRoot,shutdown.Token,geometryOnly);
            PreviewSessionStarts++;
        }
        private void ResetPreviewSession()
        {
            previewSession?.Dispose();
            previewSession=null;
        }
        // Called with the existing library semaphore held. Do not cancel a loaded session for a page change.
        // Finish the current model, commit its cache, then honor cancellation before the next request.
        private string ExtractContinuous(GameAssetRecord entry,string[] targets)
        {
            var result=ExtractContinuousBatch(new[]{entry},targets,new AssetBatchResult());
            if(result.Paths.TryGetValue(entry.Id,out string path))return path;
            throw new IOException(result.Errors.TryGetValue(entry.Id,out string error)?error:L.T("#EXPORT_MISSING"));
        }
        private AssetBatchResult ExtractContinuousBatch(GameAssetRecord[] entries,string[] targets,AssetBatchResult result)
        {
            shutdown.Token.ThrowIfCancellationRequested();
            string archive=OriginArchive(entries[0],targets);
            string[] archives=Common.Concat(new[]{archive}).Distinct(StringComparer.OrdinalIgnoreCase).Select(a=>Path.Combine(PakDirectory,a)).Where(File.Exists).ToArray();
            try
            {
                EnsurePreviewSession();
                previewSession.Load(archives,archive);
                if(BatchPreviewsSupported&&entries.Length>1)
                {
                    try
                    {
                        CommitContinuousExports(entries,previewSession.ExportBatch(entries.Select(e=>e.guid).ToArray()),archive,result);
                        var missing=entries.Where(entry=>!result.Paths.ContainsKey(entry.Id)).ToArray();
                        if(missing.Length>0)RetryContinuousIndividually(missing,archives,archive,result,false,false);
                    }
                    catch(Exception ex)when(ex is IOException||ex is TimeoutException)
                    {
                        // Isolate the model that terminates RSX so the rest of the batch keeps its materials.
                        // Splitting the failed batch avoids restarting once per model in the common case.
                        ResetPreviewSession();
                        RetryContinuousTexturedPartitions(entries,archives,archive,result);
                    }
                }
                else foreach(var entry in entries)
                {
                    try {CommitContinuousExports(new[]{entry},previewSession.Export(entry.guid),archive,result);}
                    catch(Exception ex)when(GeometryPreviewsSupported&&(ex is IOException||ex is TimeoutException))
                    {RetryContinuousGeometry(new[]{entry},archive,result);continue;}
                    if(GeometryPreviewsSupported&&!result.Paths.ContainsKey(entry.Id))RetryContinuousGeometry(new[]{entry},archive,result);
                }
                return result;
            }
            catch(TimeoutException){ResetPreviewSession();throw;}
        }
        private void RetryContinuousTexturedPartitions(GameAssetRecord[] entries,string[] archives,string archive,AssetBatchResult result)
        {
            entries=entries.Where(entry=>!result.Paths.ContainsKey(entry.Id)).ToArray();
            if(entries.Length==0)return;
            if(entries.Length==1)
            {
                RetryContinuousIndividually(entries,archives,archive,result,false,false);
                return;
            }
            try
            {
                EnsurePreviewSession();previewSession.Load(archives,archive);
                CommitContinuousExports(entries,previewSession.ExportBatch(entries.Select(entry=>entry.guid).ToArray()),archive,result);
                var missing=entries.Where(entry=>!result.Paths.ContainsKey(entry.Id)).ToArray();
                if(missing.Length>0)RetryContinuousTexturedSplit(missing,archives,archive,result);
            }
            catch(Exception ex)when(ex is IOException||ex is TimeoutException)
            {
                ResetPreviewSession();
                RetryContinuousTexturedSplit(entries,archives,archive,result);
            }
        }
        private void RetryContinuousTexturedSplit(GameAssetRecord[] entries,string[] archives,string archive,AssetBatchResult result)
        {
            int middle=(entries.Length+1)/2;
            RetryContinuousTexturedPartitions(entries.Take(middle).ToArray(),archives,archive,result);
            RetryContinuousTexturedPartitions(entries.Skip(middle).ToArray(),archives,archive,result);
        }
        private void RetryContinuousGeometry(GameAssetRecord[] entries,string archive,AssetBatchResult result)
        {
            string[] archives=Common.Concat(new[]{archive}).Distinct(StringComparer.OrdinalIgnoreCase).Select(a=>Path.Combine(PakDirectory,a)).Where(File.Exists).ToArray();
            ResetPreviewSession();
            try
            {
                EnsurePreviewSession(true);previewSession.Load(archives,archive);
                if(entries.Length>1)
                {
                    try {CommitContinuousExports(entries,previewSession.ExportBatch(entries.Select(e=>e.guid).ToArray(),true),archive,result);}
                    catch(Exception ex)when(ex is IOException||ex is TimeoutException)
                    {RetryContinuousIndividually(entries,archives,archive,result,true,true);return;}
                    var missing=entries.Where(entry=>!result.Paths.ContainsKey(entry.Id)).ToArray();
                    if(missing.Length>0)RetryContinuousIndividually(missing,archives,archive,result,false,true);
                }
                else CommitContinuousExports(entries,previewSession.Export(entries[0].guid,true),archive,result);
            }
            catch(Exception ex)when(ex is IOException||ex is TimeoutException)
            {
                foreach(var entry in entries)if(!result.Paths.ContainsKey(entry.Id))result.Errors[entry.Id]=ex.Message;
                ResetPreviewSession();
            }
        }
        private void RetryContinuousIndividually(GameAssetRecord[] entries,string[] archives,string archive,AssetBatchResult result,bool resetFirst,bool geometryOnly)
        {
            if(resetFirst)ResetPreviewSession();
            foreach(var entry in entries)
            {
                shutdown.Token.ThrowIfCancellationRequested();
                if(result.Paths.ContainsKey(entry.Id))continue;
                try
                {
                    EnsurePreviewSession(geometryOnly);
                    previewSession.Load(archives,archive);
                    CommitContinuousExports(new[]{entry},previewSession.Export(entry.guid,geometryOnly),archive,result);
                    if(!geometryOnly&&GeometryPreviewsSupported&&!result.Paths.ContainsKey(entry.Id))RetryContinuousGeometry(new[]{entry},archive,result);
                }
                catch(Exception ex)when(ex is IOException||ex is TimeoutException)
                {
                    ResetPreviewSession();
                    if(!geometryOnly&&GeometryPreviewsSupported)RetryContinuousGeometry(new[]{entry},archive,result);
                    else result.Errors[entry.Id]=ex.Message;
                }
            }
        }
        private void CommitContinuousExports(GameAssetRecord[] entries,string output,string archive,AssetBatchResult result)
        {
            string[] exported=Directory.Exists(output)?Directory.GetFiles(output,"*_LOD0.cast",SearchOption.AllDirectories):Array.Empty<string>();
            foreach(var entry in entries)
            {
                try
                {
                    var matches=exported.Where(p=>string.Equals(Path.GetFileName(p),entry.Name+"_LOD0.cast",StringComparison.OrdinalIgnoreCase)).ToArray();
                    if(matches.Length!=1)throw new IOException(L.T("#RSX_DID_PRODUCE_SINGLE_CAST"));
                    CastReader.Read(matches[0]);
                    string source=Path.GetFullPath(Path.GetDirectoryName(matches[0])),modelRoot=ModelDirectory(entry);
                    string destination=Path.GetFullPath(Path.Combine(modelRoot,"e"+Guid.NewGuid().ToString("N").Substring(0,8)));
                    if(!source.StartsWith(Path.GetFullPath(previewSession.Root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!destination.StartsWith(Path.GetFullPath(modelRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException(L.T("#EXPORT_PATH_OUTSIDE_CACHE"));
                    Directory.CreateDirectory(modelRoot);Directory.Move(source,destination);
                    string cast=Path.Combine(destination,Path.GetFileName(matches[0]));
                    File.WriteAllText(Path.Combine(modelRoot,"complete.txt"),Path.GetRelativePath(modelRoot,cast)+"\n"+archive);
                    result.Paths[entry.Id]=cast;result.Errors.Remove(entry.Id);
                }
                catch(Exception ex)when(ex is IOException||ex is ArgumentException){result.Errors[entry.Id]=ex.Message;}
            }
        }
        public async Task ReleasePreviewSessionAsync()
        {
            await worker.WaitAsync();try{previewSession?.Dispose();previewSession=null;}finally{worker.Release();}
        }
    }
}
