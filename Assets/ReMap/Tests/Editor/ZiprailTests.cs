using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using System.Linq;
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
        public void ExportUsesFloatSupportHeightArray()
        {
            var document = Document();

            string code = ReMapGameScript.Generate(document, document.objects);

            StringAssert.Contains("[ 320.0, 320.0, 320.0 ], 1.75, 1.75", code);
        }

        [Test]
        public void ProfilesExposeBothBuildingClawsWallMountAndGroundCableClamp()
        {
            var document = Document();
            var points = document.objects.Where(item => item.customType == "ziprail-point")
                .OrderBy(item => item.customRole).ToArray();
            points[1].customProfile = "building-claw-02";
            points[2].customProfile = "wall";

            Assert.DoesNotThrow(() => document.Validate());
            Assert.That(points[1].customProfile, Is.EqualTo("building-claw-02"));
            Assert.That(points[2].customProfile, Is.EqualTo("wall"));

            string code = ReMapGameScript.Generate(document, document.objects);
            StringAssert.Contains("REMAP_ZIPRAIL_POINT_BUILDING_CLAW_02", code);
            StringAssert.Contains("REMAP_ZIPRAIL_POINT_WALL", code);
            StringAssert.Contains("mdl/props/zip_rail/zip_rail_building_claw_02.rmdl", code);
            StringAssert.Contains("mdl/props/zip_rail/zip_rail_wall_01.rmdl", code);
            StringAssert.Contains("mdl/props/zip_rail/zip_rail_ground_claw_01.rmdl", code);
        }

        [Test]
        public void ExportUsesNativeZiprailNodesAndKeepsLiveBrokenMoonRpakLoad()
        {
            var document = Document();
            string code = ReMapGameScript.Generate(document, document.objects,
                new[] { "mp_rr_divided_moon_mu1.rpak" });

            StringAssert.DoesNotContain("pak_requestload mp_rr_divided_moon_mu1.rpak", code);
            StringAssert.Contains("pak_requestload mp_rr_divided_moon_mu1.rpak",
                ReMapGameScript.GenerateLiveCommands(document, document.objects,
                    new[] { "mp_rr_divided_moon_mu1.rpak" }));
            StringAssert.Contains("ReMap_CreateZiprail( [ <0, 0, 0>, <300, 100, 80>, <600, 0, 0> ]", code);
            StringAssert.Contains("[ REMAP_ZIPRAIL_POINT_SUPPORT, REMAP_ZIPRAIL_POINT_NONE, REMAP_ZIPRAIL_POINT_ARM ]", code);
            StringAssert.Contains("PrecacheModel( $\"mdl/props/zip_rail/zip_rail_ground_post_01.rmdl\" )", code);
            StringAssert.Contains("PrecacheModel( $\"mdl/props/zip_rail/zip_rail_ground_claw_01.rmdl\" )", code);

            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = File.ReadAllText(Path.Combine(root,
                "scripts/vscripts/source/sv_remap_ziplines.source.nut"));
            StringAssert.Contains("CreateEntity( isEndpoint ? \"ziprail\" : \"script_mover_train_node\" )", script);
            StringAssert.DoesNotContain("CreateEntity( isEndpoint ? \"zipline\" : \"script_mover_train_node\" )", script);
            StringAssert.Contains("node.kv.tangent_type = 0", script);
            StringAssert.Contains("endpoint.kv.isZiprailStart = 1", script);
            StringAssert.DoesNotContain("endpoint.kv.ZiplineVersion", script);
            StringAssert.Contains("nodes[index].LinkToEnt( nodes[index - 1] )", script);
            StringAssert.DoesNotContain("nodes[index - 1].LinkToEnt( nodes[index] )", script);
            StringAssert.Contains("prop.kv.solid = 0", script);
            StringAssert.Contains("prop.kv.contents = 0", script);
            StringAssert.Contains("REMAP_ZIPRAIL_POINT_BUILDING_CLAW_02", script);
            StringAssert.Contains("REMAP_ZIPRAIL_POINT_WALL", script);
            StringAssert.Contains("origin + RotateVector( <-172, 0, 85>, angles )", script);
            StringAssert.DoesNotContain("MapEditor_CreateLinkedZipline", script);
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
