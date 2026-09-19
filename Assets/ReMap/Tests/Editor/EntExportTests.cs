using NUnit.Framework;
using ReMap.Standalone.Core;
using System;
using System.IO;
using System.Text;

namespace ReMap.Standalone.Tests
{
    public sealed class EntExportTests
    {
        private static MapDocument Document() => new MapDocument
        {
            name = "ENT test", editingMap = "mp_rr_divided_moon",
            originOffset = new Float3(1, 2, 3)
        };

        [Test]
        public void ExportsFaithfulObjectsIntoSeparateLumpFragments()
        {
            var prop = new MapObject
            {
                gameModelPath = "mdl/props/crate.rmdl", position = new Float3(1, 2, 3),
                rotation = new Float3(0, 90, 0), scale = new Float3(2, 2, 2), fadeDistance = 4000
            };
            prop.scriptProperties.Add(new ScriptProperty { scope = "kv", name = "renderamt", value = "128" });
            var sound = new MapObject { customType = "sound", soundName = "Arena.Ambient", soundRadius = 600 };
            var soundPoint = new MapObject
            {
                customType = "sound-point", parentId = sound.id, customRole = "0", position = new Float3(4, 5, 6)
            };
            var spawn = new MapObject { customType = "spawn-point", spawnPointTeam = 7 };
            var hint = new MapObject { customType = "window-hint", windowHintHalfHeight = 80, windowHintHalfWidth = 90 };

            ReMapEntFragments result = ReMapEntExporter.Generate(Document(),
                new[] { prop, sound, soundPoint, spawn, hint });

            Assert.That(result.ScriptEntityCount, Is.EqualTo(2));
            StringAssert.Contains("\"model\" \"mdl/props/crate.rmdl\"", result.Script);
            StringAssert.Contains("\"renderamt\" \"128\"", result.Script);
            StringAssert.Contains("\"classname\" \"func_window_hint\"", result.Script);
            StringAssert.Contains("\"classname\" \"ambient_generic\"", result.Sound);
            StringAssert.Contains("\"polyline_segment_0\"", result.Sound);
            StringAssert.Contains("\"teamnumber\" \"7\"", result.Spawn);
            StringAssert.Contains("\"classname\" \"info_spawnpoint_human\"", result.Spawn);
        }

        [Test]
        public void MergesAfterBaseEntitiesAndPreservesHeaderAndTerminalNull()
        {
            string original = "ENTITIES02 num_models=28\r\n{\r\n\"classname\" \"worldspawn\"\r\n}\r\n\0";
            string merged = ReMapEntExporter.Merge(original,
                "{\n\"classname\" \"prop_dynamic\"\n}\n", "test.ent");
            StringAssert.StartsWith("ENTITIES02 num_models=28\r\n", merged);
            StringAssert.Contains("\"classname\" \"worldspawn\"\r\n}\r\n{\r\n\"classname\" \"prop_dynamic\"", merged);
            Assert.That(merged[merged.Length - 1], Is.EqualTo('\0'));
        }

        [Test]
        public void DoorsWithRuntimeOnlyStateRemainNutOnly()
        {
            var door = new MapObject
            {
                customType = "door", isGroup = true, displayName = "Open gold door",
                doorType = "double", doorGold = true, doorSpawnOpen = true
            };
            ReMapEntFragments result = ReMapEntExporter.Generate(Document(), new[] { door });
            Assert.That(result.ScriptEntityCount, Is.Zero);
            Assert.That(result.NutOnlyObjects, Has.Some.Contains("Open gold door"));
        }

        [Test]
        public void ClosedDoubleDoorUsesTwoLinkedNativeDoorEntities()
        {
            var door = new MapObject { customType = "door", isGroup = true, doorType = "double" };
            ReMapEntFragments result = ReMapEntExporter.Generate(Document(), new[] { door });
            Assert.That(result.ScriptEntityCount, Is.EqualTo(2));
            Assert.That(result.Script.Split(new[] { "\"classname\" \"prop_door\"" },
                StringSplitOptions.None).Length - 1, Is.EqualTo(2));
            StringAssert.Contains("\"link_to_guid_0\"", result.Script);
        }

        [Test]
        public void ClassicZiplineExportsNativeEndpointsAndGameplaySettings()
        {
            var start = new MapObject
            {
                customType = "zipline-endpoint", customProfile = "support", ziplineArmHeight = 200
            };
            var end = new MapObject
            {
                customType = "zipline-endpoint", customProfile = "arm", position = new Float3(0, 0, 10)
            };
            var zipline = new MapObject
            {
                customType = "zipline", isGroup = true, ziplineStartId = start.id, ziplineEndId = end.id,
                ziplineWidth = 3, ziplineSpeed = 1.5f, ziplineRestPoint = true,
                ziplinePreserveVelocity = true, ziplineDetachEndOnUse = true
            };
            ReMapEntFragments result = ReMapEntExporter.Generate(Document(), new[] { zipline, start, end });
            Assert.That(result.ScriptEntityCount, Is.EqualTo(5));
            StringAssert.Contains("\"classname\" \"zipline_end\"", result.Script);
            StringAssert.Contains("\"classname\" \"zipline\"", result.Script);
            StringAssert.Contains("\"ZiplineSpeedScale\" \"1.5\"", result.Script);
            StringAssert.Contains("\"ZiplinePreserveVelocity\" \"1\"", result.Script);
            StringAssert.Contains("\"_zipline_rest_point_1\"", result.Script);
        }

        [Test]
        public void CurvedZiplineBakesBezierRopesAndCollisionFreeVisualSupports()
        {
            var curve = new MapObject { customType = "curved-zipline", isGroup = true, curvedZiplineSegments = 4 };
            var first = new MapObject { customType = "curved-zipline-point", parentId = curve.id,
                customRole = "0", customProfile = "support", ziplineArmHeight = 180 };
            var middle = new MapObject { customType = "curved-zipline-point", parentId = curve.id,
                customRole = "1", customProfile = "none", position = new Float3(2, 2, 4) };
            var last = new MapObject { customType = "curved-zipline-point", parentId = curve.id,
                customRole = "2", customProfile = "arm", position = new Float3(4, 0, 8) };
            ReMapEntFragments result = ReMapEntExporter.Generate(Document(), new[] { curve, first, middle, last });
            Assert.That(result.ScriptEntityCount, Is.EqualTo(12));
            StringAssert.Contains("\"classname\" \"move_rope\"", result.Script);
            StringAssert.Contains("\"classname\" \"keyframe_rope\"", result.Script);
            StringAssert.Contains("\"PositionInterpolator\" \"2\"", result.Script);
            StringAssert.Contains("\"solid\" \"0\"", result.Script);
            StringAssert.Contains("\"contents\" \"0\"", result.Script);
        }

        [Test]
        public void R5FlowstateZiprailExportsNativeTrainNodeChainAndModels()
        {
            var document = Document(); document.gameTarget = GameTargets.R5Flowstate;
            var rail = new MapObject { id = "01234567-89ab-cdef-0123-456789abcdef", customType = "ziprail", isGroup = true, ziplineSpeed = 1.75f, ziplineAutoDetachStart = 0f, ziplineAutoDetachEnd = 0f };
            var first = new MapObject { customType = "ziprail-point", parentId = rail.id,
                customRole = "0", customProfile = "support", ziplineArmHeight = 320 };
            var middle = new MapObject { customType = "ziprail-point", parentId = rail.id,
                customRole = "1", customProfile = "none", position = new Float3(2, 1, 4) };
            var last = new MapObject { customType = "ziprail-point", parentId = rail.id,
                customRole = "2", customProfile = "arm", position = new Float3(4, 0, 8) };
            ReMapEntFragments result = ReMapEntExporter.Generate(document, new[] { rail, first, middle, last });
            Assert.That(result.ScriptEntityCount, Is.EqualTo(11));
            Assert.That(result.SoundEntityCount, Is.EqualTo(2));
            Assert.That(result.Script.Split(new[] { "\"classname\" \"script_mover_train_node\"" }, StringSplitOptions.None).Length - 1, Is.EqualTo(3));
            StringAssert.DoesNotContain("\"script_control_omit_zipline\"", result.Script);
            StringAssert.Contains("mdl/props/zip_rail/zip_rail_ground_post_01.rmdl", result.Script);
            Assert.That(result.Script.Split(new[] { "zip_rail_cord_end_01.rmdl" }, StringSplitOptions.None).Length - 1, Is.EqualTo(2));
            StringAssert.Contains("\"script_name\" \"remap_ziprail_support\"", result.Script);
            StringAssert.Contains("\"solid\" \"6\"", result.Script);
            StringAssert.Contains("\"soundName\" \"3p_Ziprail_Emit_TowerBy\"", result.Sound);
            StringAssert.DoesNotContain("\"ZiplineVersion\"", result.Script);
            Assert.That(result.Script.Split(new[] { "\"ziplineMountReverseDistance\" \"0\"" }, StringSplitOptions.None).Length - 1, Is.EqualTo(2));
            StringAssert.DoesNotContain("\"useZiprailAutoDetachSpeed\"", result.Script);
            Assert.That(result.Script.Split(new[] { "\"isZiprailStart\" \"1\"" }, StringSplitOptions.None).Length - 1, Is.EqualTo(2));
            StringAssert.Contains("\"link_to_guid_0\" \"0123456700000001\"", result.Script);
            StringAssert.Contains("\"link_to_guid_0\" \"0123456700000002\"", result.Script);
            StringAssert.Contains("\"link_to_guid_0\" \"0123456700000003\"", result.Script);
            StringAssert.Contains("\"link_to_guid_0\" \"0123456700000004\"", result.Script);
            StringAssert.DoesNotContain("\"link_to_guid_0\" \"0123456700000000\"", result.Script);

            rail.ziplineAutoDetachStart = 100f;
            result = ReMapEntExporter.Generate(document, new[] { rail, first, middle, last });
            Assert.That(result.Script.Split(new[] { "\"useZiprailAutoDetachSpeed\" \"1\"" }, StringSplitOptions.None).Length - 1, Is.EqualTo(1));
            StringAssert.DoesNotContain("\"useZiprailAutoDetachSpeed\" \"0\"", result.Script);
        }

        [Test]
        public void WritesFiveMergedCopiesAndRejectsGeneratedBundleAsItsOwnSource()
        {
            string root = Path.Combine(Path.GetTempPath(), "ReMapEnt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                const string map = "mp_rr_divided_moon";
                const string original = "ENTITIES02 num_models=28\n{\n\"classname\" \"info_player_start\"\n}\n\0";
                foreach (string kind in new[] { "env", "fx", "script", "snd", "spawn" })
                    File.WriteAllText(Path.Combine(root, map + "_" + kind + ".ent"), original,
                        new UTF8Encoding(false));
                var prop = new MapObject { gameModelPath = "mdl/props/export_test.rmdl" };
                string output = ReMapEntExporter.WriteMergedBundle(
                    Path.Combine(root, map + "_script.ent"), Document(), new[] { prop });

                foreach (string kind in new[] { "env", "fx", "script", "snd", "spawn" })
                    Assert.That(File.Exists(Path.Combine(output, map + "_" + kind + ".ent")), Is.True, kind);
                string script = File.ReadAllText(Path.Combine(output, map + "_script.ent"));
                StringAssert.Contains("mdl/props/export_test.rmdl", script);
                Assert.That(script[script.Length - 1], Is.EqualTo('\0'));
                Assert.That(File.Exists(Path.Combine(output, "ReMap-ENT-report.txt")), Is.True);
                string report = File.ReadAllText(Path.Combine(output, "ReMap-ENT-report.txt"));
                StringAssert.Contains("ReVPK from R5Reloaded/r5sdk", report);
                StringAssert.Contains("Kawe Mazidjatari (Mauler125)", report);
                Assert.Throws<InvalidDataException>(() => ReMapEntExporter.WriteMergedBundle(
                    Path.Combine(output, map + "_script.ent"), Document(), new[] { prop }));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [TestCase(GameTargets.R5Reloaded, false)]
        [TestCase(GameTargets.R5Flowstate, true)]
        public void InstallsScriptAndSoundLumpsInTargetMapsDirectory(string target, bool flowstate)
        {
            string root = Path.Combine(Path.GetTempPath(), "remap-ent-install-" + Guid.NewGuid().ToString("N"));
            try
            {
                const string map = "mp_rr_divided_moon";
                const string original = "ENTITIES02 num_models=28\n{\n\"classname\" \"info_player_start\"\n}\n\0";
                const string generated = "ENTITIES02 num_models=28\n{\n\"classname\" \"prop_dynamic\"\n}\n\0";
                string game = Path.Combine(root, "game");
                string platform = Path.Combine(game, flowstate ? "platform_" : "platform");
                string installPlatform = Path.Combine(game, "platform");
                string bundle = Path.Combine(root, "bundle");
                Directory.CreateDirectory(game);
                Directory.CreateDirectory(platform);
                Directory.CreateDirectory(installPlatform);
                Directory.CreateDirectory(bundle);
                File.WriteAllText(Path.Combine(bundle, map + "_script.ent"), generated, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(bundle, map + "_snd.ent"), generated, new UTF8Encoding(false));
                string maps = Path.Combine(flowstate ? installPlatform : platform, "maps");
                Directory.CreateDirectory(maps);
                string destination = Path.Combine(maps, map + "_script.ent");
                string soundDestination = Path.Combine(maps, map + "_snd.ent");
                File.WriteAllText(destination, original, new UTF8Encoding(false));
                File.WriteAllText(soundDestination, original, new UTF8Encoding(false));

                string installed = ReMapEntExporter.InstallScriptLump(bundle, map, target, game, platform);
                string installedSound = ReMapEntExporter.InstallSoundLump(bundle, map, target, game, platform);

                Assert.That(installed, Is.EqualTo(destination));
                Assert.That(installedSound, Is.EqualTo(soundDestination));
                Assert.That(File.ReadAllText(destination), Is.EqualTo(generated));
                Assert.That(File.ReadAllText(soundDestination), Is.EqualTo(generated));
                Assert.That(File.ReadAllText(destination + ".remap.bak"), Is.EqualTo(original));
                Assert.That(File.ReadAllText(soundDestination + ".remap.bak"), Is.EqualTo(original));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
