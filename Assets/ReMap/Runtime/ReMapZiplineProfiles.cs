using System.Linq;
using UnityEngine;

namespace ReMap.Standalone
{
    internal sealed class ReMapZiplineProfile
    {
        internal readonly string Id;
        internal readonly string Label;
        internal readonly string ScriptConstant;
        internal readonly bool HasArm;
        internal readonly bool HasSupport;

        internal ReMapZiplineProfile(
            string id, string label, string scriptConstant, bool hasArm, bool hasSupport)
        {
            Id = id;
            Label = label;
            ScriptConstant = scriptConstant;
            HasArm = hasArm;
            HasSupport = hasSupport;
        }
    }

    internal static class ReMapZiplineProfiles
    {
        internal const string ArmModelPath = "mdl/industrial/zipline_arm.rmdl";
        internal const float MinArmHeightApex = 70f;
        internal const float MaxArmHeightApex = 290f;
        internal const float DefaultArmHeightApex = 180f;
        internal const string SupportModelPath = "mdl/industrial/security_fence_post.rmdl";
        internal static readonly string[] RequiredModelPaths = { ArmModelPath, SupportModelPath };
        internal static readonly Vector3 ArmToCableApex = new Vector3(-58f, -2f, -14f);
        internal static readonly Vector3 ArmRotationApex = new Vector3(0f, 90f, 0f);

        internal static readonly ReMapZiplineProfile[] All = {
            new ReMapZiplineProfile("none", "#NO_SUPPORT", "REMAP_ZIPLINE_END_NONE", false, false),
            new ReMapZiplineProfile("arm", "#ARM", "REMAP_ZIPLINE_END_ARM", true, false),
            new ReMapZiplineProfile("support", "#ARM_SUPPORT", "REMAP_ZIPLINE_END_SUPPORT", true, true)
        };

        internal static ReMapZiplineProfile Find(string id) =>
            All.FirstOrDefault(profile => profile.Id == id) ?? All[0];

        internal static Vector3 ArmOffsetApex(ReMapZiplineProfile profile, float height) =>
            profile.HasSupport ? new Vector3(4f, -2.5f, height) : Vector3.zero;

        internal static Vector3 CableOffsetApex(string profileId, float height)
        {
            var profile = Find(profileId);
            return profile.HasArm
                ? ArmOffsetApex(profile, height) + ArmToCableApex
                : Vector3.zero;
        }

        internal static Vector3 ArmRotationUnity() =>
            ApexDisplay.UnityAngles(ArmRotationApex);

        internal static Vector3 UnityOffset(Vector3 apex) => ApexDisplay.UnityPosition(apex);
    }
}
