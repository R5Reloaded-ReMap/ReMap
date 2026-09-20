using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ReMap.Standalone.Core;

namespace ReMap.Standalone.Tests
{
    public sealed class AssetLibraryTests
    {
        [Test] public void PlatformDirectoryFallsBackToExistingPlatformVariant()
        {
            string root = Path.Combine(Path.GetTempPath(), "remap-platform-" + Guid.NewGuid().ToString("N"));
            try
            {
                string expected = Path.Combine(root, "platform_");
                Directory.CreateDirectory(expected);
                Assert.That(RsxAssetLibrary.FindPlatformDirectory(root), Is.EqualTo(Path.GetFullPath(expected)));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
        [Test] public void SelectedMapArchivesContainOnlyExistingExactFiles()
        {
            var result = RsxAssetLibrary.SelectMapArchives(
                new[] { "mp_alpha", "mp_beta", "mp_alpha" },
                new[] { "mp_alpha.rpak", "mp_alpha_client_perm.rpak", "mp_beta.rpak", "unrelated.rpak" });
            Assert.That(result, Is.EqualTo(new[] {
                "mp_alpha.rpak", "mp_alpha_client_perm.rpak", "mp_beta.rpak"
            }));
        }

        [Test] public void SelectedVariantArchivesIncludeTheExistingBaseMapFirst()
        {
            var result = RsxAssetLibrary.SelectMapArchives(
                new[] { "mp_rr_divided_moon_mu1" },
                new[] {
                    "mp_rr_divided_moon.rpak",
                    "mp_rr_divided_moon_client_perm.rpak",
                    "mp_rr_divided_moon_mu1.rpak",
                    "mp_rr_divided_moon_mu1_client_temp.rpak",
                    "mp_rr_divided_moon_mu2.rpak",
                    "mp_rr_divided_moonlight.rpak"
                });
            Assert.That(result, Is.EqualTo(new[] {
                "mp_rr_divided_moon.rpak",
                "mp_rr_divided_moon_client_perm.rpak",
                "mp_rr_divided_moon_mu1.rpak",
                "mp_rr_divided_moon_mu1_client_temp.rpak"
            }));
        }

        [TestCase("mp_rr_divided_moon_mu1", "mp_rr_divided_moon")]
        [TestCase("mp_rr_divided_moon_mu4", "mp_rr_divided_moon")]
        [TestCase("mp_rr_desertlands_hu", "mp_rr_desertlands")]
        [TestCase("mp_rr_olympus_night", "mp_rr_olympus")]
        [TestCase("mp_rr_canyonlands_tt", "mp_rr_canyonlands")]
        [TestCase("mp_rr_desertlands_64k_x_64k", "mp_rr_desertlands")]
        [TestCase("mp_rr_divided_moonlight", "mp_rr_divided_moonlight")]
        public void BaseMapIdOnlyRecognizesKnownVariantSuffixes(string variant, string expected)
        {
            Assert.That(AssetCompatibility.BaseMapId(variant), Is.EqualTo(expected));
        }

        [Test] public void BaseMapAssetsSupportVariantsButVariantAssetsStaySpecific()
        {
            Assert.That(AssetCompatibility.Supports(false, new[] { "mp_rr_divided_moon" },
                new[] { "mp_rr_divided_moon_mu1" }), Is.True);
            Assert.That(AssetCompatibility.Supports(false, new[] { "mp_rr_divided_moon_mu1" },
                new[] { "mp_rr_divided_moon" }), Is.False);
            Assert.That(AssetCompatibility.Supports(false, new[] { "mp_rr_divided_moon_mu1" },
                new[] { "mp_rr_divided_moon_mu2" }), Is.False);
        }

        private static GameAssetRecord Record(string guid, string name, string map, string archive) => new GameAssetRecord {
            guid = guid, modelPath = name, origins = new List<AssetOrigin> { new AssetOrigin { mapId = map, archive = archive } }
        };
        [Test] public void GuidDedupPreservesOriginsButNotSameNameDifferentGuid()
        {
            var records = GameAssetIndex.Merge(new[] {
                Record("0xABC", "mdl/box.rmdl", "desertlands", "desert.rpak"),
                Record("0000000000000abc", "mdl/box.rmdl", "olympus", "olympus.rpak"),
                Record("ABC", "mdl/box.rmdl", "desertlands", "desert.rpak"),
                Record("abd", "mdl/box.rmdl", "olympus", "olympus.rpak") });
            Assert.That(records.Count, Is.EqualTo(2));
            var box = records.Single(r => r.guid == "0000000000000abc");
            Assert.That(box.origins.Count, Is.EqualTo(2));
            Assert.That(box.Supports(new[] { "desertlands", "olympus" }), Is.True);
            Assert.That(records.Single(r => r.guid.EndsWith("abd")).Supports(new[] { "desertlands", "olympus" }), Is.True);
        }
        [Test] public void CommonIsExplicitAndOneLoadedArchiveIsSufficient()
        {
            var asset = Record("abc", "mdl/common_box.rmdl", "olympus", "common_looking_name.rpak");
            Assert.That(asset.Supports(new[] { "desertlands" }), Is.False);
            Assert.That(asset.Supports(Array.Empty<string>()), Is.False);
            Assert.That(asset.Supports(new[] { "desertlands", "olympus" }), Is.True);
            Assert.That(asset.Supports(new[] { "olympus", "desertlands", "lobby" }), Is.True);
            Assert.That(asset.Supports(new HashSet<string>(new[] { "OLYMPUS" }, StringComparer.OrdinalIgnoreCase)), Is.True);
            asset.origins.Add(new AssetOrigin { archive = "common.rpak" });
            Assert.That(asset.Supports(new[] { "desertlands", "olympus" }), Is.True);
            Assert.That(asset.Supports(Array.Empty<string>()), Is.True);
        }
        [Test] public void AssembliesCanCombineModelsFromDifferentLoadedArchives() {
            var loaded = new[] { "desertlands", "olympus" };
            Assert.That(AssetCompatibility.Supports(false,new[]{"desertlands"},loaded),Is.True);
            Assert.That(AssetCompatibility.Supports(false,new[]{"olympus"},loaded),Is.True);
            Assert.That(AssetCompatibility.Supports(false,new[]{"canyonlands"},loaded),Is.False);
            Assert.That(AssetCompatibility.Supports(false,Array.Empty<string>(),loaded),Is.False);
            Assert.That(AssetCompatibility.Supports(true,Array.Empty<string>(),Array.Empty<string>()),Is.True);
        }
        [Test] public void CsvRetainsModelNamesAndRejectsOtherTypes()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "type,guid,file_name,asset_name\nmdl_,abc,common.rpak,mdl/props/box.rmdl\ntxtr,def,common.rpak,texture/box\nmdl_,abc,common.rpak,mdl/0xABC\n");
                var records = GameAssetIndex.ReadCsv(path, "", "common.rpak");
                Assert.That(records.Count, Is.EqualTo(1)); Assert.That(records[0].Name, Is.EqualTo("box"));
                Assert.That(records[0].guid, Is.EqualTo("0000000000000abc"));
            }
            finally { File.Delete(path); }
        }
        [Test] public void FlowstateCsvExtraVersionColumnsPreserveGuidAndName()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "type,pakver,rsxver,guid,file_name,asset_name\nmdl_,16,v16,abc,common.rpak,mdl/props/box.rmdl\n");
                var record = GameAssetIndex.ReadCsv(path, "", "common.rpak").Single();
                Assert.That(record.guid, Is.EqualTo("0000000000000abc")); Assert.That(record.Name, Is.EqualTo("box"));
                Assert.That(GameAssetIndex.IsSupportedCsvHeader("type,guid,file_name,asset_name"), Is.True);
                Assert.That(GameAssetIndex.IsSupportedCsvHeader("type,guid,missing"), Is.False);
            }
            finally { File.Delete(path); }
        }
        [Test] public void CategoryIsTheFirstFolderAfterMdl()
        {
            Assert.That(Record("abc", "mdl/props/interior/chair.rmdl", "olympus", "olympus.rpak").Category, Is.EqualTo("props"));
            Assert.That(Record("def", "mdl\\weapons\\rifle.rmdl", "olympus", "olympus.rpak").Category, Is.EqualTo("weapons"));
            Assert.That(Record("123", "mdl/box.rmdl", "olympus", "olympus.rpak").Category, Is.EqualTo("Uncategorized"));
            var renamed = Record("456", "mdl/props/chair.rmdl", "olympus", "olympus.rpak");
            Assert.That(renamed.Name, Is.EqualTo("chair")); renamed.modelPath = "mdl/industrial/crane.rmdl";
            Assert.That(renamed.Name, Is.EqualTo("crane")); Assert.That(renamed.Category, Is.EqualTo("industrial"));
        }

        [Test] public void ModelPathComparisonIgnoresDirectorySeparatorStyle()
        {
            Assert.That(GameAssetIndex.SameModelPath(
                @"mdl\industrial\zipline_arm.rmdl",
                "mdl/industrial/zipline_arm.rmdl"), Is.True);
        }

        [Test] public void ActiveCacheKeepsModelsSharedAndVersionsOnlyArchiveIndexes()
        {
            string cache = Path.Combine(Path.GetTempPath(), "ReMapCacheTest-" + Guid.NewGuid().ToString("N"));
            const string first = "111111111111111111111111", second = "222222222222222222222222";
            try
            {
                string legacyModel = Path.Combine(cache, first, "models", "abc"); Directory.CreateDirectory(legacyModel);
                File.WriteAllText(Path.Combine(legacyModel, "complete.txt"), "model.cast");
                string legacyIndex = Path.Combine(cache, first, "index"); Directory.CreateDirectory(legacyIndex);
                File.WriteAllText(Path.Combine(legacyIndex, "first.csv"), "first");
                string active = RsxAssetLibrary.ActivateCacheGeneration(cache, first);
                Assert.That(active, Is.EqualTo(Path.GetFullPath(cache)));
                Assert.That(File.Exists(Path.Combine(cache, "Models", "abc", "complete.txt")), Is.True);
                RsxAssetLibrary.ActivateCacheGeneration(cache, second);
                Assert.That(File.Exists(Path.Combine(cache, "Models", "abc", "complete.txt")), Is.True);
                Assert.That(File.Exists(Path.Combine(cache, "Versions", first, "index", "first.csv")), Is.True);
                Assert.That(Directory.Exists(Path.Combine(cache, "Versions", first, "Models")), Is.False);
                string secondIndex = Path.Combine(cache, "index"); Directory.CreateDirectory(secondIndex);
                File.WriteAllText(Path.Combine(secondIndex, "second.csv"), "second");
                RsxAssetLibrary.ActivateCacheGeneration(cache, first);
                Assert.That(File.Exists(Path.Combine(cache, "Models", "abc", "complete.txt")), Is.True);
                Assert.That(File.Exists(Path.Combine(cache, "index", "first.csv")), Is.True);
                Assert.That(File.Exists(Path.Combine(cache, "Versions", second, "index", "second.csv")), Is.True);
            }
            finally { if (Directory.Exists(cache)) Directory.Delete(cache, true); }
        }
        [Test] public void VersionedModelExportsAreInvalidatedWhenRsxChanges()
        {
            const string active = "222222222222222222222222";
            Assert.That(RsxAssetLibrary.CacheEntryMatchesGeneration(
                new[] { "model.cast", "common.rpak", "textured", active }, active), Is.True);
            Assert.That(RsxAssetLibrary.CacheEntryMatchesGeneration(
                new[] { "model.cast", "common.rpak", "textured", "111111111111111111111111" }, active), Is.False);
            Assert.That(RsxAssetLibrary.CacheEntryMatchesGeneration(
                new[] { "model.cast", "common.rpak", "geometry" }, active), Is.False);
            Assert.That(RsxAssetLibrary.CacheEntryMatchesGeneration(
                new[] { "model.cast", "common.rpak" }, active), Is.True);
        }
        [Test] public void TargetsAndAvailabilitySurviveCopyHistoryAndJson()
        {
            var session = new MapSession();
            session.Edit(d => { d.targetMaps.Add("desertlands"); d.objects.Add(new MapObject { assetId = "apex:abc", gameModelPath = "mdl/box.rmdl", availableMaps = new List<string> { "desertlands" } }); });
            var copy = session.Snapshot(); copy.targetMaps.Add("olympus"); copy.objects[0].availableMaps.Clear();
            Assert.That(session.Snapshot().targetMaps.Count, Is.EqualTo(1));
            Assert.That(session.Snapshot().objects[0].availableMaps.Count, Is.EqualTo(1));
            session.Edit(d => d.targetMaps.Add("olympus")); session.Undo();
            var codec = new UnityMapCodec(); var saved = codec.Decode(codec.Encode(session.Snapshot())); saved.Validate();
            Assert.That(saved.targetMaps, Is.EqualTo(new[] { "desertlands" }));
            Assert.That(saved.objects[0].gameModelPath, Is.EqualTo("mdl/box.rmdl"));
        }
        [Test] public void SelectedGameTargetSurvivesLibraryRestart()
        {
            string root = Path.Combine(Path.GetTempPath(), "ReMapTarget-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                using (var library = new RsxAssetLibrary(root))
                    library.SelectTarget(GameTargets.R5Flowstate);

                using (var restored = new RsxAssetLibrary(root))
                    Assert.That(restored.TargetGame, Is.EqualTo(GameTargets.R5Flowstate));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        [Test] public void AssetExportDirectoryCanBeChangedAndSurvivesRestart()
        {
            string root = Path.Combine(Path.GetTempPath(), "ReMapExportRoot-" + Guid.NewGuid().ToString("N"));
            string export = Path.Combine(Path.GetTempPath(), "ReMapAssetExport-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                using (var library = new RsxAssetLibrary(root))
                {
                    Assert.That(library.AssetExportDirectory, Is.EqualTo(Path.Combine(root, "AssetCache")));
                    library.ConfigureAssetExportDirectory(export);
                    Assert.That(library.AssetExportDirectory, Is.EqualTo(export));
                    Assert.That(Directory.Exists(export), Is.True);
                }

                using (var restored = new RsxAssetLibrary(root))
                {
                    Assert.That(restored.AssetExportDirectory, Is.EqualTo(export));
                    restored.ConfigureAssetExportDirectory("");
                    Assert.That(restored.AssetExportDirectory, Is.EqualTo(Path.Combine(root, "AssetCache")));
                }

                using (var reset = new RsxAssetLibrary(root))
                    Assert.That(reset.AssetExportDirectory, Is.EqualTo(Path.Combine(root, "AssetCache")));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
                if (Directory.Exists(export)) Directory.Delete(export, true);
            }
        }
        [Test] public void RelativeAssetExportDirectoryIsResolvedFromLocalRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "ReMapExportRelative-" + Guid.NewGuid().ToString("N"));
            Assert.That(RsxAssetLibrary.ResolveAssetExportDirectory(root, Path.Combine("exports", "assets")),
                Is.EqualTo(Path.Combine(root, "exports", "assets")));
        }
        [Test] public void CastRejectsNodeOutsideFileAndTruncation()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0x74736163u); writer.Write(1u); writer.Write(1u); writer.Write(0u);
                writer.Write(CastReader.Mesh); writer.Write(100000u); writer.Write(0ul); writer.Write(0u); writer.Write(0u);
                stream.Position = 0; Assert.Throws<InvalidDataException>(() => CastReader.Read(stream));
                stream.SetLength(18); stream.Position = 0; Assert.Throws<InvalidDataException>(() => CastReader.Read(stream));
            }
        }
        [Test] public void DetectsRpakFoldersForBothInstallationLayouts()
        {
            string root = Path.Combine(Path.GetTempPath(), "ReMapPaks-" + Guid.NewGuid().ToString("N"));
            try
            {
                string win64 = Path.Combine(root, "paks", "Win64"); Directory.CreateDirectory(win64);
                File.WriteAllText(Path.Combine(win64, "common.rpak"), "test");
                Assert.That(RsxAssetLibrary.FindPakDirectory(root), Is.EqualTo(Path.GetFullPath(win64)));
                Directory.Delete(Path.Combine(root, "paks"), true);
                string server = Path.Combine(root, "paks", "Win64_server"); Directory.CreateDirectory(server);
                File.WriteAllText(Path.Combine(server, "common.rpak"), "test");
                Assert.That(RsxAssetLibrary.FindPakDirectory(root), Is.EqualTo(Path.GetFullPath(server)));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        [Test] public void FirstLaunchEndsWhenSettingsAreSaved()
        {
            string root = Path.Combine(Path.GetTempPath(), "ReMapFirstLaunch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                using (var library = new RsxAssetLibrary(root))
                {
                    Assert.That(library.FirstLaunch, Is.True);
                    library.SaveSettings();
                    Assert.That(library.FirstLaunch, Is.False);
                }
                using (var restored = new RsxAssetLibrary(root))
                    Assert.That(restored.FirstLaunch, Is.False);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        [Test] public void FindsNamedModdedInstallationsAndRejectsOfficialApex()
        {
            string root = Path.Combine(Path.GetTempPath(), "ReMapInstallDetection-" + Guid.NewGuid().ToString("N"));
            try
            {
                string r5r = Path.Combine(root, "Games", "R5Reloaded", "R5R Library", "LIVE");
                string r5f = Path.Combine(root, "Mods", "R5Flowstate");
                CreateInstallation(r5r, "common_sdk.rpak", true);
                CreateInstallation(r5f, "common_flowstate.rpak", true);

                string official = Path.Combine(root, "steamapps", "common", "Apex Legends");
                CreateInstallation(official, "common.rpak", false);
                File.WriteAllText(Path.Combine(official, "r5apex.exe"), "test");

                Assert.That(RsxAssetLibrary.FindInstallation(GameTargets.R5Reloaded, new[] { root }), Is.EqualTo(Path.GetFullPath(r5r)));
                Assert.That(RsxAssetLibrary.FindInstallation(GameTargets.R5Flowstate, new[] { root }), Is.EqualTo(Path.GetFullPath(r5f)));
                Assert.That(RsxAssetLibrary.IsGameInstallation(official, GameTargets.R5Reloaded), Is.False);
                Assert.That(RsxAssetLibrary.IsGameInstallation(official, GameTargets.R5Flowstate), Is.False);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        [Test] public void DedicatedServerAndTargetSpecificRpakAreRequired()
        {
            string root = Path.Combine(Path.GetTempPath(), "ReMapInstallSignature-" + Guid.NewGuid().ToString("N"));
            try
            {
                string game = Path.Combine(root, "R5Flowstate");
                CreateInstallation(game, "common_flowstate.rpak", false);
                Assert.That(RsxAssetLibrary.IsGameInstallation(game, GameTargets.R5Flowstate), Is.False);
                File.WriteAllText(Path.Combine(game, "r5apex_ds.exe"), "test");
                Assert.That(RsxAssetLibrary.IsGameInstallation(game, GameTargets.R5Flowstate), Is.True);
                Assert.That(RsxAssetLibrary.IsGameInstallation(game, GameTargets.R5Reloaded), Is.False);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        private static void CreateInstallation(string game, string signatureRpak, bool dedicatedServer)
        {
            string paks = Path.Combine(game, "paks", "Win64");
            Directory.CreateDirectory(paks);
            File.WriteAllText(Path.Combine(paks, "common.rpak"), "test");
            if (!string.Equals(signatureRpak, "common.rpak", StringComparison.OrdinalIgnoreCase))
                File.WriteAllText(Path.Combine(paks, signatureRpak), "test");
            if (dedicatedServer) File.WriteAllText(Path.Combine(game, "r5apex_ds.exe"), "test");
        }
        [Test] public void MissingMapTargetsAreIgnoredWithoutDiscardingAvailableOnes()
        {
            var targets = RsxAssetLibrary.KeepAvailableTargets(
                new[] { "mp_rr_desertlands", "mp_rr_flowstate_only", "MP_RR_DESERTLANDS" },
                new[] { "mp_rr_desertlands", "mp_rr_olympus" });
            CollectionAssert.AreEqual(new[] { "mp_rr_desertlands" }, targets);
            CollectionAssert.AreEqual(new[] { "mp_rr_flowstate_only" }, RsxAssetLibrary.FindMissingTargets(
                new[] { "mp_rr_desertlands", "mp_rr_flowstate_only", "MP_RR_FLOWSTATE_ONLY" },
                new[] { "mp_rr_desertlands", "mp_rr_olympus" }));
        }
        [Test] public void PortRequiresEveryGuidAtTheSameInternalPath()
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject { assetId = "apex:abc", displayName = "Crate", gameModelPath = "mdl/props/crate.rmdl" });
            var compatible = new[] { Record("0000000000000abc", "MDL\\PROPS\\CRATE.RMDL", "", "common.rpak") };
            Assert.That(MapPortCompatibility.MissingModels(document, compatible), Is.Empty);
            var wrongPath = new[] { Record("0000000000000abc", "mdl/props/other.rmdl", "", "common.rpak") };
            Assert.That(MapPortCompatibility.MissingModels(document, wrongPath), Is.EqualTo(new[] { "mdl/props/crate.rmdl" }));
            Assert.That(MapPortCompatibility.MissingModels(document, Array.Empty<GameAssetRecord>()), Is.EqualTo(new[] { "mdl/props/crate.rmdl" }));
        }
        [Test] public void PortDisablesObjectsAndCustomParentsUsingMissingModels()
        {
            var document = new MapDocument();
            var door = new MapObject { assetId = "custom:door", customType = "door", isGroup = true };
            var panel = new MapObject { assetId = "apex:abc", parentId = door.id, customType = "door-component", gameModelPath = "mdl/door/panel.rmdl" };
            var compatible = new MapObject { assetId = "apex:def", gameModelPath = "mdl/props/crate.rmdl" };
            document.objects.Add(door);
            document.objects.Add(panel);
            document.objects.Add(compatible);
            var available = new[] { Record("def", "mdl/props/crate.rmdl", "", "common.rpak") };

            string[] missing = MapPortCompatibility.DisableObjectsUsingMissingModels(document, available);

            Assert.That(missing, Is.EqualTo(new[] { "mdl/door/panel.rmdl" }));
            Assert.That(panel.disabled, Is.True);
            Assert.That(door.disabled, Is.True);
            Assert.That(compatible.disabled, Is.False);
        }
        [TestCase(GameTargets.R5Reloaded, 1)]
        [TestCase(GameTargets.R5Flowstate, 1)]
        public void PortRemovesUnsupportedCableSubtrees(string target, int expectedRemoved)
        {
            var document = new MapDocument();
            var ziprail = new MapObject { customType = "ziprail", isGroup = true };
            var point = new MapObject { customType = "ziprail-point", parentId = ziprail.id };
            var curved = new MapObject { customType = "curved-zipline", isGroup = true };
            var curvedPoint = new MapObject { customType = "curved-zipline-point", parentId = curved.id };
            var prop = new MapObject { gameModelPath = "mdl/props/crate.rmdl" };
            document.objects.Add(ziprail);
            document.objects.Add(point);
            document.objects.Add(curved);
            document.objects.Add(curvedPoint);
            document.objects.Add(prop);

            int removed = MapPortCompatibility.RemoveUnsupportedObjects(document, target);

            Assert.That(removed, Is.EqualTo(expectedRemoved));
            Assert.That(document.objects.All(item =>
                GameTargets.SupportsCustomType(target, item.customType)), Is.True);
            Assert.That(document.objects, Contains.Item(prop));
        }
        [Test] public void CastAcceptsBoundedEmptyNode()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0x74736163u); writer.Write(1u); writer.Write(1u); writer.Write(0u);
                writer.Write(CastReader.Model); writer.Write(24u); writer.Write(42ul); writer.Write(0u); writer.Write(0u);
                stream.Position = 0; Assert.That(CastReader.Read(stream)[0].Hash, Is.EqualTo(42ul));
            }
        }
    }
}
