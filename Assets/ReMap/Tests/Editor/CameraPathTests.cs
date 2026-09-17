using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class CameraPathTests
    {
        [Test]
        public void DefaultPathSavesAndExportsToClient()
        {
            var factory = typeof(ReMapApp).GetMethod("CreateDefaultCameraPathObjects",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var objects = (MapObject[])factory.Invoke(null, new object[] { Vector3.zero, "" });
            var document = new MapDocument { name = "camera path", editingMap = "mp_rr_desertlands_hu" };
            document.objects.AddRange(objects);
            var path = document.objects.First(item => item.customType == "camera-path");
            path.cameraPathFov = 100f; path.cameraPathTransitionTime = 4f; path.cameraPathTrackTarget = true;
            document.Validate();
            string code = ReMapGameScript.Generate(document, document.objects);
            StringAssert.Contains("// File: remap/cl_remap_map.nut", code);
            StringAssert.Contains("ReMap_CreateCameraPath( [ <0, 0, 0>, <300, 0, 80>, <600, 100, 0> ]", code);
            StringAssert.Contains(", 100, 4, true, <300, 500, 0> )", code);
            StringAssert.DoesNotContain("CreateClientSidePointCamera", code);
        }

        [Test]
        public void ApexImplementationIsStandaloneClientCode()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = File.ReadAllText(Path.Combine(root, "scripts/vscripts/source/cl_remap_objects.source.nut"));
            StringAssert.Contains("void function ReMap_CreateCameraPath", script);
            StringAssert.Contains("CreateClientSidePointCamera", script);
            StringAssert.Contains("mover.NonPhysicsMoveTo", script);
            StringAssert.Contains("VectorToAngles( target - points[index] )", script);
        }

        [Test]
        public void RejectsInvalidSettings()
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject {
                assetId = "custom:camera-path", displayName = "Camera path", customType = "camera-path",
                isGroup = true, cameraPathTransitionTime = 0f, cameraPathFov = 180f
            });
            Assert.Throws<System.ArgumentException>(document.Validate);
        }
    }
}
