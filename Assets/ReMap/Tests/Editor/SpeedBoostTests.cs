using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class SpeedBoostTests
    {
        [Test]
        public void SavesValidatesAndExportsHistoricalSettings()
        {
            var document = new MapDocument { name = "boost", editingMap = "mp_rr_desertlands_hu" };
            document.objects.Add(new MapObject {
                assetId = "custom:speed-boost", displayName = "Speed boost",
                customType = "speed-boost", isGroup = true,
                position = ApexCoordinates.ToUnity(new Float3(10, 20, 30)),
                speedBoostColor = new Float3(12, 34, 56), speedBoostRespawnTime = 7f,
                speedBoostStrength = .45f, speedBoostDuration = 4f, speedBoostFadeTime = .75f
            });
            document.Validate();
            var restored = new UnityMapCodec().Decode(new UnityMapCodec().Encode(document));
            string code = ReMapGameScript.Generate(restored, restored.objects);
            StringAssert.Contains("PrecacheModel( $\"mdl/fx/plasma_sphere_01.rmdl\" )", code);
            StringAssert.Contains("PrecacheModel( $\"mdl/fx/ar_edge_sphere_512.rmdl\" )", code);
            StringAssert.Contains("PrecacheParticleSystem( $\"P_sprint_FP\" )", code);
            StringAssert.Contains("ReMap_CreateSpeedBoost( <10, 20, 30>, <12, 34, 56>, 7, 0.45, 4, 0.75 )", code);
        }

        [Test]
        public void ApexImplementationIsStandalone()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = File.ReadAllText(Path.Combine(root, "scripts/vscripts/source/sv_remap_objects.source.nut"));
            StringAssert.Contains("void function ReMap_CreateSpeedBoost", script);
            StringAssert.Contains("StatusEffect_AddTimed( touchingEnt, eStatusEffect.speed_boost", script);
            StringAssert.Contains("trigger.GetTouchingEntities()", script);
            StringAssert.DoesNotContain("ReMapCreateSpeedBoost", script);
            StringAssert.DoesNotContain("MapEditor_", script);
        }

        [TestCase(-1f, .35f, 3f, 0f)]
        [TestCase(5f, -1f, 3f, 0f)]
        [TestCase(5f, .35f, 0f, 0f)]
        [TestCase(5f, .35f, 3f, 4f)]
        public void RejectsInvalidTimingOrStrength(float respawn, float strength, float duration, float fade)
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject {
                assetId = "custom:speed-boost", displayName = "Speed boost",
                customType = "speed-boost", isGroup = true,
                speedBoostRespawnTime = respawn, speedBoostStrength = strength,
                speedBoostDuration = duration, speedBoostFadeTime = fade
            });
            Assert.Throws<System.ArgumentException>(document.Validate);
        }
    }
}
