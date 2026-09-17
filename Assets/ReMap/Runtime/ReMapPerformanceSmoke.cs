using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;
namespace ReMap.Standalone
{
    [Serializable] public sealed class WorkspacePerformanceReport
    {
        public string label, gpu, cpu, unity;
        public int objects, loadedModels, hierarchyRows;
        public float multiSelectMs, multiPreviewMs, multiCommitMs, multiFrameP95Ms;
        public float createMs, refreshMedianMs, selectionMedianMs, editMedianMs, frameMedianMs, frameP95Ms, groupFrameP95Ms, allocatedMB, managedMB;
    }
    public sealed partial class ReMapApp
    {
        private WorkspacePerformanceReport performanceReport;
        private static float Median(List<float> values) { values.Sort(); return values[values.Count/2]; }
        private async Task CheckPerformance()
        {
            await TreeFrames(12); string[] args = Environment.GetCommandLineArgs();
            bool real = args.Contains("-remapPerfReal");
            performanceReport = new WorkspacePerformanceReport { label = args.Contains("-remapPerfBaseline") ? "baseline" : real ? "real" : "optimized", gpu = SystemInfo.graphicsDeviceName, cpu = SystemInfo.processorType, unity = Application.unityVersion };
            var assetIds = new List<string> { "demo:cube" };
            if (real) {
                session.Edit(d => d.targetMaps = new List<string> { "mp_rr_desertlands_hu", "mp_rr_olympus_mu2" }); Refresh(); await IndexAssets(); assetIds.Clear();
                foreach (string guid in new[] { "43f9958ba65ae981", "b7f4185bbf83a8e8", "a32899530e86bd40", "fc0ebc1ace084ec5" }) {
                    var record = assetLibrary.Records.First(r => r.guid == guid); var path = assetLibrary.CachedModel(record);
                    if (path == null) throw new Exception("Performance test requires cached asset " + guid);
                    world.models.Prepare(record.Id, path); assetIds.Add(record.Id);
                }
            }
            var doc = new MapDocument { name = "Scène de référence · 3 000 objets" };
            var all = new MapObject { isGroup = true, displayName = "Scène 3k" }; doc.objects.Add(all);
            var models = new List<MapObject>();
            for (int group = 0; group < 30; group++) {
                var folder = new MapObject { isGroup = true, displayName = "Secteur " + (group+1), parentId = all.id }; doc.objects.Add(folder);
                for (int i = 0; i < 100; i++) {
                    int n = group * 100 + i;
                    // 80% small props, 18% medium, 2% detailed platforms in the real-asset scene.
                    string assetId = real ? assetIds[n % 50 == 0 ? 3 : n % 5 == 0 ? 2 : n % 2] : assetIds[0];
                    var item = new MapObject { assetId = assetId, displayName = "Objet " + (n+1), parentId = folder.id,
                        position = new Float3((n%60-30)*3, .5f, (n/60-25)*3) };
                    doc.objects.Add(item); models.Add(item);
                }
            }
            var clock = Stopwatch.StartNew(); session.Replace(doc); selectedId = models[0].id; Refresh();
            performanceReport.createMs = (float)clock.Elapsed.TotalMilliseconds;
            world.Focus(all.id); await TreeFrames(20); Debug.Log("REMAP_PERF: created 3000 objects");
            var times = new List<float>();
            for (int i = 0; i < 3; i++) { clock.Restart(); Refresh(); times.Add((float)clock.Elapsed.TotalMilliseconds); await TreeFrames(3); }
            performanceReport.refreshMedianMs = Median(times); times.Clear();
            for (int i = 0; i < 5; i++) { clock.Restart(); Select(models[i*599].id); times.Add((float)clock.Elapsed.TotalMilliseconds); await TreeFrames(3); }
            performanceReport.selectionMedianMs = Median(times); times.Clear();
            for (int i = 0; i < 3; i++) { clock.Restart(); positionInput.Change(positionInput.value + Vector3.right); CommitInspectorEdit(); times.Add((float)clock.Elapsed.TotalMilliseconds); await TreeFrames(3); }
            performanceReport.editMedianMs = Median(times);
            performanceReport.objects = snapshot.objects.Count(o => !o.isGroup); performanceReport.loadedModels = world.LoadedModelCount;
            performanceReport.hierarchyRows = hierarchyRows.Count;
            Debug.Log("REMAP_PERF: operations measured");
            // Exercise a distant row, parent movement and undo at the target size.
            string last = models.Last().id; Select(last); FocusHierarchy(last); await TreeFrames(5);
            ToolCheck(hierarchyRows.ContainsKey(last), "The last hierarchy row could not be reached.");
            var original = world.WorldPose(last).position; Select(all.id);
            positionInput.Change(new Vector3(2,0,0)); CommitInspectorEdit();
            ToolCheck(Mathf.Abs(world.WorldPose(last).position.x - original.x - 2) < .001f, "Group movement lost a descendant at 3k.");
            session.Undo(); Refresh(); ToolCheck(Mathf.Abs(world.WorldPose(last).position.x - original.x) < .001f, "Group undo failed at 3k.");
            // Select all 3,000 models individually, without relying on one parent transform.
            clock.Restart();SetSelection(models.Select(o=>o.id).ToArray());RefreshInspector();RefreshObjects();world.HighlightSelection(SelectionRoots());
            performanceReport.multiSelectMs=(float)clock.Elapsed.TotalMilliseconds;
            ToolCheck(SelectionRoots().Count==3000,"Multi-selection lost models.");
            clock.Restart();positionInput.Change(Vector3.up*.5f);performanceReport.multiPreviewMs=(float)clock.Elapsed.TotalMilliseconds;
            ToolCheck(Mathf.Abs(world.WorldPose(last).position.y-original.y-.5f)<.001f,"Multi-selection live move failed at 3k.");
            clock.Restart();CommitInspectorEdit();performanceReport.multiCommitMs=(float)clock.Elapsed.TotalMilliseconds;
            session.Undo();Refresh();ToolCheck(Mathf.Abs(world.WorldPose(last).position.y-original.y)<.001f,"Multi-selection undo failed at 3k.");
            Select(last); await TreeFrames(8);
            if (!Environment.GetCommandLineArgs().Contains("-remapPerfBaseline")) {
                ToolCheck(hierarchyRows.Count < 100, "Hierarchy instantiated offscreen rows.");
                world.Sync(snapshot, selectedId); ToolCheck(world.LastSyncTransformWrites == 0, "Unchanged scene rewrote transforms.");
                session.Edit(d => d.objects.Find(o => o.id == last).position.x += 1); Refresh();
                ToolCheck(world.LastSyncTransformWrites == 1, "Editing one model rewrote other transforms.");
                session.Undo(); Refresh();
                Select(last); FocusHierarchy(last); await TreeFrames(5);
                BeginHierarchyRename(last); await TreeFrames(3);
                hierarchyName.value = "Objet 3000 renommé";
                using (var enter = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Return })) hierarchyName.SendEvent(enter);
                await TreeFrames(5); ToolCheck(snapshot.objects.Find(o => o.id == last).displayName == "Objet 3000 renommé", "Virtual row rename failed.");
                var beforeReparent = WorldView.ToVector(world.WorldPose(last).position);
                ReparentObject(last, doc.objects.First(o => o.isGroup && o.parentId == all.id).id); await TreeFrames(4);
                ToolCheck(Vector3.Distance(WorldView.ToVector(world.WorldPose(last).position), beforeReparent) < .001f, "Reparenting a virtual row moved the object.");
                session.Undo(); Refresh(); ToggleFolder(all.id); await TreeFrames(4);
                ToolCheck(visibleHierarchyIds.Count == 1, "Collapsing the large tree did not hide descendants.");
                ToggleFolder(all.id); FocusHierarchy(last); await TreeFrames(5);
                ToolCheck(hierarchyRows.ContainsKey(last), "Last virtual row was lost after collapse/expand.");
            }
        }
        private IEnumerator PerformanceSmoke()
        {
            var check = CheckPerformance(); while (!check.IsCompleted) yield return null;
            if (check.IsFaulted) { Debug.LogException(check.Exception); Application.Quit(1); yield break; }
            var times = new List<float>(); var clock = Stopwatch.StartNew();
            for (int i = 0; i < 90; i++) { yield return new WaitForEndOfFrame(); times.Add((float)clock.Elapsed.TotalMilliseconds); clock.Restart(); yield return null; }
            performanceReport.frameMedianMs = Median(times); performanceReport.frameP95Ms = times[(int)(times.Count * .95f)];
            Select(snapshot.objects.First(o => o.isGroup).id); times.Clear();
            for (int i = 0; i < 45; i++) { yield return new WaitForEndOfFrame(); times.Add((float)clock.Elapsed.TotalMilliseconds); clock.Restart(); yield return null; }
            Median(times); performanceReport.groupFrameP95Ms = times[(int)(times.Count * .95f)];
            SetSelection(snapshot.objects.Where(o=>!o.isGroup).Select(o=>o.id).ToArray());RefreshInspector();RefreshObjects();times.Clear();clock.Restart();
            for(int i=0;i<45;i++){yield return new WaitForEndOfFrame();times.Add((float)clock.Elapsed.TotalMilliseconds);clock.Restart();yield return null;}
            Median(times);performanceReport.multiFrameP95Ms=times[(int)(times.Count*.95f)];
            performanceReport.allocatedMB = Profiler.GetTotalAllocatedMemoryLong() / (1024f*1024);
            performanceReport.managedMB = GC.GetTotalMemory(false) / (1024f*1024);
            string report = JsonUtility.ToJson(performanceReport, true); Debug.Log("REMAP_PERFORMANCE_OK\n" + report);
            File.WriteAllText(Path.Combine(Application.dataPath, "..", "performance-" + performanceReport.label + ".json"), report);
            SetStatus("Scène de référence : 3 000 objets · " + performanceReport.frameMedianMs.ToString("0.0") + " ms/image (médiane)");
            yield return new WaitForEndOfFrame(); var capture = ScreenCapture.CaptureScreenshotAsTexture();
            if (capture != null) { File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "performance-" + performanceReport.label + ".png"), capture.EncodeToPNG()); Destroy(capture); }
            Application.Quit(0);
        }
    }
}
