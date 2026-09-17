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
        private void EnsurePreviewSession()
        {
            if(previewSession!=null&&previewSession.Alive)return;
            ResetPreviewSession();
            string workerRoot=Path.Combine(CacheDirectory,"Worker");
            previewSession=new RsxPreviewSession(SessionExecutable,Path.Combine(workerRoot,"s"+Guid.NewGuid().ToString("N").Substring(0,8)),workerRoot,shutdown.Token);
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
                        if(missing.Length>0)RetryContinuousIndividually(missing,archives,archive,result,false);
                    }
                    catch(IOException)
                    {
                        // A single unsupported R5F model can terminate the whole batch process. Retry each
                        // model in a fresh/reusable session so the other seven are not false failures.
                        RetryContinuousIndividually(entries,archives,archive,result,true);
                    }
                }
                else foreach(var entry in entries)
                    CommitContinuousExports(new[]{entry},previewSession.Export(entry.guid),archive,result);
                return result;
            }
            catch(TimeoutException){ResetPreviewSession();throw;}
        }
        private void RetryContinuousIndividually(GameAssetRecord[] entries,string[] archives,string archive,AssetBatchResult result,bool resetFirst)
        {
            if(resetFirst)ResetPreviewSession();
            foreach(var entry in entries)
            {
                shutdown.Token.ThrowIfCancellationRequested();
                if(result.Paths.ContainsKey(entry.Id))continue;
                try
                {
                    EnsurePreviewSession();
                    previewSession.Load(archives,archive);
                    CommitContinuousExports(new[]{entry},previewSession.Export(entry.guid),archive,result);
                }
                catch(Exception ex)when(ex is IOException||ex is TimeoutException)
                {
                    result.Errors[entry.Id]=ex.Message;
                    ResetPreviewSession();
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
