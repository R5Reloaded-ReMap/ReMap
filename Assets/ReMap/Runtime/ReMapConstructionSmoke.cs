using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.UIElements;
namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {

        private static void ToolCheck(bool pass, string message) { if (!pass) throw new Exception(message); }
        private async Task CheckConstructionTools()
        {
            await TreeFrames(12);
            var doc = new MapDocument { name = "Construction QA" };
            var parent = new MapObject { isGroup = true, displayName = "Module", position = new Float3(0,1,0), rotation = new Float3(0,30,0), scale = new Float3(2,3,2) };
            var nested = new MapObject { isGroup = true, parentId = parent.id, displayName = "Étage" };
            var block = new MapObject { parentId = nested.id, displayName = "Bloc A", position = new Float3(0,2,0) };
            var floor = new MapObject { displayName = "Support", position = new Float3(0,.5f,0), scale = new Float3(20,1,20) };
            var hidden = new MapObject { displayName = "Support masqué", position = new Float3(0,4,0), scale = new Float3(20,1,20), disabled = true };
            doc.objects.AddRange(new[] { parent, nested, block, floor, hidden }); session.Replace(doc); Refresh();
            Select(block.id); ShowTool("select-type"); selectionMatchChoice.index = 2; selectionScopeChoice.index = 0; SelectMatchingObjects();
            ToolCheck(selectedIds.SetEquals(new[] { block.id, floor.id, hidden.id }), "Selection by type did not find every regular prop in the scene.");
            Select(nested.id); selectionScopeChoice.index = 1; SelectMatchingObjects();
            ToolCheck(selectedIds.SetEquals(new[] { block.id }), "Selection by type did not limit matching to the selected branch.");
            var replacement = catalog.Entries.Single(entry => entry.Id == "demo:cylinder");
            replacementModels.Clear(); replacementModels.Add(replacement); replacementRandom.value = false;
            var originalPosition = block.position; block.allowMantle = false; block.realmId = 7; block.scriptProperties.Add(new ScriptProperty { name = "keepMe", value = "42" });
            session.Replace(doc); Refresh(); Select(nested.id); ShowTool("replace-model"); ReplaceSelectedModels();
            var replacedBlock = snapshot.objects.Single(item => item.id == block.id);
            ToolCheck(replacedBlock.assetId == replacement.Id && replacedBlock.displayName == replacement.Name, "Batch model replacement did not update the model.");
            ToolCheck(replacedBlock.position.x == originalPosition.x && replacedBlock.position.y == originalPosition.y && replacedBlock.position.z == originalPosition.z && !replacedBlock.allowMantle && replacedBlock.realmId == 7 && replacedBlock.scriptProperties.Single().name == "keepMe", "Batch model replacement lost transforms or object parameters.");
            session.Undo(); Refresh(); block = snapshot.objects.Single(item => item.id == block.id); doc = session.Snapshot();
            Select(block.id); ShowTool("transfer-parameters"); SetParameterTransferSource();
            var originalFloorPosition = floor.position; var originalFloorAsset = floor.assetId;
            Select(floor.id); transferObjectParameters.value = true; transferGameScripts.value = true; TransferParametersToSelection();
            var transferredFloor = snapshot.objects.Single(item => item.id == floor.id);
            ToolCheck(!transferredFloor.allowMantle && transferredFloor.realmId == 7 && transferredFloor.scriptProperties.Single().name == "keepMe", "Parameter transfer did not copy ReMap and Game Script values.");
            ToolCheck(transferredFloor.assetId == originalFloorAsset && transferredFloor.position.x == originalFloorPosition.x && transferredFloor.position.y == originalFloorPosition.y && transferredFloor.position.z == originalFloorPosition.z, "Parameter transfer changed the target model or transform.");
            session.Undo(); Refresh(); block = snapshot.objects.Single(item => item.id == block.id); floor = snapshot.objects.Single(item => item.id == floor.id); doc = session.Snapshot();
            InsertZipline(new Vector3(8, 10, 0)); string automaticZiplineId = selectedId;
            session.Edit(document => { var zipline = document.objects.Single(item => item.id == automaticZiplineId); zipline.ziplineMode = "vertical"; zipline.ziplineAutomaticEnd = true; zipline.ziplineEndOffset = 32f; });
            Refresh(); AlignAutomaticZiplineEnd(automaticZiplineId, true);
            var automaticZipline = snapshot.objects.Single(item => item.id == automaticZiplineId);
            var automaticEndPose = world.WorldPose(automaticZipline.ziplineEndId);
            ToolCheck(Mathf.Abs(automaticEndPose.position.y - (1f + 32f * ApexCoordinates.MetersPerUnit)) < .002f, "Vertical automatic zipline end did not detect the floor plus its Apex offset.");
            for (int i = 0; i < 3; i++) { session.Undo(); Refresh(); }
            block = snapshot.objects.Single(item => item.id == block.id); floor = snapshot.objects.Single(item => item.id == floor.id); doc = session.Snapshot();
            Select(block.id); ShowTool("ground"); await TreeFrames();
            UpdateGizmoVisual(); await TreeFrames();
            ToolCheck(gizmoVisual.HandleCenter(7).HasValue && gizmoVisual.HandleCenter(8).HasValue && gizmoVisual.HandleCenter(9).HasValue, "Surface placement gizmo is missing its down, up, or zero control.");
            ToolCheck(gizmoVisual.Hit(gizmoVisual.HandleCenter(7).Value) == 7 && gizmoVisual.Hit(gizmoVisual.HandleCenter(8).Value) == 8 && gizmoVisual.Hit(gizmoVisual.HandleCenter(9).Value) == 9, "Surface placement gizmo controls are not clickable.");
            var placementPivot = (Vector2)world.Camera.WorldToScreenPoint(gizmoVisual.Pivot);
            var placementAxis = ((Vector2)world.Camera.WorldToScreenPoint(gizmoVisual.Pivot + Vector3.up * gizmoVisual.WorldLength) - placementPivot).normalized;
            var placementDown = gizmoVisual.HandleCenter(7).Value - placementPivot; var placementUp = gizmoVisual.HandleCenter(8).Value - placementPivot;
            ToolCheck(Mathf.Abs(placementDown.x * placementAxis.y - placementDown.y * placementAxis.x) < .1f && Mathf.Abs(placementUp.x * placementAxis.y - placementUp.y * placementAxis.x) < .1f, "Surface placement arrows are offset from their vertical axis.");
            ToolCheck(gizmoVisual.Q<Label>("placement-zero")?.text == L.T("#GO_TO_ZERO"), "Surface placement zero control does not explain its action.");
            var toolActivation = root.Q<Button>("construction-tool-activation");
            ToolCheck(toolActivation != null && toolActivation.text == "● " + L.T("#TOOL_ACTIVE"), "Construction tool activation control is missing or does not show its active state.");
            TreeClick(toolActivation); UpdateGizmoVisual(); await TreeFrames();
            ToolCheck(!ConstructionToolActive("ground") && gizmoVisual.HandleCenter(7) == null && gizmoVisual.HandleCenter(0).HasValue && toolActivation.text == "○ " + L.T("#TOOL_PAUSED"), "Pausing a construction tool did not restore the regular transform gizmo.");
            TreeClick(toolActivation); UpdateGizmoVisual(); await TreeFrames();
            ToolCheck(ConstructionToolActive("ground") && gizmoVisual.HandleCenter(7).HasValue && toolActivation.text == "● " + L.T("#TOOL_ACTIVE"), "Resuming a construction tool did not restore its gizmo.");

            string original = codec.Encode(snapshot); groundClearance.value = .2f;
            TreeClick(root.Q<Button>("tool-ground")); await TreeFrames();
            ToolCheck(Mathf.Abs(world.GeometryBounds(block.id).Value.min.y - 1.2f) < .002f, "Grounding failed under rotated/scaled parent or used hidden support.");
            ToolCheck(Vector3.Distance(positionInput.value, WorldView.ToVector(world.LocalPose(block.id).position)) < .001f, "Grounding did not refresh inspector.");
            session.Undo(); Refresh(); ToolCheck(codec.Encode(snapshot) == original, "Grounding was not one undo.");
            session.Redo(); Refresh(); ToolCheck(Mathf.Abs(world.GeometryBounds(block.id).Value.min.y - 1.2f) < .002f, "Ground redo failed.");
            var raisedPose = world.WorldPose(block.id); session.Edit(d => world.StoreWorldPose(d.objects.Find(o => o.id == block.id), new Vector3(raisedPose.position.x, -3, raisedPose.position.z), WorldView.ToVector(raisedPose.rotation))); Refresh(); Select(block.id);
            groundClearance.value = 0; PlaceSelectionToTop(); await TreeFrames();
            ToolCheck(Mathf.Abs(world.GeometryBounds(block.id).Value.max.y) < .002f, "Top placement did not attach the model under the support above: " + world.GeometryBounds(block.id).Value.max.y);
            session.Undo(); Refresh();
            // No support, including when below Y=0: do not teleport the object upwards.
            session.Edit(d => d.objects.Find(o => o.id == block.id).position = new Float3(100,-5,100)); Refresh();
            string noFloor = codec.Encode(snapshot); GroundSelection(true);
            ToolCheck(codec.Encode(snapshot) == noFloor, "Missing support moved the object.");
            // Whole group grounds by active geometry and excludes all of its own descendants.
            session.Replace(doc); Refresh(); Select(parent.id); groundClearance.value = 0; GroundSelection(false);
            ToolCheck(Mathf.Abs(world.GeometryBounds(parent.id).Value.min.y) < .002f, "Group did not sit on plane Y=0.");
            session.Undo(); Refresh();
            var second = new MapObject { parentId = nested.id, displayName = "Bloc B", position = new Float3(3,4,0), rotation = new Float3(0,35,0) };
            session.Edit(d => d.objects.Add(second)); Refresh(); Select(parent.id);
            float childSpacing = world.WorldPose(second.id).position.y - world.WorldPose(block.id).position.y;
            GroundSelection(true);
            ToolCheck(Mathf.Abs(world.GeometryBounds(parent.id).Value.min.y - 1) < .002f, "The group did not move as one branch onto the support.");
            ToolCheck(Mathf.Abs(world.WorldPose(second.id).position.y - world.WorldPose(block.id).position.y - childSpacing) < .002f, "Grounding changed spacing inside the group.");
            // Moving a folder pivot must keep every descendant fixed in world space.
            string beforePivotEdit = codec.Encode(snapshot);
            var blockBeforePivot = WorldView.ToVector(world.WorldPose(block.id).position);
            var secondBeforePivot = WorldView.ToVector(world.WorldPose(second.id).position);
            var pivotBounds = world.GeometryBounds(parent.id).Value;
            ShowTool("pivot"); await TreeFrames();
            ToolCheck(root.Q<Button>("tool-pivot-bottom")?.enabledSelf == true && root.Q<Button>("tool-pivot-center") != null && root.Q<Button>("tool-pivot-top") != null, "Folder pivot controls are missing or disabled.");
            TreeClick(root.Q<Button>("tool-pivot-bottom")); await TreeFrames();
            var expectedBottomPivot = ConstructionTools.PivotTarget(pivotBounds, GroupPivotAnchor.BottomCenter);
            ToolCheck(Vector3.Distance(WorldView.ToVector(world.WorldPose(parent.id).position), expectedBottomPivot) < .002f, "Bottom-center folder pivot was not applied.");
            ToolCheck(Vector3.Distance(WorldView.ToVector(world.WorldPose(block.id).position), blockBeforePivot) < .002f && Vector3.Distance(WorldView.ToVector(world.WorldPose(second.id).position), secondBeforePivot) < .002f, "Moving the folder pivot moved descendant geometry.");
            session.Undo(); Refresh();
            ToolCheck(codec.Encode(snapshot) == beforePivotEdit, "Folder pivot relocation was not one undo step.");
            RepositionSelectedGroupPivot(GroupPivotAnchor.Center); await TreeFrames();
            ToolCheck(Vector3.Distance(WorldView.ToVector(world.WorldPose(parent.id).position), ConstructionTools.PivotTarget(pivotBounds, GroupPivotAnchor.Center)) < .002f, "Full-center folder pivot was not applied.");
            ToolCheck(Vector3.Distance(WorldView.ToVector(world.WorldPose(block.id).position), blockBeforePivot) < .002f, "Full-center pivot moved descendant geometry.");
            session.Undo(); Refresh();
            RepositionSelectedGroupPivot(GroupPivotAnchor.TopCenter); await TreeFrames();
            ToolCheck(Vector3.Distance(WorldView.ToVector(world.WorldPose(parent.id).position), ConstructionTools.PivotTarget(pivotBounds, GroupPivotAnchor.TopCenter)) < .002f, "Top-center folder pivot was not applied.");
            ToolCheck(Vector3.Distance(WorldView.ToVector(world.WorldPose(second.id).position), secondBeforePivot) < .002f, "Top-center pivot moved descendant geometry.");
            session.Undo(); Refresh(); Select(block.id); SelectModified(second.id, true, false);
            var selectedModelBounds = world.CombinedBounds(SelectionRoots()).Value;
            ShowHierarchyMenu(parent.id, new Vector2(20, 20)); await TreeFrames();
            var contextPivot = hierarchyMenu.Children().OfType<Button>().FirstOrDefault(button => button.text == L.T("#PIVOT_FROM_SELECTION_CENTER"));
            ToolCheck(contextPivot != null, "Folder context menu does not expose selection-based pivot actions.");
            TreeClick(contextPivot); await TreeFrames();
            var actualContextPivot = WorldView.ToVector(world.WorldPose(parent.id).position);
            ToolCheck(selectedId == parent.id && Vector3.Distance(actualContextPivot, selectedModelBounds.center) < .002f, "Context pivot did not use the selected models or select the target folder. selected=" + selectedId + " target=" + parent.id + " actual=" + actualContextPivot + " expected=" + selectedModelBounds.center);
            ToolCheck(Vector3.Distance(WorldView.ToVector(world.WorldPose(block.id).position), blockBeforePivot) < .002f && Vector3.Distance(WorldView.ToVector(world.WorldPose(second.id).position), secondBeforePivot) < .002f, "Context pivot moved selected model geometry.");
            MoveSelection(Vector3.right); await TreeFrames();
            ToolCheck(Vector3.Distance(WorldView.ToVector(world.WorldPose(block.id).position), blockBeforePivot + Vector3.right) < .002f && Vector3.Distance(WorldView.ToVector(world.WorldPose(second.id).position), secondBeforePivot + Vector3.right) < .002f, "Moving the recentered folder did not move all of its models.");
            session.Undo(); Refresh();
            // Random variations stay within configured bounds and keep the shape's proportions.
            var parentScaleBefore = snapshot.objects.Find(o => o.id == parent.id).scale;
            scaleMin.value = .8f; scaleMax.value = 1.2f; RandomizeSelection(false);
            var parentScaleAfter = snapshot.objects.Find(o => o.id == parent.id).scale;
            float scaleFactor = parentScaleAfter.x / parentScaleBefore.x;
            ToolCheck(scaleFactor >= .8f && scaleFactor <= 1.2f && Mathf.Abs(parentScaleAfter.y / parentScaleBefore.y - scaleFactor) < .0001f && Mathf.Abs(parentScaleAfter.z / parentScaleBefore.z - scaleFactor) < .0001f, "Random scale changed group proportions or exceeded range.");
            string beforeInvalid = codec.Encode(snapshot); scaleMin.value = 2; scaleMax.value = 1;
            try { RandomizeSelection(false); throw new Exception("Invalid random range was accepted."); } catch (ArgumentException) { }
            ToolCheck(codec.Encode(snapshot) == beforeInvalid, "Invalid variation changed map.");
            rotationMin.value = 45; rotationMax.value = 45; var beforeRotation = world.WorldPose(block.id);
            RandomizeSelection(true);
            ToolCheck(Quaternion.Angle(Quaternion.Euler(WorldView.ToVector(beforeRotation.rotation)) * Quaternion.Euler(0,-45,0), Quaternion.Euler(WorldView.ToVector(world.WorldPose(block.id).rotation))) < .01f, "Source yaw variation was not applied.");
            // Directional clone: use world axis projection and convert back through the scaled parent.
            Select(block.id); ShowTool("duplicate"); duplicateLocal.value = true; duplicateAuto.value = true; duplicateGap.value = .3f;
            var beforePosition = WorldView.ToVector(world.WorldPose(block.id).position); var offset = DirectionOffset(Vector3.right);
            UpdateGizmoVisual(); await TreeFrames(); var directionHandle = gizmoVisual.HandleCenter(11); var directionalMirrorHandle = gizmoVisual.HandleCenter(16);
            ToolCheck(root.Q<Button>("tool-duplicate-1") == null && root.Q("directional-gizmo-guide") != null, "Directional buttons were not replaced by the gizmo guide.");
            ToolCheck(directionHandle.HasValue && gizmoVisual.Hit(directionHandle.Value) == 11, "Positive X duplication arrow is not visible or clickable.");
            ToolCheck(directionalMirrorHandle.HasValue && gizmoVisual.Hit(directionalMirrorHandle.Value) == 16, "Directional symmetry control is not visible or clickable.");
            ToolCheck(gizmoVisual.Q<Label>("directional-mirror")?.text == L.T("#MIRROR"), "Directional mirror control must explain its action directly on the button.");
            PreviewTool(() => BuildDirectionalPreview(Vector3.right)); await TreeFrames();
            ToolCheck(world.HasToolPreview && world.ToolPreviewObjectCount == 1, "Directional hover did not display one hologram.");
            directionalTargetHandle = 11; directionalMirrorCopy = true; UpdateGizmoVisual(); PreviewTool(() => BuildDirectionalPreview(Vector3.right)); await TreeFrames();
            ToolCheck(world.ToolPreviewObjectCount == 1, "Mirror mode must preview one reflected copy on the chosen side.");
            DirectionalMirrorTransform(Vector3.right, out _, out var mirrorAxis, out _); var mirrorNormal = DirectionOffset(Vector3.right).normalized; world.TryProjectedRange(new[] { block.id }, mirrorNormal, out _, out var sourceMirrorEdge);
            var sourceMirrorRotation = Quaternion.Euler(WorldView.ToVector(world.WorldPose(block.id).rotation));
            int beforeDirectionalMirror = snapshot.objects.Count; DuplicateDirection(Vector3.right); await TreeFrames();
            var mirrorId = selectedId; var expectedMirrorRotation = Quaternion.AngleAxis(180, mirrorAxis) * sourceMirrorRotation;
            world.TryProjectedRange(new[] { mirrorId }, mirrorNormal, out var mirroredNearEdge, out _);
            ToolCheck(snapshot.objects.Count == beforeDirectionalMirror + 1 && selectedIds.Count == 1 && Quaternion.Angle(expectedMirrorRotation, Quaternion.Euler(WorldView.ToVector(world.WorldPose(mirrorId).rotation))) < .01f, "Mirror mode did not create, reverse, and exclusively select one copy.");
            ToolCheck(Mathf.Abs(mirroredNearEdge - sourceMirrorEdge - duplicateGap.value) < .002f, "Mirrored copy was not placed edge to edge on the chosen side.");
            session.Undo(); Refresh(); directionalMirrorCopy = false; Select(block.id); UpdateGizmoVisual(); PreviewTool(() => BuildDirectionalPreview(Vector3.right)); await TreeFrames();
            ToolCheck(world.ToolPreviewObjectCount == 1, "Disabling mirror mode did not restore the regular preview.");
            world.ClearToolPreview(); await TreeFrames(); ToolCheck(!world.HasToolPreview, "Directional hologram was not cleared after hover.");
            DuplicateDirection(GizmoOverlay.DuplicateDirection(11)); await TreeFrames();
            ToolCheck(selectedId != block.id && selectedIds.Count == 1 && Vector3.Distance(WorldView.ToVector(world.WorldPose(selectedId).position), beforePosition + offset) < .002f, "Directional clone lost parent conversion.");
            session.Undo(); Refresh(); Select(block.id); SelectModified(second.id, true, false); duplicateSymmetric.value = true;
            var blockBasis = Quaternion.Euler(WorldView.ToVector(world.WorldPose(block.id).rotation)); var secondBasis = Quaternion.Euler(WorldView.ToVector(world.WorldPose(second.id).rotation));
            ToolCheck(Quaternion.Angle(DirectionalDuplicationBasis(), blockBasis) < .01f && Quaternion.Angle(blockBasis, secondBasis) > 1, "Corner basis did not use the first selected object.");
            Select(second.id); SelectModified(block.id, true, false); ToolCheck(Quaternion.Angle(DirectionalDuplicationBasis(), secondBasis) < .01f, "Corner basis changed with the last selected object instead of the first.");
            Select(block.id); SelectModified(second.id, true, false); UpdateGizmoVisual(); await TreeFrames();
            var cornerHandle = gizmoVisual.HandleCenter(20);
            ToolCheck(cornerHandle.HasValue && gizmoVisual.Hit(cornerHandle.Value) == 20, "Corner handle is not visible or clickable.");
            var cornerSources = SelectionRoots().ToArray(); var cornerReference = DirectionalDuplicationReferenceId(); cornerTargetHandle = 20;
            var cornerPivot = CornerPivot(20); var objectAngle = CornerObjectAngle(); var initialCornerBasis = DirectionalDuplicationBasis();
            var initialCornerRight = (initialCornerBasis * Vector3.right).normalized; var initialCornerForward = (initialCornerBasis * Vector3.forward).normalized;
            world.TryProjectedRange(cornerSources, initialCornerRight, out var globalMinRight, out _); world.TryProjectedRange(cornerSources, initialCornerForward, out var globalMinForward, out _);
            ToolCheck(cornerReference == block.id && Mathf.Abs(Vector3.Dot(cornerPivot, initialCornerRight) - globalMinRight) < .002f && Mathf.Abs(Vector3.Dot(cornerPivot, initialCornerForward) - globalMinForward) < .002f, "Corner handles did not use the global bounds while keeping the first object's basis.");
            PreviewTool(() => BuildCornerPreview(cornerPivot)); UpdateGizmoVisual(); await TreeFrames();
            ToolCheck(world.HasToolPreview && world.ToolPreviewObjectCount == 2, "Corner mode did not preview one copy of each selected model.");
            ToolCheck(gizmoVisual.HandleCenter(24).HasValue && gizmoVisual.HandleCenter(25).HasValue && gizmoVisual.HandleCenter(26).HasValue && gizmoVisual.HandleCenter(27).HasValue && gizmoVisual.HandleCenter(28).HasValue, "Corner mode must expose three controls, the target rectangle, and its direction axis.");
            var sideControl = gizmoVisual.Q<Label>("corner-side"); var axisControl = gizmoVisual.Q<Label>("corner-axis"); var objectsControl = gizmoVisual.Q<Label>("corner-objects");
            ToolCheck(sideControl?.text == L.T("#CORNER_RIGHT") && axisControl?.text == L.T("#NORTH") && objectsControl?.text == L.T("#OBJECTS_0_DEG"), "Corner controls did not display their current side, cardinal axis, and object orientation.");
            for (int cornerHit = 20; cornerHit <= 23; cornerHit++)
            {
                var centre = gizmoVisual.HandleCenter(cornerHit); ToolCheck(centre.HasValue && gizmoVisual.Hit(centre.Value) == cornerHit, "Target rectangle blocked a corner handle.");
            }
            var north = CornerAxisDirection(); var rightSide = CornerSideDirection(); var rightGuide = CornerPlacementGuide(); var rightCenter = (rightGuide[0] + rightGuide[1] + rightGuide[2] + rightGuide[3]) * .25f;
            CycleCornerSide(cornerPivot); UpdateGizmoVisual();
            var leftGuide = CornerPlacementGuide(); var leftCenter = (leftGuide[0] + leftGuide[1] + leftGuide[2] + leftGuide[3]) * .25f;
            ToolCheck(Vector3.Angle(north, CornerAxisDirection()) < .01f && Vector3.Angle(rightGuide[6] - rightGuide[5], leftGuide[6] - leftGuide[5]) < .01f && Mathf.Abs(Vector3.Dot(rightCenter - cornerPivot, north) - Vector3.Dot(leftCenter - cornerPivot, north)) < .002f && Vector3.Dot(rightCenter - cornerPivot, rightSide) > 0 && Vector3.Dot(leftCenter - cornerPivot, rightSide) < 0 && sideControl.text == L.T("#CORNER_LEFT"), "Side control changed the axis instead of moving the same zone from right to left.");
            CycleCornerSide(cornerPivot);
            for (int directionTurn = 1; directionTurn <= 4; directionTurn++)
            {
                var previousAxis = CornerAxisDirection(); CycleCornerOrbit(cornerPivot); UpdateGizmoVisual();
                ToolCheck(Vector3.Angle(previousAxis, CornerAxisDirection()) > 89.9f && Vector3.Angle(previousAxis, CornerAxisDirection()) < 90.1f, "Axis control did not advance by one cardinal direction.");
            }
            ToolCheck(Vector3.Angle(north, CornerAxisDirection()) < .01f && axisControl.text == L.T("#NORTH"), "Axis control did not cycle through north, east, south, and west.");
            var originalPreview = BuildCornerPreview(cornerPivot); var originalFirst = WorldView.ToVector(originalPreview.objects[0].position); var originalSecond = WorldView.ToVector(originalPreview.objects[1].position);
            CycleCornerObjects(cornerPivot); UpdateGizmoVisual();
            var reversedPreview = BuildCornerPreview(cornerPivot); var reversedFirst = WorldView.ToVector(reversedPreview.objects[0].position); var reversedSecond = WorldView.ToVector(reversedPreview.objects[1].position);
            var originalSpacing = originalSecond - originalFirst; var reversedSpacing = reversedSecond - reversedFirst; var objectAxis = (DirectionalDuplicationBasis() * Vector3.up).normalized;
            var originalPlanarSpacing = originalSpacing - objectAxis * Vector3.Dot(originalSpacing, objectAxis); var reversedPlanarSpacing = reversedSpacing - objectAxis * Vector3.Dot(reversedSpacing, objectAxis);
            ToolCheck(Mathf.Abs(Mathf.Abs(Mathf.DeltaAngle(objectAngle, CornerObjectAngle())) - 180) < .001f && objectsControl.text == L.T("#OBJECTS_180_DEG") && world.ToolPreviewObjectCount == 2 && Vector3.Distance(reversedPlanarSpacing, -originalPlanarSpacing) < .002f && Mathf.Abs(Vector3.Dot(reversedSpacing - originalSpacing, objectAxis)) < .002f, "Object control did not reverse the selected models as one assembly inside the target zone.");
            var guide = CornerPlacementGuide(); var targetCenter = (guide[0] + guide[1] + guide[2] + guide[3]) * .25f;
            ToolCheck(guide.Length == 7 && Vector3.Dot(targetCenter - cornerPivot, CornerAxisDirection()) > 0 && Vector3.Dot(targetCenter - cornerPivot, CornerSideDirection()) > 0, "Target rectangle was not drawn on the selected side of the cardinal axis.");
            var cornerBasis = DirectionalDuplicationBasis(); var cornerRight = (cornerBasis * Vector3.right).normalized; var cornerForward = (cornerBasis * Vector3.forward).normalized;
            int beforeAnchoredCorner = snapshot.objects.Count; DuplicateCorner(20); await TreeFrames();
            var copiedRoots = SelectionRoots(); world.TryProjectedRange(copiedRoots, cornerRight, out var copyMinRight, out var copyMaxRight); world.TryProjectedRange(copiedRoots, cornerForward, out var copyMinForward, out var copyMaxForward);
            float pivotRight = Vector3.Dot(cornerPivot, cornerRight), pivotForward = Vector3.Dot(cornerPivot, cornerForward);
            float nearestCorner = Mathf.Min(Mathf.Min((new Vector2(copyMinRight - pivotRight, copyMinForward - pivotForward)).sqrMagnitude, (new Vector2(copyMaxRight - pivotRight, copyMinForward - pivotForward)).sqrMagnitude), Mathf.Min((new Vector2(copyMaxRight - pivotRight, copyMaxForward - pivotForward)).sqrMagnitude, (new Vector2(copyMinRight - pivotRight, copyMaxForward - pivotForward)).sqrMagnitude));
            ToolCheck(snapshot.objects.Count == beforeAnchoredCorner + 2 && selectedIds.Count == 2 && DirectionalDuplicationReferenceId() != null && nearestCorner < .00001f, "Rotated selection was not anchored by its global hitbox to the chosen point.");
            session.Undo(); Refresh(); Select(block.id); SelectModified(second.id, true, false); cornerObjectTurn = 0; cornerOrbitTurn = 0; cornerSideTurn = 0; cornerTargetHandle = 20; PreviewTool(() => BuildCornerPreview(CornerPivot(20)));
            int beforeCornerCount = snapshot.objects.Count;
            for (int turn = 1; turn <= 3; turn++)
            {
                DuplicateCorner(20); await TreeFrames();
                ToolCheck(snapshot.objects.Count == beforeCornerCount + turn * 2 && selectedIds.Count == 2 && DirectionalDuplicationReferenceId() != null && cornerSources.All(id => !selectedIds.Contains(id)), "Repeated corner copy was not selected or did not preserve its first-object reference.");
            }            for (int i = 0; i < 3; i++) { session.Undo(); Refresh(); }            duplicateSymmetric.value = false; Select(nested.id); string beforeGrid = codec.Encode(snapshot);
            gridColumns.value = 3; gridRows.value = 2; gridSpacingX.value = 4; gridSpacingY.value = 5; gridPlane.index = 0;
            int branchSize = MapHierarchy.Subtree(snapshot, nested.id).Count; var firstBefore = WorldView.ToVector(world.WorldPose(nested.id).position);
            var gridBasis = Quaternion.Euler(WorldView.ToVector(world.WorldPose(nested.id).rotation));
            ShowTool("grid"); RefreshAutomaticGridPreview(); await TreeFrames();
            var gridButton = root.Q<Button>("tool-grid");
            ToolCheck(world.HasToolPreview && world.ToolPreviewObjectCount == 10, "Grid tool did not automatically display every future model hologram.");
            ToolCheck(gridButton != null && gridButton.enabledInHierarchy, "Grid action is missing or disabled for an active group.");
            TreeClick(toolActivation); await TreeFrames();
            ToolCheck(!world.HasToolPreview && !ConstructionToolActive("grid"), "Pausing the grid tool did not clear its automatic preview.");
            TreeClick(toolActivation); await TreeFrames();
            ToolCheck(world.HasToolPreview && world.ToolPreviewObjectCount == 10 && ConstructionToolActive("grid"), "Resuming the grid tool did not restore its automatic preview.");
            GridSelection(); await TreeFrames();
            string folder = selectedId; var cells = snapshot.objects.Where(o => o.parentId == folder).ToArray();
            ToolCheck(cells.Length == 6 && MapHierarchy.Subtree(snapshot, folder).Count == 1 + 6 * branchSize, "Grid lost nested branches: cells=" + cells.Length + " branch=" + branchSize + " status=" + status.text);
            ToolCheck(cells.Any(o => o.id == nested.id) && Vector3.Distance(WorldView.ToVector(world.WorldPose(nested.id).position), firstBefore) < .001f, "Grid replaced or moved first cell.");
            foreach (var cell in cells) ToolCheck(MapHierarchy.Subtree(snapshot, cell.id).Count == branchSize, "Incomplete grid cell.");
            ToolCheck(Vector3.Distance(WorldView.ToVector(world.WorldPose(cells[1].id).position), firstBefore + gridBasis * Vector3.right * 4) < .002f, "Grid did not follow the source object's local axis.");
            string gridJson = codec.Encode(snapshot); var roundtrip = codec.Decode(gridJson); roundtrip.Validate();
            session.Undo(); Refresh(); ToolCheck(codec.Encode(snapshot) == beforeGrid, "Grid was not one undo.");
            session.Redo(); Refresh(); ToolCheck(codec.Encode(snapshot) == gridJson, "Grid redo changed identities.");
            // Measurement is live when the endpoint moves, and does not mutate the map.
            ShowTool("measure"); Select(cells[0].id); measureStartId = selectedId; Select(cells[1].id); RefreshToolReadout();
            string expectedDistance = (Vector3.Distance(WorldView.ToVector(world.WorldPose(cells[0].id).position), WorldView.ToVector(world.WorldPose(cells[1].id).position))/ApexCoordinates.MetersPerUnit).ToString("0.###")+" Apex u";
            ToolCheck(measureInfo.text.Contains(expectedDistance), "Distance measurement is incorrect. Expected " + expectedDistance + ", got: " + measureInfo.text);
            measureAxisY.value = false; measureAxisZ.value = false; RefreshToolReadout();
            var axisDelta = ApexDisplay.Position(WorldView.ToVector(world.WorldPose(cells[1].id).position) - WorldView.ToVector(world.WorldPose(cells[0].id).position));
            string expectedAxisDistance = Mathf.Abs(axisDelta.x).ToString("0.###") + " Apex u";
            ToolCheck(measureInfo.text.Contains(expectedAxisDistance) && measureInfo.text.Contains("X"), "Axis-filtered distance is incorrect. Expected " + expectedAxisDistance + ", got: " + measureInfo.text);
            measureAxisY.value = true; measureAxisZ.value = true; RefreshToolReadout();
            positionInput.Change(positionInput.value + Vector3.up); RefreshToolReadout();
            string movedDistance = (Vector3.Distance(WorldView.ToVector(world.WorldPose(cells[0].id).position), WorldView.ToVector(world.WorldPose(cells[1].id).position))/ApexCoordinates.MetersPerUnit).ToString("0.###")+" Apex u";
            ToolCheck(measureInfo.text.Contains(movedDistance), "Distance does not follow live transforms. Expected " + movedDistance + ", got: " + measureInfo.text); CommitInspectorEdit();
            session.Undo(); Refresh(); Select(folder); world.Focus(folder);
            ShowTool("grid");
            toolsPanel.scrollOffset = Vector2.zero;
            SetStatus("Outils vérifiés : sol, groupes, grille, duplication, variations, mesure et annulation.");
            await TreeFrames(5);
        }
        private IEnumerator ConstructionSmoke()
        {
            var check = CheckConstructionTools(); while (!check.IsCompleted) yield return null;
            if (check.IsFaulted) Debug.LogException(check.Exception);
            yield return new WaitForEndOfFrame(); var image = ScreenCapture.CaptureScreenshotAsTexture();
            if (image != null) { File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "construction-tools-preview.png"), image.EncodeToPNG()); Destroy(image); }
            if (!check.IsFaulted) Debug.Log("REMAP_CONSTRUCTION_TOOLS_OK");
            Application.Quit(check.IsFaulted ? 1 : 0);
        }
    }
}
