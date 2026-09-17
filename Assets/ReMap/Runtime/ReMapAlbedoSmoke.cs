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
        private async Task CheckAlbedoRepair()
        {
            await TreeFrames(12);
            string sourceRoot=RsxAssetLibrary.FindLocalRoot();
            string originalRoot=RsxAssetLibrary.FindExistingModelDirectory(sourceRoot,"04c630f398c9d040") ?? throw new DirectoryNotFoundException("Cached albedo fixture not found.");
            string originalCast=Path.Combine(originalRoot,File.ReadLines(Path.Combine(originalRoot,"complete.txt")).First());
            string originalManifest=File.ReadAllText(Path.Combine(originalRoot,"textures.manifest.json"));
            var manifest=JsonUtility.FromJson<SharedTextureCache.Manifest>(originalManifest);
            string qa=Path.Combine(sourceRoot,"Logs","AlbedoQA",DateTime.UtcNow.ToString("HHmmss"));
            string modelRoot=Path.Combine(qa,"AssetCache","qa","models","04c630f398c9d040"),export=Path.Combine(modelRoot,"export");Directory.CreateDirectory(export);
            string cast=Path.Combine(export,Path.GetFileName(originalCast));File.Copy(originalCast,cast);
            string textureFolder=Path.Combine(export,CastAlbedo.ModelName(cast));Directory.CreateDirectory(textureFolder);
            foreach(var entry in manifest.entries)
            {
                if(entry.hash.Length!=64||entry.hash.Any(c=>!Uri.IsHexDigit(c)))throw new Exception("Invalid fixture hash.");
                File.Copy(Path.Combine(SharedTextureCache.RootFor(originalRoot),entry.hash+".png"),Path.Combine(textureFolder,Path.GetFileName(entry.source)),true);
            }
            await SharedTextureCache.Normalize(modelRoot,1024);
            var filtered=JsonUtility.FromJson<SharedTextureCache.Manifest>(File.ReadAllText(Path.Combine(modelRoot,"textures.manifest.json")));
            if(filtered.entries.Count!=4||filtered.entries.Any(e=>!CastAlbedo.IsColorTextureName(e.source)))throw new Exception("Non-albedo textures were retained or a required albedo was removed.");
            if(Directory.GetFiles(textureFolder,"*.png").Length!=0)throw new Exception("Raw texture copies remained after normalization.");
            if(File.ReadAllText(Path.Combine(originalRoot,"textures.manifest.json"))!=originalManifest)throw new Exception("Original texture cache was changed.");
            string asset="qa:ground-cap-albedo";world.models.Prepare(asset,cast);
            var instance=world.models.Create(asset,false);
            try
            {
                if(world.models.MissingAlbedo(asset)!=0)throw new Exception("Ground cap still has unresolved materials: "+world.models.AlbedoDiagnostics(asset));
                var materials=instance.GetComponent<MeshRenderer>().sharedMaterials;
                if(materials.Length!=4||materials.Any(m=>m.GetTexture("_BaseMap")==null))throw new Exception("Four real albedos were not assigned.");
                var thumbnail=ModelThumbnail.Render(instance);File.WriteAllBytes(Path.Combine(qa,"ground-cap-thumbnail.png"),thumbnail.EncodeToPNG());Destroy(thumbnail);
                Debug.Log("REMAP_ALBEDO_REPAIR: missing=0 materials=4 textures="+manifest.entries.Count+"->"+filtered.entries.Count);
            }
            finally{world.models.Release(asset,instance);}
            var doc=new MapDocument();var item=new MapObject{assetId=asset,displayName="canyonland_thunderdome_ground_cap_01"};doc.objects.Add(item);
            session.Replace(doc);Refresh();Select(item.id);world.Focus(item.id);
            SetStatus("Ground cap: 4 albedos resolved; missing material repaired from the existing cache.");
            Debug.Log("REMAP_ALBEDO_OK: "+qa);
        }
        private IEnumerator AlbedoSmoke()
        {
            var task=CheckAlbedoRepair();while(!task.IsCompleted)yield return null;
            if(task.IsFaulted){Debug.LogException(task.Exception);Application.Quit(1);yield break;}
            for(int i=0;i<5;i++)yield return null;
            yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();
            if(image!=null){File.WriteAllBytes(Path.Combine(Application.dataPath,"..","albedo-repair-preview.png"),image.EncodeToPNG());Destroy(image);}
            Application.Quit(0);
        }
    }
}
