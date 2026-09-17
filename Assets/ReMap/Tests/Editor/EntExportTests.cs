using NUnit.Framework;
using ReMap.Standalone.Core;
using System;

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
            var rail = new MapObject { customType = "ziprail", isGroup = true, ziplineSpeed = 1.75f };
            var first = new MapObject { customType = "ziprail-point", parentId = rail.id,
                customRole = "0", customProfile = "support", ziplineArmHeight = 320 };
            var middle = new MapObject { customType = "ziprail-point", parentId = rail.id,
                customRole = "1", customProfile = "none", position = new Float3(2, 1, 4) };
            var last = new MapObject { customType = "ziprail-point", parentId = rail.id,
                customRole = "2", customProfile = "arm", position = new Float3(4, 0, 8) };
            ReMapEntFragments result = ReMapEntExporter.Generate(document, new[] { rail, first, middle, last });
            Assert.That(result.ScriptEntityCount, Is.EqualTo(9));
            StringAssert.Contains("\"classname\" \"script_mover_train_node\"", result.Script);
            StringAssert.Contains("\"script_name\" \"script_control_omit_zipline\"", result.Script);
            StringAssert.Contains("mdl/props/zip_rail/zip_rail_ground_post_01.rmdl", result.Script);
            Assert.That(result.Script.Split(new[] { "\"isZiprailStart\" \"1\"" },
                StringSplitOptions.None).Length - 1, Is.EqualTo(2));
        }
    }
}
