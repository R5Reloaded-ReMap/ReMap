using System;
using System.Collections.Generic;
using System.Linq;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
namespace ReMap.Standalone
{
    public static class GizmoPlacement
    {
        public static Vector3 Pivot(bool centerMode, Vector3? selectionCenter, Vector3 activePivot, Vector3? customPivot = null) =>
            centerMode ? selectionCenter ?? activePivot : customPivot ?? activePivot;
        public static float NormalizeAngle(float angle) =>
            float.IsFinite(angle) ? angle % 360f : angle;
        public static float StepAngle(float angle, float step) => NormalizeAngle(angle + step);
        public static Vector3 SharedPosition(IReadOnlyList<Vector3> positions, out Vector3Int sharedAxes)
        {
            sharedAxes = Vector3Int.zero;
            if (positions == null || positions.Count == 0) return Vector3.zero;
            var first = positions[0];
            bool x = true, y = true, z = true;
            for (int i = 1; i < positions.Count; i++)
            {
                x &= Mathf.Abs(positions[i].x - first.x) <= .0001f;
                y &= Mathf.Abs(positions[i].y - first.y) <= .0001f;
                z &= Mathf.Abs(positions[i].z - first.z) <= .0001f;
            }
            sharedAxes = new Vector3Int(x ? 1 : 0, y ? 1 : 0, z ? 1 : 0);
            return new Vector3(x ? first.x : 0f, y ? first.y : 0f, z ? first.z : 0f);
        }

        public static Vector3 PositionDelta(Vector3 input, Vector3 reference, Vector3Int sharedAxes) =>
            new Vector3(
                input.x - (sharedAxes.x != 0 ? reference.x : 0f),
                input.y - (sharedAxes.y != 0 ? reference.y : 0f),
                input.z - (sharedAxes.z != 0 ? reference.z : 0f));


        public static Quaternion Basis(bool localMode, bool scaleMode, Quaternion orientation) =>
            localMode || scaleMode ? orientation : Quaternion.identity;
        public static string OrientationId(MapObject item) =>
            item?.customType == "zipline" && !string.IsNullOrEmpty(item.ziplineStartId) ? item.ziplineStartId : item?.id;
    }

    public sealed partial class ReMapApp
    {
        private const string SelectionClipboardPrefix="ReMap.Selection.v1:";
        private const string LegacySelectionClipboardPrefix="ReMapFlowstate.Selection.v1:";
        [Serializable] private sealed class SelectionClipboardData
        {
            public List<MapObject> objects=new List<MapObject>();
            public List<string> roots=new List<string>();
            public List<string> parents=new List<string>();
        }
        private readonly HashSet<string> selectedIds = new HashSet<string>();
        private bool sceneRootSelected;
        private readonly List<string> selectionOrder = new List<string>();
        private string activeSelectionId, selectionAnchor;
        // Legacy single-object commands intentionally start a new selection when changing the active id.
        private string selectedId { get => activeSelectionId; set { if (value == activeSelectionId && value != null) return; sceneRootSelected=false; activeSelectionId = value; selectedIds.Clear(); selectionOrder.Clear(); if (value != null) { selectedIds.Add(value); selectionOrder.Add(value); } selectionVersion++; ApplyAutoCollapseHierarchy(); } }
        private int selectionVersion, rootsVersion = -1;
        private MapDocument rootsDocument;
        private List<string> selectionRoots = new List<string>();
        private List<string> SelectionRoots() {
            if (rootsDocument != snapshot || rootsVersion != selectionVersion) { rootsDocument = snapshot; rootsVersion = selectionVersion; selectionRoots = MapSelection.Roots(snapshot, selectedIds); }
            return selectionRoots;
        }
        private void SetSelection(IEnumerable<string> ids, string active = null, bool preserveOrder = false)
        {
            sceneRootSelected=false;
            var requested = ids.Where(id => id != null).Distinct().ToArray(); selectedIds.Clear(); var valid = HierarchyLookup();
            foreach (var id in requested) if (valid.ContainsKey(id)) selectedIds.Add(id);
            if (!preserveOrder) selectionOrder.Clear();
            selectionOrder.RemoveAll(id => !selectedIds.Contains(id));
            foreach (var id in requested) if (selectedIds.Contains(id) && !selectionOrder.Contains(id)) selectionOrder.Add(id);
            activeSelectionId = active != null && selectedIds.Contains(active) ? active : selectedIds.LastOrDefault(); selectionVersion++; ApplyAutoCollapseHierarchy();
        }
        private void NormalizeSelection() {
            var valid = HierarchyLookup(); selectedIds.RemoveWhere(id => !valid.ContainsKey(id));
            selectionOrder.RemoveAll(id => !selectedIds.Contains(id));
            foreach (var id in selectedIds) if (!selectionOrder.Contains(id)) selectionOrder.Add(id);
            if (activeSelectionId == null || !selectedIds.Contains(activeSelectionId)) activeSelectionId = selectedIds.LastOrDefault();
            ApplyAutoCollapseHierarchy(); }
        private string VisibleSelectionTarget(string id)
        {
            var item = snapshot?.objects.Find(candidate => candidate.id == id);
            if (item?.customType == "door-component")
                item = snapshot.objects.Find(candidate => candidate.id == item.parentId);
            if (item?.customType == "curved-zipline-component")
                item = snapshot.objects.Find(candidate => candidate.id == item.parentId);
            if (item?.customType == "ziprail-component")
                item = snapshot.objects.Find(candidate => candidate.id == item.parentId);
            if (item?.customType == "zipline-component")
                item = snapshot.objects.Find(candidate => candidate.id == item.parentId);
            if (item?.customType == "jump-tower-component" && item.customRole == "base")
                item = snapshot.objects.Find(candidate => candidate.id == item.parentId);
            if (item?.customType == "zipline-endpoint" && item.customRole == "start")
                item = snapshot.objects.Find(candidate => candidate.id == item.parentId);
            return item?.id ?? id;
        }

        private void SelectModified(string id, bool additive, bool range, bool hierarchy = false, bool preserveOpenFolders = false)
        {
            CommitInspectorEdit(); CancelPlacement();
            bool previousPreserveOpenFolders = preserveOpenFoldersForSceneSelection;
            preserveOpenFoldersForSceneSelection |= preserveOpenFolders;
            try {
                if (!hierarchy && id != null) id = VisibleSelectionTarget(id);
                if (range && hierarchy && selectionAnchor != null && visibleHierarchyIds.Contains(selectionAnchor) && visibleHierarchyIds.Contains(id)) {
                    int a = visibleHierarchyIds.IndexOf(selectionAnchor), b = visibleHierarchyIds.IndexOf(id);
                    var rangeIds = visibleHierarchyIds.Skip(Math.Min(a,b)).Take(Math.Abs(a-b)+1).ToArray();
                    SetSelection(additive ? selectedIds.Concat(rangeIds).ToArray() : rangeIds, id, additive);
                } else if (additive && id != null) {
                    var next = new HashSet<string>(selectedIds); if (!next.Add(id)) next.Remove(id); SetSelection(next, id, true); selectionAnchor = id;
                } else { SetSelection(id == null ? Array.Empty<string>() : new[] {id}, id); selectionAnchor = id; }
                if (!hierarchy) RevealHierarchy(selectedId);
            }
            finally { preserveOpenFoldersForSceneSelection = previousPreserveOpenFolders; }
            world.HighlightSelection(SelectionRoots()); RefreshInspector(); RefreshObjects();
        }
        private void SelectSceneRoot()
        {
            CommitInspectorEdit(); CancelPlacement(); SetSelection(Array.Empty<string>()); sceneRootSelected=true; selectionAnchor=null;
            world.HighlightSelection(SelectionRoots()); RefreshInspector(); RefreshObjects();
        }
        private void SelectAllObjects() {
            CommitInspectorEdit(); CancelPlacement(); SetSelection(snapshot.objects.Where(o => !HiddenHierarchyObject(o)).Select(o => o.id).ToArray());
            world.HighlightSelection(SelectionRoots()); RefreshObjects(); RefreshInspector();
        }
        private bool CopySelectionToClipboard(bool notify=true)
        {
            CommitInspectorEdit();var roots=SelectionRoots().ToArray();if(roots.Length==0)return false;
            var branches=MapSelection.Branches(snapshot,roots);var rootSet=roots.ToHashSet();
            var data=new SelectionClipboardData();
            foreach(var rootId in roots){data.roots.Add(rootId);data.parents.Add(snapshot.objects.Find(o=>o.id==rootId)?.parentId??"");}
            foreach(var original in snapshot.objects.Where(o=>branches.Contains(o.id)))
            {
                var copy=original.Copy();if(rootSet.Contains(copy.id))copy.parentId="";data.objects.Add(copy);
            }
            GUIUtility.systemCopyBuffer=SelectionClipboardPrefix+JsonUtility.ToJson(data);
            if(notify)SetStatus(L.F("#COPIED_ARG0_ITEM_S",roots.Length));return true;
        }
        private void CutSelectionToClipboard()
        {
            int count=SelectionRoots().Count;if(!CopySelectionToClipboard(false))return;Delete();SetStatus(L.F("#CUT_ARG0_ITEM_S",count));
        }
        private SelectionClipboardData ReadSelectionClipboard()
        {
            string value=GUIUtility.systemCopyBuffer??"";
            string prefix=value.StartsWith(SelectionClipboardPrefix,StringComparison.Ordinal)?SelectionClipboardPrefix:
                value.StartsWith(LegacySelectionClipboardPrefix,StringComparison.Ordinal)?LegacySelectionClipboardPrefix:null;
            if(prefix==null)return null;
            try
            {
                var data=JsonUtility.FromJson<SelectionClipboardData>(value.Substring(prefix.Length));
                if(data?.objects==null||data.roots==null||data.parents==null||data.objects.Count==0||data.objects.Count>10000)return null;
                var check=new MapDocument();check.objects=data.objects.Select(o=>o.Copy()).ToList();check.Validate();return data;
            }
            catch{return null;}
        }
        private string FocusedHierarchyFolder()
        {
            var focused=root?.panel?.focusController.focusedElement as VisualElement;
            for(var current=focused;current!=null;current=current.parent)
            {
                var row=hierarchyRows.FirstOrDefault(pair=>ReferenceEquals(pair.Value,current));
                if(row.Key!=null&&snapshot.objects.Find(item=>item.id==row.Key)?.isGroup==true)return row.Key;
                if(current==objectList)break;
            }
            return null;
        }
        private void PasteSelectionFromClipboard()
        {
            string focusedFolder=FocusedHierarchyFolder();CommitInspectorEdit();CancelPlacement();var data=ReadSelectionClipboard();
            if(data==null){SetStatus(L.T("#CLIPBOARD_CONTAIN_REMAP_OBJECTS"));return;}
            var mapping=data.objects.ToDictionary(o=>o.id,_=>Guid.NewGuid().ToString("N"));var current=snapshot.objects.ToDictionary(o=>o.id);
            var roots=data.roots.Where(mapping.ContainsKey).ToArray();
            var rootPoses=new Dictionary<string,MapObject>();
            if(focusedFolder!=null)foreach(var rootId in roots)
            {
                if(rootId==focusedFolder)rootPoses[rootId]=new MapObject();
                else if(current.ContainsKey(rootId))rootPoses[rootId]=world.ReparentPose(rootId,focusedFolder);
            }
            session.Edit(doc=>
            {
                foreach(var original in data.objects)
                {
                    var copy=original.Copy();copy.id=mapping[original.id];
                    if(!string.IsNullOrEmpty(original.parentId)&&mapping.TryGetValue(original.parentId,out var parentCopy))copy.parentId=parentCopy;
                    else
                    {
                        int rootIndex=data.roots.IndexOf(original.id);string parent=rootIndex>=0&&rootIndex<data.parents.Count?data.parents[rootIndex]:"";
                        copy.parentId=MapSelection.PasteParent(snapshot,focusedFolder,parent);
                        if(focusedFolder!=null&&rootPoses.TryGetValue(original.id,out var pose)){copy.position=pose.position;copy.rotation=pose.rotation;copy.scale=pose.scale;}
                    }
                    MapHierarchy.RemapInternalReferences(copy,mapping);
                    if(data.roots.Contains(original.id))copy.displayName+=L.T("#COPY");doc.objects.Add(copy);
                }
            });
            var pasted=roots.Select(id=>mapping[id]).ToArray();snapshot=session.Snapshot();SetSelection(pasted,pasted.LastOrDefault());if(selectedId!=null)RevealHierarchy(selectedId);Refresh();if(selectedId!=null)FocusHierarchy(selectedId);SetStatus(L.F("#PASTED_ARG0_ITEM_S",pasted.Length));
        }
        private void FocusSelection() => world.FocusSelection(SelectionRoots());
        private Vector3 SelectionPivot()
        {
            Vector3 activePivot = selectedId == null ? Vector3.zero : WorldView.ToVector(world.WorldPose(selectedId).position);
            if (centrePivot)
                return GizmoPlacement.Pivot(true, world.CombinedBounds(SelectionRoots())?.center, activePivot);

            if (selectedIds.Count == 1 && selectedId != null)
            {
                var item = snapshot.objects.Find(candidate => candidate.id == selectedId);
                if (item?.customType == "zipline")
                    return GizmoPlacement.Pivot(false, null, activePivot, world.ZiplineGizmoPivot(item.ziplineStartId));
                if (item?.customType == "zipline-endpoint")
                    return GizmoPlacement.Pivot(false, null, activePivot, world.ZiplineGizmoPivot(item.id));
            }
            return GizmoPlacement.Pivot(false, null, activePivot);
        }
        private string SelectionOrientationId()
        {
            if (selectedId == null) return null;
            string orientationId = selectedId;
            var roots = SelectionRoots();
            if (roots.Count == 1)
            {
                var root = snapshot.objects.Find(candidate => candidate.id == roots[0]);
                if (root?.isGroup == true && selectedIds.Contains(root.id)) orientationId = root.id;
            }
            return GizmoPlacement.OrientationId(snapshot.objects.Find(candidate => candidate.id == orientationId));
        }
        private Quaternion SelectionBasis()
        {
            string orientationId = SelectionOrientationId();
            Quaternion orientation = orientationId == null ? Quaternion.identity :
                Quaternion.Euler(WorldView.ToVector(world.WorldPose(orientationId).rotation));
            return GizmoPlacement.Basis(localGizmo, scaleGizmo, orientation);
        }
        private bool centrePivot = true, localGizmo = true;
        private bool sceneSelectionPending, sceneMarquee;
        private Vector2 sceneSelectionStart;
        private VisualElement selectionRectangle;
        private void CancelSceneSelection() { sceneSelectionPending = sceneMarquee = false; if (selectionRectangle != null) selectionRectangle.style.display = DisplayStyle.None; }
        private bool HandleSceneSelection(Vector2 point, bool inside) {
            var mouse = Mouse.current; var keys = Keyboard.current;
            if (sceneSelectionPending) {
                if (keys?.escapeKey.wasPressedThisFrame == true || mouse.rightButton.isPressed) { CancelSceneSelection(); return true; }
                if (Vector2.Distance(point, sceneSelectionStart) > 6) sceneMarquee = true;
                var rect = Rect.MinMaxRect(Mathf.Min(point.x,sceneSelectionStart.x), Mathf.Min(point.y,sceneSelectionStart.y), Mathf.Max(point.x,sceneSelectionStart.x), Mathf.Max(point.y,sceneSelectionStart.y));
                if (sceneMarquee) {
                    if (selectionRectangle == null) { selectionRectangle = new VisualElement { pickingMode = PickingMode.Ignore }; selectionRectangle.AddToClassList("selection-rectangle"); viewport.Add(selectionRectangle); }
                    var a = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(rect.xMin,Screen.height-rect.yMax)) - viewport.worldBound.position;
                    var b = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(rect.xMax,Screen.height-rect.yMin)) - viewport.worldBound.position;
                    selectionRectangle.style.display = DisplayStyle.Flex; selectionRectangle.style.left=a.x; selectionRectangle.style.top=a.y; selectionRectangle.style.width=b.x-a.x; selectionRectangle.style.height=b.y-a.y;
                }
                if (!mouse.leftButton.isPressed) {
                    bool additive = keys?.ctrlKey.isPressed == true || keys?.shiftKey.isPressed == true;
                    if (sceneMarquee) { var ids = world.InScreenRectangle(rect).Select(VisibleSelectionTarget).Distinct().ToArray(); CommitInspectorEdit();
                        bool previousPreserveOpenFolders = preserveOpenFoldersForSceneSelection; preserveOpenFoldersForSceneSelection = true;
                        try { SetSelection(additive ? selectedIds.Concat(ids).ToArray() : ids, null, additive); }
                        finally { preserveOpenFoldersForSceneSelection = previousPreserveOpenFolders; }
                        world.HighlightSelection(SelectionRoots()); RefreshInspector(); RefreshObjects(); }
                    else SelectModified(world.Pick(point), additive, false, false, true);
                    CancelSceneSelection();
                }
                return true;
            }
            if (inside && placing == null && mouse.leftButton.wasPressedThisFrame) { CommitInspectorEdit(); sceneSelectionPending=true; sceneSelectionStart=point; return true; }
            return false;
        }
        private void MoveSelection(Vector3 delta) {
            CommitInspectorEdit(); var roots = SelectionRoots();
            if(roots.Count==1&&IsVerticalOnlyMoveTarget(snapshot.objects.Find(item=>item.id==roots[0])))
                delta=Vector3.up*delta.y;
            if(delta==Vector3.zero)return;
            var originals=world.CaptureSelection(roots);
            var lockedEnd=CaptureLockedZiplineEnd(roots);
            if(!world.PreviewSelection(originals,Vector3.zero,Quaternion.identity,delta,Quaternion.identity,Vector3.one,lockedEnd)) {SetStatus(ApexCoordinates.LimitMessage);return;}
            CommitSelectionPreview(IncludeLockedPosition(originals,lockedEnd)); Refresh();
        }
        private void GroupSelection() {
            CommitInspectorEdit(); var roots=SelectionRoots().ToArray(); if(roots.Length==0)return;
            var pivot=SelectionPivot(); var poses=roots.ToDictionary(id=>id,id=>world.ReparentPose(id,""));
            var group=new MapObject {isGroup=true,assetId="group:empty",displayName=L.T("#ASSEMBLY"),position=WorldView.ToData(pivot)};
            session.Edit(doc=> { doc.objects.Add(group); foreach(var item in doc.objects) if(poses.TryGetValue(item.id,out var pose)) { item.parentId=group.id; item.position=WorldView.ToData(WorldView.ToVector(pose.position)-pivot); item.rotation=pose.rotation; item.scale=pose.scale; } });
            selectedId=group.id; RevealHierarchy(group.id); Refresh(); FocusHierarchy(group.id);
        }
        private void ReparentSelection(string parent, string beforeSiblingId = null) {
            CommitInspectorEdit(); var roots=SelectionRoots().ToArray(); var check=snapshot.Copy();
            foreach(var id in roots) MapHierarchy.Reorder(check,id,parent,beforeSiblingId);
            var changedParents=roots.Where(id=>(snapshot.objects.Find(o=>o.id==id).parentId??"")!=(parent??"")).ToArray();
            var poses=changedParents.ToDictionary(id=>id,id=>world.ReparentPose(id,parent));
            session.Edit(doc=> { foreach(var id in roots) MapHierarchy.Reorder(doc,id,parent,beforeSiblingId); foreach(var item in doc.objects) if(poses.TryGetValue(item.id,out var p)) { item.position=p.position;item.rotation=p.rotation;item.scale=p.scale; } });
            RevealHierarchy(selectedId); Refresh(); FocusHierarchy(selectedId);
        }
        private void SetSelectionEnabled(bool enabled) { CommitInspectorEdit(); var ids=new HashSet<string>(SelectionRoots()); session.Edit(doc=> {foreach(var item in doc.objects)if(ids.Contains(item.id))item.disabled=!enabled;});Refresh(); }
        private List<SelectionPose> inspectorOriginals;
        private SelectionPose inspectorLockedZiplineEnd;
        private Vector3 inspectorPivot;
        private Quaternion inspectorBasis;
        private Vector3 multiplePositionReference;
        private Vector3Int multiplePositionSharedAxes;
        private void BuildMultipleInspector() {
            inspectorEditingId=selectedId; inspectorDirty=false; inspectorOriginals=null; inspectorLockedZiplineEnd=null;
            inspector.Add(Label(selectedIds.Count + L.T("#ITEMS") + SelectionRoots().Count + L.T("#ROOTS"), "tool-selection"));
            inspector.Add(Label(L.T("#TRANSFORMS_RELATIVE_SELECTION_WORLD_POSITION"),"note"));
            var rootPositions=SelectionRoots().Select(id=>WorldView.ToVector(world.WorldPose(id).position)).ToArray();
            multiplePositionReference=GizmoPlacement.SharedPosition(rootPositions,out multiplePositionSharedAxes);
            positionInput=new VectorInput(L.T("#APEX_MOVEMENT_U"),multiplePositionReference,1);
            var roots = SelectionRoots();
            positionInput.SetEnabled(!roots.All(id => snapshot.objects.Find(item => item.id == id)?.positionLocked == true));
            rotationInput=new VectorInput(L.T("#APEX_ANGLES"),Vector3.zero,2,()=>rotateSnap); scaleInput=new VectorInput(L.T("#SCALE_FACTOR_7490FA"),Vector3.one,3);
            foreach(var field in new[]{positionInput,rotationInput,scaleInput}) { inspector.Add(field); field.Changed+=PreviewInspectorEdit; field.RegisterCallback<PointerUpEvent>(_=>root.schedule.Execute(()=>Run(CommitInspectorEdit)),TrickleDown.TrickleDown); field.RegisterCallback<FocusOutEvent>(_=>root.schedule.Execute(()=>Run(CommitInspectorEdit))); }
            var actions=new VisualElement();actions.AddToClassList("inspector-actions");inspector.Add(actions);
            actions.Add(Button(L.T("#GROUP_CTRL_G_EAE33B"),GroupSelection));actions.Add(Button("⧉  "+L.T("#DUPLICATE"),Duplicate));actions.Add(Button("⌫  "+L.T("#DELETE"),Delete));
            var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;inspector.Add(row);row.Add(Button(L.T("#ENABLE"),()=>SetSelectionEnabled(true)));row.Add(Button(L.T("#DISABLE"),()=>SetSelectionEnabled(false)));
        }
        private void ValidateMultipleInspector() {
            var p=MultiplePositionDelta();var r=rotationInput.value;var s=scaleInput.value;
            if(!WorldView.ToData(p).IsFinite||!WorldView.ToData(r).IsFinite||!WorldView.ToData(s).IsFinite||s.x<=0||s.y<=0||s.z<=0)throw new ArgumentException(L.T("#INVALID_TRANSFORM_FINITE_VALUES_POSITIVE"));
        }
        private Vector3 MultiplePositionDelta() => GizmoPlacement.PositionDelta(
            positionInput.value, multiplePositionReference, multiplePositionSharedAxes);
        private void PreviewMultipleInspector() {
            var p=MultiplePositionDelta();var r=rotationInput.value;var s=scaleInput.value;inspectorDirty=true;
            if(!WorldView.ToData(p).IsFinite||!WorldView.ToData(r).IsFinite||!WorldView.ToData(s).IsFinite||s.x<=0||s.y<=0||s.z<=0)return;
            if(inspectorOriginals==null) {inspectorOriginals=world.CaptureSelection(SelectionRoots());inspectorPivot=SelectionPivot();inspectorBasis=SelectionBasis();}
            inspectorDirty=true; var rotation=inspectorBasis*Quaternion.Euler(r)*Quaternion.Inverse(inspectorBasis);
            if(!world.PreviewSelection(inspectorOriginals,inspectorPivot,inspectorBasis,p,rotation,s))SetStatus(ApexCoordinates.LimitMessage);UpdateGizmoVisual();
        }
        private void CommitSelectionPreview(List<SelectionPose> originals) {
            var poses=originals.ToDictionary(o=>o.Local.id,o=>world.LocalPose(o.Local.id));
            bool changed=originals.Any(o=> {var p=poses[o.Local.id];return Vector3.Distance(WorldView.ToVector(p.position),WorldView.ToVector(o.Local.position))>.00001f||Quaternion.Angle(Quaternion.Euler(WorldView.ToVector(p.rotation)),Quaternion.Euler(WorldView.ToVector(o.Local.rotation)))>.0001f||Vector3.Distance(WorldView.ToVector(p.scale),WorldView.ToVector(o.Local.scale))>.00001f;});
            if(changed)session.Edit(doc=> {foreach(var item in doc.objects)if(poses.TryGetValue(item.id,out var p)){item.position=p.position;item.rotation=p.rotation;item.scale=p.scale;if(IsJumpTowerBalloon(item))SyncJumpTowerHeightFromBalloon(doc,item);else if(item.customType=="jump-tower")NormalizeJumpTowerRotation(item);}});
        }
        private SelectionPose CaptureLockedZiplineEnd(IReadOnlyList<string> roots) {
            if(roots==null||roots.Count!=1||snapshot==null)return null;
            var zipline=snapshot.objects.Find(item=>item.id==roots[0]);
            if(zipline?.customType!="zipline"||!zipline.ziplineLockEnd||string.IsNullOrEmpty(zipline.ziplineEndId))return null;
            return world.CaptureSelection(new[]{zipline.ziplineEndId}).FirstOrDefault();
        }
        private static List<SelectionPose> IncludeLockedPosition(List<SelectionPose> originals,SelectionPose lockedPosition) {
            if(lockedPosition==null||originals.Any(item=>item.Local.id==lockedPosition.Local.id))return originals;
            var result=new List<SelectionPose>(originals){lockedPosition};return result;
        }
    }
}
