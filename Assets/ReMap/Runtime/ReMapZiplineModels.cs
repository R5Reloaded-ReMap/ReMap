using ReMap.Standalone.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private bool preparingZiplineModels;

        private GameAssetRecord ZiplineModelRecord(string modelPath) =>
            assetLibrary?.Records.FirstOrDefault(candidate => GameAssetIndex.SameModelPath(
                candidate.modelPath, modelPath) && candidate.Supports(Targets));

        private MapObject CreateZiplineComponent(
            MapObject endpoint, string role, string modelPath, Vector3 localPosition, Vector3 localRotation)
        {
            var record = ZiplineModelRecord(modelPath);
            return new MapObject {
                displayName = role == "arm" ? L.T("#ZIPLINE_ARM") : L.T("#ZIPLINE_SUPPORT"),
                parentId = endpoint.id, customType = "zipline-component", customRole = role,
                gameModelPath = modelPath, position = WorldView.ToData(localPosition),
                rotation = WorldView.ToData(localRotation),
                assetId = record?.Id ?? "custom:zipline-component:" + role,
                isGroup = record == null,
                commonAsset = record?.IsCommon ?? false,
                availableMaps = record?.origins.Select(origin => origin.mapId)
                    .Where(map => map != "").Distinct().ToList() ?? new List<string>()
            };
        }

        private void SyncZiplineComponents(MapDocument document, MapObject endpoint)
        {
            document.objects.RemoveAll(candidate =>
                candidate.parentId == endpoint.id && candidate.customType == "zipline-component");
            var profile = ReMapZiplineProfiles.Find(endpoint.customProfile);
            endpoint.customProfile = profile.Id;
            endpoint.assetId = "custom:zipline-endpoint:" + endpoint.customRole;
            endpoint.gameModelPath = "";
            endpoint.isGroup = true;
            endpoint.commonAsset = false;
            endpoint.availableMaps = new List<string>();

            // Disabled pieces do not exist in the document, so they are neither rendered nor exported.
            if (profile.HasSupport)
                document.objects.Add(CreateZiplineComponent(
                    endpoint, "support", ReMapZiplineProfiles.SupportModelPath, Vector3.zero, Vector3.zero));
            if (profile.HasArm)
            {
                var armOffset = ReMapZiplineProfiles.UnityOffset(
                    ReMapZiplineProfiles.ArmOffsetApex(profile, endpoint.ziplineArmHeight));
                document.objects.Add(CreateZiplineComponent(
                    endpoint, "arm", ReMapZiplineProfiles.ArmModelPath, armOffset,
                    ReMapZiplineProfiles.ArmRotationUnity()));
            }
        }
        private void UpdateZiplineArmHeight(MapDocument document, MapObject endpoint, float height)
        {
            endpoint.ziplineArmHeight = height;
            var profile = ReMapZiplineProfiles.Find(endpoint.customProfile);
            var arm = document.objects.FirstOrDefault(candidate =>
                candidate.parentId == endpoint.id && candidate.customType == "zipline-component" &&
                candidate.customRole == "arm");
            if (!profile.HasArm || arm == null)
            {
                SyncZiplineComponents(document, endpoint);
                return;
            }
            arm.position = WorldView.ToData(ReMapZiplineProfiles.UnityOffset(
                ReMapZiplineProfiles.ArmOffsetApex(profile, height)));
            arm.rotation = WorldView.ToData(ReMapZiplineProfiles.ArmRotationUnity());
        }


        private bool ZiplineComponentsNeedSync(MapDocument document, MapObject endpoint)
        {
            var profile = ReMapZiplineProfiles.Find(endpoint.customProfile);
            var components = document.objects.Where(candidate =>
                candidate.parentId == endpoint.id && candidate.customType == "zipline-component").ToArray();
            int expectedCount = (profile.HasSupport ? 1 : 0) + (profile.HasArm ? 1 : 0);
            if (components.Length != expectedCount) return true;

            bool Current(string role, string modelPath, Vector3 expectedPosition, Vector3 expectedRotation)
            {
                var component = components.FirstOrDefault(candidate => candidate.customRole == role);
                if (component == null || !GameAssetIndex.SameModelPath(
                    component.gameModelPath, modelPath)) return false;
                var record = ZiplineModelRecord(modelPath);
                string expectedAssetId = record?.Id ?? "custom:zipline-component:" + role;
                bool poseMatches = Vector3.Distance(WorldView.ToVector(component.position), expectedPosition) < .0001f &&
                    Quaternion.Angle(Quaternion.Euler(WorldView.ToVector(component.rotation)),
                        Quaternion.Euler(expectedRotation)) < .01f;
                return component.assetId == expectedAssetId && component.isGroup == (record == null) && poseMatches;
            }

            Vector3 armPosition = ReMapZiplineProfiles.UnityOffset(
                ReMapZiplineProfiles.ArmOffsetApex(profile, endpoint.ziplineArmHeight));
            Vector3 armRotation = ReMapZiplineProfiles.ArmRotationUnity();
            return profile.HasSupport && !Current(
                    "support", ReMapZiplineProfiles.SupportModelPath, Vector3.zero, Vector3.zero) ||
                profile.HasArm && !Current("arm", ReMapZiplineProfiles.ArmModelPath, armPosition, armRotation);
        }

        private void ApplyZiplineProfile(MapDocument document, MapObject endpoint, string profileId)
        {
            endpoint.customProfile = ReMapZiplineProfiles.Find(profileId).Id;
            SyncZiplineComponents(document, endpoint);
        }

        private async Task PrepareZiplineModels()
        {
            if (assetLibrary == null || preparingZiplineModels) return;
            preparingZiplineModels = true;
            var prepared = new List<string>();
            try
            {
                foreach (string modelPath in ReMapZiplineProfiles.RequiredModelPaths)
                {
                    var record = ZiplineModelRecord(modelPath);
                    if (record == null || !record.Supports(Targets)) continue;
                    bool wasPrepared = world.models.IsPrepared(record.Id);
                    var entry = await PrepareDropEntry(record);
                    if (this == null || entry == null) return;
                    if (!wasPrepared) prepared.Add(record.Id);
                }
                if (this == null) return;
                var endpoints = snapshot.objects.Where(candidate =>
                    candidate.customType == "zipline-endpoint").ToArray();
                bool needsSync = endpoints.Any(endpoint =>
                    ZiplineComponentsNeedSync(snapshot, endpoint));
                if (needsSync)
                    session.Edit(document => {
                        foreach (var endpoint in document.objects
                            .Where(candidate => candidate.customType == "zipline-endpoint").ToArray())
                            if (ZiplineComponentsNeedSync(document, endpoint))
                                SyncZiplineComponents(document, endpoint);
                    });
                foreach (string assetId in prepared) world.Reload(assetId);
                if (needsSync || prepared.Count > 0) Refresh();
            }
            catch (Exception exception)
            {
                if (this != null)
                    SetStatus(L.T("#ZIPLINE_SUPPORT_PREVIEW") + exception.Message);
            }
            finally
            {
                preparingZiplineModels = false;
            }
        }
    }
}
