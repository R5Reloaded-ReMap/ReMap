using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ReMap.Standalone
{
    internal sealed class ReMapZiprailProfile
    {
        internal readonly string Id;
        internal readonly string Label;
        internal readonly bool HasArm;
        internal readonly bool HasSupport;
        internal readonly string ArmModelPath;

        internal ReMapZiprailProfile(string id, string label, bool hasArm, bool hasSupport, string armModelPath = null)
        {
            Id = id;
            Label = label;
            HasArm = hasArm;
            HasSupport = hasSupport;
            ArmModelPath = armModelPath;
        }
    }

    internal sealed class ReMapZiprailComponentDefinition
    {
        internal readonly string Role;
        internal readonly string Label;
        internal readonly string ModelPath;
        internal readonly Vector3 Position;
        internal readonly Vector3 Rotation;

        internal ReMapZiprailComponentDefinition(string role, string label, string modelPath,
            Vector3 position, Vector3 rotation)
        {
            Role = role;
            Label = label;
            ModelPath = modelPath;
            Position = position;
            Rotation = rotation;
        }
    }

    internal static class ReMapZiprailProfiles
    {
        internal const string BuildingClawModelPath =
            "mdl/props/zip_rail/zip_rail_building_claw_01.rmdl";
        internal const string BuildingClaw02ModelPath =
            "mdl/props/zip_rail/zip_rail_building_claw_02.rmdl";
        internal const string WallModelPath =
            "mdl/props/zip_rail/zip_rail_wall_01.rmdl";
        internal const string CordEndModelPath =
            "mdl/props/zip_rail/zip_rail_cord_end_01.rmdl";
        internal const string GroundBaseModelPath =
            "mdl/props/zip_rail/zip_rail_ground_base_01.rmdl";
        internal const string GroundPostModelPath =
            "mdl/props/zip_rail/zip_rail_ground_post_01.rmdl";
        internal const string GroundPostTopModelPath =
            "mdl/props/zip_rail/zip_rail_ground_post_top_01.rmdl";
        internal const string GroundClawModelPath =
            "mdl/props/zip_rail/zip_rail_ground_claw_01.rmdl";
        internal const float MinSupportHeightApex = 0f;
        internal const float MaxSupportHeightApex = 1024f;
        internal const float DefaultSupportHeightApex = 320f;
        internal const float CordEndOriginOffsetApex = 18.7f;

        internal static readonly string[] RequiredModelPaths = {
            BuildingClawModelPath, BuildingClaw02ModelPath, WallModelPath,
            CordEndModelPath, GroundBaseModelPath, GroundPostModelPath,
            GroundPostTopModelPath, GroundClawModelPath
        };

        internal static readonly ReMapZiprailProfile[] All = {
            new ReMapZiprailProfile("none", "#ZIPRAIL_MOUNT_NONE", false, false),
            new ReMapZiprailProfile("arm", "#ZIPRAIL_MOUNT_BUILDING_CLAW_01", true, false, BuildingClawModelPath),
            new ReMapZiprailProfile("building-claw-02", "#ZIPRAIL_MOUNT_BUILDING_CLAW_02", true, false, BuildingClaw02ModelPath),
            new ReMapZiprailProfile("wall", "#ZIPRAIL_MOUNT_WALL", true, false, WallModelPath),
            new ReMapZiprailProfile("support", "#ZIPRAIL_MOUNT_GROUND_TOWER", true, true)
        };

        internal static ReMapZiprailProfile Find(string id) =>
            All.FirstOrDefault(profile => profile.Id == id) ?? All[0];

        internal static IReadOnlyList<ReMapZiprailComponentDefinition> Components(
            ReMapZiprailProfile profile, float supportHeightApex)
        {
            var result = new List<ReMapZiprailComponentDefinition>();
            if (profile == null || !profile.HasArm) return result;

            if (profile.HasSupport)
            {
                result.Add(Component("support-base", "#ZIPLINE_SUPPORT", GroundBaseModelPath,
                    new Vector3(0f, 0f, -supportHeightApex), Vector3.zero));
                result.Add(Component("support-post", "#ZIPLINE_SUPPORT", GroundPostModelPath,
                    Vector3.zero, Vector3.zero));
                result.Add(Component("support-top", "#ZIPLINE_ARM", GroundPostTopModelPath,
                    new Vector3(0f, 4f, -1f), Vector3.zero));
            }
            else if (profile.Id == "wall")
            {
                result.Add(Component("wall", profile.Label, profile.ArmModelPath,
                    Vector3.zero, Vector3.zero));
                result.Add(Component("ground-claw", "#ZIPRAIL_CABLE_CLAMP", GroundClawModelPath,
                    new Vector3(-172f, 0f, 85f), Vector3.zero));
            }
            else
            {
                result.Add(Component("arm", profile.Label, profile.ArmModelPath,
                    Vector3.zero, Vector3.zero));
            }
            return result;
        }

        internal static ReMapZiprailComponentDefinition CordEnd(ReMapZiprailProfile profile) =>
            Component("cord-end", "#ZIPLINE_ARM", CordEndModelPath, CableOffsetApex(profile), Vector3.zero);

        internal static Vector3 CableOffsetApex(ReMapZiprailProfile profile)
        {
            if (profile == null) return Vector3.zero;
            if (profile.Id == "arm" || profile.Id == "building-claw-02")
                return new Vector3(16f, -235f, 137f);
            if (profile.Id == "wall") return new Vector3(-113f, 0f, 38f);
            if (profile.HasSupport) return new Vector3(-6f, 4f, 9f);
            return Vector3.zero;
        }

        internal static Vector3 PivotOffsetApex(ReMapZiprailProfile profile, float supportHeightApex)
        {
            if (profile == null) return Vector3.zero;
            if (profile.HasSupport) return new Vector3(0f, 0f, -supportHeightApex);
            if (profile.Id == "wall") return new Vector3(-1024f, 0f, 0f);
            return Vector3.zero;
        }

        internal static IEnumerable<string> ModelPaths(ReMapZiprailProfile profile) =>
            Components(profile, DefaultSupportHeightApex)
                .Select(component => component.ModelPath)
                .Concat(profile != null && profile.HasArm ? new[] { CordEndModelPath } : new string[0])
                .Distinct(System.StringComparer.OrdinalIgnoreCase);

        private static ReMapZiprailComponentDefinition Component(string role, string label,
            string modelPath, Vector3 apexPosition, Vector3 apexRotation) =>
            new ReMapZiprailComponentDefinition(role, label, modelPath,
                ApexDisplay.UnityPosition(apexPosition), ApexDisplay.UnityAngles(apexRotation));
    }
}
