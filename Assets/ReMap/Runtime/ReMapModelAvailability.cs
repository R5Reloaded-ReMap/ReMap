using ReMap.Standalone.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ReMap.Standalone
{
    public static class ReMapModelAvailability
    {
        public static bool HasModel(IEnumerable<GameAssetRecord> records,
            IEnumerable<string> targets, string modelPath)
        {
            var selected = new HashSet<string>(targets ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            return (records ?? Enumerable.Empty<GameAssetRecord>()).Any(record =>
                GameAssetIndex.SameModelPath(record.modelPath, modelPath) &&
                record.Supports(selected));
        }

        internal static bool DoorProfile(IEnumerable<GameAssetRecord> records,
            IEnumerable<string> targets, ReMapDoorProfile profile) =>
            profile != null && HasModel(records, targets, profile.ModelPath);

        public static bool Door(IEnumerable<GameAssetRecord> records,
            IEnumerable<string> targets) => ReMapDoorProfiles.All.Any(profile =>
                DoorProfile(records, targets, profile));

        public static bool ZiplineProfile(IEnumerable<GameAssetRecord> records,
            IEnumerable<string> targets, string profileId) =>
            ZiplineProfile(records, targets, ReMapZiplineProfiles.Find(profileId));

        internal static bool ZiplineProfile(IEnumerable<GameAssetRecord> records,
            IEnumerable<string> targets, ReMapZiplineProfile profile)
        {
            if (profile == null) return false;
            if (profile.HasArm && !HasModel(records, targets, ReMapZiplineProfiles.ArmModelPath))
                return false;
            return !profile.HasSupport || HasModel(records, targets,
                ReMapZiplineProfiles.SupportModelPath);
        }

        internal static bool ZiprailProfile(IEnumerable<GameAssetRecord> records,
            IEnumerable<string> targets, ReMapZiprailProfile profile)
        {
            if (profile == null) return false;
            return ReMapZiprailProfiles.ModelPaths(profile).All(path =>
                HasModel(records, targets, path));
        }

        public static bool ZiprailProfile(IEnumerable<GameAssetRecord> records,
            IEnumerable<string> targets, string profileId) =>
            ZiprailProfile(records, targets, ReMapZiprailProfiles.Find(profileId));
    }
}
