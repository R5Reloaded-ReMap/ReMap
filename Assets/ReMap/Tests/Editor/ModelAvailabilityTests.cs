using NUnit.Framework;
using ReMap.Standalone.Core;
using System.Collections.Generic;

namespace ReMap.Standalone.Tests
{
    public sealed class ModelAvailabilityTests
    {
        private static GameAssetRecord Record(string path, string map = "") => new GameAssetRecord {
            guid = path.GetHashCode().ToString("x16"), modelPath = path,
            origins = new List<AssetOrigin> { new AssetOrigin { mapId = map, archive = map + ".rpak" } }
        };

        [Test]
        public void ModelsMustBelongToCommonOrASelectedRpak()
        {
            var records = new[] { Record("mdl/common.rmdl"), Record("mdl/map.rmdl", "mp_selected") };
            Assert.That(ReMapModelAvailability.HasModel(records, new[] { "mp_other" }, "mdl/common.rmdl"), Is.True);
            Assert.That(ReMapModelAvailability.HasModel(records, new[] { "mp_selected" }, "mdl/map.rmdl"), Is.True);
            Assert.That(ReMapModelAvailability.HasModel(records, new[] { "mp_other" }, "mdl/map.rmdl"), Is.False);
            Assert.That(ReMapModelAvailability.HasModel(records, new[] { "mp_selected" }, "mdl/missing.rmdl"), Is.False);
        }

        [Test]
        public void ZiplineChoicesRequireOnlyTheirOwnModels()
        {
            var arm = Record("mdl/industrial/zipline_arm.rmdl", "mp_selected");
            Assert.That(ReMapModelAvailability.ZiplineProfile(System.Array.Empty<GameAssetRecord>(),
                System.Array.Empty<string>(), "none"), Is.True);
            Assert.That(ReMapModelAvailability.ZiplineProfile(new[] { arm }, new[] { "mp_selected" },
                "arm"), Is.True);
            Assert.That(ReMapModelAvailability.ZiplineProfile(new[] { arm }, new[] { "mp_selected" },
                "support"), Is.False);
        }

        [Test]
        public void ZiprailModelChoicesRequireTheBrokenMoonRpak()
        {
            var records = new[] {
                Record("mdl/props/zip_rail/zip_rail_building_claw_01.rmdl", "mp_rr_divided_moon_mu1"),
                Record("mdl/props/zip_rail/zip_rail_cord_end_01.rmdl", "mp_rr_divided_moon_mu1"),
                Record("mdl/props/zip_rail/zip_rail_ground_base_01.rmdl", "mp_rr_divided_moon_mu1"),
                Record("mdl/props/zip_rail/zip_rail_ground_post_01.rmdl", "mp_rr_divided_moon_mu1"),
                Record("mdl/props/zip_rail/zip_rail_ground_post_top_01.rmdl", "mp_rr_divided_moon_mu1")
            };
            Assert.That(ReMapModelAvailability.ZiprailProfile(
                System.Array.Empty<GameAssetRecord>(), System.Array.Empty<string>(),
                "none"), Is.True);
            Assert.That(ReMapModelAvailability.ZiprailProfile(records,
                new[] { "mp_rr_desertlands_hu" }, "arm"), Is.False);
            Assert.That(ReMapModelAvailability.ZiprailProfile(records,
                new[] { "mp_rr_divided_moon_mu1" }, "arm"), Is.True);
            Assert.That(ReMapModelAvailability.ZiprailProfile(records,
                new[] { "mp_rr_divided_moon_mu1" }, "support"), Is.True);
        }

        [Test]
        public void DoorObjectIsAvailableWhenAtLeastOneVariantExists()
        {
            var vertical = Record("mdl/door/door_canyonlands_large_01_animated.rmdl", "mp_selected");
            Assert.That(ReMapModelAvailability.Door(new[] { vertical }, new[] { "mp_selected" }), Is.True);
            Assert.That(ReMapModelAvailability.Door(new[] { vertical }, new[] { "mp_other" }), Is.False);
        }
    }
}
