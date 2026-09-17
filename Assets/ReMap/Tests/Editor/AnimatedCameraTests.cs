using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class AnimatedCameraTests
    {
        [Test]
        public void SavesValidatesAndExportsMotionSettings()
        {
            var document = new MapDocument { name = "camera", editingMap = "mp_rr_desertlands_hu" };
            document.objects.Add(new MapObject {
                assetId = "custom:animated-camera", displayName = "Animated camera",
                customType = "animated-camera", isGroup = true,
                position = ApexCoordinates.ToUnity(new Float3(10, 20, 30)),
                animatedCameraAngleOffset = 15f, animatedCameraMaxLeft = 30f,
                animatedCameraMaxRight = 45f, animatedCameraRotationTime = 5f,
                animatedCameraTransitionTime = 1.5f
            });
            document.Validate();
            var restored = new UnityMapCodec().Decode(new UnityMapCodec().Encode(document));
            string code = ReMapGameScript.Generate(restored, restored.objects);
            StringAssert.Contains("PrecacheModel( $\"mdl/IMC_base/camera_imc_base_01.rmdl\" )", code);
            StringAssert.Contains("PrecacheModel( $\"mdl/IMC_base/camera_imc_01.rmdl\" )", code);
            StringAssert.Contains("ReMap_CreateAnimatedCamera( <10, 20, 30>, <0, 0, 0>, 15, 30, 45, 5, 1.5 )", code);
            StringAssert.Contains("script ReMap_CreateAnimatedCamera( <10, 20, 30>",
                ReMapGameScript.GenerateLiveCommands(restored, restored.objects));
        }

        [Test]
        public void ApexImplementationIsStandalone()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = File.ReadAllText(Path.Combine(root, "scripts/vscripts/source/sv_remap_objects.source.nut"));
            StringAssert.Contains("entity function ReMap_CreateAnimatedCamera", script);
            StringAssert.Contains("cameraHead.NonPhysicsRotateTo", script);
            StringAssert.DoesNotContain("ReMapCreateCamera(", script);
            StringAssert.DoesNotContain("CAMERA_BASE_MDL", script);
        }

        [TestCase(-1f, 20f, 40f, 4f, 2f)]
        [TestCase(20f, -1f, 40f, 4f, 2f)]
        [TestCase(20f, 20f, 40f, 0f, 2f)]
        [TestCase(20f, 20f, 40f, 4f, -1f)]
        public void RejectsInvalidSettings(float angle, float left, float right, float rotation, float transition)
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject {
                assetId = "custom:animated-camera", displayName = "Animated camera",
                customType = "animated-camera", isGroup = true,
                animatedCameraAngleOffset = angle, animatedCameraMaxLeft = left,
                animatedCameraMaxRight = right, animatedCameraRotationTime = rotation,
                animatedCameraTransitionTime = transition
            });
            if (angle == -1f) document.objects[0].animatedCameraAngleOffset = 361f;
            Assert.Throws<System.ArgumentException>(document.Validate);
        }
    }
}
