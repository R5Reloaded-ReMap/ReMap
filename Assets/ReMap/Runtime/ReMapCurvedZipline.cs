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
                parentId = parentId, position = WorldView.ToData(position)
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
                if (points.Length > 2) document.objects.Remove(points[points.Length - 1]);
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

        private void SyncCurvedZiplineSupport(MapDocument document, MapObject zipline)
        {
            document.objects.RemoveAll(candidate => candidate.parentId == zipline.id &&
                candidate.customType == "curved-zipline-component");
            if (!zipline.curvedZiplineSupport) return;
            var record = ZiplineModelRecord(ReMapZiplineProfiles.ArmModelPath);
            document.objects.Add(new MapObject {
                assetId = record?.Id ?? "custom:curved-zipline-component:arm",
                displayName = L.T("#ZIPLINE_ARM"), parentId = zipline.id,
                customType = "curved-zipline-component", customRole = "arm",
                gameModelPath = ReMapZiplineProfiles.ArmModelPath,
                rotation = WorldView.ToData(ReMapZiplineProfiles.ArmRotationUnity()),
                isGroup = record == null, commonAsset = record?.IsCommon ?? false,
                availableMaps = record?.origins.Select(origin => origin.mapId)
                    .Where(map => map != "").Distinct().ToList() ?? new List<string>()
            });
        }

        private bool CurvedZiplineSupportNeedsSync(MapDocument document, MapObject zipline)
        {
            var components = document.objects.Where(candidate => candidate.parentId == zipline.id &&
                candidate.customType == "curved-zipline-component").ToArray();
            if (!zipline.curvedZiplineSupport) return components.Length != 0;
            if (components.Length != 1 || components[0].customRole != "arm" ||
                !GameAssetIndex.SameModelPath(components[0].gameModelPath,
                    ReMapZiplineProfiles.ArmModelPath)) return true;
            var record = ZiplineModelRecord(ReMapZiplineProfiles.ArmModelPath);
            return components[0].assetId != (record?.Id ?? "custom:curved-zipline-component:arm") ||
                components[0].isGroup != (record == null);
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

            var support = CompactInspectorField(new Toggle(L.T("#START_ARM_SUPPORT")) {
                value = item.curvedZiplineSupport
            });
            bool supportAvailable = ReMapModelAvailability.HasModel(assetLibrary?.Records,
                Targets, ReMapZiplineProfiles.ArmModelPath);
            support.SetEnabled(supportAvailable || item.curvedZiplineSupport);
            support.tooltip = L.T("#START_ARM_SUPPORT_NO_COLLISION_HELP");
            section.Add(support);
            if (!supportAvailable)
                section.Add(Label(L.F("#MODEL_NOT_IN_SELECTED_RPAKS",
                    ReMapZiplineProfiles.ArmModelPath), "note"));
            support.RegisterValueChangedCallback(change => Run(() => {
                if (change.newValue && !supportAvailable)
                {
                    support.SetValueWithoutNotify(false);
                    return;
                }
                session.Edit(document => {
                    var zipline = document.objects.Find(candidate => candidate.id == item.id);
                    zipline.curvedZiplineSupport = change.newValue;
                    SyncCurvedZiplineSupport(document, zipline);
                });
                Refresh();
                _ = PrepareZiplineModels();
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
            section.Add(Button(L.T("#SELECT_CURVED_ZIPLINE"), () => Select(item.parentId)));
        }
    }
}
