using System;
using System.Collections.Generic;

namespace ReMap.Standalone.Core
{
    public static class GameTargets
    {
        public const string R5Reloaded = "r5reloaded";
        public const string R5Flowstate = "r5flowstate";

        public static bool IsSupported(string value) =>
            value == R5Reloaded || value == R5Flowstate;

        public static string Normalize(string value) => IsSupported(value) ? value : R5Reloaded;

        public static string DisplayName(string value) =>
            Normalize(value) == R5Flowstate ? "R5Flowstate" : "R5Reloaded";
    }

    [Serializable]
    public struct Float3
    {
        public float x, y, z;
        public Float3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public bool IsFinite => Finite(x) && Finite(y) && Finite(z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    [Serializable]
    public sealed class ScriptProperty
    {
        public string scope = "s";
        public string name = "remapValue";
        public string value = "null";

        public ScriptProperty Copy() => new ScriptProperty { scope = scope, name = name, value = value };
    }

    [Serializable]
    public sealed class MapObject
    {
        public string id = Guid.NewGuid().ToString("N");
        public string assetId = "demo:cube";
        public string displayName = L.T("#CUBE");
        public string parentId = "";
        public bool isGroup;
        public bool disabled;
        public string gameModelPath = "";
        // Empty for regular props. Custom objects remain regular hierarchy nodes so they
        // inherit grouping, undo/redo, duplication and the existing transform gizmo.
        public string customType = "";
        public string customRole = "";
        public string customProfile = "";
        public string ziplineStartId = "";
        public string ziplineEndId = "";
        public string ziplineMode = "horizontal";
        public float ziplineWidth = 2f;
        public float ziplineSpeed = 1f;
        public float ziplineLengthScale = 1f;
        public float ziplineFadeDistance = -1f;
        public float ziplineScale = 1f;
        public bool ziplinePreserveVelocity;
        public bool ziplineDropToBottom = true;
        public float ziplineAutoDetachStart = 100f;
        public float ziplineAutoDetachEnd = 100f;
        public bool ziplineRestPoint;
        public bool ziplineDetachEndOnSpawn;
        public bool ziplineDetachEndOnUse;
        public bool ziplineAutomaticEnd;
        public bool ziplineLockEnd;
        public const float DefaultZiplineEndOffsetApex = 25f;
        public float ziplineEndOffset = DefaultZiplineEndOffsetApex;
        public float ziplineArmHeight = 180f;
        public bool ziplinePushOffInDirectionX;
        public float ziplinePushOffAngle;
        public string doorType = "single";
        public bool doorGold;
        public bool doorSpawnOpen;
        public int curvedZiplineSegments = 8;
        public bool curvedZiplineSupport;
        public int lootBinSkin;
        public float jumpPadLaunchVelocity = 1000f;
        public float jumpPadForwardScale = 1.7f;
        public float jumpPadRadius = 45f;
        public bool jumpPadDoubleJump = true;
        public int spawnPointTeam;
        public float triggerRadius = 100f;
        public float triggerHalfHeight = 50f;
        public bool triggerDebug;
        public string triggerEnterCallback = "";
        public string triggerLeaveCallback = "";
        public float jumpTowerHeight = 2000f;
        public string weaponRackWeapon = "mp_weapon_rspn101";
        public float weaponRackRespawnTime = .5f;
        public int respawnHealType;
        public float respawnHealRespawnTime = 6f;
        public float respawnHealDuration = 5f;
        public int respawnHealAmount = 25;
        public bool respawnHealProgressive = true;
        public string buttonMode = "visible";
        public string buttonUseText = "";
        public string buttonCallback = "";
        public bool buttonUp = true;
        public Float3 buttonDestination;
        public Float3 buttonDirection;
        public string buttonMessage = "";
        public string buttonSubMessage = "";
        public int buttonMessageType = 4;
        public float buttonMessageDuration = 5f;
        public string buttonToken = "#FS_STRING_VAR";
        public Float3 speedBoostColor = new Float3(255f, 255f, 255f);
        public float speedBoostRespawnTime = 5f;
        public float speedBoostStrength = .35f;
        public float speedBoostDuration = 3f;
        public float speedBoostFadeTime;
        public Float3 bubbleShieldColor = new Float3(128f, 255f, 128f);
        public float cameraPathTransitionTime = 8f;
        public float cameraPathFov = 120f;
        public bool cameraPathTrackTarget;
        public bool cameraPathSpacingEnabled;
        public float cameraPathSpacing;
        public float animatedCameraAngleOffset = 20f;
        public float animatedCameraMaxLeft = 20f;
        public float animatedCameraMaxRight = 40f;
        public float animatedCameraRotationTime = 4f;
        public float animatedCameraTransitionTime = 2f;
        public string soundName = "";
        public float soundRadius;
        public bool soundWaveAmbient;
        public bool soundEnabled = true;
        public bool soundShowPolyline = true;
        public bool commonAsset;
        public List<string> availableMaps = new List<string>();
        public bool allowMantle = true;
        public float fadeDistance = 50000f;
        public int realmId = -1;
        public bool clientSide;
        public List<ScriptProperty> scriptProperties = new List<ScriptProperty>();
        public Float3 position;
        public Float3 rotation;
        public Float3 scale = new Float3(1, 1, 1);

        public MapObject Copy()
        {
            var copy = (MapObject)MemberwiseClone();
            copy.availableMaps = new List<string>(availableMaps ?? new List<string>());
            copy.scriptProperties = (scriptProperties ?? new List<ScriptProperty>()).ConvertAll(item => item?.Copy());
            return copy;
        }
    }

    [Serializable]
    public sealed class MapDocument
    {
        public const int CurrentVersion = 1;
        public int schemaVersion = CurrentVersion;
        public string name = L.T("#NEW_MAP");
        // Backward-compatible storage in Unity metres; editor fields use ApexCoordinates (Source units, Z up).
        public string coordinateSystem = "unity-y-up-meters";
        // The target is part of the map because game functions and custom objects can differ.
        public string gameTarget = GameTargets.R5Reloaded;
        public string editingMap = "";
        public List<string> targetMaps = new List<string>();
        // Scene-root translation. Objects stay local to the editor origin; game output adds this offset.
        public Float3 originOffset;
        public List<MapObject> objects = new List<MapObject>();

        public MapDocument Copy()
        {
            var result = new MapDocument { schemaVersion = schemaVersion, name = name,
                coordinateSystem = coordinateSystem, gameTarget = gameTarget, editingMap = editingMap,
                originOffset = originOffset,
                targetMaps = new List<string>(targetMaps ?? new List<string>()) };
            foreach (var item in objects) result.objects.Add(item.Copy());
            return result;
        }

        public void Validate()
        {
            if (schemaVersion != CurrentVersion)
                throw new ArgumentException(L.T("#UNSUPPORTED_SAVE_VERSION"));
            if (coordinateSystem != "unity-y-up-meters")
                throw new ArgumentException(L.T("#UNSUPPORTED_COORDINATE_SYSTEM"));
            if (!originOffset.IsFinite)
                throw new ArgumentException(L.T("#SCENE_STARTING_POSITION_CONTAIN_FINITE"));
            if (objects == null || objects.Count > 10000)
                throw new ArgumentException(L.T("#MAP_MAY_CONTAIN_MOST_10"));
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
                throw new ArgumentException(L.T("#INVALID_MAP_NAME_1_128"));
            targetMaps = targetMaps ?? new List<string>();
            if (string.IsNullOrWhiteSpace(gameTarget)) gameTarget = GameTargets.R5Reloaded;
            else if (!GameTargets.IsSupported(gameTarget))
                throw new ArgumentException(L.T("#UNSUPPORTED_TARGET_GAME"));
            editingMap = editingMap ?? "";
            if (editingMap.Length > 128)
                throw new ArgumentException(L.T("#INVALID_EDITED_MAP"));
            if (targetMaps.Count > 64 || targetMaps.Exists(m => string.IsNullOrWhiteSpace(m) || m.Length > 128))
                throw new ArgumentException(L.T("#INVALID_TARGET_MAP_LIST"));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in objects)
            {
                if (item == null || !Guid.TryParseExact(item.id, "N", out _) || !ids.Add(item.id))
                    throw new ArgumentException(L.T("#INVALID_DUPLICATE_OBJECT_ID"));
                if (string.IsNullOrWhiteSpace(item.assetId) || item.assetId.Length > 1024)
                    throw new ArgumentException(L.T("#INVALID_MODEL_REFERENCE"));
                if (string.IsNullOrWhiteSpace(item.displayName) || item.displayName.Length > 128)
                    throw new ArgumentException(L.T("#INVALID_OBJECT_NAME"));
                item.customType = item.customType ?? "";
                item.customRole = item.customRole ?? "";
                item.customProfile = item.customProfile ?? "";
                item.ziplineStartId = item.ziplineStartId ?? "";
                item.ziplineEndId = item.ziplineEndId ?? "";
                item.ziplineMode = item.ziplineMode ?? "horizontal";
                item.doorType = item.doorType ?? "single";
                item.triggerEnterCallback = item.triggerEnterCallback ?? "";
                item.triggerLeaveCallback = item.triggerLeaveCallback ?? "";
                item.weaponRackWeapon = item.weaponRackWeapon ?? "mp_weapon_rspn101";
                item.buttonMode = item.buttonMode ?? "visible";
                item.buttonUseText = item.buttonUseText ?? "";
                item.buttonCallback = item.buttonCallback ?? "";
                item.buttonMessage = item.buttonMessage ?? "";
                item.buttonSubMessage = item.buttonSubMessage ?? "";
                item.buttonToken = item.buttonToken ?? "#FS_STRING_VAR";
                item.soundName = item.soundName ?? "";
                if (item.customType == "curved-zipline" && item.customProfile == "")
                    item.customProfile = item.curvedZiplineSupport ? "arm" : "none";
                if (item.customType == "curved-zipline")
                    item.curvedZiplineSupport = item.customProfile != "none";
                if (item.customType == "curved-zipline-component")
                {
                    var legacyParent = objects.Find(candidate => candidate.id == item.parentId);
                    if (legacyParent?.customType == "curved-zipline")
                    {
                        var firstPoint = objects.Find(candidate => candidate.parentId == legacyParent.id &&
                            candidate.customType == "curved-zipline-point" && ParsePointIndex(candidate) == 0);
                        if (firstPoint != null) item.parentId = firstPoint.id;
                    }
                }
                if (item.ziplineMode == "auto") item.ziplineMode = "horizontal";
                if (item.customType != "" && item.customType != "zipline" && item.customType != "zipline-endpoint" &&
                    item.customType != "zipline-component" && item.customType != "door" &&
                    item.customType != "door-component" && item.customType != "curved-zipline" &&
                    item.customType != "curved-zipline-point" && item.customType != "curved-zipline-component" &&
                    item.customType != "loot-bin" && item.customType != "jump-pad" &&
                    item.customType != "spawn-point" && item.customType != "trigger" &&
                    item.customType != "jump-tower" && item.customType != "jump-tower-component" &&
                    item.customType != "weapon-rack" && item.customType != "respawn-heal" &&
                    item.customType != "button" && item.customType != "speed-boost" &&
                    item.customType != "speed-boost-component" && item.customType != "bubble-shield" &&
                    item.customType != "camera-path" && item.customType != "camera-path-point" &&
                    item.customType != "camera-path-target" && item.customType != "animated-camera" &&
                    item.customType != "animated-camera-component" && item.customType != "sound" &&
                    item.customType != "sound-point" && item.customType != "location-pair")
                    throw new ArgumentException(L.T("#UNKNOWN_CUSTOM_OBJECT_TYPE"));
                if (item.customType == "zipline" && !item.isGroup)
                    throw new ArgumentException(L.T("#ZIPLINE_HIERARCHY_GROUP"));
                if (item.customType == "zipline" &&
                    (item.ziplineMode != "horizontal" && item.ziplineMode != "vertical"))
                    throw new ArgumentException(L.T("#INVALID_ZIPLINE_ORIENTATION_MODE"));
                if (item.customType == "zipline" && (!Finite(item.ziplineWidth) || !Finite(item.ziplineSpeed) ||
                    !Finite(item.ziplineEndOffset) ||
                    item.ziplineWidth < 0.1f || item.ziplineWidth > 32f || item.ziplineSpeed < 0.1f || item.ziplineSpeed > 10f))
                    throw new ArgumentException(L.T("#INVALID_ZIPLINE_WIDTH_SPEED"));
                if (item.customType == "zipline" && (!Finite(item.ziplinePushOffAngle) ||
                    item.ziplinePushOffAngle < -360f || item.ziplinePushOffAngle > 360f))
                    throw new ArgumentException(L.T("#INVALID_ZIPLINE_PUSH_OFF_ANGLE"));
                if (item.customType == "zipline" && (!Finite(item.ziplineLengthScale) || item.ziplineLengthScale < 0f || item.ziplineLengthScale > 1.2f))
                    throw new ArgumentException(L.T("#INVALID_ZIPLINE_LENGTH_SCALE"));
                if (item.customType == "zipline" && (!Finite(item.ziplineFadeDistance) || item.ziplineFadeDistance < -1f ||
                    !Finite(item.ziplineScale) || item.ziplineScale < .01f || item.ziplineScale > 100f))
                    throw new ArgumentException(L.T("#INVALID_ZIPLINE_FADE_SCALE"));
                if (item.customType == "zipline" && (!Finite(item.ziplineAutoDetachStart) || !Finite(item.ziplineAutoDetachEnd) ||
                    item.ziplineAutoDetachStart < 0f || item.ziplineAutoDetachStart > 65535f ||
                    item.ziplineAutoDetachEnd < 0f || item.ziplineAutoDetachEnd > 65535f))
                    throw new ArgumentException(L.T("#INVALID_ZIPLINE_AUTO_DETACH"));

                if (item.customType == "zipline-endpoint" && (!Finite(item.ziplineArmHeight) ||
                    item.ziplineArmHeight < 70f || item.ziplineArmHeight > 290f))
                    throw new ArgumentException(L.T("#INVALID_ZIPLINE_ARM_HEIGHT"));
                if (item.customType == "door" && !item.isGroup)
                    throw new ArgumentException(L.T("#DOOR_HIERARCHY_GROUP"));
                if (item.customType == "door" && item.doorType != "single" && item.doorType != "double" &&
                    item.doorType != "vertical" && item.doorType != "horizontal")
                    throw new ArgumentException(L.T("#INVALID_DOOR_TYPE"));
                if (item.customType == "curved-zipline" && !item.isGroup)
                    throw new ArgumentException(L.T("#CURVED_ZIPLINE_HIERARCHY_GROUP"));
                if (item.customType == "curved-zipline" && item.customProfile != "none" &&
                    item.customProfile != "arm" && item.customProfile != "support")
                    throw new ArgumentException(L.T("#INVALID_CURVED_ZIPLINE_SETTINGS"));
                if (item.customType == "curved-zipline" && (item.curvedZiplineSegments < 2 ||
                    item.curvedZiplineSegments > 32 || !Finite(item.ziplineWidth) || !Finite(item.ziplineSpeed) ||
                    !Finite(item.ziplineArmHeight) || item.ziplineArmHeight < 70f || item.ziplineArmHeight > 290f ||
                    item.ziplineWidth < .1f || item.ziplineWidth > 32f ||
                    item.ziplineSpeed < .1f || item.ziplineSpeed > 10f))
                    throw new ArgumentException(L.T("#INVALID_CURVED_ZIPLINE_SETTINGS"));
                if (item.customType == "curved-zipline-point" && item.customProfile != "" &&
                    item.customProfile != "none" && item.customProfile != "arm" &&
                    item.customProfile != "support")
                    throw new ArgumentException(L.T("#INVALID_CURVED_ZIPLINE_SETTINGS"));
                if (item.customType == "curved-zipline-point" && (!Finite(item.ziplineArmHeight) ||
                    item.ziplineArmHeight < 70f || item.ziplineArmHeight > 290f))
                    throw new ArgumentException(L.T("#INVALID_ZIPLINE_ARM_HEIGHT"));
                if (item.customType == "loot-bin" && (item.lootBinSkin < 0 || item.lootBinSkin > 3))
                    throw new ArgumentException(L.T("#INVALID_LOOT_BIN_SKIN"));
                if (item.customType == "jump-pad" && (!Finite(item.jumpPadLaunchVelocity) ||
                    !Finite(item.jumpPadForwardScale) || !Finite(item.jumpPadRadius) ||
                    item.jumpPadLaunchVelocity < 100f || item.jumpPadLaunchVelocity > 5000f ||
                    item.jumpPadForwardScale < .1f || item.jumpPadForwardScale > 10f ||
                    item.jumpPadRadius < 1f || item.jumpPadRadius > 512f))
                    throw new ArgumentException(L.T("#INVALID_JUMP_PAD_SETTINGS"));
                if (item.customType == "spawn-point" && (item.spawnPointTeam < 0 || item.spawnPointTeam > 64))
                    throw new ArgumentException(L.T("#INVALID_SPAWN_POINT_TEAM"));
                if (item.customType == "trigger" && (!item.isGroup || !Finite(item.triggerRadius) ||
                    !Finite(item.triggerHalfHeight) || item.triggerRadius < .1f || item.triggerRadius > 65535f ||
                    item.triggerHalfHeight < .1f || item.triggerHalfHeight > 65535f ||
                    item.triggerEnterCallback.Length > 65535 || item.triggerLeaveCallback.Length > 65535 ||
                    item.triggerEnterCallback.IndexOf('\0') >= 0 || item.triggerLeaveCallback.IndexOf('\0') >= 0))
                    throw new ArgumentException(L.T("#INVALID_TRIGGER_SETTINGS"));
                if (item.customType == "jump-tower" && (!item.isGroup || !Finite(item.jumpTowerHeight) ||
                    item.jumpTowerHeight < 128f || item.jumpTowerHeight > 65535f))
                    throw new ArgumentException(L.T("#INVALID_JUMP_TOWER_HEIGHT"));
                if (item.customType == "weapon-rack" && (!Finite(item.weaponRackRespawnTime) ||
                    item.weaponRackRespawnTime < 0f || item.weaponRackRespawnTime > 86400f ||
                    item.weaponRackWeapon.Length > 128 ||
                    !item.weaponRackWeapon.StartsWith("mp_weapon_", StringComparison.Ordinal) ||
                    !IsScriptIdentifier(item.weaponRackWeapon)))
                    throw new ArgumentException(L.T("#INVALID_WEAPON_RACK_SETTINGS"));
                if (item.customType == "respawn-heal" && (item.respawnHealType < 0 ||
                    item.respawnHealType > 4 || !Finite(item.respawnHealRespawnTime) ||
                    !Finite(item.respawnHealDuration) || item.respawnHealRespawnTime < 0f ||
                    item.respawnHealRespawnTime > 86400f || item.respawnHealDuration < .05f ||
                    item.respawnHealDuration > 3600f || item.respawnHealAmount < 1 ||
                    item.respawnHealAmount > 1000))
                    throw new ArgumentException(L.T("#INVALID_RESPAWN_HEAL_SETTINGS"));
                if (item.customType == "button" && ((item.buttonMode != "visible" &&
                    item.buttonMode != "invisible") || !item.buttonDestination.IsFinite ||
                    !item.buttonDirection.IsFinite || item.buttonUseText.Length > 2048 ||
                    item.buttonCallback.Length > 65535 || item.buttonMessage.Length > 2048 ||
                    item.buttonSubMessage.Length > 2048 || item.buttonToken.Length > 256 ||
                    item.buttonMessageType < 0 || item.buttonMessageType > 16 ||
                    !Finite(item.buttonMessageDuration) || item.buttonMessageDuration < 0f ||
                    item.buttonMessageDuration > 3600f || item.buttonUseText.IndexOf('\0') >= 0 ||
                    item.buttonCallback.IndexOf('\0') >= 0 || item.buttonMessage.IndexOf('\0') >= 0 ||
                    item.buttonSubMessage.IndexOf('\0') >= 0 || item.buttonToken.IndexOf('\0') >= 0))
                    throw new ArgumentException(L.T("#INVALID_BUTTON_SETTINGS"));
                if (item.customType == "speed-boost" && (!item.isGroup || !item.speedBoostColor.IsFinite ||
                    item.speedBoostColor.x < 0f || item.speedBoostColor.x > 255f ||
                    item.speedBoostColor.y < 0f || item.speedBoostColor.y > 255f ||
                    item.speedBoostColor.z < 0f || item.speedBoostColor.z > 255f ||
                    !Finite(item.speedBoostRespawnTime) || item.speedBoostRespawnTime < 0f ||
                    item.speedBoostRespawnTime > 86400f || !Finite(item.speedBoostStrength) ||
                    item.speedBoostStrength < 0f || item.speedBoostStrength > 100f ||
                    !Finite(item.speedBoostDuration) || item.speedBoostDuration < .05f ||
                    item.speedBoostDuration > 3600f || !Finite(item.speedBoostFadeTime) ||
                    item.speedBoostFadeTime < 0f || item.speedBoostFadeTime > item.speedBoostDuration))
                    throw new ArgumentException(L.T("#INVALID_SPEED_BOOST_SETTINGS"));
                if (item.customType == "bubble-shield" && (!item.bubbleShieldColor.IsFinite ||
                    item.bubbleShieldColor.x < 0f || item.bubbleShieldColor.x > 255f ||
                    item.bubbleShieldColor.y < 0f || item.bubbleShieldColor.y > 255f ||
                    item.bubbleShieldColor.z < 0f || item.bubbleShieldColor.z > 255f ||
                    Math.Abs(item.scale.x - item.scale.y) > .0001f ||
                    Math.Abs(item.scale.x - item.scale.z) > .0001f))
                    throw new ArgumentException(L.T("#INVALID_BUBBLE_SHIELD_SETTINGS"));
                if (item.customType == "camera-path" && (!item.isGroup ||
                    !Finite(item.cameraPathTransitionTime) || item.cameraPathTransitionTime < .01f ||
                    item.cameraPathTransitionTime > 3600f || !Finite(item.cameraPathFov) ||
                    item.cameraPathFov < 1f || item.cameraPathFov > 179f ||
                    !Finite(item.cameraPathSpacing) || item.cameraPathSpacing < 0f ||
                    item.cameraPathSpacing > 65535f))
                    throw new ArgumentException(L.T("#INVALID_CAMERA_PATH_SETTINGS"));
                if (item.customType == "animated-camera" && (!item.isGroup ||
                    !Finite(item.animatedCameraAngleOffset) || item.animatedCameraAngleOffset < -360f ||
                    item.animatedCameraAngleOffset > 360f || !Finite(item.animatedCameraMaxLeft) ||
                    item.animatedCameraMaxLeft < 0f || item.animatedCameraMaxLeft > 360f ||
                    !Finite(item.animatedCameraMaxRight) || item.animatedCameraMaxRight < 0f ||
                    item.animatedCameraMaxRight > 360f || !Finite(item.animatedCameraRotationTime) ||
                    item.animatedCameraRotationTime < .01f || item.animatedCameraRotationTime > 3600f ||
                    !Finite(item.animatedCameraTransitionTime) || item.animatedCameraTransitionTime < 0f ||
                    item.animatedCameraTransitionTime > 3600f))
                    throw new ArgumentException(L.T("#INVALID_ANIMATED_CAMERA_SETTINGS"));
                if (item.customType == "sound" && (!item.isGroup || !Finite(item.soundRadius) ||
                    item.soundRadius < 0f || item.soundRadius > 65535f || item.soundName.Length > 256 ||
                    item.soundName.IndexOf('\0') >= 0 || item.soundName.IndexOf('\r') >= 0 ||
                    item.soundName.IndexOf('\n') >= 0 ||
                    objects.FindAll(candidate => candidate.parentId == item.id &&
                        candidate.customType == "sound-point").Count > 64))
                    throw new ArgumentException(L.T("#INVALID_SOUND_SETTINGS"));
                if (item.customType == "location-pair" && !item.isGroup)
                    throw new ArgumentException(L.T("#LOCATION_PAIR_HIERARCHY_GROUP"));
                if (!item.position.IsFinite || !item.rotation.IsFinite || !item.scale.IsFinite)
                    throw new ArgumentException(L.T("#TRANSFORMS_FINITE_NUMBERS"));
                if (!Finite(item.fadeDistance) || item.fadeDistance < -1f)
                    throw new ArgumentException(L.T("#INVALID_PROP_FADE_DISTANCE"));
                if (item.realmId < -1)
                    throw new ArgumentException(L.T("#INVALID_PROP_REALM_ID"));
                if (item.scale.x < 0.01f || item.scale.y < 0.01f || item.scale.z < 0.01f ||
                    item.scale.x > 1000 || item.scale.y > 1000 || item.scale.z > 1000)
                    throw new ArgumentException(L.T("#SCALE_BETWEEN_0_01_1"));
                item.scriptProperties = item.scriptProperties ?? new List<ScriptProperty>();
                if (item.scriptProperties.Count > 64)
                    throw new ArgumentException(L.T("#OBJECT_MAY_CONTAIN_MOST_64"));
                foreach (var property in item.scriptProperties)
                {
                    if (property == null || (property.scope != "" && property.scope != "kv" && property.scope != "e" && property.scope != "s"))
                        throw new ArgumentException(L.T("#GAME_SCRIPT_PROPERTIES_USE_KV_7AE349"));
                    if (string.IsNullOrWhiteSpace(property.name) || property.name.Length > 64 ||
                        !IsScriptIdentifier(property.name))
                        throw new ArgumentException(L.T("#INVALID_GAME_SCRIPT_PROPERTY_NAME"));
                    if (property.name == "solid" && property.scope != "kv")
                        throw new ArgumentException(L.T("#SOLID_AVAILABLE_KV"));
                    if (string.IsNullOrWhiteSpace(property.value) || property.value.Length > 2048 ||
                        property.value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                        throw new ArgumentException(L.T("#INVALID_GAME_SCRIPT_PROPERTY_VALUE"));
                }

            }
            MapHierarchy.Validate(this);
            foreach (var item in objects)
            {
                if (item.customType == "zipline")
                {
                    var start = objects.Find(o => o.id == item.ziplineStartId);
                    var end = objects.Find(o => o.id == item.ziplineEndId);
                    if (start == null || end == null || start.parentId != item.id || end.parentId != item.id ||
                        start.customType != "zipline-endpoint" || end.customType != "zipline-endpoint" ||
                        start.customRole != "start" || end.customRole != "end")
                        throw new ArgumentException(L.T("#ZIPLINE_CONTAIN_ONE_START_ONE"));
                }
                else if (item.customType == "zipline-endpoint")
                {
                    var parent = objects.Find(o => o.id == item.parentId);
                    if (parent == null || parent.customType != "zipline" ||
                        (item.customRole != "start" && item.customRole != "end") || item.customProfile.Length > 64)
                        throw new ArgumentException(L.T("#INVALID_ZIPLINE_ENDPOINT"));
                }
                else if (item.customType == "zipline-component")
                {
                    var parent = objects.Find(o => o.id == item.parentId);
                    if (parent == null || parent.customType != "zipline-endpoint")
                        throw new ArgumentException(L.T("#INVALID_ZIPLINE_COMPONENT"));
                }
                else if (item.customType == "door")
                {
                    int expected = item.doorType == "double" ? 2 : 1;
                    int components = objects.FindAll(o => o.parentId == item.id &&
                        o.customType == "door-component").Count;
                    if (components != expected)
                        throw new ArgumentException(L.T("#INVALID_DOOR_COMPONENTS"));
                }
                else if (item.customType == "door-component")
                {
                    var parent = objects.Find(o => o.id == item.parentId);
                    if (parent == null || parent.customType != "door")
                        throw new ArgumentException(L.T("#INVALID_DOOR_COMPONENTS"));
                }
                else if (item.customType == "curved-zipline")
                {
                    var points = objects.FindAll(o => o.parentId == item.id &&
                        o.customType == "curved-zipline-point");
                    points.Sort((a, b) => ParsePointIndex(a).CompareTo(ParsePointIndex(b)));
                    if (points.Count < 2)
                        throw new ArgumentException(L.T("#CURVED_ZIPLINE_NEEDS_TWO_POINTS"));
                    for (int index = 0; index < points.Count; index++)
                    {
                        if (ParsePointIndex(points[index]) != index)
                            throw new ArgumentException(L.T("#INVALID_CURVED_ZIPLINE_POINT"));
                        if (points[index].customProfile == "")
                            points[index].customProfile = index == 0 ? item.customProfile : "none";
                    }
                    item.customProfile = "";
                    item.curvedZiplineSupport = false;
                }
                else if (item.customType == "curved-zipline-point")
                {
                    var parent = objects.Find(o => o.id == item.parentId);
                    if (parent == null || parent.customType != "curved-zipline" || ParsePointIndex(item) < 0)
                        throw new ArgumentException(L.T("#INVALID_CURVED_ZIPLINE_POINT"));
                }
                else if (item.customType == "curved-zipline-component")
                {
                    var parent = objects.Find(o => o.id == item.parentId);
                    if (parent == null || parent.customType != "curved-zipline-point")
                        throw new ArgumentException(L.T("#INVALID_CURVED_ZIPLINE_POINT"));
                }
            }
            NormalizeZiplineStartPositions();
            ApexCoordinates.ValidateWorld(this);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static int ParsePointIndex(MapObject item) =>
            int.TryParse(item?.customRole, out int index) ? index : -1;
        private void NormalizeZiplineStartPositions()
        {
            foreach (var zipline in objects)
            {
                if (zipline.customType != "zipline") continue;
                var start = objects.Find(item => item.id == zipline.ziplineStartId);
                if (start == null || start.parentId != zipline.id) continue;
                var offset = start.position;
                if (Math.Abs(offset.x) < 0.000001f && Math.Abs(offset.y) < 0.000001f && Math.Abs(offset.z) < 0.000001f)
                    continue;
                // The main custom object owns the start pivot. Hidden endpoint offsets from older
                // saves are removed from both ends so the cable keeps its direction and length.
                foreach (var child in objects)
                    if (child.parentId == zipline.id)
                        child.position = new Float3(child.position.x - offset.x, child.position.y - offset.y,
                            child.position.z - offset.z);
            }
        }


        private static bool IsScriptIdentifier(string value)
        {
            if (value.Length == 0 || !IsIdentifierStart(value[0])) return false;
            for (int i = 1; i < value.Length; i++)
                if (!IsIdentifierStart(value[i]) && (value[i] < '0' || value[i] > '9')) return false;
            return true;
        }

        private static bool IsIdentifierStart(char value) =>
            (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z') || value == '_';
    }
}
