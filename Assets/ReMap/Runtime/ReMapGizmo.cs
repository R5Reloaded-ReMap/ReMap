using System.Collections.Generic;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private GizmoOverlay gizmoVisual;
        private bool rotationGizmo, scaleGizmo, draggingGizmo;
        private List<SelectionPose> dragOriginals;
        private SelectionPose dragLockedZiplineEnd;
        private Vector3 dragPivot, dragDirection;
        private Vector3 dragPlaneStart,dragPlaneU,dragPlaneV;
        private Plane dragMovePlane;
        private Quaternion dragBasis;
        private Vector2 dragStart, dragScreenAxis, dragScreenCentre;
        private Vector3 dragRingVector;
        private float dragLength, dragLastAngle, dragAccumulatedAngle;
        private int dragAxis;
        private int duplicateDirectionHandle=-1, directionalTargetHandle=-1, cornerTargetHandle=-1, placementHandle=-1, cornerSideTurn, cornerOrbitTurn, cornerObjectTurn;
        private bool directionalMirrorCopy;
        private float moveSnap=ApexCoordinates.DefaultGridMeters, rotateSnap=15, scaleSnap=.1f;
        private void BuildTransformToolbar(VisualElement toolbar) {
            toolbar.Add(Button(L.T("#SCALE_R"),SetScaleGizmo));
            var space=new Button();space.text=localGizmo?L.T("#LOCAL"):L.T("#GLOBAL");space.tooltip=L.T("#MOVE_ROTATE_USING_WORLD_ACTIVE");
            space.clicked+=()=> {CommitInspectorEdit();CancelGizmoDrag();localGizmo=!localGizmo;space.text=localGizmo?L.T("#LOCAL"):L.T("#GLOBAL");viewport.Focus();};toolbar.Add(space);
            var pivot=new Button();pivot.text=centrePivot?L.T("#CENTER"):L.T("#PIVOT");pivot.tooltip=L.T("#CENTER_SELECTION_BOUNDS_ACTIVE_OBJECT");
            pivot.clicked+=()=> {CommitInspectorEdit();CancelGizmoDrag();centrePivot=!centrePivot;PlayerPrefs.SetInt(CentrePivotPreferenceKey,centrePivot?1:0);PlayerPrefs.Save();pivot.text=centrePivot?L.T("#CENTER"):L.T("#PIVOT");viewport.Focus();};toolbar.Add(pivot);
            toolbar.Add(Button(L.T("#STEPS"),()=>ShowTool("transform-snap")));
        }
        private VisualElement snapSection;
        private void BuildSnapSettings() {
            snapSection=ToolSection(L.T("#TRANSFORM_SNAPPING"),"transform-snap");
            void Step(string name,float initial,System.Action<float> set,float max=1000) {var f=ToolFloat(snapSection,name,initial);f.isDelayed=true;f.RegisterValueChangedCallback(e=> {if(float.IsNaN(e.newValue)||float.IsInfinity(e.newValue)||e.newValue<=0||e.newValue>max){f.SetValueWithoutNotify(e.previousValue);return;}set(e.newValue);});}
            Step(L.T("#APEX_MOVEMENT_U"),moveSnap/ApexCoordinates.MetersPerUnit,v=>{moveSnap=v*ApexCoordinates.MetersPerUnit;world.GridStep=moveSnap;},ApexCoordinates.MaxWorldCoord);Step(L.T("#ROTATION_109E81"),rotateSnap,v=>rotateSnap=v);Step(L.T("#SCALE_FACTOR"),scaleSnap,v=>scaleSnap=v);
            snapSection.Add(Label(L.T("#SNAPPING_USE_STEPS_HANDLES_SCALE"),"note"));
        }
        private void SetGizmoMode(bool rotation) { CommitInspectorEdit(); CancelPlacement(); rotationGizmo=rotation;scaleGizmo=false; mode.RemoveFromClassList("mode-warning");mode.text=rotation?L.T("#ROTATION"):L.T("#MOVEMENT");viewport.Focus(); }
        private void SetScaleGizmo() {CommitInspectorEdit();CancelPlacement();rotationGizmo=false;scaleGizmo=true;ShowScaleModeNotice();viewport.Focus();}
        private void ShowScaleModeNotice(){string warning=L.T("#GAME_SCALING_PROP_DYNAMIC_SCALE");mode.AddToClassList("mode-warning");mode.text=L.T("#SCALE_PROPORTIONS_LINKED")+" · "+warning;SetStatus(warning);}
        private void OnApplicationFocus(bool focused) {if(!focused){world?.CancelNavigation();CancelSceneSelection();CancelGizmoDrag();EndLibraryDrag();EndLayoutResize();}}
        private bool ConstrainVerticalZiplineEndMovement()
        {
            if (rotationGizmo || scaleGizmo || DirectionalDuplicationActive() || SurfacePlacementActive() || selectedIds.Count != 1) return false;
            var item = snapshot?.objects.Find(candidate => candidate.id == selectedId);
            return IsVerticalZiplineEnd(item);
        }

        private void UpdateGizmoVisual() {
            if(gizmoVisual==null||snapshot==null)return;
            Vector3? pivot=placing==null&&placingAssembly==null&&placingSelection==null&&selectedId!=null?(Vector3?)SelectionPivot():null;
            bool duplicate=DirectionalDuplicationActive(); bool placement=SurfacePlacementActive(); bool cornerMode=duplicate && duplicateSymmetric?.value==true;
            bool verticalEnd = ConstrainVerticalZiplineEndMovement();
            var basis = verticalEnd || placement ? Quaternion.identity : duplicate ? DirectionalDuplicationBasis() : SelectionBasis();
            int axisMask = verticalEnd ? 1 << 1 : 7;
            gizmoVisual.Rebuild(world.Camera,pivot,duplicate||placement?false:rotationGizmo,basis,duplicate||placement?false:scaleGizmo,duplicate,cornerMode?CornerPivots():null,cornerTargetHandle,directionalMirrorCopy,cornerMode?CornerPlacementGuide():null,cornerOrbitTurn,cornerSideTurn,cornerObjectTurn,axisMask,placement);
            if(selectedIds.Count>1)world.HighlightSelection(SelectionRoots());
        }
        private bool DirectionalDuplicationActive() => ConstructionToolActive("duplicate");
        private bool SurfacePlacementActive() => ConstructionToolActive("ground");
        private string DirectionalDuplicationReferenceId()
        {
            if (duplicateSymmetric?.value != true || selectedIds.Count <= 1) return selectedId;
            var roots = SelectionRoots();
            return selectionOrder.Find(id => roots.Contains(id)) ?? (roots.Count > 0 ? roots[0] : selectedId);
        }
        private Quaternion DirectionalDuplicationBasis()
        {
            var basisId = DirectionalDuplicationReferenceId();
            return duplicateLocal?.value == true && basisId != null ? Quaternion.Euler(WorldView.ToVector(world.WorldPose(basisId).rotation)) : Quaternion.identity;
        }
        private void ClearDirectionalDuplicationHover()
        {
            duplicateDirectionHandle=-1; directionalTargetHandle=-1; cornerTargetHandle=-1; placementHandle=-1;
            if(gizmoVisual!=null)gizmoVisual.ActiveAxis=-1;
            world?.ClearToolPreview();
        }
        private bool HandleGizmoInput(Vector2 mouse,bool inside) {
            var input=Mouse.current;
            if(draggingGizmo) {
                if(Keyboard.current?.escapeKey.wasPressedThisFrame==true){CancelGizmoDrag();return true;}
                if(input.leftButton.wasReleasedThisFrame||!input.leftButton.isPressed){FinishGizmoDrag();return true;}
                var move=Vector3.zero;var rotation=Quaternion.identity;var factors=Vector3.one;
                if(rotationGizmo) {
                    float angle;var plane=new Plane(dragDirection,dragPivot);var ray=world.Camera.ScreenPointToRay(mouse);
                    if(plane.Raycast(ray,out var along)&&dragRingVector.sqrMagnitude>.001f)angle=Vector3.SignedAngle(dragRingVector,ray.GetPoint(along)-dragPivot,dragDirection);
                    else angle=Vector2.SignedAngle(dragStart-dragScreenCentre,mouse-dragScreenCentre);
                    dragAccumulatedAngle+=Mathf.DeltaAngle(dragLastAngle,angle);dragLastAngle=angle;
                    float degrees=snap?MapSession.Snap(dragAccumulatedAngle,rotateSnap):dragAccumulatedAngle;
                    rotation=Quaternion.AngleAxis(degrees,dragDirection);
                    if(selectedIds.Count>1)rotationInput?.Set(GizmoOverlay.Axis(dragAxis)*degrees);
                } else if(scaleGizmo) {
                    float change=dragAxis==3?(mouse.x-dragStart.x+mouse.y-dragStart.y)/150:Vector2.Dot(mouse-dragStart,dragScreenAxis)/Mathf.Max(1,dragScreenAxis.sqrMagnitude);
                    if(snap)change=MapSession.Snap(change,scaleSnap);
                    float factor=Mathf.Max(.01f,1+change);factors=GizmoScaleFactors(factor,dragAxis);
                    if(selectedIds.Count>1)scaleInput?.Set(factors);
                } else {
                    if(GizmoOverlay.PlaneAxes(dragAxis,out _,out _))
                    {
                        var ray=world.Camera.ScreenPointToRay(mouse);
                        if(dragMovePlane.Raycast(ray,out var along))
                        {
                            var delta=ray.GetPoint(along)-dragPlaneStart;float u=Vector3.Dot(delta,dragPlaneU),v=Vector3.Dot(delta,dragPlaneV);
                            if(snap){u=MapSession.Snap(u,moveSnap);v=MapSession.Snap(v,moveSnap);}move=dragPlaneU*u+dragPlaneV*v;
                        }
                    }
                    else
                    {
                        float amount=Vector2.Dot(mouse-dragStart,dragScreenAxis)/Mathf.Max(1,dragScreenAxis.sqrMagnitude)*dragLength;
                        if(snap)amount=MapSession.Snap(amount,moveSnap);move=dragDirection*amount;
                    }
                    if(selectedIds.Count>1)positionInput?.Set(move);
                }
                if(!world.PreviewSelection(dragOriginals,dragPivot,dragBasis,move,rotation,factors,dragLockedZiplineEnd))SetStatus(ApexCoordinates.LimitMessage);
                world.HighlightSelection(SelectionRoots());SyncInspectorValues();UpdateGizmoVisual();return true;
            }
            if(SurfacePlacementActive())
            {
                int handle=inside&&placing==null&&selectedId!=null?gizmoVisual.Hit(mouse):-1;
                if(handle<7||handle>9)handle=-1;
                if(handle!=placementHandle)
                {
                    placementHandle=handle;gizmoVisual.ActiveAxis=handle;UpdateGizmoVisual();
                }
                if(handle>=7&&input.leftButton.wasPressedThisFrame)
                {
                    int action=handle;placementHandle=-1;gizmoVisual.ActiveAxis=-1;
                    Run(()=>{if(action==7)GroundSelection(true);else if(action==8)PlaceSelectionToTop();else GroundSelection(false);});
                    UpdateGizmoVisual();return true;
                }
                if(handle>=0)return true;
            }

            if(DirectionalDuplicationActive())
            {
                bool cornerMode=duplicateSymmetric?.value==true;
                int handle=inside&&placing==null&&selectedId!=null?gizmoVisual.Hit(mouse):-1;
                if(cornerMode ? handle<20||handle>26 : handle<10||handle>16)handle=-1;
                bool corner=handle>=20&&handle<=23, cornerControl=handle>=24&&handle<=26, direction=handle>=10&&handle<=15;
                if(handle!=duplicateDirectionHandle)
                {
                    duplicateDirectionHandle=handle;gizmoVisual.ActiveAxis=handle;
                    if(cornerMode&&corner){cornerTargetHandle=handle;var pivot=CornerPivot(handle);PreviewTool(()=>BuildCornerPreview(pivot));}
                    else if(!cornerMode&&direction){directionalTargetHandle=handle;var duplicateDirection=GizmoOverlay.DuplicateDirection(handle);PreviewTool(()=>BuildDirectionalPreview(duplicateDirection));}
                    else if(handle<0)world.ClearToolPreview();
                }
                if(cornerMode&&cornerTargetHandle>=20&&(Keyboard.current?.rKey.wasPressedThisFrame==true||input.rightButton.wasPressedThisFrame))
                {
                    CycleCornerObjects(CornerPivot(cornerTargetHandle)); return true;
                }
                if(!cornerMode&&handle==16&&input.leftButton.wasPressedThisFrame)
                {
                    directionalMirrorCopy=!directionalMirrorCopy;
                    if(directionalTargetHandle>=10)PreviewTool(()=>BuildDirectionalPreview(GizmoOverlay.DuplicateDirection(directionalTargetHandle)));
                    UpdateGizmoVisual(); ToolMessage(directionalMirrorCopy?L.T("#MIRRORED_COPY_ENABLED"):L.T("#MIRRORED_COPY_DISABLED")); return true;
                }
                if(cornerControl&&cornerTargetHandle>=20&&input.leftButton.wasPressedThisFrame)
                {
                    var pivot=CornerPivot(cornerTargetHandle);
                    if(handle==24)CycleCornerSide(pivot);else if(handle==25)CycleCornerOrbit(pivot);else CycleCornerObjects(pivot);
                    return true;
                }
                if(corner&&input.leftButton.wasPressedThisFrame)
                {
                    int target=handle;ClearDirectionalDuplicationHover();DuplicateCorner(target);return true;
                }
                if(direction&&input.leftButton.wasPressedThisFrame)
                {
                    var duplicateDirection=GizmoOverlay.DuplicateDirection(handle);ClearDirectionalDuplicationHover();DuplicateDirection(duplicateDirection);return true;
                }
                if(handle>=0)return true;
            }            if(inside&&placing==null&&selectedId!=null&&input.leftButton.wasPressedThisFrame) {
                bool verticalEnd=ConstrainVerticalZiplineEndMovement();int axis=gizmoVisual.Hit(mouse);
                if(axis<0||(verticalEnd&&axis!=1))return false;
                CommitInspectorEdit();var roots=SelectionRoots();dragOriginals=world.CaptureSelection(roots);dragLockedZiplineEnd=CaptureLockedZiplineEnd(roots);if(dragOriginals.Count==0)return false;
                draggingGizmo=true;dragAxis=axis;gizmoVisual.ActiveAxis=axis;dragBasis=verticalEnd?Quaternion.identity:SelectionBasis();
                dragPivot=gizmoVisual.Pivot;dragLength=gizmoVisual.WorldLength;dragStart=mouse;dragScreenCentre=world.Camera.WorldToScreenPoint(dragPivot);
                if(GizmoOverlay.PlaneAxes(axis,out int first,out int second))
                {
                    dragPlaneU=dragBasis*GizmoOverlay.Axis(first);dragPlaneV=dragBasis*GizmoOverlay.Axis(second);dragDirection=Vector3.Cross(dragPlaneU,dragPlaneV).normalized;dragMovePlane=new Plane(dragDirection,dragPivot);
                    var moveRay=world.Camera.ScreenPointToRay(mouse);if(!dragMovePlane.Raycast(moveRay,out var moveAlong)){CancelGizmoDrag();return false;}dragPlaneStart=moveRay.GetPoint(moveAlong);dragScreenAxis=Vector2.zero;
                }
                else {dragDirection=dragBasis*GizmoOverlay.Axis(axis);dragScreenAxis=(Vector2)world.Camera.WorldToScreenPoint(dragPivot+dragDirection*dragLength)-dragScreenCentre;}
                var plane=new Plane(dragDirection,dragPivot);var ray=world.Camera.ScreenPointToRay(mouse);
                dragRingVector=plane.Raycast(ray,out var along)?ray.GetPoint(along)-dragPivot:Vector3.zero;dragLastAngle=dragAccumulatedAngle=0;return true;
            }
            return false;
        }
        private Vector3 GizmoScaleFactors(float factor,int axis)=>Vector3.one*factor;
        private void FinishGizmoDrag() {
            if(!draggingGizmo)return;draggingGizmo=false;gizmoVisual.ActiveAxis=-1;
            Run(()=>CommitSelectionPreview(IncludeLockedPosition(dragOriginals,dragLockedZiplineEnd)));dragLockedZiplineEnd=null;Refresh();AlignSelectedAutomaticZiplineEnd();
        }
        private void CancelGizmoDrag() {
            if(!draggingGizmo)return;draggingGizmo=false;dragLockedZiplineEnd=null;gizmoVisual.ActiveAxis=-1;
            if(snapshot!=null){world.Sync(snapshot,selectedId);world.HighlightSelection(SelectionRoots());SyncInspectorValues();if(selectedIds.Count>1)RefreshInspector();}
        }
    }
}
