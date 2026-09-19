using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class ZiprailTests
    {
        private static MapDocument Document()
        {
            var factory = typeof(ReMapApp).GetMethod("CreateDefaultZiprailObjects",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var objects = (MapObject[])factory.Invoke(null, new object[] { Vector3.zero, "" });
            return new MapDocument {
                name = "ziprail", gameTarget = GameTargets.R5Flowstate,
                editingMap = "mp_rr_desertlands_hu",
                targetMaps = { "mp_rr_desertlands_hu", "mp_rr_divided_moon_mu1" },
                objects = objects.ToList()
            };
        }

        [Test]
        public void NewZiprailUsesNativeDefaultsAndThreeEditablePoints()
        {
            var document = Document();
            document.Validate();
            var root = document.objects[0];

            Assert.That(root.customType, Is.EqualTo("ziprail"));
            Assert.That(root.ziplineWidth, Is.EqualTo(1.75f));
            Assert.That(root.ziplineSpeed, Is.EqualTo(1.75f));
            Assert.That(document.objects.Where(item => item.customType == "ziprail-point")
                .Select(item => item.customProfile),
                Is.EqualTo(new[] { "support", "none", "arm" }));
        }

        [Test]
        public void ZiprailIsRejectedOutsideR5Flowstate()
        {
            var document = Document();
            document.gameTarget = GameTargets.R5Reloaded;

            Assert.Throws<System.ArgumentException>(() => document.Validate());
        }

        [Test]
        public void NutExportKeepsZiprailsInTheNativeEntBundle()
        {
            var document = Document();

            string code = ReMapGameScript.Generate(document, document.objects);

            StringAssert.Contains("// Ziprails are exported through the native .ent bundle.", code);
            StringAssert.DoesNotContain("ReMap_CreateZiprail", code);
            StringAssert.DoesNotContain("REMAP_ZIPRAIL_POINT_", code);
        }

        [Test]
        public void ProfilesExposeBothBuildingClawsAndWallCableClamp()
        {
            var document = Document();
            var points = document.objects.Where(item => item.customType == "ziprail-point").OrderBy(item => item.customRole).ToArray();
            points[1].customProfile = "building-claw-02";
            points[2].customProfile = "wall";

            Assert.DoesNotThrow(() => document.Validate());
            Assert.That(points[1].customProfile, Is.EqualTo("building-claw-02"));
            Assert.That(points[2].customProfile, Is.EqualTo("wall"));

            ReMapEntFragments entities = ReMapEntExporter.Generate(document, document.objects);
            StringAssert.Contains("mdl/props/zip_rail/zip_rail_building_claw_02.rmdl", entities.Script);
            StringAssert.Contains("mdl/props/zip_rail/zip_rail_wall_01.rmdl", entities.Script);
            StringAssert.Contains("mdl/props/zip_rail/zip_rail_ground_claw_01.rmdl", entities.Script);
        }

        [Test]
        public void NutExportDoesNotExposeTheUnstableZiprailRuntimePath()
        {
            var document = Document();
            string code = ReMapGameScript.Generate(document, document.objects, new[] { "mp_rr_divided_moon_mu1.rpak" });

            StringAssert.DoesNotContain("pak_requestload mp_rr_divided_moon_mu1.rpak", code);
            StringAssert.Contains("pak_requestload mp_rr_divided_moon_mu1.rpak", ReMapGameScript.GenerateLiveCommands(document, document.objects, new[] { "mp_rr_divided_moon_mu1.rpak" }));
            StringAssert.DoesNotContain("ReMap_CreateZiprail", code);
            StringAssert.DoesNotContain("REMAP_ZIPRAIL_POINT_", code);
            StringAssert.DoesNotContain("mdl/props/zip_rail/", code);

            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = File.ReadAllText(Path.Combine(root, "scripts/vscripts/source/sv_remap_ziplines.source.nut"));
            StringAssert.DoesNotContain("ReMap_CreateZiprail", script);
            StringAssert.DoesNotContain("REMAP_ZIPRAIL_POINT_", script);
            StringAssert.Contains("PrecacheScriptSound( \"3p_Ziprail_Emit_TowerBy\" )", script);
            StringAssert.DoesNotContain("ReMap_OnZiprailSupportSpawned", script);

            string objectScript = File.ReadAllText(Path.Combine(root, "scripts/vscripts/source/sv_remap_objects.source.nut"));
            StringAssert.Contains("GetEntArrayByScriptName( \"remap_prop\" )", objectScript);
            StringAssert.Contains("prop.HasKey( \"can_mantle\" )", objectScript);
            StringAssert.Contains("prop.AllowMantle()", objectScript);
        }

        [Test]
        public void WallMountOwnsClampAndOnlyEndpointsReceiveCordEnds()
        {
            var document = Document();
            var points = document.objects.Where(item => item.customType == "ziprail-point").ToArray();
            foreach (var point in points) point.customProfile = "wall";
            ReMapEntFragments wall = ReMapEntExporter.Generate(document, document.objects);
            foreach (var point in points) point.customProfile = "support";
            ReMapEntFragments support = ReMapEntExporter.Generate(document, document.objects);

            Assert.That(wall.Script.Split(new[] { "zip_rail_ground_claw_01.rmdl" }, System.StringSplitOptions.None).Length - 1, Is.EqualTo(3));
            Assert.That(wall.Script.Split(new[] { "zip_rail_cord_end_01.rmdl" }, System.StringSplitOptions.None).Length - 1, Is.EqualTo(2));
            StringAssert.DoesNotContain("zip_rail_ground_claw_01.rmdl", support.Script);
            Assert.That(support.Script.Split(new[] { "zip_rail_cord_end_01.rmdl" }, System.StringSplitOptions.None).Length - 1, Is.EqualTo(2));
        }

        [TestCase("support", -6f, 4f, 9f)]
        [TestCase("arm", 16f, -235f, 137f)]
        [TestCase("building-claw-02", 16f, -235f, 137f)]
        [TestCase("wall", -113f, 0f, 38f)]
        public void CableOffsetsUseTheMeasuredClampCenters(string profileId, float x, float y, float z)
        {
            System.Type profiles = typeof(ReMapGameScript).Assembly.GetType("ReMap.Standalone.ReMapZiprailProfiles", true);
            object profile = profiles.GetMethod("Find", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { profileId });
            Vector3 actual = (Vector3)profiles.GetMethod("CableOffsetApex", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { profile });

            Assert.That(actual, Is.EqualTo(new Vector3(x, y, z)));
        }

        [Test]
        public void PreviewCurvePassesThroughEveryNativeNode()
        {
            var controls = new[] { Vector3.zero, new Vector3(3, 2, 1), new Vector3(8, -1, 4) };
            var curve = WorldView.ZiprailPreviewPoints(controls, 8);

            Assert.That(curve.Length, Is.EqualTo(17));
            Assert.That(curve[0], Is.EqualTo(controls[0]));
            Assert.That(curve[8], Is.EqualTo(controls[1]));
            Assert.That(curve[16], Is.EqualTo(controls[2]));
        }
    }
}
