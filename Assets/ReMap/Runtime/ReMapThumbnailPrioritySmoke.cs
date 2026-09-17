using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
using UnityEngine;
namespace ReMap.Standalone {
    public sealed partial class ReMapApp {
        private async Task CheckThumbnailPriority() {
            await TreeFrames(12);
            bool reuse=Environment.GetCommandLineArgs().Contains("-remapThumbnailReuse");
            string sourceRoot=RsxAssetLibrary.FindLocalRoot();
            string qaRoot=File.ReadAllText(Path.Combine(sourceRoot,"Logs","thumbnail-qa-root.txt")).Trim();
            assetLibrary.Dispose();assetLibrary=new RsxAssetLibrary(qaRoot);
            session.Edit(d=>d.targetMaps=new System.Collections.Generic.List<string>{"mp_rr_olympus_mu2"});Refresh();
            // Index requests made while busy must survive until the current work releases the worker.
            SetAssetBusy(true);var index=IndexAssets();await Task.Delay(100);
            if(index.IsCompleted||!indexRequested)throw new Exception("Index request was lost while busy");
            SetAssetBusy(false);await index;
            if(assetLibrary.Records.Count!=5359)throw new Exception("Unexpected Common + Olympus index count");
            var fixture=assetLibrary.Records.Single(r=>r.guid=="4981733532ae8a36");
            string fixtureRoot=assetLibrary.ModelDirectory(fixture),old=File.ReadLines(Path.Combine(fixtureRoot,"complete.txt")).First();
            string compact=assetLibrary.CachedModel(fixture);
            if((!reuse&&!old.StartsWith("batch-"))||compact==null||compact.Contains("batch-"))throw new Exception("Long cache path was not migrated");
            await Task.WhenAll(SharedTextureCache.Normalize(fixtureRoot,1024),SharedTextureCache.Normalize(fixtureRoot,1024));
            world.models.Prepare(fixture.Id,compact);var model=world.models.Create(fixture.Id,false);
            try {
                if(!model.GetComponent<MeshRenderer>().sharedMaterials.Any(m=>m.GetTexture("_BaseMap")!=null))throw new Exception("Migrated model has no loaded textures");
                var manifest=JsonUtility.FromJson<SharedTextureCache.Manifest>(File.ReadAllText(Path.Combine(fixtureRoot,"textures.manifest.json")));
                if(manifest.entries.Count!=8)throw new Exception("Long-path textures were lost");
                Debug.Log("REMAP_MIGRATED_ALBEDO_MISSING: "+world.models.MissingAlbedo(fixture.Id));
                Debug.Log("REMAP_LONG_PATH_TEXTURE_OK: "+fixture.Name+" cast="+compact.Length+" chars");
            }finally {world.models.Release(fixture.Id,model);}
            var records=assetLibrary.Records.Where(r=>r.modelPath.IndexOf("platform",StringComparison.OrdinalIgnoreCase)>=0).Skip(8).Take(8).ToArray();
            if(records.Length!=8||records.Any(r=>!r.Name.StartsWith("olympus")))throw new Exception("Screenshot page not reproduced");
            Debug.Log("REMAP_OLYMPUS_PAGE: "+string.Join(", ",records.Select(r=>r.Name)));
            if(reuse)foreach(var record in records)File.Delete(Path.Combine(assetLibrary.ModelDirectory(record),"thumbnail.png"));
            var unrelated=assetLibrary.Records.First(r=>r.Name=="hspray_epic_v21_br_rankplayer_s11_platinum_01");
            assetLibrary.Records.Clear();assetLibrary.Records.Add(unrelated);assetLibrary.Records.AddRange(records);
            search.value=unrelated.Name;RefreshCatalog();
            var background=PrepareThumbnails();
            float started=Time.realtimeSinceStartup;
            while(thumbnailExport==null) {await TreeFrames();if(Time.realtimeSinceStartup-started>10)throw new Exception("Background worker did not start");}
            var oldExport=thumbnailExport;
            search.value="platform";RefreshCatalog();
            while(thumbnailExport==oldExport) {await TreeFrames();if(Time.realtimeSinceStartup-started>5)throw new Exception("Page failed to interrupt unrelated RSX export");}
            if(failedThumbnails.Contains(unrelated.Id))throw new Exception("Cancellation was treated as asset failure");
            Debug.Log("REMAP_PAGE_PRIORITY_OK: "+(Time.realtimeSinceStartup-started).ToString("0.00")+" s");
            started=Time.realtimeSinceStartup;
            while(!records.All(r=>readyThumbnails.Contains(r.Id))) {
                await Task.Delay(100);
                if(records.Any(r=>failedThumbnails.Contains(r.Id)))throw new Exception("Platform thumbnail failed: "+string.Join(" | ",previewFailures.Values));
                if(Time.realtimeSinceStartup-started>240)throw new Exception("Olympus previews timed out");
            }
            thumbnailPaused=true;
            foreach(var record in records) {
                world.models.Prepare(record.Id,assetLibrary.CachedModel(record));var instance=world.models.Create(record.Id,false);
                try {if(!instance.GetComponent<MeshRenderer>().sharedMaterials.Any(m=>m.GetTexture("_BaseMap")!=null))throw new Exception("Platform has no loaded albedo: "+record.Name);
                    Debug.Log("REMAP_PLATFORM_MISSING_ALBEDO: "+record.Name+" = "+world.models.MissingAlbedo(record.Id));}
                finally {world.models.Release(record.Id,instance);}
                Debug.Log("REMAP_OLYMPUS_TEXTURE_OK: "+record.Name);
            }
            if(world.models.LoadedGameModelCount!=0)throw new Exception("Background models were not released");
            Debug.Log("REMAP_OLYMPUS_PAGE_SECONDS: "+(Time.realtimeSinceStartup-started).ToString("0.00"));
            backgroundStopped=true;InterruptBackgroundFor();await background;
            RefreshCatalog();SetStatus("Huit plateformes d’Olympus : miniatures et textures chargées. Ancien cache réparé et priorité de page vérifiée.");
            Debug.Log("REMAP_THUMBNAIL_PRIORITY_OK: "+qaRoot);
        }
        private IEnumerator ThumbnailPrioritySmoke() {
            var task=CheckThumbnailPriority();while(!task.IsCompleted)yield return null;
            if(task.IsFaulted){Debug.LogException(task.Exception);Application.Quit(1);yield break;}
            yield return new WaitForEndOfFrame();var shot=ScreenCapture.CaptureScreenshotAsTexture();
            if(shot!=null){File.WriteAllBytes(Path.Combine(Application.dataPath,"..","thumbnail-priority-preview.png"),shot.EncodeToPNG());Destroy(shot);}Application.Quit(0);
        }
    }
}
