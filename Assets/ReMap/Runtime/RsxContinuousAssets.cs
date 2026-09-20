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
        private string[] previewArchivePlan;
        public int PreviewSessionStarts { get; private set; }
        public int PreviewArchiveLoads => previewSession?.ArchiveLoads??0;
        public int PreviewProcessId => previewSession?.ProcessId??0;
        public string PreferredPreviewArchive => previewSession?.LastArchive;
        public static int ReadSessionProtocolVersion(string executable)
        {
            if(string.IsNullOrWhiteSpace(executable)||!File.Exists(executable))return 0;
            string manifest=executable+".remap-session";
            if(File.Exists(manifest))
            {
                try{return int.TryParse(File.ReadAllText(manifest).Trim(),out int version)&&version>0?version:0;}
                catch(IOException){return 0;}
                catch(UnauthorizedAccessException){return 0;}
            }
            // Compatibility with RSX builds produced before the single version manifest.
            for(int version=4;version>=1;version--)
                if(File.Exists(executable+".remap-session-v"+version))return version;
            return 0;
        }
        private int SessionProtocolVersion=>ReadSessionProtocolVersion(Settings.rsxExecutable);
        private string SessionExecutable
        {
            get
            {
                string configured=Settings.rsxExecutable;
                if(string.IsNullOrEmpty(configured))return null;
                return File.Exists(configured)&&SessionProtocolVersion>=1?configured:null;
            }
        }
        public bool ContinuousPreviewsSupported => SessionExecutable!=null;
        public bool BatchPreviewsSupported => ContinuousPreviewsSupported&&SessionProtocolVersion>=2;
        public bool GeometryPreviewsSupported => ContinuousPreviewsSupported&&SessionProtocolVersion>=3;
        public bool BulkExportsSupported => ContinuousPreviewsSupported&&SessionProtocolVersion>=4;
        public bool BulkTextureRepairsSupported => BulkExportsSupported;
        private void SetTargetExtractionActivity(AssetExtractionOperation operation,string archive,int modelCount) =>
            SetExtractionActivity(AssetExtractionSource.TargetGame,operation,archive,modelCount);
        private void EnsurePreviewSession(bool geometryOnly=false,bool loadAllAssetTypes=false)
        {
            if(previewSession!=null&&previewSession.Alive&&previewSession.GeometryOnly==geometryOnly&&
                previewSession.LoadAllAssetTypes==loadAllAssetTypes)return;
            ResetPreviewSession();
            string workerRoot=Path.Combine(CacheDirectory,"Worker");
            previewSession=new RsxPreviewSession(SessionExecutable,Path.Combine(workerRoot,"s"+Guid.NewGuid().ToString("N").Substring(0,8)),workerRoot,shutdown.Token,geometryOnly,loadAllAssetTypes);
            PreviewSessionStarts++;
        }
        private void ResetPreviewSession()
        {
            previewSession?.Dispose();
            previewSession=null;
        }
        internal void CancelActivePreviewOperation() => previewSession?.Abort();
        private string[] PreviewArchivePlan(string[] targets,string requiredArchive)
        {
            if(previewArchivePlan!=null)
            {
                if(previewArchivePlan.Any(path=>string.Equals(Path.GetFileName(path),requiredArchive,StringComparison.OrdinalIgnoreCase)))return previewArchivePlan;
                previewArchivePlan=previewArchivePlan.Concat(new[]{Path.Combine(PakDirectory,requiredArchive)}).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                return previewArchivePlan;
            }
            string[] available=Directory.EnumerateFiles(PakDirectory,"*.rpak",SearchOption.TopDirectoryOnly).Select(Path.GetFileName).ToArray();
            string[] selectedMaps=SelectMapArchives(targets,available);
            var pendingOrigins=new System.Collections.Generic.HashSet<string>(Records.Where(record=>record.Supports(targets)&&CachedModel(record)==null).Select(record=>OriginArchive(record,targets)),StringComparer.OrdinalIgnoreCase);
            previewArchivePlan=Common.Concat(selectedMaps.Where(pendingOrigins.Contains)).Concat(new[]{requiredArchive}).Distinct(StringComparer.OrdinalIgnoreCase).Select(name=>Path.Combine(PakDirectory,name)).Where(File.Exists).ToArray();
            return previewArchivePlan;
        }
        // Called with the existing library semaphore held. Automatic work normally reuses the loaded
        // session; an explicit foreground request may abort it so it never waits behind a bulk export.
        private string ExtractContinuous(GameAssetRecord entry,string[] targets,CancellationToken cancellation)
        {
            var result=ExtractContinuousBatch(new[]{entry},targets,new AssetBatchResult(),cancellation);
            if(result.Paths.TryGetValue(entry.Id,out string path))return path;
            throw new IOException(result.Errors.TryGetValue(entry.Id,out string error)?error:L.T("#EXPORT_MISSING"));
        }
        private AssetBatchResult ExtractContinuousBatch(GameAssetRecord[] entries,string[] targets,AssetBatchResult result,CancellationToken cancellation)
        {
            shutdown.Token.ThrowIfCancellationRequested();cancellation.ThrowIfCancellationRequested();
            // The catalog and exported map identity stay tied to the modded source, but current
            // Apex archives are preferred for editor previews when the exact GUID still exists.
            bool officialAttempted=TryExtractOfficialPreviews(entries,targets,result,cancellation);
            cancellation.ThrowIfCancellationRequested();
            entries=entries.Where(entry=>!result.Paths.ContainsKey(entry.Id)).ToArray();
            if(entries.Length==0||officialAttempted)return result;
            result.TargetAttempts.UnionWith(entries.Select(entry=>entry.Id));
            string archive=OriginArchive(entries[0],targets);
            string[] archives=PreviewArchivePlan(targets,archive);
            try
            {
                SetTargetExtractionActivity(AssetExtractionOperation.LoadingArchives,archive,entries.Length);
                EnsurePreviewSession();
                previewSession.Load(archives,archive);
                SetTargetExtractionActivity(AssetExtractionOperation.ExportingModels,archive,entries.Length);
                if(BatchPreviewsSupported&&entries.Length>1)
                {
                    try
                    {
                        CommitContinuousExports(entries,previewSession.ExportMany(entries.Select(e=>e.guid).ToArray()),archive,result);
                        var missing=entries.Where(entry=>!result.Paths.ContainsKey(entry.Id)).ToArray();
                        if(missing.Length>0)RetryContinuousIndividually(missing,archives,archive,result,false,false,cancellation);
                    }
                    catch(Exception ex)when(ex is IOException||ex is TimeoutException)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        // Isolate the model that terminates RSX so the rest of the batch keeps its materials.
                        // Splitting the failed batch avoids restarting once per model in the common case.
                        ResetPreviewSession();
                        RetryContinuousTexturedPartitions(entries,archives,archive,result,cancellation);
                    }
                }
                else foreach(var entry in entries)
                {
                    try {CommitContinuousExports(new[]{entry},previewSession.Export(entry.guid),archive,result);}
                    catch(Exception ex)when(GeometryPreviewsSupported&&(ex is IOException||ex is TimeoutException))
                    {cancellation.ThrowIfCancellationRequested();RetryContinuousGeometry(new[]{entry},archives,archive,result,cancellation);continue;}
                    if(GeometryPreviewsSupported&&!result.Paths.ContainsKey(entry.Id))RetryContinuousGeometry(new[]{entry},archives,archive,result,cancellation);
                }
                return result;
            }
            catch(Exception ex)when((ex is IOException||ex is TimeoutException)&&cancellation.IsCancellationRequested)
            {ResetPreviewSession();cancellation.ThrowIfCancellationRequested();throw;}
            catch(TimeoutException){ResetPreviewSession();throw;}
        }
        private void RetryContinuousTexturedPartitions(GameAssetRecord[] entries,string[] archives,string archive,AssetBatchResult result,CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            entries=entries.Where(entry=>!result.Paths.ContainsKey(entry.Id)).ToArray();
            if(entries.Length==0)return;
            if(entries.Length==1)
            {
                RetryContinuousIndividually(entries,archives,archive,result,false,false,cancellation);
                return;
            }
            try
            {
                SetTargetExtractionActivity(AssetExtractionOperation.LoadingArchives,archive,entries.Length);
                EnsurePreviewSession();previewSession.Load(archives,archive);
                SetTargetExtractionActivity(AssetExtractionOperation.ExportingModels,archive,entries.Length);
                CommitContinuousExports(entries,previewSession.ExportMany(entries.Select(entry=>entry.guid).ToArray()),archive,result);
                var missing=entries.Where(entry=>!result.Paths.ContainsKey(entry.Id)).ToArray();
                if(missing.Length>0)RetryContinuousTexturedSplit(missing,archives,archive,result,cancellation);
            }
            catch(Exception ex)when(ex is IOException||ex is TimeoutException)
            {
                cancellation.ThrowIfCancellationRequested();
                ResetPreviewSession();
                RetryContinuousTexturedSplit(entries,archives,archive,result,cancellation);
            }
        }
        private void RetryContinuousTexturedSplit(GameAssetRecord[] entries,string[] archives,string archive,AssetBatchResult result,CancellationToken cancellation)
        {
            int middle=(entries.Length+1)/2;
            RetryContinuousTexturedPartitions(entries.Take(middle).ToArray(),archives,archive,result,cancellation);
            RetryContinuousTexturedPartitions(entries.Skip(middle).ToArray(),archives,archive,result,cancellation);
        }
        private void RetryContinuousGeometry(GameAssetRecord[] entries,string[] archives,string archive,AssetBatchResult result,CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            ResetPreviewSession();
            try
            {
                SetTargetExtractionActivity(AssetExtractionOperation.LoadingArchives,archive,entries.Length);
                EnsurePreviewSession(true);previewSession.Load(archives,archive);
                SetTargetExtractionActivity(AssetExtractionOperation.ExportingModels,archive,entries.Length);
                if(entries.Length>1)
                {
                    try {CommitContinuousExports(entries,previewSession.ExportMany(entries.Select(e=>e.guid).ToArray(),true),archive,result,true);}
                    catch(Exception ex)when(ex is IOException||ex is TimeoutException)
                    {cancellation.ThrowIfCancellationRequested();RetryContinuousIndividually(entries,archives,archive,result,true,true,cancellation);return;}
                    var missing=entries.Where(entry=>!result.Paths.ContainsKey(entry.Id)).ToArray();
                    if(missing.Length>0)RetryContinuousIndividually(missing,archives,archive,result,false,true,cancellation);
                }
                else CommitContinuousExports(entries,previewSession.Export(entries[0].guid,true),archive,result,true);
            }
            catch(Exception ex)when(ex is IOException||ex is TimeoutException)
            {
                cancellation.ThrowIfCancellationRequested();
                foreach(var entry in entries)if(!result.Paths.ContainsKey(entry.Id))result.Errors[entry.Id]=ex.Message;
                ResetPreviewSession();
            }
        }
        private void RetryContinuousIndividually(GameAssetRecord[] entries,string[] archives,string archive,AssetBatchResult result,bool resetFirst,bool geometryOnly,CancellationToken cancellation)
        {
            if(resetFirst)ResetPreviewSession();
            foreach(var entry in entries)
            {
                shutdown.Token.ThrowIfCancellationRequested();cancellation.ThrowIfCancellationRequested();
                if(result.Paths.ContainsKey(entry.Id))continue;
                try
                {
                    SetTargetExtractionActivity(AssetExtractionOperation.LoadingArchives,archive,1);
                    EnsurePreviewSession(geometryOnly);
                    previewSession.Load(archives,archive);
                    SetTargetExtractionActivity(AssetExtractionOperation.ExportingModels,archive,1);
                    CommitContinuousExports(new[]{entry},previewSession.Export(entry.guid,geometryOnly),archive,result,geometryOnly);
                    if(!geometryOnly&&GeometryPreviewsSupported&&!result.Paths.ContainsKey(entry.Id))RetryContinuousGeometry(new[]{entry},archives,archive,result,cancellation);
                }
                catch(Exception ex)when(ex is IOException||ex is TimeoutException)
                {
                    cancellation.ThrowIfCancellationRequested();
                    ResetPreviewSession();
                    if(!geometryOnly&&GeometryPreviewsSupported)RetryContinuousGeometry(new[]{entry},archives,archive,result,cancellation);
                    else result.Errors[entry.Id]=ex.Message;
                }
            }
        }
        private void CommitContinuousExports(GameAssetRecord[] entries,string output,string archive,AssetBatchResult result,bool geometryOnly=false)
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
                    File.WriteAllText(Path.Combine(modelRoot,"complete.txt"),Path.GetRelativePath(modelRoot,cast)+"\n"+archive+"\n"+(geometryOnly?"geometry":"textured")+"\n"+ActiveCacheGeneration());
                    result.Paths[entry.Id]=cast;result.Errors.Remove(entry.Id);
                }
                catch(Exception ex)when(ex is IOException||ex is ArgumentException){result.Errors[entry.Id]=ex.Message;}
            }
        }
        public async Task ReleasePreviewSessionAsync(CancellationToken cancellation=default)
        {
            await worker.WaitAsync(cancellation);try{cancellation.ThrowIfCancellationRequested();previewSession?.Dispose();previewSession=null;previewArchivePlan=null;}finally{worker.Release();}
        }
    }
}
