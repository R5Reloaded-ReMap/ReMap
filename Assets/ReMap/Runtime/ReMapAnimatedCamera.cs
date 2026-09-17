using ReMap.Standalone.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        internal const string AnimatedCameraBaseModelPath = "mdl/IMC_base/camera_imc_base_01.rmdl";
        internal const string AnimatedCameraHeadModelPath = "mdl/IMC_base/camera_imc_01.rmdl";
        private bool preparingAnimatedCameraModels;

        private GameAssetRecord AnimatedCameraModelRecord(string path) => assetLibrary?.Records.FirstOrDefault(candidate =>
            GameAssetIndex.SameModelPath(candidate.modelPath, path) && candidate.Supports(Targets));

        private bool AnimatedCameraModelsAvailable() => AnimatedCameraModelRecord(AnimatedCameraBaseModelPath) != null &&
            AnimatedCameraModelRecord(AnimatedCameraHeadModelPath) != null;

        private MapObject CreateAnimatedCameraComponent(MapObject camera, string role, string path,
            Vector3 position, Vector3 rotation, float scale)
        {
            var record = AnimatedCameraModelRecord(path);
            return new MapObject {
                assetId = record?.Id ?? "custom:animated-camera-component:" + role,
                displayName = role == "base" ? L.T("#ANIMATED_CAMERA_BASE") : L.T("#ANIMATED_CAMERA_HEAD"),
                customType = "animated-camera-component", customRole = role, parentId = camera.id,
                gameModelPath = path, position = WorldView.ToData(position), rotation = WorldView.ToData(rotation),
                scale = new Float3(scale, scale, scale), isGroup = record == null,
                commonAsset = record?.IsCommon ?? false,
                availableMaps = record?.origins.Select(origin => origin.mapId)
                    .Where(map => map != "").Distinct().ToList() ?? new List<string>()
            };
        }

        private void SyncAnimatedCameraComponents(MapDocument document, MapObject camera)
        {
            document.objects.RemoveAll(candidate => candidate.parentId == camera.id &&
                candidate.customType == "animated-camera-component");
            camera.assetId = "custom:animated-camera"; camera.gameModelPath = ""; camera.isGroup = true;
            camera.commonAsset = false; camera.availableMaps = new List<string>();
            document.objects.Add(CreateAnimatedCameraComponent(camera, "base", AnimatedCameraBaseModelPath,
                Vector3.zero, Vector3.zero, 1f));
            document.objects.Add(CreateAnimatedCameraComponent(camera, "head", AnimatedCameraHeadModelPath,
                ApexDisplay.UnityPosition(new Vector3(16, 0, 8)),
                ApexDisplay.UnityAngles(new Vector3(camera.animatedCameraAngleOffset, 0, 0)), 1f));
        }

        internal MapObject[] CreateAnimatedCamera(Vector3 position, string parent = "")
        {
            var camera = new MapObject {
                assetId = "custom:animated-camera", displayName = L.T("#ANIMATED_CAMERA"),
                customType = "animated-camera", parentId = parent ?? "", position = WorldView.ToData(position),
                isGroup = true, animatedCameraAngleOffset = 20f, animatedCameraMaxLeft = 20f,
                animatedCameraMaxRight = 40f, animatedCameraRotationTime = 4f,
                animatedCameraTransitionTime = 2f
            };
            var document = new MapDocument(); document.objects.Add(camera);
            SyncAnimatedCameraComponents(document, camera);
            return document.objects.ToArray();
        }

        private void InsertAnimatedCamera(Vector3 position, string parent = "")
        {
            var created = CreateAnimatedCamera(position, parent);
            session.Edit(document => document.objects.AddRange(created));
            selectedId = created[0].id; RevealHierarchy(selectedId); Refresh();
            _ = PrepareAnimatedCameraModels();
            SetStatus(L.T("#ANIMATED_CAMERA_CREATED"));
        }

        private void BuildAnimatedCameraInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#ANIMATED_CAMERA"), "inspector-subsection-title"));
            var offset = CompactInspectorField(new FloatField(L.T("#ANIMATED_CAMERA_ANGLE_OFFSET")) { value = item.animatedCameraAngleOffset, isDelayed = true });
            var left = CompactInspectorField(new FloatField(L.T("#ANIMATED_CAMERA_MAX_LEFT")) { value = item.animatedCameraMaxLeft, isDelayed = true });
            var right = CompactInspectorField(new FloatField(L.T("#ANIMATED_CAMERA_MAX_RIGHT")) { value = item.animatedCameraMaxRight, isDelayed = true });
            var rotation = CompactInspectorField(new FloatField(L.T("#ANIMATED_CAMERA_ROTATION_TIME")) { value = item.animatedCameraRotationTime, isDelayed = true });
            var transition = CompactInspectorField(new FloatField(L.T("#ANIMATED_CAMERA_TRANSITION_TIME")) { value = item.animatedCameraTransitionTime, isDelayed = true });
            section.Add(offset); section.Add(left); section.Add(right); section.Add(rotation); section.Add(transition);
            section.Add(Label(L.T("#ANIMATED_CAMERA_HELP"), "note"));

            void Change(Action<MapObject> change, bool sync = false)
            {
                CommitInspectorEdit();
                session.Edit(document => {
                    var edited = document.objects.Find(candidate => candidate.id == item.id);
                    change(edited); if (sync) SyncAnimatedCameraComponents(document, edited);
                });
                Refresh();
            }
            offset.RegisterValueChangedCallback(change => Change(edited => edited.animatedCameraAngleOffset = Mathf.Clamp(change.newValue, -360f, 360f), true));
            left.RegisterValueChangedCallback(change => Change(edited => edited.animatedCameraMaxLeft = Mathf.Clamp(change.newValue, 0f, 360f)));
            right.RegisterValueChangedCallback(change => Change(edited => edited.animatedCameraMaxRight = Mathf.Clamp(change.newValue, 0f, 360f)));
            rotation.RegisterValueChangedCallback(change => Change(edited => edited.animatedCameraRotationTime = Mathf.Clamp(change.newValue, .01f, 3600f)));
            transition.RegisterValueChangedCallback(change => Change(edited => edited.animatedCameraTransitionTime = Mathf.Clamp(change.newValue, 0f, 3600f)));
        }

        private bool AnimatedCameraComponentsNeedSync(MapDocument document, MapObject camera)
        {
            var components = document.objects.Where(candidate => candidate.parentId == camera.id &&
                candidate.customType == "animated-camera-component").ToArray();
            if (components.Length != 2) return true;
            var basePart = components.FirstOrDefault(candidate => candidate.customRole == "base");
            var head = components.FirstOrDefault(candidate => candidate.customRole == "head");
            if (basePart == null || head == null) return true;
            var baseRecord = AnimatedCameraModelRecord(AnimatedCameraBaseModelPath);
            var headRecord = AnimatedCameraModelRecord(AnimatedCameraHeadModelPath);
            Vector3 headPosition = ApexDisplay.UnityPosition(new Vector3(16, 0, 8));
            Vector3 headRotation = ApexDisplay.UnityAngles(new Vector3(camera.animatedCameraAngleOffset, 0, 0));
            return basePart.assetId != (baseRecord?.Id ?? "custom:animated-camera-component:base") ||
                head.assetId != (headRecord?.Id ?? "custom:animated-camera-component:head") ||
                Vector3.Distance(WorldView.ToVector(head.position), headPosition) > .0001f ||
                Quaternion.Angle(Quaternion.Euler(WorldView.ToVector(head.rotation)), Quaternion.Euler(headRotation)) > .01f;
        }

        private async Task PrepareAnimatedCameraModels()
        {
            if (assetLibrary == null || snapshot == null || preparingAnimatedCameraModels) return;
            preparingAnimatedCameraModels = true;
            var prepared = new List<string>();
            try
            {
                foreach (string path in new[] { AnimatedCameraBaseModelPath, AnimatedCameraHeadModelPath })
                {
                    var record = AnimatedCameraModelRecord(path);
                    if (record == null) return;
                    bool wasPrepared = world.models.IsPrepared(record.Id);
                    if (await PrepareDropEntry(record) == null || this == null) return;
                    if (!wasPrepared) prepared.Add(record.Id);
                }
                bool needsSync = snapshot.objects.Where(candidate => candidate.customType == "animated-camera")
                    .Any(camera => AnimatedCameraComponentsNeedSync(snapshot, camera));
                if (needsSync)
                    session.Edit(document => {
                        foreach (var camera in document.objects.Where(candidate => candidate.customType == "animated-camera").ToArray())
                            if (AnimatedCameraComponentsNeedSync(document, camera)) SyncAnimatedCameraComponents(document, camera);
                    });
                foreach (string assetId in prepared) world.Reload(assetId);
                if (needsSync || prepared.Count > 0) Refresh();
            }
            catch (Exception exception)
            {
                if (this != null) SetStatus(L.T("#ANIMATED_CAMERA_PREVIEW_ERROR") + exception.Message);
            }
            finally { preparingAnimatedCameraModels = false; }
        }
    }
}
