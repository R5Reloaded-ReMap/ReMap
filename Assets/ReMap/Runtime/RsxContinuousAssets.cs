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
        // Called with the existing library semaphore held. Do not cancel a loaded session for a page change.
        // Finish the current model, commit its cache, then honor cancellation before the next request.
        private string ExtractContinuous(GameAssetRecord entry,string[] targets)
        {
            shutdown.Token.ThrowIfCancellationRequested();
            if(previewSession==null||!previewSession.Alive)
            {
                previewSession?.Dispose();previewSession=null;
                string workerRoot=Path.Combine(CacheDirectory,"Worker");
                previewSession=new RsxPreviewSession(SessionExecutable,Path.Combine(workerRoot,"s"+Guid.NewGuid().ToString("N").Substring(0,8)),workerRoot,shutdown.Token);
                PreviewSessionStarts++;
            }
            string archive=OriginArchive(entry,targets);
            string[] archives=Common.Concat(new[]{archive}).Distinct(StringComparer.OrdinalIgnoreCase).Select(a=>Path.Combine(PakDirectory,a)).Where(File.Exists).ToArray();
            try
            {
                previewSession.Load(archives,archive);
                string output=previewSession.Export(entry.guid);
                var matches=Directory.GetFiles(output,"*_LOD0.cast",SearchOption.AllDirectories).Where(p=>string.Equals(Path.GetFileName(p),entry.Name+"_LOD0.cast",StringComparison.OrdinalIgnoreCase)).ToArray();
                if(matches.Length!=1)throw new IOException(L.T("#RSX_DID_PRODUCE_SINGLE_CAST"));
                CastReader.Read(matches[0]);
                string source=Path.GetFullPath(Path.GetDirectoryName(matches[0])),modelRoot=ModelDirectory(entry);
                string destination=Path.GetFullPath(Path.Combine(modelRoot,"e"+Guid.NewGuid().ToString("N").Substring(0,8)));
                if(!source.StartsWith(Path.GetFullPath(previewSession.Root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!destination.StartsWith(Path.GetFullPath(modelRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException(L.T("#EXPORT_PATH_OUTSIDE_CACHE"));
                Directory.CreateDirectory(modelRoot);Directory.Move(source,destination);
                string cast=Path.Combine(destination,Path.GetFileName(matches[0]));
                File.WriteAllText(Path.Combine(modelRoot,"complete.txt"),Path.GetRelativePath(modelRoot,cast)+"\n"+archive);
                return cast;
            }
            catch(TimeoutException){previewSession.Dispose();previewSession=null;throw;}
        }
        public async Task ReleasePreviewSessionAsync()
        {
            await worker.WaitAsync();try{previewSession?.Dispose();previewSession=null;}finally{worker.Release();}
        }
    }
}
