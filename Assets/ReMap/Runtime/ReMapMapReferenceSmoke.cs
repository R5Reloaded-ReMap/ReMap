using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace ReMap.Standalone
{
    [Serializable]
    public sealed class MapReferenceMprtStageResult
    {
        public int placed;
        public float frameMedianMs, frameP95Ms, medianFps, p95Fps, framesWithin60BudgetPercent;
        public float allocatedMB, reservedMB, managedMB;
    }

    [Serializable]
    public sealed class MapReferencePerformanceResult
    {
        public string target, map, gpu, cpu, unity;
        public int surfaces, meshChunks, vertices, triangles, staticProps;
        public bool mprtRequested;
        public int resolvedStaticProps, missingStaticProps, uniqueMprtModels, cachedMprtModels;
        public int placedMprtModels, failedMprtModels;
        public float loadSeconds, frameMedianMs, frameP95Ms, medianFps, p95Fps, framesWithin60BudgetPercent;
        public float allocatedMB, reservedMB, managedMB, mprtPrepareSeconds;
        public List<MapReferenceMprtStageResult> mprtStages = new List<MapReferenceMprtStageResult>();
    }

    [Serializable]
    public sealed class MapReferencePerformanceReport
    {
        public List<MapReferencePerformanceResult> results = new List<MapReferencePerformanceResult>();
    }

    public sealed partial class ReMapApp
    {
        private sealed class PreparedMapReference
        {
            public MapReferenceData data;
            public float loadSeconds;
            public int resolvedStaticProps, missingStaticProps, uniqueMprtModels, cachedMprtModels, failedMprtModels;
            public float mprtPrepareSeconds;
            public readonly List<KeyValuePair<MprtPlacement, GameAssetRecord>> mprtPlacements =
                new List<KeyValuePair<MprtPlacement, GameAssetRecord>>();
        }

        private async Task<PreparedMapReference> PrepareMapReferenceComparison(string target, string map, int mprtLimit)
        {
            assetLibrary.SelectTarget(target, false);
            selectedId = null;
            session.Replace(new MapDocument { name = GameTargets.DisplayName(target) + " · " + map,
                gameTarget = target, targetMaps = new List<string> { map } });
            Refresh();
            var timer = Stopwatch.StartNew();
            MapReferenceData data = await MapReferenceExtractor.LoadAsync(assetLibrary, map, false, null, CancellationToken.None);
            timer.Stop();
            var prepared = new PreparedMapReference { data = data, loadSeconds = (float)timer.Elapsed.TotalSeconds };
            world.ClearMapReference();
            world.LoadBspReference(data.Map, data.ExtractionRoot, new Float3());
            world.SetMapReferenceVisibility(true, false);
            if (!world.FocusBspReference()) throw new InvalidOperationException("The BSP reference has no visible geometry.");
            if (mprtLimit >= 0) await PrepareMprtComparison(prepared, mprtLimit);
            return prepared;
        }

        private async Task PrepareMprtComparison(PreparedMapReference prepared, int limit)
        {
            var timer = Stopwatch.StartNew();
            if (assetLibrary.Records.Count == 0 && assetLibrary.Configured) await IndexAssets();
            int missing;
            var resolved = ResolveMprtModels(prepared.data.Map.StaticProps, out missing);
            var available = resolved.Where(pair => pair.Value != null).ToList();
            var unique = available.Select(pair => pair.Value).GroupBy(record => record.Id)
                .Select(group => group.First()).ToArray();
            prepared.resolvedStaticProps = available.Count;
            prepared.missingStaticProps = missing;
            prepared.uniqueMprtModels = unique.Length;
            prepared.cachedMprtModels = unique.Count(record => assetLibrary.CachedModel(record) != null);
            if (limit <= 0) { prepared.mprtPrepareSeconds = (float)timer.Elapsed.TotalSeconds; return; }

            double x = 0, y = 0, z = 0;
            foreach (var pair in available) { x += pair.Key.Position.x; y += pair.Key.Position.y; z += pair.Key.Position.z; }
            double divisor = Math.Max(1, available.Count);
            var center = new Float3((float)(x / divisor), (float)(y / divisor), (float)(z / divisor));
            var selected = available.OrderBy(pair => SquaredDistance(pair.Key.Position, center))
                .Take(Math.Min(limit, available.Count)).ToList();
            var selectedUnique = selected.Select(pair => pair.Value).GroupBy(record => record.Id)
                .Select(group => group.First()).ToArray();
            var ready = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < selectedUnique.Length; i++)
            {
                GameAssetRecord record = selectedUnique[i];
                SetStatus("MPRT " + (i + 1) + "/" + selectedUnique.Length + " · " + record.Name);
                try
                {
                    string path = await assetLibrary.ExtractAsync(record, Targets, CancellationToken.None);
                    await SharedTextureCache.Normalize(assetLibrary.ModelDirectory(record), assetLibrary.Settings.textureLimit);
                    world.models.Prepare(record.Id, path); ready.Add(record.Id);
                }
                catch (Exception exception)
                {
                    prepared.failedMprtModels++;
                    Debug.LogWarning(record.modelPath + ": " + exception.Message);
                }
            }
            prepared.mprtPlacements.AddRange(selected.Where(pair => ready.Contains(pair.Value.Id)));
            prepared.mprtPrepareSeconds = (float)timer.Elapsed.TotalSeconds;
        }

        private static double SquaredDistance(Float3 a, Float3 b)
        {
            double x = a.x - b.x, y = a.y - b.y, z = a.z - b.z;
            return x * x + y * y + z * z;
        }

        private IEnumerator MapReferenceComparisonSmoke()
        {
            string map = CommandLineValue("-remapMapReferenceMap=") ?? "mp_rr_party_crasher";
            string requestedTarget = CommandLineValue("-remapMapReferenceTarget=");
            bool includeMprt = Environment.GetCommandLineArgs().Contains("-remapMapReferenceMprt");
            bool streaming = Environment.GetCommandLineArgs().Contains("-remapMapReferenceStreaming");
            bool uncapped = Environment.GetCommandLineArgs().Contains("-remapMapReferenceUncapped");
            int mprtLimit = includeMprt ? (streaming ? int.MaxValue :
                CommandLineInt("-remapMapReferenceMprtLimit=", 50000)) : -1;
            if (uncapped) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; }
            string[] targets = string.IsNullOrWhiteSpace(requestedTarget)
                ? new[] { GameTargets.R5Reloaded, GameTargets.R5Flowstate }
                : new[] { GameTargets.Normalize(requestedTarget) };
            var report = new MapReferencePerformanceReport();
            foreach (string target in targets)
            {
                Task<PreparedMapReference> preparation = PrepareMapReferenceComparison(target, map, mprtLimit);
                while (!preparation.IsCompleted) yield return null;
                if (preparation.IsFaulted)
                {
                    Debug.LogException(preparation.Exception);
                    Application.Quit(1);
                    yield break;
                }

                PreparedMapReference prepared = preparation.Result;
                ApexBspMap bsp = prepared.data.Map;
                var result = new MapReferencePerformanceResult
                {
                    target = target,
                    map = map,
                    gpu = SystemInfo.graphicsDeviceName,
                    cpu = SystemInfo.processorType,
                    unity = Application.unityVersion,
                    surfaces = bsp.Surfaces.Count,
                    meshChunks = world.BspReferenceMeshCount,
                    vertices = bsp.Surfaces.Sum(surface => surface.Positions.Length),
                    triangles = bsp.Surfaces.Sum(surface => surface.Indices.Length / 3),
                    staticProps = bsp.StaticProps.Count,
                    loadSeconds = prepared.loadSeconds,
                    mprtRequested = includeMprt,
                    resolvedStaticProps = prepared.resolvedStaticProps,
                    missingStaticProps = prepared.missingStaticProps,
                    uniqueMprtModels = prepared.uniqueMprtModels,
                    cachedMprtModels = prepared.cachedMprtModels,
                    failedMprtModels = prepared.failedMprtModels,
                    mprtPrepareSeconds = prepared.mprtPrepareSeconds
                };

                SetStatus(GameTargets.DisplayName(target) + " · " + map + " · BSP de collision");
                MapReferenceMprtStageResult latest = null;
                yield return MeasureMapReferenceStage(0, includeMprt ? 45 : 90, includeMprt ? 120 : 240,
                    stage => { latest = stage; result.mprtStages.Add(stage); });

                if (includeMprt && prepared.mprtPlacements.Count > 0)
                {
                    if (streaming)
                    {
                        world.ConfigureMprtReference(prepared.mprtPlacements.Select(pair => new MapReferenceModel {
                            AssetId = pair.Value.Id, Placement = pair.Key }),
                            MprtReferenceOptions.From(assetLibrary.Settings), new Float3());
                        world.SetMapReferenceVisibility(true, true);
                        for (int frame = 0; frame < 420; frame++)
                        {
                            if ((frame & 31) == 0) SetStatus("Streaming MPRT · " +
                                world.MprtReferenceActiveCount.ToString("N0") + "/" +
                                world.MprtReferenceAvailableCount.ToString("N0"));
                            yield return null;
                        }
                        result.placedMprtModels = world.MprtReferenceActiveCount;
                        yield return MeasureMapReferenceStage(result.placedMprtModels, 45, 120,
                            stage => { latest = stage; result.mprtStages.Add(stage); });
                    }
                    else
                    {
                        world.BeginMprtReference(new Float3());
                        int placed = 0;
                        int[] stages = new[] { 1000, 5000, 10000, 25000, 50000, 100000, 200000,
                            prepared.mprtPlacements.Count }
                            .Where(value => value > 0 && value <= prepared.mprtPlacements.Count).Distinct().OrderBy(value => value).ToArray();
                        foreach (int stageTarget in stages)
                        {
                            while (placed < stageTarget)
                            {
                                var pair = prepared.mprtPlacements[placed++];
                                world.AddMprtReference(new MapReferenceModel { AssetId = pair.Value.Id, Placement = pair.Key });
                                if ((placed & 127) == 0)
                                {
                                    SetStatus(GameTargets.DisplayName(target) + " · placement MPRT " + placed.ToString("N0") +
                                        "/" + prepared.mprtPlacements.Count.ToString("N0"));
                                    yield return null;
                                }
                            }
                            world.SetMapReferenceVisibility(true, true);
                            yield return MeasureMapReferenceStage(placed, 45, 120,
                                stage => { latest = stage; result.mprtStages.Add(stage); });
                        }
                        result.placedMprtModels = placed;
                    }
                }
                result.frameMedianMs = latest.frameMedianMs;
                result.frameP95Ms = latest.frameP95Ms;
                result.medianFps = latest.medianFps;
                result.p95Fps = latest.p95Fps;
                result.framesWithin60BudgetPercent = latest.framesWithin60BudgetPercent;
                result.allocatedMB = latest.allocatedMB;
                result.reservedMB = latest.reservedMB;
                result.managedMB = latest.managedMB;
                report.results.Add(result);

                SetStatus(GameTargets.DisplayName(target) + " · " + result.medianFps.ToString("0.0") +
                    " FPS médiane · P95 " + result.frameP95Ms.ToString("0.00") + " ms · " +
                    result.triangles.ToString("N0") + " triangles");
                yield return new WaitForEndOfFrame();
                Texture2D capture = ScreenCapture.CaptureScreenshotAsTexture();
                if (capture == null) throw new InvalidOperationException("A visible player window is required for the BSP capture.");
                string screenshot = Path.Combine(Application.dataPath, "..", "map-reference-" + target +
                    (includeMprt ? "-mprt" : "") + ".png");
                try { File.WriteAllBytes(screenshot, capture.EncodeToPNG()); }
                finally { Destroy(capture); }
                Debug.Log("REMAP_MAP_REFERENCE_CAPTURE: " + screenshot);
                world.ClearMapReference();
                yield return null;
            }

            string json = JsonUtility.ToJson(report, true);
            string reportPath = Path.Combine(Application.dataPath, "..", "map-reference-performance.json");
            File.WriteAllText(reportPath, json);
            Debug.Log("REMAP_MAP_REFERENCE_PERFORMANCE_OK\n" + json);
            Application.Quit(0);
        }

        private IEnumerator MeasureMapReferenceStage(int placed, int warmupFrames, int sampleFrames,
            Action<MapReferenceMprtStageResult> completed)
        {
            SetStatus("Mesure MPRT · " + placed.ToString("N0") + " modèles");
            for (int i = 0; i < warmupFrames; i++) yield return new WaitForEndOfFrame();
            var frameTimes = new List<float>(sampleFrames);
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < sampleFrames; i++)
            {
                yield return new WaitForEndOfFrame();
                frameTimes.Add((float)timer.Elapsed.TotalMilliseconds); timer.Restart();
            }
            frameTimes.Sort();
            float median = frameTimes[frameTimes.Count / 2];
            float p95 = frameTimes[Mathf.Clamp(Mathf.CeilToInt(frameTimes.Count * .95f) - 1, 0, frameTimes.Count - 1)];
            completed(new MapReferenceMprtStageResult {
                placed = placed, frameMedianMs = median, frameP95Ms = p95,
                medianFps = 1000f / median, p95Fps = 1000f / p95,
                framesWithin60BudgetPercent = frameTimes.Count(time => time <= 16.667f) * 100f / frameTimes.Count,
                allocatedMB = Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f),
                reservedMB = Profiler.GetTotalReservedMemoryLong() / (1024f * 1024f),
                managedMB = GC.GetTotalMemory(false) / (1024f * 1024f)
            });
        }

        private static string CommandLineValue(string prefix)
        {
            string argument = Environment.GetCommandLineArgs().FirstOrDefault(value =>
                value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            return argument == null ? null : argument.Substring(prefix.Length).Trim();
        }

        private static int CommandLineInt(string prefix, int fallback)
        {
            return int.TryParse(CommandLineValue(prefix), out int value) ? Math.Max(0, value) : fallback;
        }
    }
}
