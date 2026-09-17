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
        private async Task CheckEditorFeatures() {
            await TreeFrames(12);enabled=false;moveSnap=1;world.GridStep=moveSnap;
            void Check(bool value,string message){if(!value)throw new Exception(message);}
            void Near(Vector3 a,Vector3 b,string message){Check(Vector3.Distance(a,b)<.015f,message+": "+a+" / "+b);}
            var parent=new MapObject{isGroup=true,assetId="group:empty",displayName="Bâtiment",position=new Float3(3,1,0),rotation=new Float3(0,35,0),scale=new Float3(2,2,2)};
            var a=new MapObject{assetId="demo:cube",displayName="Bloc A",parentId=parent.id,position=new Float3(-1,1,0)};
            var b=new MapObject{assetId="demo:cube",displayName="Bloc B",position=new Float3(-3,2,0)};
            var hidden=new MapObject{assetId="demo:cube",displayName="Masqué",parentId=parent.id,disabled=true,position=new Float3(0,0,2)};
            var doc=new MapDocument();doc.objects.AddRange(new[]{parent,a,b,hidden});session.Replace(doc);selectedId=null;Refresh();
            string oldClipboard=GUIUtility.systemCopyBuffer;
            var shortcutPrevious=Keyboard.current;var shortcutKeyboard=InputSystem.AddDevice<Keyboard>();
            try
            {
                void Shortcut(params Key[] pressed){InputSystem.QueueStateEvent(shortcutKeyboard,new KeyboardState(pressed));InputSystem.Update();shortcutKeyboard.MakeCurrent();HandleGlobalKeyboardShortcuts();InputSystem.QueueStateEvent(shortcutKeyboard,new KeyboardState());InputSystem.Update();}
                Select(a.id);string originalName=snapshot.objects.Find(o=>o.id==a.id).displayName;
                objectNameInput.Focus();objectNameInput.value="Nom modifié";
                Check(inspectorDirty,"Focused inspector text edit was not staged");
                Shortcut(Key.LeftCtrl,Key.Z);
                Check(snapshot.objects.Find(o=>o.id==a.id).displayName==originalName,"Ctrl+Z did not undo the focused inspector edit");
                Check(root.panel.focusController.focusedElement==viewport,"Ctrl+Z did not release inspector focus");
                Select(parent.id);int clipboardBase=snapshot.objects.Count;
                Shortcut(Key.LeftCtrl,Key.C);Shortcut(Key.LeftCtrl,Key.V);Check(snapshot.objects.Count==clipboardBase+3,"Ctrl+V did not paste the copied hierarchy branch");
                Shortcut(Key.LeftCtrl,Key.X);Check(snapshot.objects.Count==clipboardBase,"Ctrl+X did not cut the pasted hierarchy branch");
                Shortcut(Key.LeftCtrl,Key.Z);Check(snapshot.objects.Count==clipboardBase+3,"Ctrl+Z did not restore the cut branch");
                Shortcut(Key.LeftCtrl,Key.Z);Check(snapshot.objects.Count==clipboardBase,"Second Ctrl+Z did not undo the paste");
                Select(b.id);Shortcut(Key.LeftCtrl,Key.C);var copiedWorld=world.WorldPose(b.id);
                Select(parent.id);FocusHierarchy(parent.id);await TreeFrames();
                Shortcut(Key.LeftCtrl,Key.V);var pastedIntoFolder=selectedId;
                Check(snapshot.objects.Find(o=>o.id==pastedIntoFolder)?.parentId==parent.id,"Ctrl+V did not paste into the focused folder");
                Near(WorldView.ToVector(world.WorldPose(pastedIntoFolder).position),WorldView.ToVector(copiedWorld.position),"Paste into focused folder moved the object");
                Shortcut(Key.LeftCtrl,Key.Z);Check(snapshot.objects.Count==clipboardBase,"Undo did not remove the object pasted into the focused folder");
                Select(parent.id);
                Shortcut(Key.LeftCtrl,Key.D);Check(placingSelection!=null&&snapshot.objects.Count==clipboardBase,"Ctrl+D placement="+(placingSelection!=null)+" count="+snapshot.objects.Count+" expected="+clipboardBase+" selected="+selectedIds.Count);
                Shortcut(Key.Escape);Check(placingSelection==null&&snapshot.objects.Count==clipboardBase,"Escape did not cancel duplicate placement");
                InsertZipline(Vector3.zero);string sourceZipline=selectedId;
                var sourceCustom=snapshot.objects.Find(o=>o.id==sourceZipline);
                session.Edit(d=>ApplyZiplineProfile(d,d.objects.Find(o=>o.id==sourceCustom.ziplineStartId),"arm"));Refresh();Select(sourceZipline);
                int customBase=snapshot.objects.Count;await TreeFrames();viewport.Focus();Shortcut(Key.LeftCtrl,Key.D);Check(placingSelection!=null&&snapshot.objects.Count==customBase,"Ctrl+D did not prepare a custom object: selection="+selectedIds.Count+" focus="+root.panel.focusController.focusedElement+" status="+status.text);
                var customTemplate=placingSelection;var customRoots=placingSelectionRoots;CancelPlacement();
                var armPlacement=new Vector3(7,3,-5);var customCopies=InsertSelectionDuplicate(customTemplate,customRoots,armPlacement);
                var copiedZipline=snapshot.objects.Find(o=>o.id==customCopies.Single());
                Near(world.ZiplineGizmoPivot(copiedZipline.ziplineStartId),armPlacement,"Ctrl+D arm zipline placement did not use the gizmo pivot");
                Check(copiedZipline!=null&&copiedZipline.ziplineStartId!=sourceCustom.ziplineStartId&&copiedZipline.ziplineEndId!=sourceCustom.ziplineEndId,"Ctrl+D kept the source custom references.");
                Check(snapshot.objects.Find(o=>o.id==copiedZipline.ziplineStartId)?.parentId==copiedZipline.id&&snapshot.objects.Find(o=>o.id==copiedZipline.ziplineEndId)?.parentId==copiedZipline.id,"Ctrl+D did not remap the duplicated zipline endpoints.");
                session.Undo();Refresh();
                session.Edit(d=>ApplyZiplineProfile(d,d.objects.Find(o=>o.id==sourceCustom.ziplineStartId),"support"));Refresh();Select(sourceZipline);
                customBase=snapshot.objects.Count;await TreeFrames();viewport.Focus();Shortcut(Key.LeftCtrl,Key.D);Check(placingSelection!=null&&snapshot.objects.Count==customBase,"Ctrl+D did not prepare a supported zipline");
                customTemplate=placingSelection;customRoots=placingSelectionRoots;CancelPlacement();
                var supportPlacement=new Vector3(-4,2,9);customCopies=InsertSelectionDuplicate(customTemplate,customRoots,supportPlacement);
                copiedZipline=snapshot.objects.Find(o=>o.id==customCopies.Single());
                Near(world.ZiplineGizmoPivot(copiedZipline.ziplineStartId),supportPlacement,"Ctrl+D supported zipline placement did not use the gizmo pivot");
                session.Undo();Refresh();session.Undo();Refresh();session.Undo();Refresh();session.Undo();Refresh();
                Check(snapshot.objects.Count==clipboardBase,"Custom Ctrl+D test did not restore the original document.");
                placing=catalog.Entries[0];Shortcut(Key.Escape);Check(placing==null,"Escape did not cancel placement outside the scene focus");
            }
            finally{InputSystem.RemoveDevice(shortcutKeyboard);shortcutPrevious?.MakeCurrent();GUIUtility.systemCopyBuffer=oldClipboard;}
            Select(parent.id);SelectModified(a.id,true,false);Check(SelectionRoots().SequenceEqual(new[]{parent.id}),"Parent/child normalization");
            var original=world.WorldPose(a.id);var originalLocal=world.LocalPose(a.id);MoveSelection(new Vector3(0,2,0));Near(WorldView.ToVector(world.WorldPose(a.id).position),WorldView.ToVector(original.position)+Vector3.up*2,"Child did not follow its parent");Near(WorldView.ToVector(world.LocalPose(a.id).position),WorldView.ToVector(originalLocal.position),"Moving a parent changed the child's local coordinates");session.Undo();Refresh();
            session.Edit(d=>d.objects.Find(o=>o.id==a.id).position=new Float3());Refresh();Near(WorldView.ToVector(world.WorldPose(a.id).position),WorldView.ToVector(world.WorldPose(parent.id).position),"Child local zero is not the parent origin");session.Undo();Refresh();
            Select(a.id);SelectModified(b.id,true,false);Check(selectedIds.Count==2,"Additive selection");
            var pa=WorldView.ToVector(world.WorldPose(a.id).position);var pb=WorldView.ToVector(world.WorldPose(b.id).position);long revision=session.Revision;
            positionInput.Change(Vector3.right);positionInput.Change(Vector3.right*2);Near(WorldView.ToVector(world.WorldPose(a.id).position),pa+Vector3.right*2,"Relative inspector live A");Near(WorldView.ToVector(world.WorldPose(b.id).position),pb+Vector3.right*2,"Relative inspector live B");
            CommitInspectorEdit();session.Undo();Refresh();Check(session.Revision==revision,"Inspector did not use one undo");Near(WorldView.ToVector(world.WorldPose(a.id).position),pa,"Inspector undo");
            var pivot=SelectionPivot();rotationInput.Change(new Vector3(0,90,0));Near(WorldView.ToVector(world.WorldPose(a.id).position),pivot+Quaternion.Euler(0,90,0)*(pa-pivot),"Rotate selection pivot");CommitInspectorEdit();session.Undo();Refresh();
            var oldMouse=Mouse.current;var oldKeys=Keyboard.current;var mouse=InputSystem.AddDevice<Mouse>();var keys=InputSystem.AddDevice<Keyboard>();
            void MouseAt(Vector2 point,ushort buttons){InputSystem.QueueStateEvent(mouse,new MouseState{position=point,buttons=buttons});InputSystem.Update();mouse.MakeCurrent();keys.MakeCurrent();}
            try {
                FocusSelection();await TreeFrames();SetScaleGizmo();UpdateGizmoVisual();var centre=(Vector2)world.Camera.WorldToScreenPoint(gizmoVisual.Pivot);
                var sa=WorldView.ToVector(world.LocalPose(a.id).scale);pivot=SelectionPivot();pa=WorldView.ToVector(world.WorldPose(a.id).position);
                MouseAt(centre,1);HandleGizmoInput(centre,true);Check(draggingGizmo&&dragAxis==3,"Uniform scale handle: hit="+gizmoVisual.Hit(centre)+" enabled="+mouse.enabled+" pressed="+mouse.leftButton.wasPressedThisFrame);
                MouseAt(centre+Vector2.right*150,1);HandleGizmoInput(centre+Vector2.right*150,true);Near(WorldView.ToVector(world.LocalPose(a.id).scale),sa*2,"Live scale");Near(WorldView.ToVector(world.WorldPose(a.id).position),pivot+(pa-pivot)*2,"Scale offsets");Near(scaleInput.value,Vector3.one*2,"Scale inspector sync");
                MouseAt(centre+Vector2.right*150,0);HandleGizmoInput(centre+Vector2.right*150,true);session.Undo();Refresh();Near(WorldView.ToVector(world.LocalPose(a.id).scale),sa,"Scale undo");
                SetGizmoMode(false);localGizmo=true;SetSelection(new[]{a.id,b.id},a.id);RefreshInspector();UpdateGizmoVisual();
                var direction=SelectionBasis()*Vector3.right;var origin=(Vector2)world.Camera.WorldToScreenPoint(gizmoVisual.Pivot);var end=(Vector2)world.Camera.WorldToScreenPoint(gizmoVisual.Pivot+direction*gizmoVisual.WorldLength);var hit=Vector2.Lerp(origin,end,.8f);var to=hit+(end-origin)/gizmoVisual.WorldLength;
                pa=WorldView.ToVector(world.WorldPose(a.id).position);MouseAt(hit,1);HandleGizmoInput(hit,true);Check(draggingGizmo&&dragAxis==0,"Local X handle");MouseAt(to,1);HandleGizmoInput(to,true);Near(WorldView.ToVector(world.WorldPose(a.id).position),pa+direction,"Local translation");
                CancelGizmoDrag();MouseAt(to,0);Near(WorldView.ToVector(world.WorldPose(a.id).position),pa,"Cancel drag restores all");localGizmo=false;
                Select(a.id);await TreeFrames();
                using(var e=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,modifiers=EventModifiers.Control,mousePosition=hierarchyRows[b.id].worldBound.center}))hierarchyRows[b.id].SendEvent(e);
                Check(selectedIds.SetEquals(new[]{a.id,b.id}),"Hierarchy Ctrl click");EndLibraryDrag();
                Select(parent.id);SelectModified(b.id,false,true,true);Check(selectedIds.Count==4,"Hierarchy Shift range includes nested rows");
                MouseAt(world.Camera.pixelRect.center,0);Select(null);var rect=world.Camera.pixelRect;
                var start=new Vector2(rect.xMin+2,rect.yMin+2);var finish=new Vector2(rect.xMax-2,rect.yMax-2);
                MouseAt(start,1);HandleSceneSelection(start,true);MouseAt(finish,1);HandleSceneSelection(finish,true);MouseAt(finish,0);HandleSceneSelection(finish,true);
                Check(selectedIds.Contains(a.id)&&selectedIds.Contains(b.id)&&!selectedIds.Contains(hidden.id)&&!selectedIds.Contains(parent.id),"Marquee selected hidden/group or missed models");
            } finally {InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keys);oldMouse?.MakeCurrent();oldKeys?.MakeCurrent();CancelSceneSelection();}
            Select(a.id);SelectModified(b.id,true,false);var before=codec.Encode(snapshot);var positions=SelectionRoots().ToDictionary(id=>id,id=>world.WorldPose(id));GroupSelection();string group=selectedId;
            foreach(var pair in positions)Near(WorldView.ToVector(world.WorldPose(pair.Key).position),WorldView.ToVector(pair.Value.position),"Grouping preserves world pose");
            session.Undo();Refresh();Check(codec.Encode(snapshot)==before,"Group undo");
            Select(a.id);SelectModified(b.id,true,false);ReparentSelection(parent.id);foreach(var pair in positions)Near(WorldView.ToVector(world.WorldPose(pair.Key).position),WorldView.ToVector(pair.Value.position),"Batch reparent");session.Undo();Refresh();
            Select(parent.id);SelectModified(b.id,true,false);before=codec.Encode(snapshot);try{ReparentSelection(parent.id);throw new Exception("Cycle accepted");}catch(ArgumentException){}Check(codec.Encode(snapshot)==before,"Invalid batch reparent mutated map");
            var assembly=CaptureAssembly("QA-bâtiment");Check(assembly.objects.Count==5&&assembly.objects.Any(o=>o.disabled),"Assembly hierarchy/disabled metadata");
            var qaFiles=new MapFiles(Path.Combine(Application.temporaryCachePath,"EditorAssemblyQA"),codec);qaFiles.Save("assembly",assembly);var loaded=qaFiles.Load("assembly");Check(codec.Encode(assembly)==codec.Encode(loaded),"Assembly disk roundtrip");
            int countBefore=snapshot.objects.Count;string first=InsertAssembly(loaded,new Vector3(8,0,0));string second=InsertAssembly(loaded,new Vector3(-8,0,0));Check(snapshot.objects.Count==countBefore+10,"Assembly insert count");Check(!MapHierarchy.Subtree(snapshot,first).Overlaps(MapHierarchy.Subtree(snapshot,second)),"Assembly instances share identifiers");session.Undo();Refresh();Check(snapshot.objects.Count==countBefore+5,"Assembly insert one undo");
            Select(parent.id);SelectModified(b.id,true,false);assemblyName.value="qa-assemblage";SaveAssembly();Check(catalogMode.index==1&&catalogList.childCount>0,"Assembly library UI");
            var savedSlot=Directory.GetFiles(AssembliesDirectory,"map-*.remap.json").Select(Path.GetFileName).First();savedSlot=savedSlot.Substring(4,savedSlot.Length-15);BeginAssemblyPlacement(savedSlot);Check(placingAssembly!=null,"Assembly placement entry");CancelPlacement();
            var savedMouse=Mouse.current;var dropMouse=InputSystem.AddDevice<Mouse>();
            void DropMouse(Vector2 point,ushort buttons){InputSystem.QueueStateEvent(dropMouse,new MouseState{position=point,buttons=buttons});InputSystem.Update();dropMouse.MakeCurrent();}
            try {
                BeginAssemblyPlacement(savedSlot);int beforePlace=snapshot.objects.Count;int assemblyCount=placingAssembly.objects.Count;
                var point=new Vector2(world.Camera.pixelRect.center.x,world.Camera.pixelRect.yMin+35);
                Check(world.SurfacePoint(point,1,out _),"Assembly placement surface");
                DropMouse(point,1);HandleAssemblyInput(viewport.worldBound.center,point,true);DropMouse(point,0);
                Check(snapshot.objects.Count==beforePlace+assemblyCount&&placingAssembly==null,"Assembly click placement");session.Undo();Refresh();
                Select(a.id);SelectModified(b.id,true,false);int duplicateCount=MapSelection.Branches(snapshot,SelectionRoots()).Count;
                Check(world.SurfacePoint(point,1,out var duplicateSurface),"Duplicate placement surface");BeginSelectionDuplicatePlacement();
                Check(placingSelection!=null&&snapshot.objects.Count==beforePlace,"Duplicate placement changed the document before click");
                DropMouse(point,0);HandleAssemblyInput(viewport.worldBound.center,point,true);
                Check(GameObject.Find("Selection placement preview")!=null&&snapshot.objects.Count==beforePlace,"Duplicate selection ghost was not displayed before click");
                DropMouse(point,1);HandleAssemblyInput(viewport.worldBound.center,point,true);DropMouse(point,0);
                Check(placingSelection==null&&snapshot.objects.Count==beforePlace+duplicateCount&&selectedIds.Count==2,"Duplicate click did not insert the selected branches exactly once");
                var duplicateBounds=world.CombinedBounds(SelectionRoots());Check(duplicateBounds.HasValue&&Mathf.Abs(duplicateBounds.Value.min.y-duplicateSurface.y)<.03f,"Duplicate preview base was not placed on the clicked surface");session.Undo();Refresh();
                Select(parent.id);FocusHierarchy(parent.id);await TreeFrames(5);
                assemblyDragSlot=savedSlot;assemblyDragStart=Vector2.zero;assemblyDragging=false;dragObjectId=null;
                var folderPoint=hierarchyRows[parent.id].worldBound.center;
                DropMouse(point,1);HandleAssemblyInput(folderPoint,point,false);Check(assemblyDragging,"Assembly drag start");
                DropMouse(point,0);HandleAssemblyInput(folderPoint,point,false);
                Check(snapshot.objects.Count==beforePlace+assemblyCount&&snapshot.objects.Find(o=>o.id==selectedId).parentId==parent.id,"Assembly drop into folder");
                session.Undo();Refresh();
            } finally {InputSystem.RemoveDevice(dropMouse);savedMouse?.MakeCurrent();CancelPlacement();}
            Select(parent.id);SelectModified(b.id,true,false);var count=snapshot.objects.Count;Duplicate();Check(selectedIds.Count==2&&snapshot.objects.Count==count+4,"Batch duplication");Delete();Check(snapshot.objects.Count==count,"Batch deletion");session.Undo();Refresh();Check(snapshot.objects.Count==count+4,"Batch delete undo");
            Select(parent.id);SelectModified(b.id,true,false);FocusSelection();SetScaleGizmo();UpdateGizmoVisual();ShowConstructionTools(false);enabled=true;SetStatus("Sélection multiple, échelle, axes locaux, annulation, dossiers et assemblages vérifiés.");
            Debug.Log("REMAP_EDITOR_FEATURES_OK");
        }
        private IEnumerator EditorFeaturesSmoke() {
            var check=CheckEditorFeatures();while(!check.IsCompleted)yield return null;
            if(check.IsFaulted){Debug.LogException(check.Exception);Application.Quit(1);yield break;}
            yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();if(image!=null){File.WriteAllBytes(Path.Combine(Application.dataPath,"..","editor-features-preview.png"),image.EncodeToPNG());Destroy(image);}Application.Quit(0);
        }
    }
}
