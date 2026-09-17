using ReMap.Standalone.Core;
using System;
using System.Linq;
using UnityEngine;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private void CreateZipline()
        {
            CommitInspectorEdit();
            CancelPlacement();
            InsertZipline(SelectionPivot());
        }

        private void InsertZipline(Vector3 pivot, string parent = "")
        {
            var objects = CreateDefaultZiplineObjects(pivot, parent);
            var zipline = objects[0];
            var start = objects[1];
            var end = objects[2];
            var defaultStart = ReMapZiplineProfiles.All.Last(profile =>
                ReMapModelAvailability.ZiplineProfile(assetLibrary?.Records, Targets, profile));
            start.customProfile = defaultStart.Id;
            session.Edit(document => {
                document.objects.Add(zipline);
                document.objects.Add(start);
                document.objects.Add(end);
                SyncZiplineComponents(document, start);
                SyncZiplineComponents(document, end);
            });
            selectedId = zipline.id;
            RevealHierarchy(zipline.id);
            Refresh();
            _ = PrepareZiplineModels();
            SetStatus(L.T("#ZIPLINE_CREATED_MAIN_OBJECT_CONTROLS"));
        }

        internal static MapObject[] CreateDefaultZiplineObjects(Vector3 pivot, string parent = "")
        {
            var zipline = new MapObject {
                assetId = "custom:zipline", displayName = L.T("#ZIPLINE"), isGroup = true,
                customType = "zipline", ziplineMode = "vertical", ziplineWidth = 2f, ziplineSpeed = 1f, ziplineLengthScale = 1f,
                ziplineFadeDistance = -1f, ziplineScale = 1f, ziplineDropToBottom = true,
                ziplineAutoDetachStart = 100f, ziplineAutoDetachEnd = 100f,
                parentId = parent ?? "", position = WorldView.ToData(pivot)
            };
            var start = CreateZiplineEndpoint(zipline.id, "start", "support", Vector3.zero);
            var end = CreateZiplineEndpoint(
                zipline.id, "end", "none", Vector3.down * ApexCoordinates.MetersPerUnit * 400f);
            zipline.ziplineStartId = start.id;
            zipline.ziplineEndId = end.id;
            return new[] { zipline, start, end };
        }

        private static MapObject CreateZiplineEndpoint(
            string parent, string role, string profileId, Vector3 position) =>
            new MapObject {
                displayName = role == "start" ? L.T("#START") : L.T("#END"),
                customType = "zipline-endpoint", customRole = role, parentId = parent,
                customProfile = profileId, assetId = "custom:zipline-endpoint:" + role, isGroup = true,
                ziplineArmHeight = ReMapZiplineProfiles.DefaultArmHeightApex,
                position = WorldView.ToData(position)
            };

        private void ChangeZipline(string id, Action<MapObject> change)
        {
            session.Edit(document => change(document.objects.Find(candidate => candidate.id == id)));
            Refresh();
        }

        private void SwapZiplineEnds(string id)
        {
            session.Edit(document => {
                var zipline = document.objects.Find(candidate => candidate.id == id);
                var start = document.objects.Find(candidate => candidate.id == zipline.ziplineStartId);
                var end = document.objects.Find(candidate => candidate.id == zipline.ziplineEndId);
                string oldStart = zipline.ziplineStartId;
                zipline.ziplineStartId = zipline.ziplineEndId;
                zipline.ziplineEndId = oldStart;
                start.customRole = "end";
                start.displayName = L.T("#END");
                end.customRole = "start";
                end.displayName = L.T("#START");
                SyncZiplineComponents(document, start);
                SyncZiplineComponents(document, end);
            });
            Refresh();
        }
    }
}
