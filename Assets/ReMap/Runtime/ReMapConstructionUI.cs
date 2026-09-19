using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.UIElements;
namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private ScrollView toolsPanel;
        private VisualElement toolsWindow;
        private DropdownField toolChoice;
        private Button toolActivationButton;
        private Label toolSelection, toolResult, toolInfo, measureInfo;
        private Toggle duplicateLocal, duplicateAuto, duplicateSymmetric;
        private FloatField rotationMin, rotationMax, scaleMin, scaleMax;
        private ApexDistanceField groundClearance,duplicateGap,gridSpacingX,gridSpacingY;
        private IntegerField gridColumns, gridRows;
        private DropdownField gridPlane;
        private Toggle measureAxisX, measureAxisY, measureAxisZ;
        private string measureStartId, automaticGridKey, gridPreviewBlockedSelection;
        private bool toolsOpen, constructionToolEnabled = true;
        private readonly System.Random toolRandom = new System.Random();
        private readonly List<Button> selectionTools = new List<Button>();
        private readonly List<Button> folderPivotTools = new List<Button>();
        private readonly List<VisualElement> toolSections = new List<VisualElement>();
        private readonly List<string> toolSectionIds = new List<string>();
        private readonly List<string> toolSectionTitles = new List<string>();
        private void BuildConstructionTools()
        {
            toolsWindow = new VisualElement { name = "construction-tools-window" }; toolsWindow.AddToClassList("floating-window"); toolsWindow.AddToClassList("tools-window"); root.Add(toolsWindow);
            var title = DockTitle(L.T("#CONSTRUCTION_TOOLS_6E6587")); title.AddToClassList("floating-title"); toolsWindow.Add(title);
            toolActivationButton = Button("", ToggleConstructionTool, "tool-activation"); toolActivationButton.name = "construction-tool-activation"; toolActivationButton.tooltip = L.T("#TOGGLE_TOOL_ACTIVE"); title.Add(toolActivationButton);
            title.Add(Button("×", () => ShowConstructionTools(false), "dock-close")); BindFloatingPanel(title, toolsWindow);
            toolChoice = new DropdownField(L.T("#TOOL"), new List<string>(), 0); toolChoice.AddToClassList("floating-tool-choice"); toolsWindow.Add(toolChoice);
            toolsPanel = new ScrollView { name = "construction-tools" }; toolsPanel.AddToClassList("construction-tools"); toolsWindow.Add(toolsPanel);
            toolSelection = Label(L.T("#SELECT_OBJECT_GROUP"), "tool-selection"); toolsPanel.Add(toolSelection);

            toolResult = Label(L.T("#ONE_OPERATION_ONE_UNDO_STEP"), "tool-result"); toolsPanel.Add(toolResult);
            BuildSelectionByTypeTool();
            BuildModelReplacementTool();
            BuildParameterTransferTool();
            var ground = ToolSection(L.T("#PLACE_SURFACE"), "ground", true);
            groundClearance = ToolDistance(ground, L.T("#APEX_CLEARANCE_U"), 0);
            ToolButton(ground, L.T("#BOTTOM_SURFACE_BELOW"), () => GroundSelection(true), "tool-ground");
            ToolButton(ground, L.T("#TOP_SURFACE_ABOVE"), PlaceSelectionToTop, "tool-top");
            ToolButton(ground, L.T("#BOTTOM_Z_0_PLANE"), () => GroundSelection(false), "tool-plane");
            ground.Add(Label(L.T("#PLACE_BOTTOM_SUPPORT_ATTACH_TOP"), "note"));
            ground.Add(Label(L.T("#USE_GIZMO_AROUND_SELECTION"), "note"));
            var duplicate = ToolSection(L.T("#DIRECTIONAL_DUPLICATION"), "duplicate", true);
            duplicateLocal = new Toggle(L.T("#OBJECT_AXES")) { value = true }; duplicate.Add(duplicateLocal);
            duplicateAuto = new Toggle(L.T("#EDGE_EDGE")) { value = true }; duplicate.Add(duplicateAuto);
            duplicateSymmetric = new Toggle(L.T("#DUPLICATE_AROUND_CORNER")); duplicate.Add(duplicateSymmetric);

            duplicateGap = ToolDistance(duplicate, L.T("#APEX_GAP_STEP_U"), 0);
            duplicateGap.tooltip = L.T("#EDGE_EDGE_GAP_BETWEEN_COPIES");
            duplicateLocal.RegisterValueChangedCallback(_=>ClearDirectionalDuplicationHover());
            duplicateAuto.RegisterValueChangedCallback(_=>ClearDirectionalDuplicationHover());
            duplicateGap.RegisterValueChangedCallback(_=>ClearDirectionalDuplicationHover());
            duplicateSymmetric.RegisterValueChangedCallback(e=> { duplicateLocal.SetEnabled(!e.newValue); duplicateAuto.SetEnabled(!e.newValue); duplicateGap.SetEnabled(!e.newValue); if(e.newValue){cornerSideTurn=0;cornerOrbitTurn=0;cornerObjectTurn=0;} ClearDirectionalDuplicationHover(); });
            var guide = new VisualElement { name = "directional-gizmo-guide" }; guide.AddToClassList("directional-gizmo-guide");
            guide.Add(Label(L.T("#USE_GIZMO_AROUND_SELECTION"), "directional-gizmo-title"));
            var legend=ToolRow(guide);
            var x=Label("X", "directional-axis");x.AddToClassList("axis-x");legend.Add(x);
            var y=Label("Y", "directional-axis");y.AddToClassList("axis-y");legend.Add(y);
            var z=Label("Z", "directional-axis");z.AddToClassList("axis-z");legend.Add(z);
            guide.Add(Label(L.T("#ARROWS_ONE_COPY_MIRROR_MIRROR"), "note"));duplicate.Add(guide);
            var grid = ToolSection(L.T("#CREATE_GRID_WALL"), "grid");
            gridColumns = new IntegerField(L.T("#COLUMNS")) { value = 3 }; gridRows = new IntegerField(L.T("#ROWS")) { value = 3 }; grid.Add(gridColumns); grid.Add(gridRows);
            gridPlane = new DropdownField(L.T("#PLANE"), new List<string> { L.T("#GROUND_XY"), L.T("#WALL_XZ") }, 0); grid.Add(gridPlane);
            gridSpacingX = ToolDistance(grid, L.T("#APEX_HORIZONTAL_STEP_U"), 128*ApexCoordinates.MetersPerUnit); gridSpacingY = ToolDistance(grid, L.T("#APEX_VERTICAL_STEP_U"), 128*ApexCoordinates.MetersPerUnit);
            ToolButton(grid, L.T("#MATCH_OBJECT_DIMENSIONS"), FitGridSpacing, "tool-fit-grid");
            ToolButton(grid, L.T("#CREATE_GROUP"), GridSelection, "tool-grid");
            void GridChanged() { gridPreviewBlockedSelection = null; automaticGridKey = null; RefreshAutomaticGridPreview(); }
            gridColumns.RegisterValueChangedCallback(_=>GridChanged()); gridRows.RegisterValueChangedCallback(_=>GridChanged()); gridPlane.RegisterValueChangedCallback(_=>GridChanged());
            gridSpacingX.RegisterValueChangedCallback(_=>GridChanged()); gridSpacingY.RegisterValueChangedCallback(_=>GridChanged());
            grid.Add(Label(L.T("#OBJECT_AXES_SELECTION_BECOMES_FIRST"), "note"));
            var random = ToolSection(L.T("#RANDOM_VARIATIONS"), "random");
            rotationMin = ToolFloat(random, L.T("#MIN_Z_ROTATION"), 0); rotationMax = ToolFloat(random, L.T("#MAX_Z_ROTATION"), 360);
            ToolButton(random, L.T("#ADD_Z_ROTATION"), () => RandomizeSelection(true), "tool-random-rotation");
            scaleMin = ToolFloat(random, L.T("#MIN_FACTOR"), .9f); scaleMax = ToolFloat(random, L.T("#MAX_FACTOR"), 1.1f);
            ToolButton(random, L.T("#MULTIPLY_SCALE"), () => RandomizeSelection(false), "tool-random-scale");
            random.Add(Label(L.T("#ROTATION_AROUND_WORLD_Z_SCALE"), "note"));
            var align = ToolSection(L.T("#ADJUST_SELECTION"), "adjust");
            ToolButton(align, L.T("#SNAP_PIVOTS_ACTIVE_STEP"), () => AdjustSelection("snap"), "tool-snap");
            ToolButton(align, L.T("#RESET_LOCAL_ROTATION"), () => AdjustSelection("rotation"), "tool-reset-rotation");
            ToolButton(align, L.T("#RESET_LOCAL_SCALE"), () => AdjustSelection("scale"), "tool-reset-scale");
            var pivot = ToolSection(L.T("#FOLDER_PIVOT"), "pivot");
            folderPivotTools.Add(ToolButton(pivot, L.T("#PIVOT_TO_BOTTOM_CENTER"), () => RepositionSelectedGroupPivot(GroupPivotAnchor.BottomCenter), "tool-pivot-bottom"));
            folderPivotTools.Add(ToolButton(pivot, L.T("#PIVOT_TO_BOUNDS_CENTER"), () => RepositionSelectedGroupPivot(GroupPivotAnchor.Center), "tool-pivot-center"));
            folderPivotTools.Add(ToolButton(pivot, L.T("#PIVOT_TO_TOP_CENTER"), () => RepositionSelectedGroupPivot(GroupPivotAnchor.TopCenter), "tool-pivot-top"));
            pivot.Add(Label(L.T("#PIVOT_REPOSITION_KEEP_GEOMETRY"), "note"));
            var measure = ToolSection(L.T("#MEASURE_INFORMATION"), "measure");
            ToolButton(measure, L.T("#SET_CURRENT_SELECTION_START"), () => { measureStartId = selectedId; RefreshToolReadout(); }, "tool-measure-start");
            var measureAxes = ToolRow(measure); measureAxes.AddToClassList("measure-axis-row");
            measureAxes.Add(Label(L.T("#MEASURE_AXES"), "measure-axis-label"));
            measureAxisX = new Toggle("X") { value = true }; measureAxisY = new Toggle("Y") { value = true }; measureAxisZ = new Toggle("Z") { value = true };
            measureAxes.Add(measureAxisX); measureAxes.Add(measureAxisY); measureAxes.Add(measureAxisZ);
            measureAxisX.RegisterValueChangedCallback(_ => RefreshToolReadout());
            measureAxisY.RegisterValueChangedCallback(_ => RefreshToolReadout());
            measureAxisZ.RegisterValueChangedCallback(_ => RefreshToolReadout());
            measureInfo = Label(L.T("#CHOOSE_START_SELECT_END"), "tool-info"); measure.Add(measureInfo);
            measure.Add(Button(L.T("#CLEAR_MEASUREMENT"), () => { measureStartId = null; RefreshToolReadout(); }));
            toolInfo = Label("", "tool-info"); measure.Add(toolInfo);
            ToolButton(measure, L.T("#COPY_INFORMATION"), () => GUIUtility.systemCopyBuffer = toolInfo.text + "\n" + measureInfo.text, "tool-copy-info");
            toolChoice.choices = toolSectionTitles.ToList();
            toolChoice.SetValueWithoutNotify(toolChoice.choices[0]);
            toolChoice.RegisterValueChangedCallback(_ => SelectTool(toolChoice.index));
            SelectTool(0);
            root.schedule.Execute(() => { RefreshToolReadout(); RefreshAutomaticGridPreview(); }).Every(200);
            ShowConstructionTools(false);
        }
        private VisualElement ToolSection(string text, string id, bool expanded = false)
        {
            var section = new VisualElement { name = "tools-" + id }; section.AddToClassList("tool-section"); section.Add(Label(text, "tool-section-title"));
            toolSections.Add(section); toolSectionIds.Add(id); toolSectionTitles.Add(text); toolsPanel.Add(section); return section;
        }
        private sealed class ApexDistanceField : FloatField {
            public ApexDistanceField(string title,float meters):base(title){value=meters;}
            public new float value {get=>base.value*ApexCoordinates.MetersPerUnit;set=>base.value=value/ApexCoordinates.MetersPerUnit;}
        }
        private static ApexDistanceField ToolDistance(VisualElement parent,string title,float meters) {
            var field=new ApexDistanceField(title,meters);parent.Add(field);return field;
        }
        private static FloatField ToolFloat(VisualElement parent, string label, float value)
        {
            var field = new FloatField(label) { value = value }; parent.Add(field); return field;
        }
        private static VisualElement ToolRow(VisualElement parent)
        {
            var row = new VisualElement(); row.AddToClassList("tool-row"); parent.Add(row); return row;
        }
        private Button ToolButton(VisualElement parent, string text, Action action, string id, Func<MapDocument> preview = null)
        {
            var button = Button(text, () => { world.ClearToolPreview(); try { action(); } catch (Exception ex) { ToolMessage(ex.Message); throw; } });
            button.name = id; parent.Add(button); selectionTools.Add(button);
            if (preview != null)
            {
                button.RegisterCallback<PointerEnterEvent>(_ => PreviewTool(preview));
                button.RegisterCallback<PointerLeaveEvent>(_ => world.ClearToolPreview());
                button.RegisterCallback<DetachFromPanelEvent>(_ => world.ClearToolPreview());
            }
            return button;
        }
        private void ShowConstructionTools(bool show)
        {
            CommitInspectorEdit(); bool opening = show && !toolsOpen; toolsOpen = show;
            if (toolsWindow == null) return;
            if (opening) constructionToolEnabled = true;
            toolsWindow.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show)
            {
                toolsWindow.style.width = Mathf.Clamp(380, 300, Mathf.Max(300, root.resolvedStyle.width - 16));
                toolsWindow.style.height = Mathf.Clamp(430, 240, Mathf.Max(240, root.resolvedStyle.height - 16));
                toolsWindow.BringToFront();
            }
            else ClearDirectionalDuplicationHover();
            UpdateConstructionToolActivation();
            UpdateGizmoVisual();
            if (show && constructionToolEnabled) RefreshAutomaticGridPreview();
            RefreshToolReadout();
        }
        private void ShowTool(string id)
        {
            int index = toolSectionIds.IndexOf(id); if (index < 0) index = 0;
            ShowConstructionTools(true); toolChoice.SetValueWithoutNotify(toolChoice.choices[index]); SelectTool(index);
        }
        private void SelectTool(int index)
        {
            constructionToolEnabled = true;
            for (int i = 0; i < toolSections.Count; i++) toolSections[i].style.display = i == index ? DisplayStyle.Flex : DisplayStyle.None;
            ClearDirectionalDuplicationHover(); UpdateConstructionToolActivation(); UpdateGizmoVisual(); RefreshAutomaticGridPreview(); RefreshToolReadout();
        }
        private bool ConstructionToolActive(string id = null)
        {
            if (!toolsOpen || !constructionToolEnabled || toolChoice == null || toolChoice.index < 0 || toolChoice.index >= toolSectionIds.Count) return false;
            return id == null || toolSectionIds[toolChoice.index] == id;
        }
        private void ToggleConstructionTool()
        {
            constructionToolEnabled = !constructionToolEnabled;
            ClearDirectionalDuplicationHover(); automaticGridKey = null;
            UpdateConstructionToolActivation(); UpdateGizmoVisual();
            if (constructionToolEnabled) RefreshAutomaticGridPreview();
        }
        private void UpdateConstructionToolActivation()
        {
            if (toolActivationButton == null) return;
            toolActivationButton.text = (constructionToolEnabled ? "● " : "○ ") + L.T(constructionToolEnabled ? "#TOOL_ACTIVE" : "#TOOL_PAUSED");
            toolActivationButton.EnableInClassList("paused", !constructionToolEnabled);
        }
        private void BindFloatingPanel(VisualElement handle, VisualElement panel)
        {
            Vector2 pointerStart = default, panelStart = default; int pointerId = -1; bool dragging = false;
            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || e.target is Button || (e.target as VisualElement)?.GetFirstAncestorOfType<Button>() != null) return;
                pointerStart = e.position; panelStart = panel.worldBound.position; pointerId = e.pointerId; dragging = true;
                handle.CapturePointer(pointerId); panel.BringToFront(); e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging || e.pointerId != pointerId) return;
                Vector2 next = panelStart + (Vector2)e.position - pointerStart;
                panel.style.left = Mathf.Clamp(next.x, 0, Mathf.Max(0, root.worldBound.width - panel.resolvedStyle.width));
                panel.style.top = Mathf.Clamp(next.y, 0, Mathf.Max(0, root.worldBound.height - panel.resolvedStyle.height));
                e.StopPropagation();
            });
            void End()
            {
                if (!dragging) return; dragging = false;
                if (handle.HasPointerCapture(pointerId)) handle.ReleasePointer(pointerId);
            }
            handle.RegisterCallback<PointerUpEvent>(e => { if (e.pointerId == pointerId) { End(); e.StopPropagation(); } });
            handle.RegisterCallback<PointerCaptureOutEvent>(_ => End());
        }
        private void PreviewTool(Func<MapDocument> create)
        {
            if (!ConstructionToolActive() || selectedId == null) { world.ClearToolPreview(); return; }
            try { CommitInspectorEdit(); world.PreviewTool(create()); }
            catch { world.ClearToolPreview(); }
        }
        private void DirectionalMirrorTransform(Vector3 direction, out Vector3 pivot, out Vector3 axis, out Vector3 offset)
        {
            var roots = SelectionRoots(); if (roots.Count == 0) throw new InvalidOperationException(L.T("#SELECT_OBJECT_GROUP_0ED54C"));
            pivot = SelectionPivot(); axis = (DirectionalDuplicationBasis() * Vector3.up).normalized;
            if (!duplicateAuto.value) { offset = DirectionOffset(direction); return; }
            var normal = DirectionOffset(direction).normalized; var turn = Quaternion.AngleAxis(180, axis);
            if (!world.TryProjectedRange(roots, normal, out _, out var sourceEdge) || !world.TryProjectedRangeAfterTurn(roots, pivot, turn, Quaternion.identity, normal, out var turnedNearEdge, out _)) throw new InvalidOperationException(L.T("#SELECT_OBJECT_GROUP_0ED54C"));
            offset = normal * (sourceEdge + duplicateGap.value - turnedNearEdge);
        }
        private MapDocument BuildDirectionalPreview(Vector3 direction)
        {
            if (!directionalMirrorCopy) return BuildToolPreview(new[] { DirectionOffset(direction) });
            var roots = SelectionRoots().ToArray(); DirectionalMirrorTransform(direction, out var pivot, out var axis, out var offset); var turn = Quaternion.AngleAxis(180, axis); var preview = new MapDocument();
            foreach (var rootId in roots)
            {
                var branch = MapHierarchy.Subtree(snapshot, rootId); var rootPose = world.WorldPose(rootId); var sourceRoot = WorldView.ToVector(rootPose.position); var targetRoot = pivot + turn * (sourceRoot - pivot) + offset;
                foreach (var item in snapshot.objects.Where(candidate => branch.Contains(candidate.id) && !candidate.isGroup && MapHierarchy.IsEnabled(snapshot, candidate.id)))
                {
                    var pose = world.WorldPose(item.id); var copy = item.Copy(); var sourcePosition = WorldView.ToVector(pose.position);
                    copy.id = Guid.NewGuid().ToString("N"); copy.parentId = ""; copy.position = WorldView.ToData(targetRoot + turn * (sourcePosition - sourceRoot)); copy.rotation = WorldView.ToData((turn * Quaternion.Euler(WorldView.ToVector(pose.rotation))).eulerAngles); copy.scale = pose.scale; preview.objects.Add(copy);
                }
            }
            return preview;
        }
        private string[] CornerRoots()
        {
            var roots = SelectionRoots(); var referenceId = DirectionalDuplicationReferenceId();
            if (referenceId == null || roots.Count == 0 || roots[0] == referenceId) return roots.ToArray();
            return new[] { referenceId }.Concat(roots.Where(id => id != referenceId)).ToArray();
        }
        private Vector3 CornerSelectionCenter()
        {
            var roots = SelectionRoots(); var basis = DirectionalDuplicationBasis();
            var right = (basis * Vector3.right).normalized; var forward = (basis * Vector3.forward).normalized; var up = (basis * Vector3.up).normalized;
            if (!world.TryProjectedRange(roots, right, out var minRight, out var maxRight) || !world.TryProjectedRange(roots, forward, out var minForward, out var maxForward) || !world.TryProjectedRange(roots, up, out var minUp, out var maxUp)) return SelectionPivot();
            return right * ((minRight + maxRight) * .5f) + forward * ((minForward + maxForward) * .5f) + up * ((minUp + maxUp) * .5f);
        }
        private Vector3 CornerAnchorOffset(IReadOnlyCollection<string> roots, Vector3 pivot, Quaternion orbit, Quaternion objectRotation, Vector3 objectPivot)
        {
            var axisDirection = CornerAxisDirection(); var sideDirection = CornerSideDirection();
            if (!world.TryProjectedRangeAfterTurn(roots, pivot, orbit, objectRotation, objectPivot, axisDirection, out var minAxis, out _) || !world.TryProjectedRangeAfterTurn(roots, pivot, orbit, objectRotation, objectPivot, sideDirection, out var minSide, out _)) return Vector3.zero;
            return axisDirection * (Vector3.Dot(pivot, axisDirection) - minAxis) + sideDirection * (Vector3.Dot(pivot, sideDirection) - minSide);
        }        private MapDocument BuildCornerPreview(Vector3 pivot)
        {
            var roots = CornerRoots(); if (roots.Length == 0) throw new InvalidOperationException(L.T("#SELECT_OBJECT_GROUP_0ED54C"));
            var axis = (DirectionalDuplicationBasis() * Vector3.up).normalized; float orbitAngle = CornerOrbitAngle(); var objectPivot = CornerSelectionCenter();
            var orbit = Quaternion.AngleAxis(orbitAngle, axis); var objectRotation = Quaternion.AngleAxis(CornerObjectAngle(), axis); var delta = objectRotation * orbit;
            var turnedObjectPivot = pivot + orbit * (objectPivot - pivot); var anchorOffset = CornerAnchorOffset(roots, pivot, orbit, objectRotation, objectPivot); var preview = new MapDocument();
            foreach (var rootId in roots)
            {
                var branch = MapHierarchy.Subtree(snapshot, rootId); var rootPose = world.WorldPose(rootId); var sourceRoot = WorldView.ToVector(rootPose.position);
                var orbitRoot = pivot + orbit * (sourceRoot - pivot); var targetRoot = turnedObjectPivot + objectRotation * (orbitRoot - turnedObjectPivot) + anchorOffset;
                foreach (var item in snapshot.objects.Where(candidate => branch.Contains(candidate.id) && !candidate.isGroup && MapHierarchy.IsEnabled(snapshot, candidate.id)))
                {
                    var pose = world.WorldPose(item.id); var copy = item.Copy(); var sourcePosition = WorldView.ToVector(pose.position);
                    copy.id = Guid.NewGuid().ToString("N"); copy.parentId = "";
                    copy.position = WorldView.ToData(targetRoot + delta * (sourcePosition - sourceRoot));
                    copy.rotation = WorldView.ToData((delta * Quaternion.Euler(WorldView.ToVector(pose.rotation))).eulerAngles);
                    copy.scale = pose.scale; preview.objects.Add(copy);
                }
            }
            return preview;
        }
        private Vector3[] CornerPivots()
        {
            var roots = SelectionRoots(); if (roots.Count == 0) return Array.Empty<Vector3>();
            var basis = DirectionalDuplicationBasis(); var right = basis * Vector3.right; var forward = basis * Vector3.forward; var axis = basis * Vector3.up;
            if (!world.TryProjectedRange(roots, right, out var minRight, out var maxRight) || !world.TryProjectedRange(roots, forward, out var minForward, out var maxForward) || !world.TryProjectedRange(roots, axis, out var minAxis, out var maxAxis)) return Array.Empty<Vector3>();
            var centerOnAxis = axis * ((minAxis + maxAxis) * .5f);
            return new[] { centerOnAxis + right * minRight + forward * minForward, centerOnAxis + right * maxRight + forward * minForward, centerOnAxis + right * maxRight + forward * maxForward, centerOnAxis + right * minRight + forward * maxForward };
        }
        private Vector3 CornerPivot(int handle)
        {
            var pivots = CornerPivots(); int index = handle - 20;
            if (index < 0 || index >= pivots.Length) throw new ArgumentException(L.T("#CHOOSE_VISIBLE_CORNER_HANDLE"));
            return pivots[index];
        }
        private Vector3 CornerAxisDirection()
        {
            var basis = DirectionalDuplicationBasis();
            switch ((cornerOrbitTurn % 4 + 4) % 4)
            {
                case 1: return (basis * Vector3.right).normalized;
                case 2: return -(basis * Vector3.forward).normalized;
                case 3: return -(basis * Vector3.right).normalized;
                default: return (basis * Vector3.forward).normalized;
            }
        }
        private Vector3 CornerSideDirection()
        {
            var up = (DirectionalDuplicationBasis() * Vector3.up).normalized;
            var rightOfAxis = Vector3.Cross(up, CornerAxisDirection()).normalized;
            return cornerSideTurn == 0 ? rightOfAxis : -rightOfAxis;
        }
        private float CornerOrbitAngle() => CornerOrbitAngle(cornerTargetHandle >= 20 && cornerTargetHandle <= 23 ? cornerTargetHandle : 20);
        private float CornerOrbitAngle(int handle) => ((cornerOrbitTurn % 4 + 4) % 4) * 90;        private float CornerObjectAngle() => cornerObjectTurn * 180;
        private Vector3[] CornerPlacementGuide()
        {
            if (cornerTargetHandle < 20 || cornerTargetHandle > 23) return null;
            var roots = CornerRoots(); if (roots.Length == 0) return null;
            var pivot = CornerPivot(cornerTargetHandle); var basis = DirectionalDuplicationBasis(); var up = (basis * Vector3.up).normalized;
            var right = (basis * Vector3.right).normalized; var forward = (basis * Vector3.forward).normalized;
            var orbit = Quaternion.AngleAxis(CornerOrbitAngle(cornerTargetHandle), up); var objectRotation = Quaternion.AngleAxis(CornerObjectAngle(), up); var objectPivot = CornerSelectionCenter();
            var offset = CornerAnchorOffset(roots, pivot, orbit, objectRotation, objectPivot);
            if (!world.TryProjectedRangeAfterTurn(roots, pivot, orbit, objectRotation, objectPivot, right, out var minRight, out var maxRight) || !world.TryProjectedRangeAfterTurn(roots, pivot, orbit, objectRotation, objectPivot, forward, out var minForward, out var maxForward)) return null;
            float offsetRight = Vector3.Dot(offset, right), offsetForward = Vector3.Dot(offset, forward);
            minRight += offsetRight; maxRight += offsetRight; minForward += offsetForward; maxForward += offsetForward;
            var height = up * Vector3.Dot(pivot, up);
            var a = height + right * minRight + forward * minForward; var b = height + right * maxRight + forward * minForward;
            var c = height + right * maxRight + forward * maxForward; var d = height + right * minRight + forward * maxForward;
            var direction = CornerAxisDirection(); float length = Mathf.Max(Mathf.Max(Vector3.Dot(a - pivot, direction), Vector3.Dot(b - pivot, direction)), Mathf.Max(Vector3.Dot(c - pivot, direction), Vector3.Dot(d - pivot, direction)));
            length = Mathf.Max(length, Mathf.Max(maxRight - minRight, maxForward - minForward) * .6f);
            return new[] { a, b, c, d, a, pivot, pivot + direction * length };
        }
        private void CycleCornerSide(Vector3 pivot)
        {
            cornerSideTurn = (cornerSideTurn + 1) % 2; PreviewTool(() => BuildCornerPreview(pivot));
            ToolMessage(L.T(cornerSideTurn == 0 ? "#SIDE_RIGHT_AXIS" : "#SIDE_LEFT_AXIS"));
        }
        private void CycleCornerOrbit(Vector3 pivot)
        {
            cornerOrbitTurn = (cornerOrbitTurn + 1) % 4; PreviewTool(() => BuildCornerPreview(pivot));
            string[] messages = { "#AXIS_NORTH", "#AXIS_EAST", "#AXIS_SOUTH", "#AXIS_WEST" };
            ToolMessage(L.T(messages[cornerOrbitTurn]));
        }
        private void CycleCornerObjects(Vector3 pivot)
        {
            cornerObjectTurn = (cornerObjectTurn + 1) % 2; PreviewTool(() => BuildCornerPreview(pivot));
            ToolMessage(L.T(cornerObjectTurn == 0 ? "#OBJECTS_ORIGINAL_ORIENTATION" : "#OBJECTS_ROTATED_180_INSIDE_TARGET"));
        }
        private void DuplicateCorner(int handle)
        {
            var roots = CornerRoots(); if (roots.Length == 0) throw new InvalidOperationException(L.T("#SELECT_OBJECT_GROUP_0ED54C"));
            var pivot = CornerPivot(handle); var axis = (DirectionalDuplicationBasis() * Vector3.up).normalized; float orbitAngle = CornerOrbitAngle(handle); var objectPivot = CornerSelectionCenter();
            var orbit = Quaternion.AngleAxis(orbitAngle, axis); var objectRotation = Quaternion.AngleAxis(CornerObjectAngle(), axis); var anchorOffset = CornerAnchorOffset(roots, pivot, orbit, objectRotation, objectPivot); var copies = new List<string>();
            ToolEdit(d =>
            {
                foreach (var id in roots) ConstructionTools.Targets(d, id, false);
                ConstructionTools.Budget(d, MapSelection.Branches(d, roots).Count);
                foreach (var id in roots) copies.Add(ConstructionTools.CornerTurnCopy(d, world, id, pivot, axis, orbitAngle, CornerObjectAngle(), anchorOffset, objectPivot));
            }, L.T("#COPY_CREATED_CHOSEN_CORNER"));
            SetSelection(copies, copies.LastOrDefault()); Refresh();
        }
        private MapDocument BuildGridPreview()
        {
            if (selectedIds.Count > 1) throw new InvalidOperationException(L.T("#GROUP_SELECTION_CTRL_G_CREATE"));
            ConstructionTools.Targets(snapshot, selectedId, false);
            int columns = gridColumns.value, rows = gridRows.value;
            if (columns < 1 || rows < 1 || columns > 100 || rows > 100 || (long)columns * rows < 2)
                throw new ArgumentException(L.T("#CHOOSE_1_100_ROWS_COLUMNS"));
            ConstructionTools.Range(gridSpacingX.value, 0, 10000, L.T("#HORIZONTAL_STEP"));
            ConstructionTools.Range(gridSpacingY.value, 0, 10000, L.T("#VERTICAL_STEP"));
            int branchSize = MapHierarchy.Subtree(snapshot, selectedId).Count;
            ConstructionTools.Budget(snapshot, checked((columns * rows - 1) * branchSize + 1));
            GridAxes(out var columnAxis, out var rowAxis);
            var columnStep = columnAxis * gridSpacingX.value;
            var rowStep = rowAxis * gridSpacingY.value;
            var offsets = new List<Vector3>();
            for (int row = 0; row < rows; row++) for (int column = 0; column < columns; column++)
                if (row != 0 || column != 0) offsets.Add(columnStep * column + rowStep * row);
            return BuildToolPreview(offsets);
        }
        private MapDocument BuildToolPreview(IEnumerable<Vector3> offsets)
        {
            var roots = SelectionRoots();
            if (roots.Count == 0) throw new InvalidOperationException(L.T("#SELECT_OBJECT_GROUP_0ED54C"));
            var branches = MapSelection.Branches(snapshot, roots);
            var source = snapshot.objects.Where(item => branches.Contains(item.id) && !item.isGroup && MapHierarchy.IsEnabled(snapshot, item.id)).ToArray();
            if (source.Length == 0) throw new InvalidOperationException(L.T("#SELECT_OBJECT_GROUP_0ED54C"));
            var preview = new MapDocument();
            foreach (var offset in offsets) foreach (var item in source)
            {
                var pose = world.WorldPose(item.id); var copy = item.Copy();
                copy.id = Guid.NewGuid().ToString("N"); copy.parentId = "";
                copy.position = WorldView.ToData(WorldView.ToVector(pose.position) + offset);
                copy.rotation = pose.rotation; copy.scale = pose.scale; preview.objects.Add(copy);
            }
            return preview;
        }
        private void ToolMessage(string message) { toolResult.text = message; SetStatus(message); }
        private void ToolEdit(Action<MapDocument> edit, string message, Func<string> newSelection = null)
        {
            CommitInspectorEdit(); CancelPlacement();
            var document = session.Snapshot(); string before = codec.Encode(document);
            edit(document); document.Validate();
            if (codec.Encode(document) == before) { ToolMessage(L.T("#NO_CHANGE_CHECK_SELECTION_SURFACES")); return; }
            session.Edit(d => d.objects = document.objects.Select(o => o.Copy()).ToList());
            if (newSelection != null) selectedId = newSelection();
            RevealHierarchy(selectedId); Refresh(); RefreshToolReadout(); ToolMessage(message);
        }
        private void GroundSelection(bool surface)
        {
            ToolEdit(d => ConstructionTools.Drop(d, world, SelectionRoots().SelectMany(id=>ConstructionTools.Targets(d,id,false)).Distinct().ToArray(), surface, groundClearance.value), surface ? L.T("#SELECTION_PLACED_AVAILABLE_SUPPORTS") : L.T("#SELECTION_PLACED_Z_0"));
        }
        private void PlaceSelectionToTop()
        {
            ToolEdit(d => ConstructionTools.PlaceTop(d, world, SelectionRoots().SelectMany(id=>ConstructionTools.Targets(d,id,false)).Distinct().ToArray(), groundClearance.value), L.T("#SELECTION_ATTACHED_NEAREST_SUPPORTS"));
        }
        private Vector3 DirectionOffset(Vector3 direction)
        {
            var axis = duplicateLocal.value ? Quaternion.Euler(WorldView.ToVector(world.WorldPose(selectedId).rotation)) * direction : direction;
            ConstructionTools.Range(duplicateGap.value, 0, 10000, L.T("#GAP_STEP"));
            float size = duplicateAuto.value ? world.ProjectedSize(selectedId, axis) : 0;
            if(duplicateAuto.value && selectedIds.Count>1) {var bounds=world.CombinedBounds(SelectionRoots());if(bounds.HasValue)size=Vector3.Dot(bounds.Value.size,new Vector3(Mathf.Abs(axis.x),Mathf.Abs(axis.y),Mathf.Abs(axis.z)));}
            float step = size + duplicateGap.value;
            if (step < .001f) throw new ArgumentException(L.T("#ENTER_POSITIVE_STEP_SELECT_VISIBLE"));
            return axis * step;
        }
        private void DuplicateDirection(Vector3 direction)
        {
            CommitInspectorEdit(); var roots = SelectionRoots().ToArray(); if (roots.Length == 0) throw new InvalidOperationException(L.T("#SELECT_OBJECT_GROUP_0ED54C"));
            var copies = new List<string>(); Vector3 pivot = default, axis = default, offset;
            if (directionalMirrorCopy) DirectionalMirrorTransform(direction, out pivot, out axis, out offset); else offset = DirectionOffset(direction);
            ToolEdit(d =>
            {
                foreach (var id in roots) ConstructionTools.Targets(d, id, false);
                ConstructionTools.Budget(d, MapSelection.Branches(d, roots).Count);
                foreach (var id in roots) copies.Add(directionalMirrorCopy ? ConstructionTools.CornerTurnCopy(d, world, id, pivot, axis, 180, 0, offset) : ConstructionTools.Duplicate(d, world, id, offset));
            }, directionalMirrorCopy ? L.T("#MIRRORED_COPY_CREATED_EDGE_EDGE") : L.T("#COPY_CREATED_SELECTED_USE_BUTTON"));
            SetSelection(copies, copies.LastOrDefault()); Refresh();
        }        private void FitGridSpacing()
        {
            if(selectedIds.Count>1)throw new InvalidOperationException(L.T("#GROUP_SELECTION_CTRL_G_MEASURING"));
            CommitInspectorEdit(); ConstructionTools.Targets(snapshot, selectedId, false);
            GridAxes(out var columnAxis, out var rowAxis);
            gridSpacingX.value = world.ProjectedSize(selectedId, columnAxis);
            gridSpacingY.value = world.ProjectedSize(selectedId, rowAxis);
        }
        private void GridAxes(out Vector3 columnAxis, out Vector3 rowAxis)
        {
            var rotation = Quaternion.Euler(WorldView.ToVector(world.WorldPose(selectedId).rotation));
            columnAxis = rotation * Vector3.right;
            rowAxis = rotation * (gridPlane.index == 0 ? Vector3.forward : Vector3.up);
        }
        private void GridSelection()
        {
            if(selectedIds.Count>1)throw new InvalidOperationException(L.T("#GROUP_SELECTION_CTRL_G_CREATE"));
            string folder = null;
            ToolEdit(d => {
                ConstructionTools.Targets(d, selectedId, false);
                ConstructionTools.Range(gridSpacingX.value, 0, 10000, L.T("#HORIZONTAL_STEP")); ConstructionTools.Range(gridSpacingY.value, 0, 10000, L.T("#VERTICAL_STEP"));
                GridAxes(out var columnAxis, out var rowAxis);
                folder = ConstructionTools.Grid(d, world, selectedId, gridColumns.value, gridRows.value, columnAxis * gridSpacingX.value, rowAxis * gridSpacingY.value);
            }, L.T("#GRID_CREATED_GROUP_ORIGINAL_FIRST"), () => folder);
            gridPreviewBlockedSelection = folder; automaticGridKey = null; world.ClearToolPreview();
        }
        private void RefreshAutomaticGridPreview()
        {
            bool active = ConstructionToolActive("grid");
            if (!active || selectedId == null || selectedId == gridPreviewBlockedSelection)
            {
                if (automaticGridKey != null) world.ClearToolPreview();
                automaticGridKey = null; return;
            }
            string key = session.Revision + "|" + string.Join(",", selectedIds.OrderBy(id=>id)) + "|" + gridColumns.value + "|" + gridRows.value + "|" + gridPlane.index + "|" + gridSpacingX.value + "|" + gridSpacingY.value;
            if (key == automaticGridKey) return;
            try { world.PreviewTool(BuildGridPreview()); automaticGridKey = key; }
            catch { world.ClearToolPreview(); automaticGridKey = null; }
        }
        private void RandomizeSelection(bool rotation)
        {
            ToolEdit(d => ConstructionTools.Randomize(d, world, SelectionRoots().SelectMany(id=>ConstructionTools.Targets(d,id,false)).Distinct().ToArray(), rotation, rotation ? -rotationMax.value : scaleMin.value, rotation ? -rotationMin.value : scaleMax.value, toolRandom), rotation ? L.T("#Z_ROTATIONS_APPLIED") : L.T("#SCALES_RANDOMIZED"));
        }
        private void AdjustSelection(string kind)
        {
            ToolEdit(d => {
                foreach(var id in SelectionRoots()) { ConstructionTools.Targets(d,id,false); var item = d.objects.Single(o=>o.id==id);
                if (kind == "rotation") item.rotation = default;
                else if (kind == "scale") item.scale = new Float3(1, 1, 1);
                else { var pose = world.WorldPose(id); var p = pose.position;
                    world.StoreWorldPose(item, new Vector3(MapSession.Snap(p.x, moveSnap), MapSession.Snap(p.y, moveSnap), MapSession.Snap(p.z, moveSnap)), WorldView.ToVector(pose.rotation)); } }
            }, L.T("#TRANSFORM_ADJUSTED"));
        }
        private void RepositionSelectedGroupPivot(GroupPivotAnchor anchor)
        {
            if (selectedIds.Count != 1) throw new InvalidOperationException(L.T("#SELECT_SINGLE_FOLDER_PIVOT"));
            RepositionGroupPivot(selectedId, anchor, null);
        }
        private void RepositionGroupPivot(string groupId, GroupPivotAnchor anchor, IReadOnlyList<string> referenceIds)
        {
            var selected = snapshot.objects.Find(item => item.id == groupId);
            if (selected?.isGroup != true) throw new InvalidOperationException(L.T("#SELECT_SINGLE_FOLDER_PIVOT"));
            Bounds? bounds = referenceIds != null && referenceIds.Count > 0
                ? world.CombinedBounds(referenceIds)
                : world.GeometryBounds(groupId);
            if (!bounds.HasValue) throw new InvalidOperationException(L.T("#NO_ACTIVE_GEOMETRY"));
            Vector3 target = ConstructionTools.PivotTarget(bounds.Value, anchor);
            ToolEdit(document => ConstructionTools.RepositionGroupPivot(document, world, groupId, target), L.T("#PIVOT_REPOSITIONED"));
        }
        private void RefreshToolReadout()
        {
            if (toolsPanel == null || !toolsOpen || snapshot == null) return;
            RefreshParameterTransferSource();
            var item = snapshot.objects.Find(o => o.id == selectedId);
            bool enabled = item != null && MapHierarchy.IsEnabled(snapshot, item.id);
            foreach (var button in selectionTools) button.SetEnabled(enabled);
            bool folderPivotEnabled = enabled && selectedIds.Count == 1 && item.isGroup && string.IsNullOrEmpty(item.customType) && world.GeometryBounds(item.id).HasValue;
            foreach (var button in folderPivotTools) button.SetEnabled(folderPivotEnabled);
            toolSelection.text = selectedIds.Count>1 ? selectedIds.Count+L.T("#SELECTED_ITEMS") : item == null ? L.T("#NO_SELECTION") : item.displayName + (item.isGroup ? L.T("#GROUP") : "");
            if (item == null) { toolInfo.text = ""; measureInfo.text = L.T("#SELECT_END_OBJECT"); return; }
            var pose = world.WorldPose(item.id); var bounds = world.GeometryBounds(item.id);
            toolInfo.text = (item.isGroup ? L.T("#GROUP_34CA0E") : L.T("#MODEL") + (string.IsNullOrEmpty(item.gameModelPath) ? item.assetId : item.gameModelPath)) +
                L.T("#WORLD_APEX_PIVOT_U") + FormatVector(ApexDisplay.GamePosition(WorldView.ToVector(pose.position),snapshot.originOffset)) + L.T("#APEX_ANGLES_P_Y_R") + FormatVector(ApexDisplay.Angles(WorldView.ToVector(pose.rotation))) +
                (bounds.HasValue ? L.T("#APEX_DIMENSIONS_X_Y_Z") + FormatVector(ApexDisplay.Position(bounds.Value.size)) : L.T("#NO_ACTIVE_GEOMETRY"));
            var from = snapshot.objects.Find(o => o.id == measureStartId);
            if (from == null) { measureStartId = null; measureInfo.text = L.T("#CHOOSE_START_SELECT_END"); }
            else { var delta = WorldView.ToVector(pose.position) - WorldView.ToVector(world.WorldPose(from.id).position);
                var apexDelta = ApexDisplay.Position(delta);
                var measuredDelta = new Vector3(measureAxisX.value ? apexDelta.x : 0, measureAxisY.value ? apexDelta.y : 0, measureAxisZ.value ? apexDelta.z : 0);
                string axes = string.Join(" / ", new[] { measureAxisX.value ? "X" : "", measureAxisY.value ? "Y" : "", measureAxisZ.value ? "Z" : "" }.Where(axis => axis != ""));
                if (axes == "") axes = L.T("#NO_AXIS");
                measureInfo.text = from.displayName + " → " + item.displayName + L.T("#DISTANCE_BETWEEN_PIVOTS") + measuredDelta.magnitude.ToString("0.###") + L.T("#APEX_U_DELTA_X_Y") + FormatVector(apexDelta) + L.F("#MEASURED_AXES_ARG0", axes); }
        }
        private static string FormatNumber(float value) => value.ToString("0.0##", CultureInfo.InvariantCulture);
        private static string FormatVector(Vector3 v) => "< " + FormatNumber(v.x) + ", " + FormatNumber(v.y) + ", " + FormatNumber(v.z) + " >";
    }
}
