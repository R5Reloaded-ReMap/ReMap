using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class RespawnHealTests
    {
        [TestCase(0, "w_loot_wep_iso_health_main_large.rmdl")]
        [TestCase(1, "w_loot_wep_iso_shield_battery_large.rmdl")]
        [TestCase(2, "w_loot_wep_iso_health_main_small.rmdl")]
        [TestCase(3, "w_loot_wep_iso_shield_battery_small.rmdl")]
        [TestCase(4, "w_loot_wep_iso_phoenix_kit_v1.rmdl")]
        public void SavesValidatesAndExportsEveryHistoricalType(int type, string model)
        {
            var document = new MapDocument { name = "heal", editingMap = "mp_rr_desertlands_hu" };
            document.objects.Add(new MapObject {
                assetId = "custom:respawn-heal", displayName = "Respawn heal",
                customType = "respawn-heal", respawnHealType = type,
                respawnHealRespawnTime = 6f, respawnHealDuration = 5f,
                respawnHealAmount = 25, respawnHealProgressive = true,
                position = ApexCoordinates.ToUnity(new Float3(10, 20, 30))
            });
            document.Validate();
            var restored = new UnityMapCodec().Decode(new UnityMapCodec().Encode(document));
            string code = ReMapGameScript.Generate(restored, restored.objects);
            StringAssert.Contains(model, code);
            StringAssert.Contains("ReMap_CreateRespawnHeal( <10, 20, 30>, " + type + ", 6, 5, 25, true )", code);
        }

        [Test]
        public void ApexImplementationIsIndependentAndFunctional()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = File.ReadAllText(Path.Combine(root, "scripts/vscripts/source/sv_remap_objects.source.nut"));
            StringAssert.Contains("void function ReMap_CreateRespawnHeal", script);
            StringAssert.Contains("trigger.SetEnterCallback( ReMap_OnRespawnHealEnter )", script);
            StringAssert.Contains("StatusEffect_AddEndless", script);
            StringAssert.Contains("table<entity, ReMapRespawnHealState> respawnHeals", script);
            StringAssert.Contains("state.active = true", script);
            StringAssert.Contains("file.respawnHeals[trigger] = state", script);
            StringAssert.Contains("targetHealth = minint(", script);
            StringAssert.Contains("targetShield = minint(", script);
            StringAssert.DoesNotContain("targetHealth = min(", script);
            StringAssert.DoesNotContain("targetShield = min(", script);
            StringAssert.DoesNotContain("trigger.s.remap", script);
            StringAssert.DoesNotContain("ReMapCreateRespawnableHeal(", script);
            StringAssert.DoesNotContain("DetermineHealModel(", script);
            StringAssert.DoesNotContain("MapEditor_", script);
        }

        [TestCase(5, 6f, 5f, 25)]
        [TestCase(0, -1f, 5f, 25)]
        [TestCase(0, 6f, 0f, 25)]
        [TestCase(0, 6f, 5f, 0)]
        public void RejectsInvalidSettings(int type, float respawn, float duration, int amount)
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject {
                customType = "respawn-heal", assetId = "custom:respawn-heal", displayName = "Heal",
                respawnHealType = type, respawnHealRespawnTime = respawn,
                respawnHealDuration = duration, respawnHealAmount = amount
            });
            Assert.Throws<System.ArgumentException>(document.Validate);
        }
    }
}
