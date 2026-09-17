using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
using UnityEngine;
using Debug=UnityEngine.Debug;
namespace ReMap.Standalone {
    public sealed partial class ReMapApp {
        private async Task CheckAssetQueue() {
            await TreeFrames(12);
            string sourceRoot=RsxAssetLibrary.FindLocalRoot();string qaBase=Path.Combine(sourceRoot,"Logs","AssetQueueQA");bool reuse=Environment.GetCommandLineArgs().Contains("-remapQueueReuse");string qaRoot=reuse?Directory.GetDirectories(qaBase).OrderByDescending(p=>p).First():Path.Combine(qaBase,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(qaRoot);
            if(!reuse)File.Copy(Path.Combine(sourceRoot,"asset-source.local.json"),Path.Combine(qaRoot,"asset-source.local.json"));
            // Reuse metadata only. None of the model exports or thumbnails exist in this fresh QA cache.
            foreach(var cache in reuse?Array.Empty<string>():Directory.GetDirectories(Path.Combine(sourceRoot,"AssetCache"))) {
                string index=Path.Combine(cache,"index");if(!Directory.Exists(index))continue;
                string target=Path.Combine(qaRoot,"AssetCache",Path.GetFileName(cache),"index");Directory.CreateDirectory(target);
                foreach(var csv in Directory.GetFiles(index,"*.csv"))File.Copy(csv,Path.Combine(target,Path.GetFileName(csv)));
            }
            var originalLibrary=assetLibrary;assetLibrary=new RsxAssetLibrary(qaRoot);
            try {
                await IndexAssets();
                var wanted=new[]{"4e75875aec311c31","00fd30d319c8911e","8076d9bdd8a5d0f0","cf927ce201f690ed","ba553cd44270cb0a","ce54cdcf6a1a995c"};
                // GUID normalization preserves leading zeros.
                wanted[1]=GameAssetIndex.NormalizeGuid("fd30d319c8911e");
                var records=wanted.Select(g=>assetLibrary.Records.Single(r=>r.guid==g)).ToArray();
                var watch=Stopwatch.StartNew();var batch=await assetLibrary.ExtractBatchAsync(records,Array.Empty<string>());
                Debug.Log("REMAP_BATCH_SECONDS: "+watch.Elapsed.TotalSeconds.ToString("0.00"));
                if(batch.Paths.Count!=6)throw new Exception("Batch missing assets: "+string.Join(" | ",batch.Errors.Values));
                foreach(var record in records) {
                    await SharedTextureCache.Normalize(assetLibrary.ModelDirectory(record),1024);
                    world.models.Prepare(record.Id,batch.Paths[record.Id]);var model=world.models.Create(record.Id,false);Texture2D image=null;
                    try {image=ModelThumbnail.Render(model);if(image==null)throw new Exception("Thumbnail absent");File.WriteAllBytes(Path.Combine(assetLibrary.ModelDirectory(record),"thumbnail.png"),image.EncodeToPNG());Debug.Log("REMAP_BATCH_PREVIEW_OK: "+record.Name+" missing_albedo="+world.models.MissingAlbedo(record.Id));}
                    finally {if(image!=null)Destroy(image);world.models.Release(record.Id,model);}
                }
                watch.Restart();var cached=await assetLibrary.ExtractBatchAsync(records,Array.Empty<string>());Debug.Log("REMAP_BATCH_CACHE_MS: "+watch.Elapsed.TotalMilliseconds);
                if(cached.Paths.Count!=6)throw new Exception("Batch cache failed");
                search.value="plat";RefreshCatalog();
                var task=PreviewGameAsset(records[0]);if(loadingOverlay.style.display.value==UnityEngine.UIElements.DisplayStyle.Flex)throw new Exception("Preview blocks the editor");await task;
                if(previewEntry?.Id!=records[0].Id)throw new Exception("Foreground preview failed");
                // Regenerate images through the actual background scheduler, not only the batch API.
                assetLibrary.Records.Clear();assetLibrary.Records.AddRange(records);
                foreach(var record in records)File.Delete(Path.Combine(assetLibrary.ModelDirectory(record),"thumbnail.png"));
                thumbnailStateRoot=null;RefreshCatalog();await PrepareThumbnails();
                if(records.Any(r=>!ThumbnailMissingAlbedo(r)))throw new Exception("Missing albedo label not saved");
                if(thumbnailDone!=6||world.models.LoadedGameModelCount!=0)throw new Exception("Background completion / resource release failed: "+thumbnailDone);
                RefreshCatalog();SetStatus("Six aperçus auparavant absents : extraction groupée, cache et chargement non bloquant vérifiés.");
                Debug.Log("REMAP_ASSET_QUEUE_OK: "+qaRoot);
            }catch{assetLibrary.Dispose();assetLibrary=originalLibrary;throw;}
            // Keep QA records until the capture; disposal happens in OnDestroy.
            originalLibrary.Dispose();
        }
        private IEnumerator AssetQueueSmoke() {
            var task=CheckAssetQueue();while(!task.IsCompleted)yield return null;
            if(task.IsFaulted){Debug.LogException(task.Exception);Application.Quit(1);yield break;}
            yield return new WaitForEndOfFrame();var shot=ScreenCapture.CaptureScreenshotAsTexture();if(shot!=null){File.WriteAllBytes(Path.Combine(Application.dataPath,"..","asset-queue-preview.png"),shot.EncodeToPNG());Destroy(shot);}Application.Quit(0);
        }
    }
}
