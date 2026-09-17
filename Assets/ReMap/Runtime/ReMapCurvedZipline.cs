using ReMap.Standalone.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        internal static MapObject[] CreateDefaultCurvedZiplineObjects(Vector3 pivot, string parent = "")
        {
            var zipline = new MapObject {
                assetId = "custom:curved-zipline", displayName = L.T("#CURVED_ZIPLINE"),
                isGroup = true, customType = "curved-zipline", parentId = parent ?? "",
                position = WorldView.ToData(pivot), curvedZiplineSegments = 8,
                ziplineWidth = 2f, ziplineSpeed = 1f
            };
            return new[] {
                zipline,
                CreateCurvedZiplinePoint(zipline.id, 0, Vector3.zero),
                CreateCurvedZiplinePoint(zipline.id, 1,
                    ApexDisplay.UnityPosition(new Vector3(300f, 100f, 80f))),
                CreateCurvedZiplinePoint(zipline.id, 2,
                    ApexDisplay.UnityPosition(new Vector3(600f, 0f, 0f)))
            };
        }

        private static MapObject CreateCurvedZiplinePoint(string parentId, int index, Vector3 position) =>
            new MapObject {
                assetId = "custom:curved-zipline-point:" + index.ToString(CultureInfo.InvariantCulture),
                displayName = L.F("#CONTROL_POINT_ARG0", index + 1), isGroup = true,
                customType = "curved-zipline-point", customRole = index.ToString(CultureInfo.InvariantCulture),
                parentId = parentId, position = WorldView.ToData(position), customProfile = "none",
                ziplineArmHeight = ReMapZiplineProfiles.DefaultArmHeightApex
            };

        private void InsertCurvedZipline(Vector3 pivot, string parent = "")
        {
            var objects = CreateDefaultCurvedZiplineObjects(pivot, parent);
            session.Edit(document => document.objects.AddRange(objects));
            selectedId = objects[0].id;
            RevealHierarchy(selectedId);
            Refresh();
            SetStatus(L.T("#CURVED_ZIPLINE_CREATED"));
        }

        private MapObject[] CurvedZiplinePoints(MapDocument document, string ziplineId) =>
            document.objects.Where(candidate => candidate.parentId == ziplineId &&
                candidate.customType == "curved-zipline-point")
                .OrderBy(candidate => int.TryParse(candidate.customRole, out int index) ? index : int.MaxValue)
                .ToArray();

        private void AddCurvedZiplinePoint(string ziplineId)
        {
            session.Edit(document => {
                var points = CurvedZiplinePoints(document, ziplineId);
                Vector3 last = WorldView.ToVector(points[points.Length - 1].position);
                Vector3 previous = points.Length > 1
                    ? WorldView.ToVector(points[points.Length - 2].position)
                    : last - Vector3.right * 5f;
                Vector3 position = last + (last - previous);
                document.objects.Add(CreateCurvedZiplinePoint(ziplineId, points.Length, position));
            });
            Refresh();
        }

        private void RemoveCurvedZiplinePoint(string ziplineId)
        {
            session.Edit(document => {
                var points = CurvedZiplinePoints(document, ziplineId);
                if (points.Length > 2)
                {
                    var removed = MapHierarchy.Subtree(document, points[points.Length - 1].id);
                    document.objects.RemoveAll(candidate => removed.Contains(candidate.id));
                }
            });
            selectedId = ziplineId;
            Refresh();
        }

        private static void NormalizeCurvedZiplinePoints(MapDocument document, string ziplineId)
        {
            var points = document.objects.Where(candidate => candidate.parentId == ziplineId &&
                candidate.customType == "curved-zipline-point")
                .OrderBy(candidate => int.TryParse(candidate.customRole, out int index) ? index : int.MaxValue)
                .ToArray();
            for (int index = 0; index < points.Length; index++)
            {
                points[index].customRole = index.ToString(CultureInfo.InvariantCulture);
                points[index].assetId = "custom:curved-zipline-point:" + points[index].customRole;
                points[index].displayName = L.F("#CONTROL_POINT_ARG0", index + 1);
            }
        }

        private void SyncCurvedZiplineSupport(MapDocument document, MapObject point)
        {
            document.objects.RemoveAll(candidate => candidate.parentId == point.id &&
                candidate.customType == "curved-zipline-component");
            var profile = ReMapZiplineProfiles.Find(point.customProfile);
            point.customProfile = profile.Id;

            void AddComponent(string role, string label, string modelPath,
                Vector3 position, Vector3 rotation)
            {
                var record = ZiplineModelRecord(modelPath);
                document.objects.Add(new MapObject {
                    assetId = record?.Id ?? "custom:curved-zipline-component:" + role,
                    displayName = L.T(label), parentId = point.id,
                    customType = "curved-zipline-component", customRole = role,
                    gameModelPath = modelPath, position = WorldView.ToData(position),
                    rotation = WorldView.ToData(rotation), isGroup = record == null,
                    commonAsset = record?.IsCommon ?? false,
                    availableMaps = record?.origins.Select(origin => origin.mapId)
                        .Where(map => map != "").Distinct().ToList() ?? new List<string>()
                });
            }

            if (profile.HasSupport)
                AddComponent("support", "#ZIPLINE_SUPPORT", ReMapZiplineProfiles.SupportModelPath,
                    Vector3.zero, Vector3.zero);
            if (profile.HasArm)
                AddComponent("arm", "#ZIPLINE_ARM", ReMapZiplineProfiles.ArmModelPath,
                    ReMapZiplineProfiles.UnityOffset(ReMapZiplineProfiles.ArmOffsetApex(
                        profile, point.ziplineArmHeight)), ReMapZiplineProfiles.ArmRotationUnity());
        }

        private bool CurvedZiplineSupportNeedsSync(MapDocument document, MapObject point)
        {
            var components = document.objects.Where(candidate => candidate.parentId == point.id &&
                candidate.customType == "curved-zipline-component").ToArray();
            var profile = ReMapZiplineProfiles.Find(point.customProfile);
            int expectedCount = (profile.HasSupport ? 1 : 0) + (profile.HasArm ? 1 : 0);
            if (components.Length != expectedCount) return true;

            bool Current(string role, string modelPath, Vector3 position, Vector3 rotation)
            {
                var component = components.FirstOrDefault(candidate => candidate.customRole == role);
                if (component == null || !GameAssetIndex.SameModelPath(component.gameModelPath, modelPath))
                    return false;
                var record = ZiplineModelRecord(modelPath);
                bool poseMatches = Vector3.Distance(WorldView.ToVector(component.position), position) < .0001f &&
                    Quaternion.Angle(Quaternion.Euler(WorldView.ToVector(component.rotation)),
                        Quaternion.Euler(rotation)) < .01f;
                return component.assetId == (record?.Id ?? "custom:curved-zipline-component:" + role) &&
                    component.isGroup == (record == null) && poseMatches;
            }

            Vector3 armPosition = ReMapZiplineProfiles.UnityOffset(
                ReMapZiplineProfiles.ArmOffsetApex(profile, point.ziplineArmHeight));
            return profile.HasSupport && !Current("support", ReMapZiplineProfiles.SupportModelPath,
                    Vector3.zero, Vector3.zero) ||
                profile.HasArm && !Current("arm", ReMapZiplineProfiles.ArmModelPath,
                    armPosition, ReMapZiplineProfiles.ArmRotationUnity());
        }

        private void ApplyCurvedZiplineProfile(MapDocument document, MapObject point, string profileId)
        {
            point.customProfile = ReMapZiplineProfiles.Find(profileId).Id;
            SyncCurvedZiplineSupport(document, point);
        }

        private void BuildCurvedZiplineInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#CURVED_ZIPLINE"), "inspector-subsection-title"));
            var points = CurvedZiplinePoints(snapshot, item.id);
            section.Add(Label(L.F("#ARG0_CONTROL_POINTS", points.Length), "inspector-inline-help"));
            var actions = new VisualElement(); actions.AddToClassList("inspector-actions");
            actions.Add(Button(L.T("#ADD_POINT"), () => AddCurvedZiplinePoint(item.id)));
            var remove = Button(L.T("#REMOVE_LAST_POINT"), () => RemoveCurvedZiplinePoint(item.id));
            remove.SetEnabled(points.Length > 2); actions.Add(remove);
            actions.Add(Button(L.T("#SELECT_FIRST_POINT"), () => Select(points[0].id)));
            actions.Add(Button(L.T("#SELECT_LAST_POINT"), () => Select(points[points.Length - 1].id)));
            section.Add(actions);

            var smooth = CompactInspectorField(new IntegerField(L.T("#CURVE_SEGMENTS_PER_SPAN")) {
                value = item.curvedZiplineSegments, isDelayed = true
            });
            section.Add(smooth);
            smooth.RegisterValueChangedCallback(change => Run(() => {
                int value = Mathf.Clamp(change.newValue, 2, 32);
                if (value != change.newValue) smooth.SetValueWithoutNotify(value);
                session.Edit(document => document.objects.Find(candidate => candidate.id == item.id)
                    .curvedZiplineSegments = value);
                Refresh();
            }));

            var width = CompactInspectorField(new FloatField(L.T("#CABLE_WIDTH")) {
                value = item.ziplineWidth, isDelayed = true
            });
            section.Add(width);
            width.RegisterValueChangedCallback(change => Run(() => {
                float value = Mathf.Clamp(change.newValue, .1f, 32f);
                session.Edit(document => document.objects.Find(candidate => candidate.id == item.id)
                    .ziplineWidth = value);
                Refresh();
            }));

            var speed = CompactInspectorField(new FloatField(L.T("#SPEED_SCALE")) {
                value = item.ziplineSpeed, isDelayed = true
            });
            section.Add(speed);
            speed.RegisterValueChangedCallback(change => Run(() => {
                float value = Mathf.Clamp(change.newValue, .1f, 10f);
                session.Edit(document => document.objects.Find(candidate => candidate.id == item.id)
                    .ziplineSpeed = value);
                Refresh();
            }));
        }

        private void BuildCurvedZiplinePointInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#CURVED_ZIPLINE_CONTROL_POINT"), "inspector-subsection-title"));
            section.Add(Label(L.T("#MOVE_CONTROL_POINT_HELP"), "inspector-inline-help"));
            AddZiplinePointPositionLock(item, section);
            var current = ReMapZiplineProfiles.Find(item.customProfile);
            var profiles = ReMapZiplineProfiles.All.Where(profile =>
                ReMapModelAvailability.ZiplineProfile(assetLibrary?.Records, Targets, profile)).ToList();
            var labels = profiles.Select(profile => L.T(profile.Label)).ToList();
            if (!profiles.Contains(current))
            {
                profiles.Insert(0, current);
                labels.Insert(0, L.F("#ARG0_UNAVAILABLE", L.T(current.Label)));
                section.Add(Label(L.T("#ZIPLINE_SUPPORT_MODELS_UNAVAILABLE"), "note"));
            }
            var model = CompactInspectorField(new DropdownField(L.T("#ZIPLINE_SUPPORT"), labels,
                Math.Max(0, profiles.IndexOf(current))));
            model.tooltip = L.T("#START_ARM_SUPPORT_NO_COLLISION_HELP");
            section.Add(model);
            model.RegisterValueChangedCallback(change => Run(() => {
                int index = Math.Max(0, labels.IndexOf(change.newValue));
                session.Edit(document => {
                    var point = document.objects.Find(candidate => candidate.id == item.id);
                    if (!ReMapModelAvailability.ZiplineProfile(assetLibrary?.Records, Targets,
                        profiles[index])) return;
                    ApplyCurvedZiplineProfile(document, point, profiles[index].Id);
                });
                Refresh();
                _ = PrepareZiplineModels();
            }));

            if (current.HasSupport)
            {
                var height = CompactInspectorField(new FloatField(L.T("#ARM_HEIGHT_APEX_U")) {
                    value = item.ziplineArmHeight, isDelayed = true
                });
                section.Add(height);
                height.RegisterValueChangedCallback(change => Run(() => {
                    float clampedHeight = Mathf.Clamp(change.newValue,
                        ReMapZiplineProfiles.MinArmHeightApex, ReMapZiplineProfiles.MaxArmHeightApex);
                    if (!Mathf.Approximately(change.newValue, clampedHeight))
                        height.SetValueWithoutNotify(clampedHeight);
                    session.Edit(document => {
                        var point = document.objects.Find(candidate => candidate.id == item.id);
                        point.ziplineArmHeight = clampedHeight;
                        SyncCurvedZiplineSupport(document, point);
                    });
                    snapshot = session.Snapshot();
                    world.Sync(snapshot, selectedId);
                    UpdateGizmoVisual();
                }));
            }
            section.Add(Button(L.T("#SELECT_CURVED_ZIPLINE"), () => Select(item.parentId)));
        }

        private static bool IsLockableZiplinePoint(MapObject item) => item != null &&
            (item.customType == "curved-zipline-point" || item.customType == "ziprail-point");

        private void AddZiplinePointPositionLock(MapObject item, VisualElement section)
        {
            var locked = CompactInspectorField(new Toggle(L.T("#LOCK_CONTROL_POINT_POSITION")) {
                value = item.positionLocked
            });
            locked.tooltip = L.T("#LOCK_CONTROL_POINT_POSITION_HELP");
            section.Add(locked);
            locked.RegisterValueChangedCallback(change => Run(() => {
                CommitInspectorEdit();
                session.Edit(document => {
                    var point = document.objects.Find(candidate => candidate.id == item.id);
                    if (IsLockableZiplinePoint(point)) point.positionLocked = change.newValue;
                });
                Refresh();
            }));
        }
    }
}
