using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class CurvedZiplineTests
    {
        private static MapDocument Document()
        {
            var factory = typeof(ReMapApp).GetMethod("CreateDefaultCurvedZiplineObjects",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var objects = (MapObject[])factory.Invoke(null, new object[] { Vector3.zero, "" });
            objects[0].curvedZiplineSupport = true;
            return new MapDocument {
                name = "curve", gameTarget = GameTargets.R5Flowstate,
                editingMap = "mp_rr_desertlands_hu", objects = objects.ToList()
            };
        }

        [Test]
        public void NewCurvedZiplineHasThreeEditablePoints()
        {
            var document = Document();
            document.Validate();
            var root = document.objects[0];
            Assert.That(root.customType, Is.EqualTo("curved-zipline"));
            Assert.That(root.curvedZiplineSegments, Is.EqualTo(8));
            Assert.That(document.objects.Count(item => item.customType == "curved-zipline-point"), Is.EqualTo(3));
        }

        [Test]
        public void BezierPreviewPassesThroughEveryControlPoint()
        {
            var controls = new[] { Vector3.zero, new Vector3(3, 2, 1), new Vector3(8, -1, 4) };
            var curve = WorldView.CurvedZiplinePreviewPoints(controls, 8);

            Assert.That(curve.Length, Is.EqualTo(17));
            Assert.That(curve[0], Is.EqualTo(controls[0]));
            Assert.That(curve[8], Is.EqualTo(controls[1]));
            Assert.That(curve[16], Is.EqualTo(controls[2]));
        }

        [Test]
        public void ExportsStandaloneBezierZiplineWithCollisionFreeSupport()
        {
            var document = Document();
            string code = ReMapGameScript.Generate(document, document.objects);
            StringAssert.Contains("ReMap_CreateCurvedZipline( [ <0, 0, 0>, <300, 100, 80>, <600, 0, 0> ], 8, true", code);

            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = File.ReadAllText(Path.Combine(root,
                "scripts/vscripts/source/sv_remap_ziplines.source.nut"));
            StringAssert.Contains("array<vector> function ReMap_BuildBezierPath", script);
            StringAssert.Contains("arm.kv.solid = 0", script);
            StringAssert.Contains("arm.kv.contents = 0", script);
            StringAssert.DoesNotContain("MapEditor_", script);
            StringAssert.DoesNotContain("GetBezierOfPath", script);
            StringAssert.DoesNotContain("GetAllPointsOnBezier", script);
        }

        [Test]
        public void DuplicateKeepsAnIndependentPointHierarchy()
        {
            var document = Document();
            string source = document.objects[0].id;
            string duplicate = MapHierarchy.Duplicate(document, source);
            document.Validate();

            Assert.That(document.objects.Count(item => item.parentId == duplicate &&
                item.customType == "curved-zipline-point"), Is.EqualTo(3));
            Assert.That(document.objects.Where(item => item.parentId == source).Select(item => item.id)
                .Intersect(document.objects.Where(item => item.parentId == duplicate).Select(item => item.id)),
                Is.Empty);
        }
    }
}
