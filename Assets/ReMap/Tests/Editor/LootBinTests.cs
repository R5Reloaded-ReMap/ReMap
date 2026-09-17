using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class LootBinTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void SavesValidatesAndExportsEveryHistoricalSkin(int skin)
        {
            var document = new MapDocument {
                name = "loot", editingMap = "mp_rr_desertlands_hu"
            };
            var lootBin = new MapObject {
                assetId = "custom:loot-bin", displayName = "Loot bin", customType = "loot-bin",
                gameModelPath = "mdl/props/loot_bin/loot_bin_01_animated.rmdl",
                lootBinSkin = skin, position = ApexCoordinates.ToUnity(new Float3(10, 20, 30))
            };
            document.objects.Add(lootBin);
            document.Validate();
            var restored = new UnityMapCodec().Decode(new UnityMapCodec().Encode(document));

            string code = ReMapGameScript.Generate(restored, restored.objects);
            StringAssert.Contains("PrecacheModel( $\"mdl/props/loot_bin/loot_bin_01_animated.rmdl\" )", code);
            StringAssert.Contains("ReMap_CreateLootBin( <10, 20, 30>, <0, 0, 0>, " + skin + " )", code);
            StringAssert.DoesNotContain("ReMap_CreateProp( $\"mdl/props/loot_bin", code);
        }

        [Test]
        public void ApexImplementationIsStandalone()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = File.ReadAllText(Path.Combine(root,
                "scripts/vscripts/source/sv_remap_objects.source.nut"));
            StringAssert.Contains("entity function ReMap_CreateLootBin", script);
            StringAssert.Contains("lootBin.SetSkin( skin )", script);
            StringAssert.DoesNotContain("MapEditor_", script);
        }

        [Test]
        public void RejectsUnknownSkin()
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject {
                customType = "loot-bin", lootBinSkin = 4,
                assetId = "custom:loot-bin", displayName = "Loot bin"
            });
            Assert.Throws<System.ArgumentException>(document.Validate);
        }
    }
}
