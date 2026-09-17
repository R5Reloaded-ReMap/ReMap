using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class JumpTowerTests
    {
        [Test]
        public void SavesValidatesAndExportsHeightAndModels()
        {
            var document = new MapDocument {
                name = "tower", editingMap = "mp_rr_desertlands_hu"
            };
            document.objects.Add(new MapObject {
                assetId = "custom:jump-tower", displayName = "Jump tower", customType = "jump-tower",
                isGroup = true, position = ApexCoordinates.ToUnity(new Float3(10, 20, 30)),
                jumpTowerHeight = 2400f
            });
            document.Validate();
            var restored = new UnityMapCodec().Decode(new UnityMapCodec().Encode(document));

            string code = ReMapGameScript.Generate(restored, restored.objects);
            StringAssert.Contains("PrecacheModel( $\"mdl/props/zipline_balloon/zipline_balloon_base.rmdl\" )", code);
            StringAssert.Contains("PrecacheModel( $\"mdl/props/zipline_balloon/zipline_balloon.rmdl\" )", code);
            StringAssert.Contains("ReMap_CreateJumpTower( <10, 20, 30>, <0, 0, 0>, 2400 )", code);
            StringAssert.Contains("script ReMap_CreateJumpTower( <10, 20, 30>, <0, 0, 0>, 2400 )",
                ReMapGameScript.GenerateLiveCommands(restored, restored.objects));
        }

        [Test]
        public void ApexImplementationBuildsEveryFunctionalPartWithoutLegacyWrapper()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = File.ReadAllText(Path.Combine(root,
                "scripts/vscripts/source/sv_remap_objects.source.nut"));
            StringAssert.Contains("void function ReMap_CreateJumpTower", script);
            StringAssert.Contains("REMAP_JUMP_TOWER_BASE_MODEL", script);
            StringAssert.Contains("ReMap_CreateZipline( topCable", script);
            StringAssert.Contains("ForcedSkydiveTriggerThink_EnterCallback", script);
            StringAssert.DoesNotContain("ReMapCreateJumpTower(", script);
            StringAssert.DoesNotContain("MapEditor_", script);
        }

        [TestCase(127f)]
        [TestCase(65536f)]
        public void RejectsInvalidHeight(float height)
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject {
                customType = "jump-tower", assetId = "custom:jump-tower",
                displayName = "Jump tower", isGroup = true, jumpTowerHeight = height
            });
            Assert.Throws<System.ArgumentException>(document.Validate);
        }
    }
}
