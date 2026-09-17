using NUnit.Framework;
using ReMap.Standalone.Core;
using System.Linq;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class ZiplineTests
    {
        [Test]
        public void AutomaticEndDefaultsTwentyFiveApexUnitsAboveSurface()
        {
            var zipline = new MapObject { customType = "zipline" };

            Assert.That(zipline.ziplineEndOffset,
                Is.EqualTo(MapObject.DefaultZiplineEndOffsetApex));
            Assert.That(zipline.ziplineEndOffset, Is.EqualTo(25f));
        }

        [Test]
        public void EndPositionLockDefaultsOffAndSurvivesSaveRoundTrip()
        {
            var document = Document();
            Assert.That(document.objects[0].ziplineLockEnd, Is.False);

            document.objects[0].ziplineLockEnd = true;
            var restored = new UnityMapCodec().Decode(new UnityMapCodec().Encode(document));

            Assert.That(restored.objects[0].ziplineLockEnd, Is.True);
        }

        [Test]
        public void NewZiplineDefaultsToVerticalArmAndSupport()
        {
            var factory = typeof(ReMapApp).GetMethod("CreateDefaultZiplineObjects",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(factory, Is.Not.Null);
            var objects = (MapObject[])factory.Invoke(null,
                new object[] { new Vector3(1, 2, 3), "" });
            MapObject zipline = objects[0], start = objects[1], end = objects[2];

            Assert.That(zipline.ziplineMode, Is.EqualTo("vertical"));
            Assert.That(start.customProfile, Is.EqualTo("support"));
            Assert.That(end.customProfile, Is.EqualTo("none"));
            Assert.That(WorldView.ToVector(end.position), Is.EqualTo(
                Vector3.down * ApexCoordinates.MetersPerUnit * 400f));
        }

        private static MapDocument Document()
        {
            var document = new MapDocument { name = "zipline", gameTarget = GameTargets.R5Flowstate, editingMap = "mp_rr_desertlands_hu" };
            var zipline = new MapObject { assetId = "custom:zipline", displayName = "Zipline", isGroup = true,
                customType = "zipline", ziplineMode = "horizontal", ziplineWidth = 2, ziplineSpeed = 1, ziplineLengthScale = .9f };
            var start = new MapObject { assetId = "custom:zipline-endpoint:start", displayName = "Start", isGroup = true,
                customType = "zipline-endpoint",
                customRole = "start", customProfile = "arm", parentId = zipline.id };
            var end = new MapObject { assetId = "custom:zipline-endpoint:end", displayName = "End", isGroup = true,
                customType = "zipline-endpoint",
                customRole = "end", customProfile = "support", parentId = zipline.id, position = new Float3(10.16f, 0, 0) };
            var startArm = new MapObject { assetId = "apex:zipline-arm", displayName = "Zipline arm",
                gameModelPath = "mdl/industrial/zipline_arm.rmdl", customType = "zipline-component", customRole = "arm", parentId = start.id };
            var endSupport = new MapObject { assetId = "apex:zipline-support", displayName = "Zipline support",
                gameModelPath = "mdl/industrial/security_fence_post.rmdl", customType = "zipline-component", customRole = "support", parentId = end.id };
            var endArm = new MapObject { assetId = "apex:zipline-arm", displayName = "Zipline arm",
                gameModelPath = "mdl/industrial/zipline_arm.rmdl", customType = "zipline-component", customRole = "arm", parentId = end.id };
            zipline.ziplineStartId = start.id; zipline.ziplineEndId = end.id;
            document.objects.AddRange(new[] { zipline, start, end, startArm, endSupport, endArm });
            return document;
        }

        [Test]
        public void ValidatesAndExportsSingleCompositeZipline()
        {
            var document = Document(); document.Validate();
            string code = ReMapGameScript.Generate(document, document.objects);
            StringAssert.Contains("Sh_ReMap_Clear()", code);
            StringAssert.DoesNotContain("ReMap_ClearZiplines()", code);
            StringAssert.Contains("ReMap_CreateZipline( <0, 0, 0>, <0, 0, 0>, <400, 0, 0>, <0, 0, 0>, REMAP_ZIPLINE_END_ARM, REMAP_ZIPLINE_END_SUPPORT, false, 2, 1, 180, 180, true, 0.9, 0, -1, 1, false, true, 100, 100, false, false, false )", code);
            StringAssert.DoesNotContain("PrecacheModel( $\"mdl/industrial/zipline_arm.rmdl\" )", code);
            StringAssert.DoesNotContain("ReMap_CreateProp( $\"mdl/industrial/zipline_arm.rmdl\"", code);
            StringAssert.DoesNotContain("ReMap_CreateProp( $\"mdl/industrial/security_fence_post.rmdl\"", code);
        }

        [Test]
        public void R5ReloadedExportsTheSameZiplineProfilesAndSettings()
        {
            var document = Document();
            document.gameTarget = GameTargets.R5Reloaded;
            document.Validate();
            string code = ReMapGameScript.Generate(document, document.objects);
            StringAssert.Contains("ReMap_CreateZipline( <0, 0, 0>, <0, 0, 0>, <400, 0, 0>, <0, 0, 0>, REMAP_ZIPLINE_END_ARM, REMAP_ZIPLINE_END_SUPPORT, false, 2, 1, 180, 180, true, 0.9, 0, -1, 1, false, true, 100, 100, false, false, false )", code);
        }

        [Test]
        public void VerticalZiplineExportsPushOffAngle()
        {
            var document = Document();
            document.objects[0].ziplineMode = "vertical";
            document.objects[0].ziplinePushOffAngle = 45f;
            string code = ReMapGameScript.Generate(document, document.objects);
            StringAssert.Contains(", true, 2, 1, 180, 180, true, 0.9, 45, -1, 1, false, true, 100, 100, false, false, false )", code);
        }
        [Test]
        public void VerticalZiplineExportsNegativePushOffAngle()
        {
            var document = Document();
            document.objects[0].ziplineMode = "vertical";
            document.objects[0].ziplinePushOffAngle = -45f;
            string code = ReMapGameScript.Generate(document, document.objects);
            StringAssert.Contains(", true, 2, 1, 180, 180, true, 0.9, -45, -1, 1, false, true, 100, 100, false, false, false )", code);
        }

        [Test]
        public void ExportsAdvancedGameplaySettings()
        {
            var document = Document(); var zipline = document.objects[0];
            zipline.ziplineFadeDistance = 5000f;
            zipline.ziplineScale = 1.25f;
            zipline.ziplinePreserveVelocity = true;
            zipline.ziplineDropToBottom = false;
            zipline.ziplineAutoDetachStart = 50f;
            zipline.ziplineAutoDetachEnd = 25f;
            zipline.ziplineRestPoint = true;
            zipline.ziplineDetachEndOnSpawn = true;
            zipline.ziplineDetachEndOnUse = true;
            string code = ReMapGameScript.Generate(document, document.objects);
            StringAssert.Contains(", 0, 5000, 1.25, true, false, 50, 25, true, true, true )", code);
        }

        [Test]
        public void AutoDetachGuidesUseApexDistancesAtBothEnds()
        {
            bool visible = WorldView.TryZiplineDetachGuides(Vector3.zero, Vector3.right * 10f,
                100f, 50f, out var startEnd, out var endStart);
            Assert.That(visible, Is.True);
            Assert.That(startEnd.x, Is.EqualTo(100f * ApexCoordinates.MetersPerUnit).Within(.0001f));
            Assert.That(endStart.x, Is.EqualTo(10f - 50f * ApexCoordinates.MetersPerUnit).Within(.0001f));
            Assert.That(WorldView.TryZiplineDetachGuides(Vector3.zero, Vector3.right,
                100f, 100f, out _, out _), Is.False);
        }

        [Test]
        public void VerticalPushOffAngleUsesWorldAxes()
        {
            Vector3 zero = WorldView.ZiplinePushDirection(0f);
            Vector3 fortyFive = WorldView.ZiplinePushDirection(45f);

            Assert.That(zero, Is.EqualTo(Vector3.right));
            Assert.That(fortyFive.x, Is.EqualTo(Mathf.Sqrt(.5f)).Within(.0001f));
            Assert.That(fortyFive.y, Is.Zero.Within(.0001f));
            Assert.That(fortyFive.z, Is.EqualTo(Mathf.Sqrt(.5f)).Within(.0001f));
        }


        [Test]
        public void DuplicateKeepsItsOwnEndpoints()
        {
            var document = Document(); string source = document.objects[0].id;
            string duplicate = MapHierarchy.Duplicate(document, source); document.Validate();
            var zipline = document.objects.Single(o => o.id == duplicate);
            Assert.That(zipline.ziplineStartId, Is.Not.EqualTo(document.objects[0].ziplineStartId));
            Assert.That(document.objects.Single(o => o.id == zipline.ziplineStartId).parentId, Is.EqualTo(duplicate));
            Assert.That(document.objects.Single(o => o.id == zipline.ziplineEndId).parentId, Is.EqualTo(duplicate));
            Assert.That(document.objects.Count(o => o.customType == "zipline-component" &&
                MapHierarchy.Subtree(document, duplicate).Contains(o.id)), Is.EqualTo(3));
        }

        [Test]
        public void AssemblyInsertKeepsItsOwnZiplineEndpoints()
        {
            var assembly = Document();
            var destination = new MapDocument();
            string duplicate = MapAssembly.Insert(destination, assembly, new Float3(3, 4, 5));
            destination.Validate();
            var zipline = destination.objects.Single(o => o.id == duplicate);
            Assert.That(zipline.ziplineStartId, Is.Not.EqualTo(assembly.objects[0].ziplineStartId));
            Assert.That(destination.objects.Single(o => o.id == zipline.ziplineStartId).parentId, Is.EqualTo(duplicate));
            Assert.That(destination.objects.Single(o => o.id == zipline.ziplineEndId).parentId, Is.EqualTo(duplicate));
        }

        [Test]
        public void RejectsMissingEndpoint()
        {
            var document = Document(); document.objects.RemoveAll(o => o.customRole == "end");
            Assert.Throws<System.ArgumentException>(() => document.Validate());
        }

        [Test]
        public void MainObjectPivotOwnsTheHiddenStartEndpointPosition()
        {
            var document = Document();
            var zipline = document.objects[0];
            var start = document.objects.Single(item => item.id == zipline.ziplineStartId);
            var end = document.objects.Single(item => item.id == zipline.ziplineEndId);
            zipline.position = new Float3(11, 12, 13);
            start.position = new Float3(2, 3, 4);
            end.position = new Float3(8, 10, 12);

            document.Validate();

            Assert.That(zipline.position.x, Is.EqualTo(11));
            Assert.That(zipline.position.y, Is.EqualTo(12));
            Assert.That(zipline.position.z, Is.EqualTo(13));
            Assert.That(start.position.x, Is.Zero);
            Assert.That(start.position.y, Is.Zero);
            Assert.That(start.position.z, Is.Zero);
            Assert.That(end.position.x, Is.EqualTo(6));
            Assert.That(end.position.y, Is.EqualTo(7));
            Assert.That(end.position.z, Is.EqualTo(8));
        }

        [Test]
        public void ParentFolderPivotCompensationDoesNotResetZiplineLocalPosition()
        {
            var document = Document();
            var zipline = document.objects[0];
            var start = document.objects.Single(item => item.id == zipline.ziplineStartId);
            var folder = new MapObject { displayName = "Platform", isGroup = true,
                position = new Float3(100, 200, 300) };
            document.objects.Insert(0, folder);
            zipline.parentId = folder.id;
            zipline.position = new Float3(15, 25, 35);
            var worldBefore = new Float3(
                folder.position.x + zipline.position.x,
                folder.position.y + zipline.position.y,
                folder.position.z + zipline.position.z);

            folder.position = new Float3(107, 197, 311);
            zipline.position = new Float3(8, 28, 24);
            document.Validate();

            Assert.That(zipline.position.x, Is.EqualTo(8));
            Assert.That(zipline.position.y, Is.EqualTo(28));
            Assert.That(zipline.position.z, Is.EqualTo(24));
            Assert.That(folder.position.x + zipline.position.x, Is.EqualTo(worldBefore.x));
            Assert.That(folder.position.y + zipline.position.y, Is.EqualTo(worldBefore.y));
            Assert.That(folder.position.z + zipline.position.z, Is.EqualTo(worldBefore.z));
            Assert.That(start.position.x, Is.Zero);
            Assert.That(start.position.y, Is.Zero);
            Assert.That(start.position.z, Is.Zero);
        }

        [Test]
        public void LegacyAutomaticModeMigratesToHorizontal()
        {
            var document = Document();
            document.objects[0].ziplineMode = "auto";
            document.Validate();
            Assert.That(document.objects[0].ziplineMode, Is.EqualTo("horizontal"));
        }

        [Test]
        public void RejectsInvalidLengthScale()
        {
            var document = Document();
            document.objects[0].ziplineLengthScale = 1.201f;
            Assert.Throws<System.ArgumentException>(() => document.Validate());
        }

        [TestCase(-1.001f, 1f, 100f, 100f)]
        [TestCase(-1f, .009f, 100f, 100f)]
        [TestCase(-1f, 1f, -.001f, 100f)]
        [TestCase(-1f, 1f, 100f, 65535.1f)]
        public void RejectsInvalidAdvancedSettings(float fade, float scale, float detachStart, float detachEnd)
        {
            var document = Document(); var zipline = document.objects[0];
            zipline.ziplineFadeDistance = fade; zipline.ziplineScale = scale;
            zipline.ziplineAutoDetachStart = detachStart; zipline.ziplineAutoDetachEnd = detachEnd;
            Assert.Throws<System.ArgumentException>(() => document.Validate());
        }

        [TestCase(float.NaN)]
        [TestCase(-360.001f)]
        [TestCase(360.001f)]
        public void RejectsInvalidPushOffAngle(float angle)
        {
            var document = Document();
            document.objects[0].ziplinePushOffAngle = angle;
            Assert.Throws<System.ArgumentException>(() => document.Validate());
        }

        [TestCase(-360f)]
        [TestCase(0f)]
        [TestCase(360f)]
        public void AcceptsPushOffAngleRangeBoundaries(float angle)
        {
            var document = Document();
            document.objects[0].ziplinePushOffAngle = angle;
            Assert.DoesNotThrow(() => document.Validate());
        }

        [TestCase(69.999f)]
        [TestCase(290.001f)]
        public void RejectsArmHeightOutsideSupportedRange(float height)
        {
            var document = Document();
            document.objects.Single(candidate => candidate.customRole == "start")
                .ziplineArmHeight = height;
            Assert.Throws<System.ArgumentException>(() => document.Validate());
        }
    }
}
