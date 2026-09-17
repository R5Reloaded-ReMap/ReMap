using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class SpawnPointTests
    {
        [Test]
        public void SavesValidatesAndExportsTeam()
        {
            var document = new MapDocument {
                name = "spawn", editingMap = "mp_rr_desertlands_hu"
            };
            document.objects.Add(new MapObject {
                assetId = "custom:spawn-point", displayName = "Player spawn", customType = "spawn-point",
                gameModelPath = "mdl/dev/mp_spawn.rmdl",
                position = ApexCoordinates.ToUnity(new Float3(10, 20, 30)), spawnPointTeam = 3
            });
            document.Validate();
            var restored = new UnityMapCodec().Decode(new UnityMapCodec().Encode(document));

            string code = ReMapGameScript.Generate(restored, restored.objects);
            StringAssert.Contains("PrecacheModel( $\"mdl/dev/mp_spawn.rmdl\" )", code);
            StringAssert.Contains("ReMap_CreateSpawnPoint( <10, 20, 30>, <0, 0, 0>, 3 )", code);
            StringAssert.DoesNotContain("ReMap_CreateProp( $\"mdl/dev/mp_spawn.rmdl", code);
        }

        [Test]
        public void ApexImplementationPreservesLegacyGameModes()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = File.ReadAllText(Path.Combine(root,
                "scripts/vscripts/source/sv_remap_objects.source.nut"));
            StringAssert.Contains("CreateEntity( \"info_spawnpoint_human\" )", script);
            StringAssert.Contains("spawnPoint.kv.gamemode_tdm = 1", script);
            StringAssert.Contains("spawnPoint.kv.gamemode_ffa = 1", script);
            StringAssert.Contains("spawnPoint.kv.gamemode_ctf = 1", script);
            StringAssert.DoesNotContain("MapEditor_", script);
        }

        [TestCase(-1)]
        [TestCase(65)]
        public void RejectsInvalidTeam(int team)
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject {
                customType = "spawn-point", assetId = "custom:spawn-point",
                displayName = "Player spawn", spawnPointTeam = team
            });
            Assert.Throws<System.ArgumentException>(document.Validate);
        }
    }
}
