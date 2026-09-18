using ReMap.Standalone.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ReMap.Standalone
{
    // Owned by RsxAssetLibrary's semaphore. The protocol acknowledges complete, closed exports.
    internal sealed class RsxPreviewSession : IDisposable
    {
        private readonly Process process;
        private readonly StreamWriter log;
        private readonly object logGate = new object();
        private readonly CancellationToken shutdown;
        private string loadedArchives;
        private long logged;
        internal readonly string Root;
        internal string LastArchive { get; private set; }
        internal int ArchiveLoads { get; private set; }
        internal int ProcessId => process.Id;
        internal bool GeometryOnly { get; }
        internal bool Alive { get { try { return !process.HasExited; } catch { return false; } } }
        internal RsxPreviewSession(string executable,string root,string workingDirectory,CancellationToken shutdown,bool geometryOnly=false)
        {
            this.shutdown=shutdown;GeometryOnly=geometryOnly;Root=root;Directory.CreateDirectory(root);Directory.CreateDirectory(workingDirectory);
            log=new StreamWriter(Path.Combine(root,"session.log"),false){AutoFlush=true};
            process=new Process();
            string threads=Math.Min(4,Math.Max(1,Environment.ProcessorCount/2)).ToString();
            // Several R5F asset loaders mutate shared registries while parsing. Keep archive parsing serial;
            // model export remains parallel and the loaded archive is reused across batches.
            var arguments=new[]{"-nogui","-embedded","-export","--loadwhitelist",geometryOnly?"mdl_,Ptch":"mdl_,matl,txtr,shdr,shds,Ptch","--parsethreads","1","--exportthreads",threads}
                .Concat(geometryOnly?Array.Empty<string>():new[]{"-matltextures","--texturemaxsize",SharedTextureCache.PreviewMaximumSize.ToString()})
                .Concat(new[]{"--remap-session",root}).ToArray();
            if(!geometryOnly&&File.Exists(executable+".remap-albedo-v1"))arguments=arguments.Concat(new[]{"-albedoonly"}).ToArray();
            process.StartInfo=new ProcessStartInfo{FileName=executable,Arguments=string.Join(" ",arguments.Select(Quote)),WorkingDirectory=workingDirectory,
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
                StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            process.ErrorDataReceived+=(_,e)=>WriteLog(e.Data);
            try
            {
                if(!process.Start())throw new IOException(L.T("#UNABLE_START_RSX_PREVIEW_SESSION"));
                process.BeginErrorReadLine();
                var ready=ReadReply();if(ready.Length<3||ready[1]!="READY"||ready[2]!="1")throw new IOException(L.T("#UNSUPPORTED_RSX_SESSION_PROTOCOL"));
            }
            catch {Dispose();throw;}
        }
        private static string Quote(string value)
        {
            if(value.IndexOfAny(new[]{'"','\r','\n','\t'})>=0)throw new ArgumentException(L.T("#INVALID_RSX_SESSION_ARGUMENT"));
            return "\""+value.TrimEnd('\\')+"\"";
        }
        private void WriteLog(string text)
        {
            if(text==null)return;
            lock(logGate) {if(logged>=8*1024*1024)return;try{log.WriteLine(text);logged+=text.Length;}catch(ObjectDisposedException){}}
        }
        private string[] ReadReply()
        {
            var timer=Stopwatch.StartNew();
            while(true)
            {
                shutdown.ThrowIfCancellationRequested();
                int remaining=Math.Max(1,180000-(int)timer.ElapsedMilliseconds);
                var read=process.StandardOutput.ReadLineAsync();
                if(!read.Wait(remaining,shutdown))throw new TimeoutException(L.T("#RSX_SESSION_EXCEEDED_THREE_MINUTES"));
                string line=read.GetAwaiter().GetResult();
                if(line==null)throw new IOException(L.T("#RSX_PREVIEW_SESSION_EXITED_LOG")+Path.Combine(Root,"session.log"));
                WriteLog(line);
                if(line.StartsWith("REMAP_SESSION\t",StringComparison.Ordinal))return line.Split('\t');
                if(timer.ElapsedMilliseconds>=180000)throw new TimeoutException(L.T("#RSX_SESSION_TIMED_OUT_LOG")+Path.Combine(Root,"session.log"));
            }
        }
        private void Send(string line){shutdown.ThrowIfCancellationRequested();process.StandardInput.WriteLine(line);process.StandardInput.Flush();}
        internal void Load(string[] archives,string origin)
        {
            foreach(string path in archives)if(path.IndexOfAny(new[]{'\r','\n','\t'})>=0)throw new ArgumentException(L.T("#INVALID_ARCHIVE_PATH"));
            string key=string.Join("\t",archives);
            if(key!=loadedArchives)
            {
                Send("LOAD\t"+key);var reply=ReadReply();
                if(reply.Length<2||reply[1]!="LOADED")throw new IOException(L.T("#RSX_ARCHIVE_LOAD_FAILED")+string.Join(" ",reply));
                loadedArchives=key;ArchiveLoads++;
            }
            LastArchive=origin;
        }
        internal string Export(string guid,bool geometryOnly=false)
        {
            string job=Guid.NewGuid().ToString("N").Substring(0,8);
            Send((geometryOnly?"EXPORTGEOMETRY":"EXPORT")+"\t"+guid+"\t"+job);var reply=ReadReply();
            if(reply.Length!=4||reply[1]!="DONE"||!string.Equals(reply[2],guid,StringComparison.OrdinalIgnoreCase)||reply[3]!=job)
                throw new IOException(L.T("#RSX_MODEL_EXPORT_FAILED")+string.Join(" ",reply));
            return Path.Combine(Root,job);
        }
        internal string ExportBatch(string[] guids,bool geometryOnly=false)
        {
            if(guids==null||guids.Length<2||guids.Length>8)throw new ArgumentException(L.T("#BATCH_1_8_MODELS_REQUIRED"));
            string job=Guid.NewGuid().ToString("N").Substring(0,8);
            Send((geometryOnly?"EXPORTBATCHGEOMETRY":"EXPORTBATCH")+"\t"+job+"\t"+string.Join("\t",guids));var reply=ReadReply();
            if(reply.Length!=3||reply[1]!="BATCHDONE"||reply[2]!=job)
                throw new IOException(L.T("#RSX_MODEL_EXPORT_FAILED")+string.Join(" ",reply));
            return Path.Combine(Root,job);
        }
        public void Dispose()
        {
            try
            {
                if(!process.HasExited)
                {
                    try {process.StandardInput.WriteLine("QUIT");process.StandardInput.Flush();process.StandardInput.Close();} catch(IOException){}
                    if(!process.WaitForExit(2000)){process.Kill();process.WaitForExit(2000);}
                }
            }
            catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
            process.Dispose();lock(logGate)log.Dispose();
        }
    }
}
