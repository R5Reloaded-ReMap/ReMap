using ReMap.Standalone.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private MapObject ZiplineEndpoint(MapObject zipline, bool start) =>
            snapshot.objects.Find(candidate =>
                candidate.id == (start ? zipline.ziplineStartId : zipline.ziplineEndId));

        private bool IsVerticalZiplineEnd(MapObject item)
        {
            if (item?.customType != "zipline-endpoint" || item.customRole != "end") return false;
            var zipline = snapshot?.objects.Find(candidate => candidate.id == item.parentId);
            return zipline?.customType == "zipline" && zipline.ziplineMode == "vertical";
        }


        private void AddZiplineSupportField(
            VisualElement section, MapObject endpoint, string label)
        {
            var profiles = ReMapZiplineProfiles.All;
            var labels = profiles.Select(profile => L.T(profile.Label)).ToList();
            int selected = Math.Max(0, Array.FindIndex(
                profiles, profile => profile.Id == endpoint.customProfile));
            var support = CompactInspectorField(new DropdownField(label, labels, selected));
            section.Add(support);
            support.RegisterValueChangedCallback(change => Run(() => {
                int index = Math.Max(0, labels.IndexOf(change.newValue));
                session.Edit(document => {
                    var edited = document.objects.Find(candidate => candidate.id == endpoint.id);
                    ApplyZiplineProfile(document, edited, profiles[index].Id);
                });
                Refresh();
                _ = PrepareZiplineModels();
            }));
            if (!profiles[selected].HasSupport) return;

            var height = CompactInspectorField(new FloatField(L.T("#ARM_HEIGHT_APEX_U")) {
                value = endpoint.ziplineArmHeight, isDelayed = true
            });
            section.Add(height);
            height.RegisterValueChangedCallback(change => Run(() => {
                float clampedHeight = Mathf.Clamp(change.newValue,
                    ReMapZiplineProfiles.MinArmHeightApex, ReMapZiplineProfiles.MaxArmHeightApex);
                if (!Mathf.Approximately(change.newValue, clampedHeight))
                    height.SetValueWithoutNotify(clampedHeight);
                session.Edit(document => {
                    var edited = document.objects.Find(candidate => candidate.id == endpoint.id);
                    UpdateZiplineArmHeight(document, edited, clampedHeight);
                });
                snapshot = session.Snapshot();
                world.Sync(snapshot, selectedId);
                UpdateGizmoVisual();
            }));
        }

        private void AddZiplineEndPlacementFields(
            VisualElement section, MapObject zipline, MapObject end)
        {
            bool vertical = zipline.ziplineMode == "vertical";
            if (!vertical)
                AddZiplineSupportField(section, end, L.T("#END_SUPPORT"));

            var lockEnd = CompactInspectorField(new Toggle(L.T("#LOCK_ZIPLINE_END")) {
                value = zipline.ziplineLockEnd, name = "zipline-lock-end"
            });
            lockEnd.tooltip = L.T("#LOCK_ZIPLINE_END_HELP");
            section.Add(lockEnd);
            lockEnd.RegisterValueChangedCallback(change => Run(() =>
                ChangeZipline(zipline.id, edited => edited.ziplineLockEnd = change.newValue)));

            if (!vertical) return;
            var automatic = CompactInspectorField(new Toggle(L.T("#AUTOMATIC_VERTICAL_END_SURFACE")) {
                value = zipline.ziplineAutomaticEnd, name = "zipline-automatic-end"
            });
            section.Add(automatic);
            automatic.RegisterValueChangedCallback(change => Run(() => {
                ChangeZipline(zipline.id, edited => {
                    if (change.newValue && Mathf.Approximately(edited.ziplineEndOffset, 0f))
                        edited.ziplineEndOffset = MapObject.DefaultZiplineEndOffsetApex;
                    edited.ziplineAutomaticEnd = change.newValue;
                });
                if (change.newValue) AlignAutomaticZiplineEnd(zipline.id, true);
            }));
            if (!zipline.ziplineAutomaticEnd) return;

            var offset = CompactInspectorField(new FloatField(L.T("#VERTICAL_GROUND_OFFSET_APEX_U")) {
                value = zipline.ziplineEndOffset, isDelayed = true, name = "zipline-end-offset"
            });
            section.Add(offset);
            offset.RegisterValueChangedCallback(change => Run(() => {
                ChangeZipline(zipline.id, edited =>
                    edited.ziplineEndOffset = Mathf.Clamp(change.newValue, -65535f, 65535f));
                AlignAutomaticZiplineEnd(zipline.id, true);
            }));
            section.Add(Button(L.T("#REFRESH_AUTOMATIC_END"),
                () => AlignAutomaticZiplineEnd(zipline.id, true)));
        }

        private void AddZiplineEndGameplayFields(VisualElement section, MapObject zipline)
        {
            var autoDetachEnd = CompactInspectorField(new FloatField(L.T("#AUTO_DETACH_END_APEX_U")) {
                value = zipline.ziplineAutoDetachEnd, isDelayed = true, name = "zipline-auto-detach-end"
            });
            section.Add(autoDetachEnd);
            autoDetachEnd.RegisterValueChangedCallback(change => Run(() =>
                ChangeZipline(zipline.id, edited => edited.ziplineAutoDetachEnd =
                    Mathf.Clamp(change.newValue, 0f, 65535f))));
            var detachOnSpawn = CompactInspectorField(new Toggle(L.T("#DETACH_END_ON_SPAWN")) {
                value = zipline.ziplineDetachEndOnSpawn, name = "zipline-detach-end-spawn"
            });
            section.Add(detachOnSpawn);
            detachOnSpawn.RegisterValueChangedCallback(change => Run(() =>
                ChangeZipline(zipline.id, edited => edited.ziplineDetachEndOnSpawn = change.newValue)));
            var detachOnUse = CompactInspectorField(new Toggle(L.T("#DETACH_END_ON_USE")) {
                value = zipline.ziplineDetachEndOnUse, name = "zipline-detach-end-use"
            });
            section.Add(detachOnUse);
            detachOnUse.RegisterValueChangedCallback(change => Run(() =>
                ChangeZipline(zipline.id, edited => edited.ziplineDetachEndOnUse = change.newValue)));
        }

        private void BuildZiplineInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#ZIPLINE"), "inspector-subsection-title"));
            var points = new VisualElement();
            points.AddToClassList("inspector-actions");
            points.Add(Button(L.T("#SELECT_END"), () => Select(item.ziplineEndId)));
            section.Add(points);


            var start = ZiplineEndpoint(item, true);
            var end = ZiplineEndpoint(item, false);
            if (start == null || end == null)
            {
                section.Add(Label(
                    L.T("#ZIPLINE_CONTAIN_ONE_START_ONE"), "note"));
                return;
            }

            bool vertical = item.ziplineMode == "vertical";
            var verticalField = CompactInspectorField(new Toggle(L.T("#VERTICAL_ZIPLINE")) { value = vertical });
            section.Add(verticalField);
            verticalField.RegisterValueChangedCallback(change => Run(() => {
                session.Edit(document => {
                    var zipline = document.objects.Find(candidate => candidate.id == item.id);
                    zipline.ziplineMode = change.newValue ? "vertical" : "horizontal";
                    if (change.newValue)
                    {
                        var endpoint = document.objects.Find(
                            candidate => candidate.id == zipline.ziplineEndId);
                        var startEndpoint = document.objects.Find(
                            candidate => candidate.id == zipline.ziplineStartId);
                        var position = WorldView.ToVector(endpoint.position);
                        var startPosition = WorldView.ToVector(startEndpoint.position);
                        position.x = startPosition.x;
                        position.z = startPosition.z;
                        if (Mathf.Abs(position.y - startPosition.y) < ApexCoordinates.MetersPerUnit)
                            position.y = startPosition.y + 400f * ApexCoordinates.MetersPerUnit;
                        endpoint.position = WorldView.ToData(position);
                        ApplyZiplineProfile(document, endpoint, "none");
                    }
                    else zipline.ziplineAutomaticEnd = false;
                });
                Refresh();
                if (change.newValue && item.ziplineAutomaticEnd)
                    AlignAutomaticZiplineEnd(item.id, true);
            }));

            AddZiplineSupportField(section, start, L.T("#START_SUPPORT"));
            AddZiplineEndPlacementFields(section, item, end);

            var lengthScale = CompactInspectorField(new FloatField(L.T("#LENGTH_SCALE_CABLE_GRAVITY")) {
                value = item.ziplineLengthScale, isDelayed = true
            });
            lengthScale.tooltip = L.T("Range 0-1.2; 0.9 is typical; values above 1 are exceptional.");
            section.Add(lengthScale);
            lengthScale.RegisterValueChangedCallback(change => Run(() =>
                ChangeZipline(item.id, zipline =>
                    zipline.ziplineLengthScale = Mathf.Clamp(change.newValue, 0f, 1.2f))));

            if (vertical)
            {
                var push = CompactInspectorField(new Toggle(L.T("#PUSH_OFF_LOCAL_X_DIRECTION")) {
                    value = item.ziplinePushOffInDirectionX
                });
                section.Add(push);
                push.RegisterValueChangedCallback(change => Run(() =>
                    ChangeZipline(item.id, zipline =>
                        zipline.ziplinePushOffInDirectionX = change.newValue)));
                var pushAngle = CompactInspectorField(new EndlessFloatField(L.T("#PUSH_OFF_ANGLE_DEGREES")) {
                    value = item.ziplinePushOffAngle, isDelayed = true
                });
                section.Add(pushAngle);
                pushAngle.RegisterValueChangedCallback(change => Run(() => {
                    float normalizedAngle = float.IsFinite(change.newValue)
                        ? GizmoPlacement.NormalizeAngle(change.newValue)
                        : 0f;
                    if (!Mathf.Approximately(change.newValue, normalizedAngle))
                        pushAngle.SetValueWithoutNotify(normalizedAngle);
                    session.Edit(document => document.objects.Find(candidate => candidate.id == item.id)
                        .ziplinePushOffAngle = normalizedAngle);
                    snapshot = session.Snapshot();
                    world.Sync(snapshot, selectedId);
                    UpdateGizmoVisual();
                }));
                pushAngle.tooltip = L.T("#ARROW_SCENE_SHOWS_PLAYER_S");
            }

            var width = CompactInspectorField(new FloatField(L.T("#CABLE_WIDTH")) {
                value = item.ziplineWidth, isDelayed = true
            });
            section.Add(width);
            width.RegisterValueChangedCallback(change => Run(() =>
                ChangeZipline(item.id, zipline =>
                    zipline.ziplineWidth = Mathf.Clamp(change.newValue, .1f, 32f))));
            var speed = CompactInspectorField(new FloatField(L.T("#SPEED_SCALE")) {
                value = item.ziplineSpeed, isDelayed = true
            });
            section.Add(speed);
            speed.RegisterValueChangedCallback(change => Run(() =>
                ChangeZipline(item.id, zipline =>
                    zipline.ziplineSpeed = Mathf.Clamp(change.newValue, .1f, 10f))));

            section.Add(Label(L.T("#ZIPLINE_GAMEPLAY"), "inspector-subsection-title"));
            var cableScale = CompactInspectorField(new FloatField(L.T("#CABLE_SCALE")) {
                value = item.ziplineScale, isDelayed = true
            });
            section.Add(cableScale);
            cableScale.RegisterValueChangedCallback(change => Run(() =>
                ChangeZipline(item.id, zipline =>
                    zipline.ziplineScale = Mathf.Clamp(change.newValue, .01f, 100f))));
            var fadeDistance = CompactInspectorField(new FloatField(L.T("#FADE_DISTANCE_APEX_U")) {
                value = item.ziplineFadeDistance, isDelayed = true
            });
            section.Add(fadeDistance);
            fadeDistance.RegisterValueChangedCallback(change => Run(() =>
                ChangeZipline(item.id, zipline =>
                    zipline.ziplineFadeDistance = Mathf.Clamp(change.newValue, -1f, 65535f))));
            var preserveVelocity = CompactInspectorField(new Toggle(L.T("#PRESERVE_VELOCITY")) {
                value = item.ziplinePreserveVelocity
            });
            section.Add(preserveVelocity);
            preserveVelocity.RegisterValueChangedCallback(change => Run(() =>
                ChangeZipline(item.id, zipline => zipline.ziplinePreserveVelocity = change.newValue)));
            var dropToBottom = CompactInspectorField(new Toggle(L.T("#DROP_TO_BOTTOM")) {
                value = item.ziplineDropToBottom
            });
            section.Add(dropToBottom);
            dropToBottom.RegisterValueChangedCallback(change => Run(() =>
                ChangeZipline(item.id, zipline => zipline.ziplineDropToBottom = change.newValue)));
            var autoDetachStart = CompactInspectorField(new FloatField(L.T("#AUTO_DETACH_START_APEX_U")) {
                value = item.ziplineAutoDetachStart, isDelayed = true
            });
            section.Add(autoDetachStart);
            autoDetachStart.RegisterValueChangedCallback(change => Run(() =>
                ChangeZipline(item.id, zipline => zipline.ziplineAutoDetachStart =
                    Mathf.Clamp(change.newValue, 0f, 65535f))));
            var restPoint = CompactInspectorField(new Toggle(L.T("#REST_POINTS")) {
                value = item.ziplineRestPoint
            });
            section.Add(restPoint);
            restPoint.RegisterValueChangedCallback(change => Run(() =>
                ChangeZipline(item.id, zipline => zipline.ziplineRestPoint = change.newValue)));
            AddZiplineEndGameplayFields(section, item);
            section.Add(Button(L.T("#SWAP_START_END"), () => SwapZiplineEnds(item.id)));
            bool componentsNeedSync = ZiplineComponentsNeedSync(snapshot, start) ||
                ZiplineComponentsNeedSync(snapshot, end);
            if (componentsNeedSync || ReMapZiplineProfiles.RequiredModelPaths.Any(path => {
                var record = assetLibrary?.Records.FirstOrDefault(candidate =>
                    GameAssetIndex.SameModelPath(candidate.modelPath, path));
                return record != null && record.Supports(Targets) &&
                    !world.models.IsPrepared(record.Id);
            })) _ = PrepareZiplineModels();
        }

        private void BuildZiplineEndpointInspector(MapObject item, VisualElement section)
        {
            bool endRole = item.customRole == "end";
            section.Add(Label(L.T(endRole ? "#ZIPLINE_END" : "#ZIPLINE_START"), "inspector-subsection-title"));
            string note = IsVerticalZiplineEnd(item)
                ? "#VERTICAL_ZIPLINE_END_MOVE_APEX"
                : "#MOVE_ROTATE_END_GIZMO_ZIPLINE";
            section.Add(Label(L.T(note), "note"));
            var zipline = snapshot.objects.Find(candidate =>
                candidate.id == item.parentId && candidate.customType == "zipline");
            if (endRole && zipline != null)
            {
                AddZiplineEndPlacementFields(section, zipline, item);
                section.Add(Label(L.T("#ZIPLINE_GAMEPLAY"), "inspector-subsection-title"));
                AddZiplineEndGameplayFields(section, zipline);
            }
            section.Add(Button(L.T("#SELECT_ZIPLINE"), () => Select(item.parentId)));
        }

        private void AlignAutomaticZiplineEnd(string ziplineId, bool reportFailure)
        {
            snapshot = session.Snapshot();
            world.Sync(snapshot, selectedId);
            var zipline = snapshot.objects.Find(candidate => candidate.id == ziplineId);
            if (zipline == null || zipline.ziplineMode != "vertical" || !zipline.ziplineAutomaticEnd) return;
            var start = snapshot.objects.Find(candidate => candidate.id == zipline.ziplineStartId);
            var end = snapshot.objects.Find(candidate => candidate.id == zipline.ziplineEndId);
            if (start == null || end == null) return;
            var excluded = MapHierarchy.Subtree(snapshot, zipline.id);
            bool vertical = zipline.ziplineMode == "vertical";
            Vector3 startCable = world.ZiplineCableAnchorPosition(start.id);
            bool found = vertical
                ? world.TryZiplineSurfaceBelow(startCable, excluded, out float surfaceHeight)
                : world.TryZiplineSurfaceBelow(end.id, excluded, out surfaceHeight);
            if (!found)
            {
                if (reportFailure)
                    SetStatus(L.T("#NO_MODEL_FOUND_BELOW_ZIPLINE"));
                return;
            }
            var pose = world.WorldPose(end.id);
            var position = WorldView.ToVector(pose.position);
            float wanted = surfaceHeight +
                zipline.ziplineEndOffset * ApexCoordinates.MetersPerUnit;
            if (vertical)
            {
                position.x = startCable.x;
                position.z = startCable.z;
            }
            position.y = wanted;
            if (Vector3.Distance(position, WorldView.ToVector(pose.position)) < .0001f) return;
            session.Edit(document => {
                var edited = document.objects.Find(candidate => candidate.id == end.id);
                world.StoreWorldPoseForParent(
                    edited, position, WorldView.ToVector(pose.rotation));
            });
            Refresh();
            if (reportFailure)
                SetStatus(L.T(vertical
                    ? "#VERTICAL_ZIPLINE_BOTTOM_ALIGNED"
                    : "#ZIPLINE_END_ALIGNED_MODEL_BELOW"));
        }

        private void AlignSelectedAutomaticZiplineEnd()
        {
            var selected = snapshot?.objects.Find(candidate => candidate.id == selectedId);
            string ziplineId = selected?.customType == "zipline" ? selected.id :
                selected?.customType == "zipline-endpoint" ? selected.parentId : null;
            var zipline = snapshot?.objects.Find(candidate => candidate.id == ziplineId);
            if (ziplineId != null && zipline?.ziplineLockEnd != true)
                AlignAutomaticZiplineEnd(ziplineId, false);
        }
    }
}
