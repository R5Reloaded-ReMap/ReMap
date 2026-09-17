using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
using UnityEngine;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private CancellationTokenSource mapReferenceCancellation;
        private string mapReferenceRequestKey;
        private bool mapReferencePresent;

        private void SyncMapReference()
        {
            if (world == null || assetLibrary == null || snapshot == null) return;
            world.SetMapReferenceOrigin(snapshot.originOffset);
            world.SetMprtReferenceOptions(MprtReferenceOptions.From(assetLibrary.Settings));
            world.SetMapReferenceVisibility(assetLibrary.Settings.showMainBsp, assetLibrary.Settings.showMprtModels);
            string map = (snapshot.editingMap ?? "").Trim();
            if ((!assetLibrary.Settings.showMainBsp && !assetLibrary.Settings.showMprtModels) || map.Length == 0)
            {
                mapReferenceCancellation?.Cancel();
                mapReferenceRequestKey = null;
                if (mapReferencePresent) { world.ClearMapReference(); mapReferencePresent = false; }
                return;
            }
            string key = assetLibrary.TargetGame + "|" + map + "|" + assetLibrary.Settings.showMainBsp + "|" +
                assetLibrary.Settings.showMprtModels + "|" + (assetLibrary.CacheRoot ?? "");
            if (key == mapReferenceRequestKey) return;
            mapReferenceRequestKey = key;
            _ = LoadMapReferenceAsync(key, map, false);
        }

        private void RequestMapReferenceReload(bool forceExtraction = false)
        {
            mapReferenceRequestKey = null;
            if (snapshot == null) return;
            string map = (snapshot.editingMap ?? "").Trim();
            if (map.Length == 0)
            {
                SyncMapReference();
                SetStatus(L.T("#SELECT_EDITED_MAP_FIRST"));
                return;
            }
            string key = assetLibrary.TargetGame + "|" + map + "|" + assetLibrary.Settings.showMainBsp + "|" +
                assetLibrary.Settings.showMprtModels + "|" + (assetLibrary.CacheRoot ?? "") + (forceExtraction ? "|force" : "");
            mapReferenceRequestKey = key;
            _ = LoadMapReferenceAsync(key, map, forceExtraction);
        }

        private void SetMapReferenceOptions(bool? showBsp = null, bool? showMprt = null)
        {
            if (showBsp.HasValue) assetLibrary.Settings.showMainBsp = showBsp.Value;
            if (showMprt.HasValue) assetLibrary.Settings.showMprtModels = showMprt.Value;
            assetLibrary.SaveSettings();
            RequestMapReferenceReload();
            RefreshInspector();
        }

        private void SetMprtReferenceTuning(int? preset = null, float? distance = null, int? maximum = null,
            float? minimumSize = null, int? density = null)
        {
            AssetSourceSettings settings = assetLibrary.Settings;
            if (preset.HasValue)
            {
                MprtReferenceProfiles.Apply(settings, preset.Value);
            }
            if (distance.HasValue) settings.mprtDistance = Mathf.Clamp(distance.Value, 50, 5000);
            if (maximum.HasValue) settings.mprtMaxActive = Mathf.Clamp(maximum.Value, 100, 50000);
            if (minimumSize.HasValue) settings.mprtMinimumSize = Mathf.Clamp(minimumSize.Value, 0, 100);
            if (density.HasValue) settings.mprtDensityPercent = Mathf.Clamp(density.Value, 1, 100);
            if (!preset.HasValue) settings.mprtPreset = 3;
            assetLibrary.SaveSettings();
            world.SetMprtReferenceOptions(MprtReferenceOptions.From(settings));
            SetStatus(L.F("#MPRT_STREAMING_STATUS_ARG0_ARG1", world.MprtReferenceActiveCount,
                world.MprtReferenceAvailableCount));
        }

        private async Task LoadMapReferenceAsync(string requestKey, string mapId, bool forceExtraction)
        {
            mapReferenceCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            mapReferenceCancellation = cancellation;
            bool loadingActive = true;
            Action cancel = () => {
                if (!loadingActive || cancellation.IsCancellationRequested) return;
                try { cancellation.Cancel(); }
                catch (ObjectDisposedException) { return; }
                if (this != null && requestKey == mapReferenceRequestKey)
                {
                    Loading(false);
                    SetStatus(L.T("#MAP_REFERENCE_LOADING_CANCELLED"));
                }
            };
            InterruptBackgroundFor();
            Loading(true, L.T("#PREPARING_MAP_REFERENCE"), 0, cancel);
            try
            {
                var progress = new Progress<MapReferenceProgress>(state => {
                    if (loadingActive && CurrentMapReferenceRequest(requestKey, cancellation))
                        Loading(true, state.Message, state.Value, cancel);
                });
                MapReferenceData data = await MapReferenceExtractor.LoadAsync(assetLibrary, mapId, forceExtraction,
                    progress, cancellation.Token);
                if (!CurrentMapReferenceRequest(requestKey, cancellation)) return;

                world.ClearMapReference(); mapReferencePresent = true;
                if (assetLibrary.Settings.showMainBsp)
                {
                    var bspProgress = new Progress<float>(value => {
                        if (loadingActive && CurrentMapReferenceRequest(requestKey, cancellation))
                            Loading(true, L.T("#BUILDING_BSP_MESHES"), .43f + .13f * value, cancel);
                    });
                    Loading(true, L.T("#BUILDING_BSP_MESHES"), .43f, cancel);
                    await world.LoadBspReferenceAsync(data.Map, data.ExtractionRoot, snapshot.originOffset,
                        bspProgress, cancellation.Token);
                    if (!CurrentMapReferenceRequest(requestKey, cancellation)) return;
                }

                int missing = 0, failed = 0, placed = 0;
                if (assetLibrary.Settings.showMprtModels && data.Map.StaticProps.Count > 0)
                {
                    if (assetLibrary.Records.Count == 0 && assetLibrary.Configured)
                    {
                        Loading(true, L.T("#INDEXING_MODELS_FOR_MPRT"), .57f, cancel);
                        await IndexAssets(cancellation.Token, cancel);
                        if (!CurrentMapReferenceRequest(requestKey, cancellation)) return;
                    }
                    Loading(true, L.T("#RESOLVING_MPRT_MODELS"), .59f, cancel);
                    MprtResolution resolution = await ResolveMprtModelsAsync(data.Map.StaticProps, cancellation.Token);
                    if (!CurrentMapReferenceRequest(requestKey, cancellation)) return;
                    missing = resolution.Missing;
                    var resolved = resolution.Models;
                    var unique = resolution.UniqueRecords;
                    var prepared = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < unique.Length; i++)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        GameAssetRecord record = unique[i];
                        Loading(true, L.F("#EXTRACTING_MPRT_MODEL_ARG0_ARG1", i + 1, unique.Length),
                            .61f + .27f * (i + 1) / Math.Max(1, unique.Length), cancel);
                        try
                        {
                            string path = await assetLibrary.ExtractAsync(record, Targets, cancellation.Token);
                            await SharedTextureCache.Normalize(assetLibrary.ModelDirectory(record),
                                assetLibrary.Settings.textureLimit, cancellation.Token);
                            if (!CurrentMapReferenceRequest(requestKey, cancellation)) return;
                            world.models.Prepare(record.Id, path); prepared.Add(record.Id);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception exception) { failed++; Debug.LogWarning(record.modelPath + ": " + exception.Message); }
                    }

                    cancellation.Token.ThrowIfCancellationRequested();
                    Loading(true, L.T("#PREPARING_MPRT_STREAMING_INDEX"), .89f, cancel);
                    MapReferenceModel[] references = await BuildMprtReferencesAsync(resolved, prepared,
                        cancellation.Token);
                    if (!CurrentMapReferenceRequest(requestKey, cancellation)) return;
                    placed = references.Length;
                    var mprtProgress = new Progress<float>(value => {
                        if (loadingActive && CurrentMapReferenceRequest(requestKey, cancellation))
                            Loading(true, L.T("#PREPARING_MPRT_STREAMING_INDEX"), .9f + .09f * value, cancel);
                    });
                    await world.ConfigureMprtReferenceAsync(references,
                        MprtReferenceOptions.From(assetLibrary.Settings), snapshot.originOffset,
                        mprtProgress, cancellation.Token);
                    if (!CurrentMapReferenceRequest(requestKey, cancellation)) return;
                }
                world.SetMapReferenceVisibility(assetLibrary.Settings.showMainBsp, assetLibrary.Settings.showMprtModels);
                SetStatus(L.F("#MAP_REFERENCE_READY_ARG0_ARG1_ARG2", data.Map.Surfaces.Count, placed, missing + failed));
            }
            catch (OperationCanceledException)
            {
                if (requestKey == mapReferenceRequestKey)
                {
                    world.ClearMapReference(); mapReferencePresent = false;
                    SetStatus(L.T("#MAP_REFERENCE_LOADING_CANCELLED"));
                }
            }
            catch (Exception exception)
            {
                if (requestKey == mapReferenceRequestKey)
                {
                    world.ClearMapReference(); mapReferencePresent = false;
                    SetStatus(exception.Message); Debug.LogWarning(exception);
                }
            }
            finally
            {
                // Progress<T> callbacks can already be queued on Unity's synchronization
                // context. Invalidate them before hiding the overlay and disposing the token,
                // otherwise a late callback can reopen a dead dialog with a stale Cancel action.
                loadingActive = false;
                if (this != null && requestKey == mapReferenceRequestKey) Loading(false);
                if (ReferenceEquals(mapReferenceCancellation, cancellation)) mapReferenceCancellation = null;
                cancellation.Dispose();
            }
        }

        private bool CurrentMapReferenceRequest(string key, CancellationTokenSource cancellation) =>
            this != null && !backgroundStopped && ReferenceEquals(mapReferenceCancellation, cancellation) &&
            !cancellation.IsCancellationRequested && key == mapReferenceRequestKey;

        private List<KeyValuePair<MprtPlacement, GameAssetRecord>> ResolveMprtModels(
            IReadOnlyList<MprtPlacement> placements, out int missing)
        {
            MprtResolution result = ResolveMprtModelsCore(placements,
                assetLibrary.Records.Where(record => record.Supports(Targets)).ToArray(), CancellationToken.None);
            missing = result.Missing;
            return result.Models;
        }

        private sealed class MprtResolution
        {
            public List<KeyValuePair<MprtPlacement, GameAssetRecord>> Models;
            public GameAssetRecord[] UniqueRecords;
            public int Missing;
        }

        private Task<MprtResolution> ResolveMprtModelsAsync(IReadOnlyList<MprtPlacement> placements,
            CancellationToken cancellation)
        {
            var compatible = assetLibrary.Records.Where(record => record.Supports(Targets)).ToArray();
            return Task.Run(() => ResolveMprtModelsCore(placements, compatible, cancellation), cancellation);
        }

        private static MprtResolution ResolveMprtModelsCore(IReadOnlyList<MprtPlacement> placements,
            IReadOnlyList<GameAssetRecord> compatible, CancellationToken cancellation)
        {
            var exact = new Dictionary<string, GameAssetRecord>(StringComparer.OrdinalIgnoreCase);
            var filenames = new Dictionary<string, GameAssetRecord>(StringComparer.OrdinalIgnoreCase);
            var ambiguousFilenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameAssetRecord record in compatible)
            {
                cancellation.ThrowIfCancellationRequested();
                string path = GameAssetIndex.NormalizeModelPath(record.modelPath);
                if (!exact.ContainsKey(path)) exact.Add(path, record);
                string filename = Path.GetFileName(path);
                if (filenames.ContainsKey(filename)) ambiguousFilenames.Add(filename);
                else filenames.Add(filename, record);
            }
            foreach (string filename in ambiguousFilenames) filenames.Remove(filename);

            var models = new List<KeyValuePair<MprtPlacement, GameAssetRecord>>(placements.Count);
            var unique = new Dictionary<string, GameAssetRecord>(StringComparer.Ordinal);
            int missing = 0;
            for (int i = 0; i < placements.Count; i++)
            {
                if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                MprtPlacement placement = placements[i];
                string path = GameAssetIndex.NormalizeModelPath(placement.ModelPath);
                if (!exact.TryGetValue(path, out GameAssetRecord record))
                    filenames.TryGetValue(Path.GetFileName(path), out record);
                if (record == null) missing++;
                else if (!unique.ContainsKey(record.Id)) unique.Add(record.Id, record);
                models.Add(new KeyValuePair<MprtPlacement, GameAssetRecord>(placement, record));
            }
            cancellation.ThrowIfCancellationRequested();
            return new MprtResolution { Models = models, UniqueRecords = unique.Values.ToArray(), Missing = missing };
        }

        private static Task<MapReferenceModel[]> BuildMprtReferencesAsync(
            IReadOnlyList<KeyValuePair<MprtPlacement, GameAssetRecord>> resolved,
            HashSet<string> prepared, CancellationToken cancellation) =>
            Task.Run(() => {
                var references = new List<MapReferenceModel>(resolved.Count);
                for (int i = 0; i < resolved.Count; i++)
                {
                    if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                    var pair = resolved[i];
                    if (pair.Value != null && prepared.Contains(pair.Value.Id))
                        references.Add(new MapReferenceModel { AssetId = pair.Value.Id, Placement = pair.Key });
                }
                cancellation.ThrowIfCancellationRequested();
                return references.ToArray();
            }, cancellation);
    }
}
