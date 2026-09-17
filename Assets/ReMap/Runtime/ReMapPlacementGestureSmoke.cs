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
        private async Task CheckPlacementGestures()
        {
            await TreeFrames(12); enabled = false;
            void Check(bool value, string message) { if (!value) throw new Exception(message); }
            var record = new GameAssetRecord { guid = "04c630f398c9d040", modelPath = "canyonland_thunderdome_ground_cap_01.rmdl" };
            record.origins.Add(new AssetOrigin());
            string fixture = RsxAssetLibrary.FindExistingModelDirectory(assetLibrary.LocalRoot,record.guid) ?? throw new DirectoryNotFoundException("Cached placement fixture not found.");
            string cast = Path.Combine(fixture, File.ReadLines(Path.Combine(fixture,"complete.txt")).First());
            world.models.Prepare(record.Id,cast);
            var model = world.models.Create(record.Id,false);
            CatalogEntry entry;
            try { entry = RememberPlacementEntry(record,model); } finally { world.models.Release(record.Id,model); }
            var card = new Button {text="Placement QA"}; card.style.width=160;card.style.height=120;catalogList.Clear();catalogList.Add(card); RegisterDragSource(card,null,record);
            await TreeFrames(3);
            int count = snapshot.objects.Count;
            card.RegisterCallback<PointerDownEvent>(e=>Debug.Log("REMAP_DOUBLE_EVENT: count="+e.clickCount+" button="+e.button),TrickleDown.TrickleDown);
            using (var down = PointerDownEvent.GetPooled(new Event {type=EventType.MouseDown,button=0,clickCount=2,mousePosition=card.worldBound.center})) card.SendEvent(down);
            Check(placing?.Id==record.Id && !dragCandidate && suppressCardClick,"Double-click did not activate placement exclusively: placing="+placing?.Id+" candidate="+dragCandidate+" suppressed="+suppressCardClick+" ready="+(ReadyPlacementEntry(record)!=null)+" busy="+assetBusy);
            Check(snapshot.objects.Count==count,"Double-click inserted an object immediately.");
            CancelPlacement(); card.RemoveFromHierarchy();

            var previousMouse=Mouse.current; var previousKeys=Keyboard.current;
            var mouse=InputSystem.AddDevice<Mouse>(); var keys=InputSystem.AddDevice<Keyboard>();
            void Input(Vector2 point, ushort buttons) { InputSystem.QueueStateEvent(mouse,new MouseState {position=point,buttons=buttons}); InputSystem.Update(); }
            Vector2 Panel(Vector2 point) => RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(point.x,Screen.height-point.y));
            GameObject Ghost() => GameObject.Find("Placement preview");
            try
            {
                world.Focus(null);
                var point=world.Camera.pixelRect.center;
                Check(world.Placement(point,entry,snap,out var expected),"Missing test surface.");
                dragEntry=null; dragRecord=record; dragObjectId=null; dragCandidate=true; libraryDragStart=Vector2.zero;
                Input(point,1); HandleLibraryDrag(Panel(point),point);
                var ghost=Ghost();
                Check(libraryDragging && ghost!=null,"No real model during drag.");
                Check(ghost.GetComponent<MeshFilter>().sharedMesh.vertexCount>100,"Drag displayed a placeholder instead of the model.");
                Check(ghost.GetComponents<Collider>().All(c=>!c.enabled),"Ghost intercepts placement rays.");
                Check(Vector3.Distance(ghost.transform.position,expected)<.001f,"Ghost is not aligned like Place.");
                Check(snapshot.objects.Count==count,"Drag changed the document before release.");
                var next=point+Vector2.right*90;
                Check(world.Placement(next,entry,snap,out expected),"Missing second surface.");
                Input(next,1);HandleLibraryDrag(Panel(next),next);
                Check(Ghost()==ghost && Vector3.Distance(ghost.transform.position,expected)<.001f,"Ghost did not follow the cursor or was rebuilt each frame.");
                Input(next,1);HandleLibraryDrag(new Vector2(-20,-20),next);
                Check(Ghost()==null,"Ghost remained outside the viewport.");
                Input(next,1);HandleLibraryDrag(Panel(next),next);
                Check(Ghost()!=null,"Ghost failed to return to the scene.");
                Input(next,0);HandleLibraryDrag(Panel(next),next);
                Check(!libraryDragging && Ghost()==null && snapshot.objects.Count==count+1,"Drop did not create exactly one object and remove the ghost.");
                Check(Vector3.Distance(WorldView.ToVector(snapshot.objects.Last().position),expected)<.001f,"Final drop differs from preview.");
                session.Undo();Refresh();
                Check(snapshot.objects.Count==count,"Drop cannot be undone as one action.");
                dragEntry=entry;dragRecord=record;dragObjectId=null;dragCandidate=true;libraryDragStart=Vector2.zero;
                Input(point,1);HandleLibraryDrag(Panel(point),point);
                InputSystem.QueueStateEvent(keys,new KeyboardState(Key.Escape));InputSystem.Update();HandleLibraryDrag(Panel(point),point);
                Check(!libraryDragging && Ghost()==null && snapshot.objects.Count==count,"Escape left a ghost or placed an object.");
                InputSystem.QueueStateEvent(keys,new KeyboardState());InputSystem.Update();

                // A slow preparation must not reactivate placement or a canceled drag.
                var slow=new GameAssetRecord {guid="0000000000000123",modelPath="qa_delayed.rmdl"};slow.origins.Add(new AssetOrigin());
                var pending=new TaskCompletionSource<CatalogEntry>();dropPreparations[slow.Id]=pending.Task;
                var request=RequestModelPlacement(slow);CancelPlacement();pending.SetResult(entry);await request;
                Check(placing==null,"Canceled double-click returned after loading.");dropPreparations.Remove(slow.Id);
                pending=new TaskCompletionSource<CatalogEntry>();dropPreparations[slow.Id]=pending.Task;
                dragEntry=null;dragRecord=slow;dragObjectId=null;dragCandidate=libraryDragging=true;
                StartDragPreview();EndLibraryDrag();pending.SetResult(entry);await TreeFrames(3);
                Check(dragEntry==null && Ghost()==null && snapshot.objects.Count==count,"Canceled asynchronous drag returned.");dropPreparations.Remove(slow.Id);
                Debug.Log("REMAP_PLACEMENT_GESTURES_OK: double-click, real mesh, tracking, viewport exit, drop alignment, undo, Escape, delayed cancellation");
            }
            finally { EndLibraryDrag();CancelPlacement();InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keys);previousMouse?.MakeCurrent();previousKeys?.MakeCurrent();enabled=true; }
        }
        private IEnumerator PlacementGestureSmoke()
        {
            var task=CheckPlacementGestures();while(!task.IsCompleted)yield return null;
            if(task.IsFaulted){Debug.LogException(task.Exception);Application.Quit(1);}else Application.Quit(0);
        }
    }
}
