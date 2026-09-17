using ReMap.Standalone.Core;
using System;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        internal static MapObject[] CreateDefaultCameraPathObjects(Vector3 pivot, string parent = "")
        {
            var path = new MapObject {
                assetId = "custom:camera-path", displayName = L.T("#CAMERA_PATH"), isGroup = true,
                customType = "camera-path", parentId = parent ?? "", position = WorldView.ToData(pivot),
                cameraPathTransitionTime = 8f, cameraPathFov = 120f
            };
            return new[] {
                path,
                CreateCameraPathPoint(path.id, 0, Vector3.zero),
                CreateCameraPathPoint(path.id, 1, ApexDisplay.UnityPosition(new Vector3(300, 0, 80))),
                CreateCameraPathPoint(path.id, 2, ApexDisplay.UnityPosition(new Vector3(600, 100, 0))),
                new MapObject {
                    assetId = "custom:camera-path-target", displayName = L.T("#CAMERA_PATH_TARGET"),
                    customType = "camera-path-target", parentId = path.id, isGroup = true,
                    position = WorldView.ToData(ApexDisplay.UnityPosition(new Vector3(300, 500, 0)))
                }
            };
        }

        private static MapObject CreateCameraPathPoint(string parentId, int index, Vector3 position) =>
            new MapObject {
                assetId = "custom:camera-path-point:" + index.ToString(CultureInfo.InvariantCulture),
                displayName = L.F("#CAMERA_POINT_ARG0", index + 1), customType = "camera-path-point",
                customRole = index.ToString(CultureInfo.InvariantCulture), parentId = parentId,
                isGroup = true, position = WorldView.ToData(position)
            };

        private MapObject[] CameraPathPoints(MapDocument document, string pathId) =>
            document.objects.Where(candidate => candidate.parentId == pathId &&
                candidate.customType == "camera-path-point")
                .OrderBy(candidate => int.TryParse(candidate.customRole, out int index) ? index : int.MaxValue)
                .ToArray();

        private void InsertCameraPath(Vector3 position, string parent = "")
        {
            var created = CreateDefaultCameraPathObjects(position, parent);
            session.Edit(document => document.objects.AddRange(created));
            selectedId = created[0].id; RevealHierarchy(selectedId); Refresh();
            SetStatus(L.T("#CAMERA_PATH_CREATED"));
        }

        private void AddCameraPathPoint(string pathId)
        {
            session.Edit(document => {
                var points = CameraPathPoints(document, pathId);
                Vector3 last = WorldView.ToVector(points[points.Length - 1].position);
                Vector3 previous = points.Length > 1 ? WorldView.ToVector(points[points.Length - 2].position) : last - Vector3.right * 5f;
                document.objects.Add(CreateCameraPathPoint(pathId, points.Length, last + last - previous));
            });
            Refresh();
        }

        private void RemoveCameraPathPoint(string pathId)
        {
            session.Edit(document => {
                var points = CameraPathPoints(document, pathId);
                if (points.Length <= 2) return;
                var removed = MapHierarchy.Subtree(document, points[points.Length - 1].id);
                document.objects.RemoveAll(candidate => removed.Contains(candidate.id));
            });
            selectedId = pathId; Refresh();
        }

        private void ApplyCameraPathSpacing(MapDocument document, MapObject path)
        {
            if (!path.cameraPathSpacingEnabled || path.cameraPathSpacing <= 0f) return;
            var points = CameraPathPoints(document, path.id);
            float spacing = path.cameraPathSpacing * ApexCoordinates.MetersPerUnit;
            for (int index = 1; index < points.Length; index++)
            {
                Vector3 previous = WorldView.ToVector(points[index - 1].position);
                Vector3 current = WorldView.ToVector(points[index].position);
                Vector3 direction = (current - previous).normalized;
                if (direction.sqrMagnitude < .000001f) direction = Vector3.right;
                points[index].position = WorldView.ToData(previous + direction * spacing);
            }
        }

        private void BuildCameraPathInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#CAMERA_PATH"), "inspector-subsection-title"));
            var points = CameraPathPoints(snapshot, item.id);
            section.Add(Label(L.F("#ARG0_CAMERA_POINTS", points.Length), "inspector-inline-help"));
            var actions = new VisualElement(); actions.AddToClassList("inspector-actions");
            actions.Add(Button(L.T("#ADD_POINT"), () => AddCameraPathPoint(item.id)));
            var remove = Button(L.T("#REMOVE_LAST_POINT"), () => RemoveCameraPathPoint(item.id));
            remove.SetEnabled(points.Length > 2); actions.Add(remove);
            actions.Add(Button(L.T("#SELECT_LAST_POINT"), () => Select(points[points.Length - 1].id)));
            section.Add(actions);

            var transition = CompactInspectorField(new FloatField(L.T("#CAMERA_PATH_TRANSITION_TIME")) { value = item.cameraPathTransitionTime, isDelayed = true });
            var fov = CompactInspectorField(new FloatField(L.T("#CAMERA_PATH_FOV")) { value = item.cameraPathFov, isDelayed = true });
            var track = CompactInspectorField(new Toggle(L.T("#CAMERA_PATH_TRACK_TARGET")) { value = item.cameraPathTrackTarget });
            var spacingEnabled = CompactInspectorField(new Toggle(L.T("#CAMERA_PATH_ENABLE_SPACING")) { value = item.cameraPathSpacingEnabled });
            var spacing = CompactInspectorField(new FloatField(L.T("#CAMERA_PATH_SPACING")) { value = item.cameraPathSpacing, isDelayed = true });
            section.Add(transition); section.Add(fov); section.Add(track); section.Add(spacingEnabled); section.Add(spacing);
            section.Add(Label(L.T("#CAMERA_PATH_HELP"), "note"));

            void Change(Action<MapObject> change, bool applySpacing = false)
            {
                CommitInspectorEdit();
                session.Edit(document => {
                    var edited = document.objects.Find(candidate => candidate.id == item.id);
                    change(edited); if (applySpacing) ApplyCameraPathSpacing(document, edited);
                });
                Refresh();
            }
            transition.RegisterValueChangedCallback(change => Change(edited => edited.cameraPathTransitionTime = Mathf.Clamp(change.newValue, .01f, 3600f)));
            fov.RegisterValueChangedCallback(change => Change(edited => edited.cameraPathFov = Mathf.Clamp(change.newValue, 1f, 179f)));
            track.RegisterValueChangedCallback(change => Change(edited => edited.cameraPathTrackTarget = change.newValue));
            spacingEnabled.RegisterValueChangedCallback(change => Change(edited => edited.cameraPathSpacingEnabled = change.newValue, true));
            spacing.RegisterValueChangedCallback(change => Change(edited => edited.cameraPathSpacing = Mathf.Clamp(change.newValue, 0f, 65535f), true));
        }

        private void BuildCameraPathNodeInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(item.customType == "camera-path-target" ? L.T("#CAMERA_PATH_TARGET") :
                L.T("#CAMERA_PATH_POINT"), "inspector-subsection-title"));
            section.Add(Label(item.customType == "camera-path-target" ? L.T("#CAMERA_PATH_TARGET_HELP") :
                L.T("#CAMERA_PATH_POINT_HELP"), "note"));
            section.Add(Button(L.T("#SELECT_CAMERA_PATH"), () => Select(item.parentId)));
        }
    }
}
