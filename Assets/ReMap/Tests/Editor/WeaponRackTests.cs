using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class WeaponRackTests
    {
        [Test]
        public void SavesValidatesAndExportsHistoricalSettings()
        {
            var document = new MapDocument {
                name = "rack", editingMap = "mp_rr_desertlands_hu"
            };
            document.objects.Add(new MapObject {
                assetId = "custom:weapon-rack", displayName = "Weapon rack",
                customType = "weapon-rack", gameModelPath = "mdl/industrial/gun_rack_arm_down.rmdl",
                weaponRackWeapon = "mp_weapon_wingman", weaponRackRespawnTime = 2.5f,
                position = ApexCoordinates.ToUnity(new Float3(10, 20, 30))
            });
            document.Validate();
            var restored = new UnityMapCodec().Decode(new UnityMapCodec().Encode(document));

            string code = ReMapGameScript.Generate(restored, restored.objects);
            StringAssert.Contains("PrecacheModel( $\"mdl/industrial/gun_rack_arm_down.rmdl\" )", code);
            StringAssert.Contains("PrecacheParticleSystem( $\"P_impact_shieldbreaker_sparks\" )", code);
            StringAssert.Contains("ReMap_CreateWeaponRack( <10, 20, 30>, <0, 0, 0>, \"mp_weapon_wingman\", 2.5 )", code);
            StringAssert.Contains("script ReMap_CreateWeaponRack( <10, 20, 30>, <0, 0, 0>, \"mp_weapon_wingman\", 2.5 )",
                ReMapGameScript.GenerateLiveCommands(restored, restored.objects));
        }

        [Test]
        public void ApexImplementationOwnsRackAndRespawnBehaviour()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = File.ReadAllText(Path.Combine(root,
                "scripts/vscripts/source/sv_remap_objects.source.nut"));
            StringAssert.Contains("entity function ReMap_CreateWeaponRack", script);
            StringAssert.Contains("rack.SetValueForModelKey( REMAP_WEAPON_RACK_MODEL )", script);
            StringAssert.Contains("entity weapon = SpawnGenericLoot", script);
            StringAssert.Contains("weapon.WaitSignal( \"OnItemPickup\" )", script);
            StringAssert.DoesNotContain("entity rack = CreateWeaponRack(", script);
            StringAssert.DoesNotContain("SpawnWeaponOnRack(", script);
            StringAssert.DoesNotContain("MapEditor_", script);
        }

        [TestCase("weapon_wingman", .5f)]
        [TestCase("mp_weapon_wingman\"; quit", .5f)]
        [TestCase("mp_weapon_wingman", -1f)]
        public void RejectsInvalidSettings(string weapon, float respawnTime)
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject {
                customType = "weapon-rack", assetId = "custom:weapon-rack",
                displayName = "Weapon rack", weaponRackWeapon = weapon,
                weaponRackRespawnTime = respawnTime
            });
            Assert.Throws<System.ArgumentException>(document.Validate);
        }
    }
}
