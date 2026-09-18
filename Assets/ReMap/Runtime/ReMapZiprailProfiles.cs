using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ReMap.Standalone
{
    internal sealed class ReMapZiprailProfile
    {
        internal readonly string Id;
        internal readonly string Label;
        internal readonly string ScriptConstant;
        internal readonly bool HasArm;
        internal readonly bool HasSupport;
        internal readonly string ArmModelPath;

        internal ReMapZiprailProfile(string id, string label, string scriptConstant,
            bool hasArm, bool hasSupport, string armModelPath = null)
        {
            Id = id;
            Label = label;
            ScriptConstant = scriptConstant;
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
        internal const float MinSupportHeightApex = 40f;
        internal const float MaxSupportHeightApex = 1024f;
        internal const float DefaultSupportHeightApex = 320f;

        internal static readonly string[] RequiredModelPaths = {
            BuildingClawModelPath, BuildingClaw02ModelPath, WallModelPath,
            CordEndModelPath, GroundBaseModelPath, GroundPostModelPath,
            GroundPostTopModelPath, GroundClawModelPath
        };

        internal static readonly ReMapZiprailProfile[] All = {
            new ReMapZiprailProfile("none", "#ZIPRAIL_MOUNT_NONE", "REMAP_ZIPRAIL_POINT_NONE", false, false),
            new ReMapZiprailProfile("arm", "#ZIPRAIL_MOUNT_BUILDING_CLAW_01", "REMAP_ZIPRAIL_POINT_ARM", true, false, BuildingClawModelPath),
            new ReMapZiprailProfile("building-claw-02", "#ZIPRAIL_MOUNT_BUILDING_CLAW_02", "REMAP_ZIPRAIL_POINT_BUILDING_CLAW_02", true, false, BuildingClaw02ModelPath),
            new ReMapZiprailProfile("wall", "#ZIPRAIL_MOUNT_WALL", "REMAP_ZIPRAIL_POINT_WALL", true, false, WallModelPath),
            new ReMapZiprailProfile("support", "#ZIPRAIL_MOUNT_GROUND_TOWER", "REMAP_ZIPRAIL_POINT_SUPPORT", true, true)
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
                result.Add(Component("ground-claw", "#ZIPRAIL_CABLE_CLAMP", GroundClawModelPath,
                    new Vector3(-172f, 0f, 85f), Vector3.zero));
                result.Add(Component("cord-end", "#ZIPLINE_ARM", CordEndModelPath,
                    Vector3.zero, new Vector3(0f, 90f, 0f)));
            }
            else
            {
                // Broken Moon places the cable 235 units in front and 137 units above
                // the building claw. The editor point remains on the rail itself.
                result.Add(Component("arm", profile.Label, profile.ArmModelPath,
                    new Vector3(0f, 235f, -137f), Vector3.zero));
                result.Add(Component("cord-end", "#ZIPLINE_ARM", CordEndModelPath,
                    Vector3.zero, new Vector3(0f, 180f, 0f)));
            }
            return result;
        }

        internal static IEnumerable<string> ModelPaths(ReMapZiprailProfile profile) =>
            Components(profile, DefaultSupportHeightApex)
                .Select(component => component.ModelPath)
                .Distinct(System.StringComparer.OrdinalIgnoreCase);

        private static ReMapZiprailComponentDefinition Component(string role, string label,
            string modelPath, Vector3 apexPosition, Vector3 apexRotation) =>
            new ReMapZiprailComponentDefinition(role, label, modelPath,
                ApexDisplay.UnityPosition(apexPosition), ApexDisplay.UnityAngles(apexRotation));
    }
}
