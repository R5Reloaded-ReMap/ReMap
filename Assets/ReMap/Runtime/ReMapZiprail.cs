using ReMap.Standalone.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private bool preparingZiprailModels;

        internal static MapObject[] CreateDefaultZiprailObjects(Vector3 pivot, string parent = "")
        {
            var ziprail = new MapObject {
                assetId = "custom:ziprail", displayName = L.T("#ZIPRAIL"), isGroup = true,
                customType = "ziprail", parentId = parent ?? "", position = WorldView.ToData(pivot),
                curvedZiplineSegments = 8, ziplineWidth = 1.75f, ziplineSpeed = 1.75f,
                ziplineAutoDetachStart = 0f, ziplineAutoDetachEnd = 0f
            };
            return new[] {
                ziprail,
                CreateZiprailPoint(ziprail.id, 0, Vector3.zero, "support"),
                CreateZiprailPoint(ziprail.id, 1,
                    ApexDisplay.UnityPosition(new Vector3(300f, 100f, 80f)), "none"),
                CreateZiprailPoint(ziprail.id, 2,
                    ApexDisplay.UnityPosition(new Vector3(600f, 0f, 0f)), "arm")
            };
        }

        private static MapObject CreateZiprailPoint(string parentId, int index,
            Vector3 position, string profile = "none") => new MapObject {
                assetId = "custom:ziprail-point:" + index.ToString(CultureInfo.InvariantCulture),
                displayName = L.F("#CONTROL_POINT_ARG0", index + 1), isGroup = true,
                customType = "ziprail-point", customRole = index.ToString(CultureInfo.InvariantCulture),
                parentId = parentId, position = WorldView.ToData(position), customProfile = profile,
                ziplineArmHeight = ReMapZiprailProfiles.DefaultSupportHeightApex
            };

        private void InsertZiprail(Vector3 pivot, string parent = "")
        {
            if (!GameTargets.SupportsCustomType(snapshot.gameTarget, "ziprail"))
                throw new InvalidOperationException(L.T("#UNSUPPORTED_TARGET_GAME"));
            var objects = CreateDefaultZiprailObjects(pivot, parent);
            session.Edit(document => {
                document.objects.AddRange(objects);
                foreach (var point in objects.Where(candidate => candidate.customType == "ziprail-point"))
                {
                    var profile = ReMapZiprailProfiles.Find(point.customProfile);
                    if (!ReMapModelAvailability.ZiprailProfile(assetLibrary?.Records, Targets, profile))
                        point.customProfile = "none";
                    SyncZiprailComponents(document, point);
                }
            });
            selectedId = objects[0].id;
            RevealHierarchy(selectedId);
            Refresh();
            _ = PrepareZiprailModels();
            SetStatus(L.T("#ZIPRAIL_CREATED"));
        }

        private MapObject[] ZiprailPoints(MapDocument document, string ziprailId) =>
            document.objects.Where(candidate => candidate.parentId == ziprailId &&
                candidate.customType == "ziprail-point")
                .OrderBy(candidate => int.TryParse(candidate.customRole, out int index)
                    ? index : int.MaxValue).ToArray();

        private void AddZiprailPoint(string ziprailId)
        {
            session.Edit(document => {
                var points = ZiprailPoints(document, ziprailId);
                Vector3 last = WorldView.ToVector(points[points.Length - 1].position);
                Vector3 previous = points.Length > 1
                    ? WorldView.ToVector(points[points.Length - 2].position)
                    : last - Vector3.right * 5f;
                var point = CreateZiprailPoint(ziprailId, points.Length,
                    last + (last - previous));
                CopyControlPointMount(points[points.Length - 1], point);
                document.objects.Add(point);
                SyncAllZiprailComponents(document, ziprailId);
            });
            Refresh();
        }

        private void AddZiprailPointAfter(string pointId)
        {
            string createdId = null;
            session.Edit(document => {
                var selected = document.objects.Find(candidate => candidate.id == pointId);
                if (selected?.customType != "ziprail-point") return;
                var points = ZiprailPoints(document, selected.parentId);
                int selectedIndex = Array.FindIndex(points, candidate => candidate.id == pointId);
                if (selectedIndex < 0) return;
                int insertionIndex = selectedIndex + 1;
                Vector3 position = InsertedControlPointPosition(points, selectedIndex);
                string beforeSiblingId = insertionIndex < points.Length ? points[insertionIndex].id : null;
                for (int index = insertionIndex; index < points.Length; index++)
                    SetZiprailPointIndex(points[index], index + 1);
                var point = CreateZiprailPoint(selected.parentId, insertionIndex, position);
                CopyControlPointMount(selected, point);
                document.objects.Add(point);
                MapHierarchy.Reorder(document, point.id, selected.parentId, beforeSiblingId);
                NormalizeZiprailPoints(document, selected.parentId);
                SyncAllZiprailComponents(document, selected.parentId);
                createdId = point.id;
            });
            if (createdId == null) return;
            selectedId = createdId; RevealHierarchy(createdId); Refresh();
        }

        private void RemoveZiprailPoint(string ziprailId)
        {
            session.Edit(document => {
                var points = ZiprailPoints(document, ziprailId);
                if (points.Length <= 2) return;
                var removed = MapHierarchy.Subtree(document, points[points.Length - 1].id);
                document.objects.RemoveAll(candidate => removed.Contains(candidate.id));
                SyncAllZiprailComponents(document, ziprailId);
            });
            selectedId = ziprailId;
            Refresh();
        }

        private static void NormalizeZiprailPoints(MapDocument document, string ziprailId)
        {
            var points = document.objects.Where(candidate => candidate.parentId == ziprailId &&
                candidate.customType == "ziprail-point")
                .OrderBy(candidate => int.TryParse(candidate.customRole, out int index)
                    ? index : int.MaxValue).ToArray();
            for (int index = 0; index < points.Length; index++)
                SetZiprailPointIndex(points[index], index);
        }

        private static void SetZiprailPointIndex(MapObject point, int index)
        {
            point.customRole = index.ToString(CultureInfo.InvariantCulture);
            point.assetId = "custom:ziprail-point:" + point.customRole;
            point.displayName = L.F("#CONTROL_POINT_ARG0", index + 1);
        }

        private GameAssetRecord ZiprailModelRecord(string modelPath) =>
            assetLibrary?.Records.FirstOrDefault(candidate => GameAssetIndex.SameModelPath(
                candidate.modelPath, modelPath) && candidate.Supports(Targets));

        private MapObject CreateZiprailComponent(MapObject point,
            ReMapZiprailComponentDefinition definition)
        {
            var record = ZiprailModelRecord(definition.ModelPath);
            return new MapObject {
                assetId = record?.Id ?? "custom:ziprail-component:" + definition.Role,
                displayName = L.T(definition.Label), parentId = point.id,
                customType = "ziprail-component", customRole = definition.Role,
                gameModelPath = definition.ModelPath, position = WorldView.ToData(definition.Position),
                rotation = WorldView.ToData(definition.Rotation), isGroup = record == null,
                commonAsset = record?.IsCommon ?? false,
                availableMaps = record?.origins.Select(origin => origin.mapId)
                    .Where(map => map != "").Distinct().ToList() ?? new List<string>()
            };
        }

        private void SyncZiprailComponents(MapDocument document, MapObject point)
        {
            document.objects.RemoveAll(candidate => candidate.parentId == point.id &&
                candidate.customType == "ziprail-component");
            var profile = ReMapZiprailProfiles.Find(point.customProfile);
            point.customProfile = profile.Id;
            point.assetId = "custom:ziprail-point:" + point.customRole;
            point.gameModelPath = "";
            point.isGroup = true;
            point.commonAsset = false;
            point.availableMaps = new List<string>();
            foreach (var definition in ZiprailComponentDefinitions(document, point))
                document.objects.Add(CreateZiprailComponent(point, definition));
        }

        private void SyncAllZiprailComponents(MapDocument document, string ziprailId)
        {
            foreach (var point in ZiprailPoints(document, ziprailId))
                SyncZiprailComponents(document, point);
        }

        private IReadOnlyList<ReMapZiprailComponentDefinition> ZiprailComponentDefinitions(MapDocument document, MapObject point)
        {
            var profile = ReMapZiprailProfiles.Find(point.customProfile);
            var definitions = ReMapZiprailProfiles.Components(profile, point.ziplineArmHeight).ToList();
            var points = ZiprailPoints(document, point.parentId);
            bool endpoint = points.Length >= 2 && (points[0].id == point.id || points[points.Length - 1].id == point.id);
            if (endpoint && profile.HasArm) definitions.Add(ReMapZiprailProfiles.CordEnd(profile));
            return definitions;
        }

        private bool ZiprailComponentsNeedSync(MapDocument document, MapObject point)
        {
            var expected = ZiprailComponentDefinitions(document, point);
            var components = document.objects.Where(candidate => candidate.parentId == point.id &&
                candidate.customType == "ziprail-component").ToArray();
            if (components.Length != expected.Count) return true;
            foreach (var definition in expected)
            {
                var component = components.FirstOrDefault(candidate =>
                    candidate.customRole == definition.Role);
                if (component == null || !GameAssetIndex.SameModelPath(
                    component.gameModelPath, definition.ModelPath)) return true;
                var record = ZiprailModelRecord(definition.ModelPath);
                if (component.assetId != (record?.Id ?? "custom:ziprail-component:" + definition.Role) ||
                    component.isGroup != (record == null) ||
                    Vector3.Distance(WorldView.ToVector(component.position), definition.Position) >= .0001f ||
                    Quaternion.Angle(Quaternion.Euler(WorldView.ToVector(component.rotation)),
                        Quaternion.Euler(definition.Rotation)) >= .01f) return true;
            }
            return false;
        }

        private void ApplyZiprailProfile(MapDocument document, MapObject point, string profileId)
        {
            point.customProfile = ReMapZiprailProfiles.Find(profileId).Id;
            SyncZiprailComponents(document, point);
        }

        private async Task PrepareZiprailModels()
        {
            if (assetLibrary == null || preparingZiprailModels || snapshot == null ||
                GameTargets.Normalize(snapshot.gameTarget) != GameTargets.R5Flowstate) return;
            preparingZiprailModels = true;
            var prepared = new List<string>();
            try
            {
                foreach (string modelPath in ReMapZiprailProfiles.RequiredModelPaths)
                {
                    var record = ZiprailModelRecord(modelPath);
                    if (record == null || !record.Supports(Targets)) continue;
                    bool wasPrepared = world.models.IsPrepared(record.Id);
                    var entry = await PrepareDropEntry(record);
                    if (this == null || entry == null) return;
                    if (!wasPrepared) prepared.Add(record.Id);
                }
                if (this == null) return;
                var points = snapshot.objects.Where(candidate =>
                    candidate.customType == "ziprail-point").ToArray();
                bool needsSync = points.Any(point => ZiprailComponentsNeedSync(snapshot, point));
                if (needsSync)
                    session.Edit(document => {
                        foreach (var point in document.objects.Where(candidate =>
                            candidate.customType == "ziprail-point").ToArray())
                            if (ZiprailComponentsNeedSync(document, point))
                                SyncZiprailComponents(document, point);
                    });
                foreach (string assetId in prepared) world.Reload(assetId);
                if (needsSync || prepared.Count > 0) Refresh();
            }
            catch (Exception exception)
            {
                if (this != null) SetStatus(L.T("#ZIPLINE_SUPPORT_PREVIEW") + exception.Message);
            }
            finally { preparingZiprailModels = false; }
        }

        private void BuildZiprailInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#ZIPRAIL"), "inspector-subsection-title"));
            var points = ZiprailPoints(snapshot, item.id);
            section.Add(Label(L.F("#ARG0_CONTROL_POINTS", points.Length), "inspector-inline-help"));
            var actions = new VisualElement(); actions.AddToClassList("inspector-actions");
            actions.Add(Button(L.T("#ADD_POINT"), () => AddZiprailPoint(item.id)));
            var remove = Button(L.T("#REMOVE_LAST_POINT"), () => RemoveZiprailPoint(item.id));
            remove.SetEnabled(points.Length > 2); actions.Add(remove);
            actions.Add(Button(L.T("#SELECT_FIRST_POINT"), () => Select(points[0].id)));
            actions.Add(Button(L.T("#SELECT_LAST_POINT"), () => Select(points[points.Length - 1].id)));
            section.Add(actions);

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

            var detachStart = CompactInspectorField(new FloatField(L.T("#AUTO_DETACH_START_APEX_U")) {
                value = item.ziplineAutoDetachStart, isDelayed = true
            });
            section.Add(detachStart);
            detachStart.RegisterValueChangedCallback(change => Run(() => {
                float value = Mathf.Clamp(change.newValue, 0f, 65535f);
                session.Edit(document => document.objects.Find(candidate => candidate.id == item.id)
                    .ziplineAutoDetachStart = value);
                Refresh();
            }));

            var detachEnd = CompactInspectorField(new FloatField(L.T("#AUTO_DETACH_END_APEX_U")) {
                value = item.ziplineAutoDetachEnd, isDelayed = true
            });
            section.Add(detachEnd);
            detachEnd.RegisterValueChangedCallback(change => Run(() => {
                float value = Mathf.Clamp(change.newValue, 0f, 65535f);
                session.Edit(document => document.objects.Find(candidate => candidate.id == item.id)
                    .ziplineAutoDetachEnd = value);
                Refresh();
            }));
        }

        private void BuildZiprailPointInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#ZIPRAIL_CONTROL_POINT"), "inspector-subsection-title"));
            section.Add(Label(L.T("#MOVE_CONTROL_POINT_HELP"), "inspector-inline-help"));
            AddZiplinePointPositionLock(item, section);
            var current = ReMapZiprailProfiles.Find(item.customProfile);
            var profiles = ReMapZiprailProfiles.All.Where(profile =>
                ReMapModelAvailability.ZiprailProfile(assetLibrary?.Records, Targets, profile)).ToList();
            var labels = profiles.Select(profile => L.T(profile.Label)).ToList();
            if (!profiles.Contains(current))
            {
                profiles.Insert(0, current);
                labels.Insert(0, L.F("#ARG0_UNAVAILABLE", L.T(current.Label)));
                section.Add(Label(L.T("#ZIPLINE_SUPPORT_MODELS_UNAVAILABLE"), "note"));
            }
            var model = CompactInspectorField(new DropdownField(L.T("#ZIPRAIL_MOUNT_STYLE"), labels,
                Math.Max(0, profiles.IndexOf(current))));
            model.AddToClassList("ziprail-profile-field");
            model.tooltip = L.T("#ZIPRAIL_MOUNT_STYLE_HELP");
            section.Add(model);
            model.RegisterValueChangedCallback(change => Run(() => {
                int index = Math.Max(0, labels.IndexOf(change.newValue));
                session.Edit(document => {
                    var point = document.objects.Find(candidate => candidate.id == item.id);
                    if (!ReMapModelAvailability.ZiprailProfile(assetLibrary?.Records, Targets,
                        profiles[index])) return;
                    ApplyZiprailProfile(document, point, profiles[index].Id);
                });
                Refresh();
                _ = PrepareZiprailModels();
            }));

            if (current.HasSupport)
            {
                var height = CompactInspectorField(new FloatField(L.T("#ZIPRAIL_SUPPORT_HEIGHT_APEX_U")) {
                    value = item.ziplineArmHeight, isDelayed = false
                });
                section.Add(height);
                height.RegisterValueChangedCallback(change => Run(() => {
                    float value = Mathf.Clamp(change.newValue,
                        ReMapZiprailProfiles.MinSupportHeightApex,
                        ReMapZiprailProfiles.MaxSupportHeightApex);
                    if (!Mathf.Approximately(change.newValue, value))
                        height.SetValueWithoutNotify(value);
                    session.Edit(document => {
                        var point = document.objects.Find(candidate => candidate.id == item.id);
                        point.ziplineArmHeight = value;
                        SyncZiprailComponents(document, point);
                    });
                    snapshot = session.Snapshot();
                    world.Sync(snapshot, selectedId);
                    UpdateGizmoVisual();
                }));
            }
            section.Add(Button(L.T("#ADD_POINT_AFTER_SELECTED"), () =>
                AddZiprailPointAfter(item.id)));
            section.Add(Button(L.T("#SELECT_ZIPLINE"), () => Select(item.parentId)));
        }
    }
}
