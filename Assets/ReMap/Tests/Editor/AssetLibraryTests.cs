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
        [Test] public void SelectedMapArchivesContainOnlyExistingExactFiles()
        {
            var result = RsxAssetLibrary.SelectMapArchives(
                new[] { "mp_alpha", "mp_beta", "mp_alpha" },
                new[] { "mp_alpha.rpak", "mp_alpha_client_perm.rpak", "mp_beta.rpak", "unrelated.rpak" });
            Assert.That(result, Is.EqualTo(new[] {
                "mp_alpha.rpak", "mp_alpha_client_perm.rpak", "mp_beta.rpak"
            }));
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
