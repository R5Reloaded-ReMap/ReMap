using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;
namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private static async Task TreeFrames(int count = 3) { for (int i = 0; i < count; i++) await Task.Yield(); }
        private static void TreeClick(VisualElement target)
        {
            using (var e = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = target.worldBound.center })) target.SendEvent(e);
            using (var e = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = target.worldBound.center })) target.SendEvent(e);
        }
        private async Task CheckNestedHierarchy()
        {
            await TreeFrames(12);
            session.Replace(new MapDocument()); selectedId = null; Refresh();
            CreateGroupAt(""); string zone = selectedId; CommitHierarchyName(zone, "Zone de départ");
            collapsedGroups.Add(zone); RefreshObjects();
            await TreeFrames();
            TreeClick(childFolderButton); await TreeFrames();
            string building = selectedId;
            if(renamingHierarchyId!=building||hierarchyName==null||root.panel.focusController.focusedElement!=hierarchyName)throw new Exception("A newly created folder did not open and focus its inline name editor.");
            if (building == zone || snapshot.objects.Find(o => o.id == building).parentId != zone || collapsedGroups.Contains(zone)) throw new Exception("Subfolder button: objects=" + snapshot.objects.Count + " selected=" + selectedId + " parent=" + snapshot.objects.Find(o => o.id == building)?.parentId + " closed=" + collapsedGroups.Contains(zone));
            CommitHierarchyName(building, "Bâtiments");
            TreeClick(childFolderButton); await TreeFrames(); string floor = selectedId; CommitHierarchyName(floor, "Étage 1");
            if (snapshot.objects.Find(o => o.id == floor).parentId != building) throw new Exception("Third hierarchy level failed.");
            await DropLibraryItem(catalog.Entries[0], null, null, floor, Vector2.zero, false); string block = selectedId; CommitHierarchyName(block, "Bloc de test");
            var originalWorld = WorldView.ToVector(world.WorldPose(block).position);
            Select(zone); positionInput.Change(new Vector3(5, 0, 2)); CommitInspectorEdit();
            if (Vector3.Distance(WorldView.ToVector(world.WorldPose(block).position), originalWorld + new Vector3(5, 0, 2)) > .001f) throw new Exception("Nested group did not carry grandchild geometry.");
            session.Undo(); Refresh();
            if (Vector3.Distance(WorldView.ToVector(world.WorldPose(block).position), originalWorld) > .001f) throw new Exception("Nested group move undo failed.");
            var files = new MapFiles(Path.Combine(Application.temporaryCachePath, "NestedTreeQA"), codec); files.Save("nested", snapshot);
            var saved = files.Load("nested");
            if (saved.objects.Find(o => o.id == floor).parentId != building || saved.objects.Find(o => o.id == block).parentId != floor) throw new Exception("Nested parents lost during save/load.");
            session.Edit(d => d.objects.Find(o => o.id == zone).disabled = true); Refresh();
            if (world.GenerationObjects(snapshot).Any()) throw new Exception("Disabled ancestor did not exclude nested objects.");
            session.Undo(); Refresh();
            var beforeRejected = codec.Encode(snapshot); dragObjectId = zone;
            if (ValidHierarchyDrop(floor) || ValidHierarchyDrop(zone)) throw new Exception("Cyclic drop accepted.");
            if (codec.Encode(snapshot) != beforeRejected) throw new Exception("Drop validation mutated the map.");
            dragObjectId = null;
            // Move an entire subtree using the same pointer path as the interactive tree.
            CreateGroupAt(""); string destination = selectedId; CommitHierarchyName(destination, "Décor extérieur");
            autoCollapseOtherFolders=true;Select(destination);
            if(!collapsedGroups.Contains(zone))throw new Exception("Exclusive hierarchy did not close a folder outside the selection.");
            Select(block);
            if(collapsedGroups.Contains(zone)||collapsedGroups.Contains(building)||collapsedGroups.Contains(floor))throw new Exception("Exclusive hierarchy closed a folder containing the selection.");
            // A scene selection is often the first half of moving objects into an already open nested folder.
            // Keep the complete n+3 destination path visible until the hierarchy drag begins.
            collapsedGroups.Remove(zone);collapsedGroups.Remove(building);collapsedGroups.Remove(floor);RefreshObjects();await TreeFrames();
            SelectModified(destination,false,false,false,true);
            if(collapsedGroups.Contains(zone)||collapsedGroups.Contains(building)||collapsedGroups.Contains(floor))throw new Exception("Scene selection closed an open nested destination folder.");
            using(var sceneDown=PointerDownEvent.GetPooled(new Event {type=EventType.MouseDown,button=0,mousePosition=hierarchyRows[destination].worldBound.center}))hierarchyRows[destination].SendEvent(sceneDown);
            if(collapsedGroups.Contains(zone)||collapsedGroups.Contains(building)||collapsedGroups.Contains(floor))throw new Exception("Beginning a drag after scene selection closed the nested destination folder.");
            EndLibraryDrag();
            Select(block);
            // Opening a destination is intentional: pressing another row to drag must not close it before movement starts.
            collapsedGroups.Remove(destination);RefreshObjects();await TreeFrames();
            using(var down=PointerDownEvent.GetPooled(new Event {type=EventType.MouseDown,button=0,mousePosition=hierarchyRows[building].worldBound.center}))hierarchyRows[building].SendEvent(down);
            if(collapsedGroups.Contains(destination))throw new Exception("Selecting a drag source closed the open destination folder.");
            EndLibraryDrag();
            autoCollapseOtherFolders=false;
            collapsedGroups.Add(destination); Select(building); await TreeFrames();
            var originalMouse = Mouse.current; var testMouse = InputSystem.AddDevice<Mouse>();
            try
            {
                using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = hierarchyRows[building].worldBound.center })) hierarchyRows[building].SendEvent(down);
                if (!dragCandidate || dragObjectId != building) throw new Exception("Tree row did not begin a folder drag.");
                Vector2 point = hierarchyRows[destination].worldBound.center;
                InputSystem.QueueStateEvent(testMouse, new MouseState { position = world.Camera.pixelRect.center, buttons = 1 }); InputSystem.Update(); testMouse.MakeCurrent();
                HandleLibraryDrag(point, world.Camera.pixelRect.center);
                if (!libraryDragging || hierarchyDropRow == null || !hierarchyDropRow.ClassListContains("tree-drop")) throw new Exception("Folder drop highlight missing.");
                hoverFolderSince = Time.unscaledTime - 1; HoverHierarchyDrop(point);
                if (collapsedGroups.Contains(destination)) throw new Exception("Hover did not open target folder.");
                await TreeFrames(); point = hierarchyRows[destination].worldBound.center;
                InputSystem.QueueStateEvent(testMouse, new MouseState { position = world.Camera.pixelRect.center }); InputSystem.Update(); testMouse.MakeCurrent();
                HandleLibraryDrag(point, world.Camera.pixelRect.center);
                if (snapshot.objects.Find(o => o.id == building).parentId != destination || snapshot.objects.Find(o => o.id == floor).parentId != building) throw new Exception("Dragging a folder did not preserve its subtree.");
                if (Vector3.Distance(WorldView.ToVector(world.WorldPose(block).position), originalWorld) > .001f) throw new Exception("Folder drop moved descendant world geometry.");
            }
            finally { EndLibraryDrag(); InputSystem.RemoveDevice(testMouse); originalMouse?.MakeCurrent(); }
            session.Undo(); Refresh(); if (snapshot.objects.Find(o => o.id == building).parentId != zone) throw new Exception("Folder drop undo failed.");
            Select(floor); FocusHierarchy(floor); await TreeFrames();
            BeginHierarchyRename(floor);
            await TreeFrames(); if (renamingHierarchyId != floor || hierarchyName == null) throw new Exception("Inline rename: id=" + renamingHierarchyId + " selected=" + selectedId + " focus=" + root.panel.focusController.focusedElement);
            hierarchyName.value = "Passerelles";
            using (var enter = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Return })) hierarchyName.SendEvent(enter);
            await TreeFrames(); if (snapshot.objects.Find(o => o.id == floor).displayName != "Passerelles") throw new Exception("Inline rename was not committed.");
            Select(building); Duplicate(); string copy = selectedId; RevealHierarchy(copy); RefreshObjects();
            if (MapHierarchy.Subtree(snapshot, copy).Count != 3) throw new Exception("Folder duplication lost descendants.");
            Delete(); if (snapshot.objects.Any(o => o.parentId == copy)) throw new Exception("Folder deletion left orphans.");
            Select(floor); world.Focus(zone); await TreeFrames();
            ShowHierarchyMenu(floor, hierarchyRows[floor].worldBound.center);
            if (!hierarchyMenu.Children().OfType<Button>().Any(b => b.text == ReMap.Standalone.Core.L.T("#CREATE_SUBGROUP"))) throw new Exception("Folder context action missing.");
            hierarchyMenu.style.display = DisplayStyle.None;
            SetStatus("Dossiers imbriqués : création, déplacement, annulation, sauvegarde et renommage vérifiés.");
        }
        private IEnumerator HierarchySmoke()
        {
            var check = CheckNestedHierarchy(); while (!check.IsCompleted) yield return null;
            if (check.IsFaulted) Debug.LogException(check.Exception);
            else
            {
                yield return new WaitForEndOfFrame(); var image = ScreenCapture.CaptureScreenshotAsTexture();
                if (image != null) { File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "nested-hierarchy-preview.png"), image.EncodeToPNG()); Destroy(image); }
                Debug.Log("REMAP_NESTED_HIERARCHY_OK");
            }
            Application.Quit(check.IsFaulted ? 1 : 0);
        }
    }
}
