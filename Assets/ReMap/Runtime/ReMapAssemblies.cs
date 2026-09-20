using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private TextField assemblyName;
        private VisualElement assemblySaveOverlay;
        private MapDocument placingAssembly;
        private MapDocument placingSelection;
        private string[] placingSelectionRoots;
        private string assemblyDragSlot;
        private Vector2 assemblyDragStart;
        private bool assemblyDragging;
        private readonly CatalogEntry assemblyMarker = new CatalogEntry("demo:cube", L.T("#PLACEMENT_POINT"), "", Vector3.one * .15f);
        private string AssembliesDirectory => Environment.GetCommandLineArgs().Contains("-remapEditorSmoke") ? Path.Combine(Application.temporaryCachePath,"EditorAssembliesQA") : PersistentDirectory("Assemblies");
        private MapFiles AssemblyFiles => new MapFiles(AssembliesDirectory,codec);
        private void BuildAssemblyControls(VisualElement bar) {
            var saveSelection=Button(L.T("#SAVE_SELECTION"),()=>ShowAssemblySave(true));saveSelection.name="save-selection-button";bar.Add(saveSelection);
            assemblySaveOverlay=new VisualElement();assemblySaveOverlay.AddToClassList("modal-overlay");root.Add(assemblySaveOverlay);
            var panel=new VisualElement();panel.AddToClassList("assembly-save-panel");assemblySaveOverlay.Add(panel);
            var heading=DockTitle(L.T("#SAVE_SELECTION_ASSEMBLY"));panel.Add(heading);heading.Add(Button("×",()=>ShowAssemblySave(false),"dock-close"));
            var content=new VisualElement();content.AddToClassList("assembly-save-content");panel.Add(content);
            content.Add(Label(L.T("#SELECTED_OBJECTS_HIERARCHY_BECOME_REUSABLE"),"note"));
            assemblyName=new TextField(L.T("#ASSEMBLY_NAME")){value="assembly",isDelayed=true,tooltip=L.T("#ASSEMBLY_NAME_LETTERS_NUMBERS_HYPHENS")};content.Add(assemblyName);
            var actions=new VisualElement();actions.AddToClassList("dialog-actions");panel.Add(actions);
            actions.Add(Button(L.T("#CANCEL"),()=>ShowAssemblySave(false)));actions.Add(Button(L.T("#SAVE_SELECTION"),SaveAssembly,"primary"));
            assemblySaveOverlay.style.display=DisplayStyle.None;
            root.RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode==KeyCode.Escape&&AssemblySaveOpen){ShowAssemblySave(false);e.StopPropagation();}},TrickleDown.TrickleDown);
        }
        private void ShowAssemblySave(bool show) {
            if(assemblySaveOverlay==null)return;
            if(show){HideCommandMenu();CommitInspectorEdit();CancelPlacement();assemblyName.SetValueWithoutNotify("assembly");assemblySaveOverlay.style.display=DisplayStyle.Flex;assemblySaveOverlay.BringToFront();assemblyName.Focus();}
            else assemblySaveOverlay.style.display=DisplayStyle.None;
        }
        private bool AssemblySaveOpen=>assemblySaveOverlay!=null&&assemblySaveOverlay.style.display.value!=DisplayStyle.None;
        private MapDocument CaptureAssembly(string name) {
            var roots=SelectionRoots();if(roots.Count==0)throw new InvalidOperationException(L.T("#SELECT_ITEMS_SAVE"));
            var branches=MapSelection.Branches(snapshot,roots);var rootIds=roots.ToHashSet();
            var bounds=world.CombinedBounds(roots);var pivot=bounds.HasValue?new Vector3(bounds.Value.center.x,bounds.Value.min.y,bounds.Value.center.z):SelectionPivot();
            var assembly=new MapDocument {name=name,gameTarget=snapshot.gameTarget,editingMap=snapshot.editingMap,
                targetMaps=new List<string>(snapshot.targetMaps??new List<string>())};var group=new MapObject{isGroup=true,assetId="group:empty",displayName=name};assembly.objects.Add(group);
            foreach(var original in snapshot.objects.Where(o=>branches.Contains(o.id))) {
                var copy=original.Copy();if(rootIds.Contains(copy.id)) {
                    var pose=world.ReparentPose(copy.id,"");copy.parentId=group.id;copy.position=WorldView.ToData(WorldView.ToVector(pose.position)-pivot);copy.rotation=pose.rotation;copy.scale=pose.scale;
                    if(!MapHierarchy.IsEnabled(snapshot,copy.id))copy.disabled=true;
                }
                assembly.objects.Add(copy);
            }
            assembly.Validate();return assembly;
        }
        private void SaveAssembly() {
            CommitInspectorEdit();string name=assemblyName.value.Trim();AssemblyFiles.PathFor(name);
            string unique=name;int suffix=2;while(File.Exists(AssemblyFiles.PathFor(unique))) {unique=name+"-"+suffix++;AssemblyFiles.PathFor(unique);}
            var assembly=CaptureAssembly(unique);AssemblyFiles.Save(unique,assembly);
            assemblySaveOverlay.style.display=DisplayStyle.None;catalogMode.index=1;ResetCatalog();SetStatus(L.T("#ASSEMBLY_SAVED")+AssemblyFiles.PathFor(unique));
        }
        private void RefreshAssemblies() {
            var names=Directory.Exists(AssembliesDirectory)?Directory.GetFiles(AssembliesDirectory,"map-*.remap.json").Select(Path.GetFileName).Select(n=>n.Substring(4,n.Length-4-11)).Where(n=>n.IndexOf(search.value,StringComparison.OrdinalIgnoreCase)>=0).OrderBy(n=>n).ToArray():Array.Empty<string>();
            pageState.text=L.F("#ARG0_ASSEMBLIES",names.Length);
            foreach(string name in names) {
                var card=Button(name+L.T("#PERSONAL_ASSEMBLY"),()=> {if(!assemblyDragging&&!suppressCardClick)BeginAssemblyPlacement(name);},"game-card");
                card.tooltip=L.T("#CLICK_PLACE_SCENE_DRAG_SCENE");
                card.RegisterCallback<PointerDownEvent>(e=> {if(e.button==0){dragObjectId=null;assemblyDragSlot=name;assemblyDragStart=e.position;assemblyDragging=false;suppressCardClick=false;}});catalogList.Add(card);
            }
            if(names.Length==0)catalogList.Add(Label(L.T("#SELECT_OBJECTS_USE_SAVE_SELECTION"),"note"));
        }
        private void BeginAssemblyPlacement(string name) {
            CommitInspectorEdit();CancelPlacement();var assembly=AssemblyFiles.Load(name);
            if(assembly.objects.Any(Incompatible))throw new InvalidOperationException(L.T("#ASSEMBLY_CONTAINS_MODELS_MISSING_SELECTED"));
            placingAssembly=assembly;mode.text=L.T("#ASSEMBLY_765658")+name;viewport.Focus();SetStatus(L.T("#SMALL_MARKER_SHOWS_ASSEMBLY_BASE"));
        }
        private Vector3 SelectionDuplicatePlacementPivot(string[] roots)
        {
            if (roots.Length == 1 && selectedIds.Count == 1 && selectedId == roots[0])
            {
                var rootObject = snapshot.objects.Find(candidate => candidate.id == roots[0]);
                if (rootObject?.customType == "zipline")
                    return world.ZiplineGizmoPivot(rootObject.ziplineStartId);
            }
            var bounds = world.CombinedBounds(roots);
            return bounds.HasValue
                ? new Vector3(bounds.Value.center.x, bounds.Value.min.y, bounds.Value.center.z)
                : SelectionPivot();
        }
        private void BeginSelectionDuplicatePlacement()
        {
            CommitInspectorEdit();
            var roots=SelectionRoots().ToArray();
            if(roots.Length==0){SetStatus(L.T("#SELECT_ONE_MORE_OBJECTS_DUPLICATE"));return;}
            var branches=MapSelection.Branches(snapshot,roots);var rootIds=roots.ToHashSet();
            var pivot=SelectionDuplicatePlacementPivot(roots);
            var template=new MapDocument{name="duplicate",gameTarget=snapshot.gameTarget,editingMap=snapshot.editingMap,
                targetMaps=new List<string>(snapshot.targetMaps??new List<string>())};
            foreach(var original in snapshot.objects.Where(o=>branches.Contains(o.id)))
            {
                var copy=original.Copy();
                if(rootIds.Contains(copy.id))
                {
                    var pose=world.ReparentPose(copy.id,"");copy.parentId="";
                    copy.position=WorldView.ToData(WorldView.ToVector(pose.position)-pivot);copy.rotation=pose.rotation;copy.scale=pose.scale;
                    if(!MapHierarchy.IsEnabled(snapshot,copy.id))copy.disabled=true;
                }
                template.objects.Add(copy);
            }
            template.Validate();CancelPlacement();placingSelection=template;placingSelectionRoots=roots;
            mode.text=L.T("#DUPLICATE_PLACEMENT");viewport.Focus();
            SetStatus(L.T("#MOVE_DUPLICATE_CLICK_SURFACE_ESCAPE"));
        }
        private string[] InsertSelectionDuplicate(MapDocument template,string[] roots,Vector3 position)
        {
            CommitInspectorEdit();
            if(snapshot.objects.Count+template.objects.Count>10000)throw new InvalidOperationException(L.T("#MAP_MAY_CONTAIN_MOST_10"));
            var mapping=template.objects.ToDictionary(o=>o.id,_=>Guid.NewGuid().ToString("N"));var rootSet=roots.ToHashSet();
            foreach(var modelId in template.objects.Where(o=>!o.isGroup).Select(o=>o.assetId).Distinct())
            {
                if(world.models.IsPrepared(modelId))continue;
                var record=assetLibrary.Records.FirstOrDefault(o=>o.Id==modelId);if(record==null)continue;
                string path=assetLibrary.CachedModel(record);if(path==null)continue;world.models.Prepare(modelId,path);world.Reload(modelId);
            }
            session.Edit(doc=>
            {
                foreach(var original in template.objects)
                {
                    var copy=original.Copy();copy.id=mapping[original.id];
                    copy.parentId=!string.IsNullOrEmpty(original.parentId)&&mapping.TryGetValue(original.parentId,out var parent)?parent:"";
                    MapHierarchy.RemapInternalReferences(copy,mapping);
                    if(rootSet.Contains(original.id)){copy.position=WorldView.ToData(WorldView.ToVector(original.position)+position);copy.displayName=MapHierarchy.NextDuplicateName(doc,original.displayName);}
                    doc.objects.Add(copy);
                }
            });
            var inserted=roots.Where(mapping.ContainsKey).Select(id=>mapping[id]).ToArray();snapshot=session.Snapshot();SetSelection(inserted,inserted.LastOrDefault());
            if(selectedId!=null)RevealHierarchy(selectedId);Refresh();if(selectedId!=null)FocusHierarchy(selectedId);return inserted;
        }
        private string InsertAssembly(MapDocument assembly,Vector3 position,string parent="") {
            CommitInspectorEdit();
            if(assembly.objects.Any(Incompatible))throw new InvalidOperationException(L.T("#SOME_ASSEMBLY_MODELS_MISSING_SELECTED"));
            // Prepare only the incoming model references. Existing instances and their shared resources stay resident.
            foreach(var modelId in assembly.objects.Where(o=>!o.isGroup).Select(o=>o.assetId).Distinct()) {
                if(world.models.IsPrepared(modelId))continue;
                var record=assetLibrary.Records.FirstOrDefault(o=>o.Id==modelId);if(record==null)continue;
                string path=assetLibrary.CachedModel(record);if(path==null)continue;
                world.models.Prepare(modelId,path);world.Reload(modelId);
            }
            string id=null;session.Edit(doc=> {id=MapAssembly.Insert(doc,assembly,WorldView.ToData(position)); if(!string.IsNullOrEmpty(parent))MapHierarchy.Reparent(doc,id,parent);});
            selectedId=id;RevealHierarchy(id);Refresh();return id;
        }
        private bool HandleAssemblyInput(Vector2 panelPoint,Vector2 screenPoint,bool inside) {
            var mouse=Mouse.current;
            if(Keyboard.current?.escapeKey.wasPressedThisFrame==true&&(placingAssembly!=null||placingSelection!=null||assemblyDragSlot!=null)){CancelPlacement();return true;}
            if(assemblyDragSlot!=null) {
                if(!assemblyDragging&&mouse.leftButton.isPressed&&Vector2.Distance(panelPoint,assemblyDragStart)>6) {CommitInspectorEdit();CancelGizmoDrag();placingAssembly=null;placing=null;world.ClearPreview();assemblyDragging=true;suppressCardClick=true;}
                if(assemblyDragging) {
                    string parent=HoverHierarchyDrop(panelPoint);
                    if(!mouse.leftButton.isPressed) {
                        string slotName=assemblyDragSlot;assemblyDragSlot=null;assemblyDragging=false;EndLibraryDrag();
                        Run(()=> {var saved=AssemblyFiles.Load(slotName);if(inside&&world.SurfacePoint(screenPoint,snap?moveSnap:0,out var p))InsertAssembly(saved,p);else if(parent!=null)InsertAssembly(saved,Vector3.zero,parent);});return true;
                    }
                    return true;
                }
                if(!mouse.leftButton.isPressed)assemblyDragSlot=null;
            }
            if(placingAssembly==null&&placingSelection==null)return false;
            if(inside) {
                world.Navigate(Time.unscaledDeltaTime);
                if(world.SurfacePoint(screenPoint,snap?moveSnap:0,out var point)) {
                    if(placingSelection!=null)world.Preview(placingSelection,point);else world.Preview(assemblyMarker,point);
                    if(mouse.leftButton.wasPressedThisFrame)Run(()=>
                    {
                        var selection=placingSelection;var roots=placingSelectionRoots;var assembly=placingAssembly;CancelPlacement();
                        if(selection!=null)InsertSelectionDuplicate(selection,roots,point);else InsertAssembly(assembly,point);
                    });
                }else world.ClearPreview();
            }else world.ClearPreview();
            return true;
        }
    }
}
