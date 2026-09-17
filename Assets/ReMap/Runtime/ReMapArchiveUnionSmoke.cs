using System;
using System.Collections;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.UIElements;
namespace ReMap.Standalone {
    public sealed partial class ReMapApp {
        private async Task CheckArchiveUnion() {
            await TreeFrames(12);
            string local=RsxAssetLibrary.FindLocalRoot(),qa=Path.Combine(local,"Logs","ArchiveSearchQA",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(qa);File.Copy(Path.Combine(local,"asset-source.local.json"),Path.Combine(qa,"asset-source.local.json"));
            foreach(var cache in Directory.GetDirectories(Path.Combine(local,"AssetCache"))) {
                string source=Path.Combine(cache,"index");if(!Directory.Exists(source))continue;
                string target=Path.Combine(qa,"AssetCache",Path.GetFileName(cache),"index");Directory.CreateDirectory(target);
                foreach(var csv in Directory.GetFiles(source,"*.csv"))File.Copy(csv,Path.Combine(target,Path.GetFileName(csv)));
            }
            assetLibrary.Dispose();assetLibrary=new RsxAssetLibrary(qa);
            session.Edit(d=>d.targetMaps=new System.Collections.Generic.List<string>{"mp_rr_olympus_mu2"});Refresh();await IndexAssets();
            search.value="thunderdome_cage_ceiling_128x128_03";RefreshCatalog();
            if(visibleAssets.Count!=0)throw new Exception("Unexpected Thunderdome source in Olympus-only catalogue");
            ShowIndexing(true);mapToggles["mp_rr_desertlands_hu"].value=true;ShowIndexing(false);
            if(settingsIndexTask!=null)throw new Exception("Closing the indexing page unexpectedly started indexing.");
            ApplyIndexingChanges();
            if(settingsIndexTask==null)throw new Exception("Applying the indexing page did not request indexing");await settingsIndexTask;
            var thunderdome=assetLibrary.Records.Single(r=>r.Name=="thunderdome_cage_ceiling_128x128_03");
            if(thunderdome.guid!="119fd55c4d9f80e7")throw new Exception("Wrong exact model GUID");
            Debug.Log("REMAP_SETTINGS_AUTO_INDEX_OK: "+thunderdome.Name);
            if(!thunderdome.Supports(Targets)||thunderdome.Supports(new[]{"mp_rr_olympus_mu2"}))throw new Exception("Thunderdome archive union incorrect");
            search.value=thunderdome.Name;RefreshCatalog();
            if(visibleAssets.Count!=1||visibleAssets[0].Id!=thunderdome.Id||!catalogList.Q<Button>().enabledSelf)throw new Exception("Thunderdome hidden/disabled despite its loaded archive");
            var item=new MapObject{assetId=thunderdome.Id,commonAsset=false,availableMaps=thunderdome.origins.Select(o=>o.mapId).ToList()};
            if(Incompatible(item))throw new Exception("Scene and assembly compatibility differs from catalogue");
            var pending=ThumbnailQueue.Next(assetLibrary.Records,new[]{thunderdome},new System.Collections.Generic.HashSet<string>(),new System.Collections.Generic.HashSet<string>(),thunderdome.Name,Targets);
            if(pending.Length==0||pending[0].Id!=thunderdome.Id)throw new Exception("Thumbnail queue excludes union model");
            if(assetLibrary.OriginArchive(thunderdome,Targets)!="mp_rr_desertlands_hu.rpak")throw new Exception("Wrong extraction archive");
            session.Edit(d=>d.targetMaps=new System.Collections.Generic.List<string>{"mp_rr_olympus_mu2"});Refresh();
            if(visibleAssets.Any(r=>r.Id==thunderdome.Id)||!Incompatible(item))throw new Exception("Removing source archive keeps model available");
            session.Undo();Refresh();search.value=thunderdome.Name;RefreshCatalog();
            Debug.Log("REMAP_ARCHIVE_UNION_OK: available="+assetLibrary.Records.Count(r=>r.Supports(Targets))+" unique="+assetLibrary.Records.Count+" thunderdome="+visibleAssets.Count);
            SetStatus("Common + Desertlands + Olympus : modèles réunis ; Thunderdome disponible et doublons évités.");
        }
        private IEnumerator ArchiveUnionSmoke() {
            var task=CheckArchiveUnion();while(!task.IsCompleted)yield return null;
            if(task.IsFaulted){Debug.LogException(task.Exception);Application.Quit(1);yield break;}
            yield return new WaitForEndOfFrame();var shot=ScreenCapture.CaptureScreenshotAsTexture();
            if(shot!=null){File.WriteAllBytes(Path.Combine(Application.dataPath,"..","thunderdome-search-preview.png"),shot.EncodeToPNG());Destroy(shot);}Application.Quit(0);
        }
    }
}
