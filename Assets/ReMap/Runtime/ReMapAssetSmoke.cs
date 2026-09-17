using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ReMap.Standalone.Core;
using UnityEngine;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private IEnumerator AssetSmokeCheck()
        {
            for (int i = 0; i < 8; i++) yield return null;
            root.SetEnabled(false);
            session.Edit(d => d.targetMaps = new List<string> { "mp_rr_desertlands_hu", "mp_rr_olympus_mu2" }); Refresh();
            var indexing = IndexAssets(); while (!indexing.IsCompleted) yield return null;
            bool success = false;
            var sampleAsset = assetLibrary.Records.FirstOrDefault(r => r.guid == "fc0ebc1ace084ec5");
            foreach (string guid in new[] { "43f9958ba65ae981", "a32899530e86bd40", "b7f4185bbf83a8e8", "fc0ebc1ace084ec5" })
            {
                var entry = assetLibrary.Records.FirstOrDefault(r => r.guid == guid);
                if (entry == null) { Debug.LogError("Missing test asset " + guid); Application.Quit(1); yield break; }
                search.value = entry.Name;
                var preview = PreviewGameAsset(entry); while (!preview.IsCompleted) yield return null;
                if (preview.IsFaulted) Debug.LogException(preview.Exception);
                if (previewEntry == null || currentThumbnail == null) { Debug.LogError("Preview failed: " + entry.modelPath); Application.Quit(1); yield break; }
                Debug.Log("REMAP_PREVIEW_OK: " + entry.modelPath + " | " + previewText.text);
            }
            var originalRecords = assetLibrary.Records.ToArray();
            var cacheRecord = assetLibrary.Records.First(r => r.guid == "43f9958ba65ae981");
            string thumbnailPath = Path.Combine(assetLibrary.ModelDirectory(cacheRecord), "thumbnail.png");
            File.Delete(thumbnailPath);
            assetLibrary.Records.Clear(); assetLibrary.Records.Add(cacheRecord);
            var preparation = PrepareThumbnails(); while (!preparation.IsCompleted) yield return null;
            bool generated = File.Exists(thumbnailPath);
            var stamp = generated ? File.GetLastWriteTimeUtc(thumbnailPath) : DateTime.MinValue;
            var reused = PrepareThumbnails(); while (!reused.IsCompleted) yield return null;
            bool reusedCache = generated && File.GetLastWriteTimeUtc(thumbnailPath) == stamp;
            assetLibrary.Records.Clear(); assetLibrary.Records.AddRange(originalRecords);
            Debug.Log("REMAP_THUMBNAIL_CACHE: generated=" + generated + " reused=" + reusedCache + " resident_models=" + world.models.LoadedGameModelCount);
            try
            {
                if (!reusedCache || world.models.LoadedGameModelCount != 0) throw new Exception("Background thumbnail cache/release failed.");
                if (sampleAsset == null || previewEntry == null || currentThumbnail == null) throw new Exception("Real asset preview failed.");
                if (assetLibrary.Records.Select(r => r.guid).Distinct().Count() != assetLibrary.Records.Count) throw new Exception("Duplicate GUID in catalogue.");
                var desert = assetLibrary.Records.FirstOrDefault(r => !r.IsCommon && r.origins.Any(o => o.mapId == "mp_rr_desertlands_hu") && !r.origins.Any(o => o.mapId == "mp_rr_olympus_mu2"));
                if (desert == null || !desert.Supports(Targets) || desert.Supports(new[] { "mp_rr_olympus_mu2" }) || !desert.Supports(new[] { "mp_rr_desertlands_hu" })) throw new Exception("Loaded archive union check failed.");
                var item = new MapObject { assetId = previewEntry.Id, displayName = previewEntry.Name, gameModelPath = sampleAsset.modelPath, commonAsset = true, position = new Float3(0, previewEntry.PlacementLift + .3f, -1) };
                session.Edit(d => d.objects.Add(item)); selectedId = item.id; Refresh();
                if (world.LoadedModelCount != 4) throw new Exception("Unexpected resource count for one real model.");
                Duplicate(); if (world.LoadedModelCount != 4) throw new Exception("Duplicate mesh resources not shared.");
                session.Undo(); Refresh(); selectedId = item.id; Select(item.id); world.Focus(item.id);
                var testFiles = new MapFiles(Path.Combine(Application.temporaryCachePath, "AssetSmoke"), codec);
                testFiles.Save("assets", snapshot); var roundTrip = testFiles.Load("assets");
                if (!roundTrip.targetMaps.SequenceEqual(Targets) || roundTrip.objects.Last().gameModelPath != sampleAsset.modelPath) throw new Exception("Asset provenance / targets lost in save.");
                session.Replace(new MapDocument()); Refresh(); world.models.ForgetPrepared();
                session.Replace(roundTrip); Refresh();
                if (world.models.LoadedGameModelCount != 0) throw new Exception("Cache reset simulation failed.");
                RestoreCachedModels();
                if (world.models.LoadedGameModelCount != 1) throw new Exception("Cached model failed to restore after reopening.");
                selectedId = item.id; Select(item.id); world.Focus(item.id);
                SetStatus("Modèle réel affiché · dédoublonnage GUID, cartes cibles et ressources partagées vérifiés.");
                Debug.Log("REMAP_ASSET_SMOKE_DATA: unique=" + assetLibrary.Records.Count + " common=" + assetLibrary.Records.Count(r => r.IsCommon) + " loaded_archives=" + assetLibrary.Records.Count(r => r.Supports(Targets)) + " selected=" + sampleAsset.modelPath);
                success = true;
            }
            catch (Exception ex) { Debug.LogException(ex); }
            root.SetEnabled(true);
            for (int i = 0; i < 3; i++) yield return null;
            if (success)
            {
                yield return new WaitForEndOfFrame();
                try
                {
                    var capture = ScreenCapture.CaptureScreenshotAsTexture();
                    if (capture == null) throw new Exception("Visible player window required.");
                    try { File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "asset-preview.png"), capture.EncodeToPNG()); }
                    finally { Destroy(capture); }
                    Debug.Log("REMAP_ASSET_SMOKE_OK");
                }
                catch (Exception ex) { success = false; Debug.LogException(ex); }
            }
            Application.Quit(success ? 0 : 1);
        }
    }
}
