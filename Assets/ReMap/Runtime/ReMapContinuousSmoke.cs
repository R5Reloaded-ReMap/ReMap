using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
using UnityEngine;
namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private async Task CheckContinuousPreviews()
        {
            await TreeFrames(12);
            string sourceRoot=RsxAssetLibrary.FindLocalRoot();
            string[] targets=assetLibrary.Maps.Take(2).Select(map=>map.Id).ToArray();
            if(targets.Length==0)throw new Exception("No map archive was detected for the active game.");
            await assetLibrary.IndexAsync(targets,null);
            string qaRoot=Path.Combine(sourceRoot,"Logs","SQA",DateTime.UtcNow.ToString("HHmmss"));Directory.CreateDirectory(qaRoot);
            File.Copy(Path.Combine(sourceRoot,"asset-source.local.json"),Path.Combine(qaRoot,"asset-source.local.json"));
            string index=Path.Combine(qaRoot,"AssetCache","index");Directory.CreateDirectory(index);
            foreach(var csv in Directory.GetFiles(Path.Combine(assetLibrary.CacheRoot,"index"),"*.csv"))File.Copy(csv,Path.Combine(index,Path.GetFileName(csv)));
            assetLibrary.Dispose();assetLibrary=new RsxAssetLibrary(qaRoot);await assetLibrary.IndexAsync(targets,null);
            if(!assetLibrary.ContinuousPreviewsSupported)throw new Exception("Continuous backend was not detected.");
            if(!assetLibrary.BatchPreviewsSupported)throw new Exception("Batch backend was not detected.");
            var records=assetLibrary.Records.Where(record=>record.Supports(targets)).GroupBy(record=>assetLibrary.OriginArchive(record,targets),StringComparer.OrdinalIgnoreCase)
                .Select(group=>group.GroupBy(record=>record.Name,StringComparer.OrdinalIgnoreCase).Select(items=>items.First()).Take(8).ToArray()).FirstOrDefault(group=>group.Length>=2);
            if(records==null)throw new Exception("No archive with at least two distinct models was indexed for the active game.");
            var timing=System.Diagnostics.Stopwatch.StartNew();
            AssetBatchResult extracted=await assetLibrary.ExtractBatchAsync(records,targets);
            for(int i=0;i<records.Length;i++)
            {
                var itemTime=System.Diagnostics.Stopwatch.StartNew();
                if(!extracted.Paths.TryGetValue(records[i].Id,out string path))throw new Exception(extracted.Errors.TryGetValue(records[i].Id,out string error)?error:"Batch export did not return the model.");
                await SharedTextureCache.Normalize(assetLibrary.ModelDirectory(records[i]),1024);
                world.models.Prepare(records[i].Id,path);var model=world.models.Create(records[i].Id,false);
                try{var thumbnail=ModelThumbnail.Render(model);File.WriteAllBytes(Path.Combine(assetLibrary.ModelDirectory(records[i]),"thumbnail.png"),thumbnail.EncodeToPNG());Destroy(thumbnail);}
                finally{world.models.Release(records[i].Id,model);}
                Debug.Log("REMAP_SESSION_MODEL: "+i+" "+records[i].Name+" seconds="+itemTime.Elapsed.TotalSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" pid="+assetLibrary.PreviewProcessId);
            }
            int pid=assetLibrary.PreviewProcessId;
            if(assetLibrary.PreviewSessionStarts!=1||assetLibrary.PreviewArchiveLoads!=1)throw new Exception("RSX restarted/reloaded while processing one archive.");
            Debug.Log("REMAP_SESSION_MODELS_SECONDS: "+timing.Elapsed.TotalSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
            string firstArchive=assetLibrary.OriginArchive(records[0],targets);
            var other=assetLibrary.Records.FirstOrDefault(record=>record.Supports(targets)&&!string.Equals(assetLibrary.OriginArchive(record,targets),firstArchive,StringComparison.OrdinalIgnoreCase));
            if(other==null)throw new Exception("No model from a second archive was indexed for the active game.");
            string otherPath=await assetLibrary.ExtractAsync(other,targets);CastReader.Read(otherPath);
            if(assetLibrary.PreviewProcessId!=pid||assetLibrary.PreviewSessionStarts!=1||assetLibrary.PreviewArchiveLoads!=2)throw new Exception("Changing archives restarted RSX or retained the wrong archives.");
            if(world.models.LoadedGameModelCount!=0)throw new Exception("Thumbnail models remained resident in Unity.");
            await assetLibrary.ReleasePreviewSessionAsync();
            if(assetLibrary.PreviewProcessId!=0)throw new Exception("RSX session not released.");
            timing.Restart();foreach(var record in records)await assetLibrary.ExtractAsync(record,targets);
            if(assetLibrary.PreviewSessionStarts!=1||assetLibrary.PreviewProcessId!=0)throw new Exception("Cached models unnecessarily started RSX.");
            Debug.Log("REMAP_SESSION_CACHE_SECONDS: "+timing.Elapsed.TotalSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
            Debug.Log("REMAP_CONTINUOUS_PREVIEW_OK: "+qaRoot);
        }
        private IEnumerator ContinuousPreviewSmoke()
        {
            var task=CheckContinuousPreviews();while(!task.IsCompleted)yield return null;
            if(task.IsFaulted){Debug.LogException(task.Exception);Application.Quit(1);}else Application.Quit(0);
        }
    }
}
