using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class JumpPadTests
    {
        [Test]
        public void SavesValidatesAndExportsLaunchSettings()
        {
            var document = new MapDocument {
                name = "jump pad", editingMap = "mp_rr_desertlands_hu"
            };
            document.objects.Add(new MapObject {
                assetId = "custom:jump-pad", displayName = "Jump pad", customType = "jump-pad",
                gameModelPath = "mdl/props/octane_jump_pad/octane_jump_pad.rmdl",
                position = ApexCoordinates.ToUnity(new Float3(10, 20, 30)),
                jumpPadLaunchVelocity = 1250f, jumpPadForwardScale = 1.5f,
                jumpPadRadius = 52f, jumpPadDoubleJump = false
            });
            document.Validate();
            var restored = new UnityMapCodec().Decode(new UnityMapCodec().Encode(document));

            string code = ReMapGameScript.Generate(restored, restored.objects);
            StringAssert.Contains("PrecacheModel( $\"mdl/props/octane_jump_pad/octane_jump_pad.rmdl\" )", code);
            StringAssert.Contains("ReMap_CreateJumpPad( <10, 20, 30>, <0, 0, 0>, true, 50000, -1, 1, 1250, 1.5, 52, false )", code);
            StringAssert.DoesNotContain("ReMap_CreateProp( $\"mdl/props/octane_jump_pad", code);
        }

        [Test]
        public void ApexImplementationIsStandalone()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = File.ReadAllText(Path.Combine(root,
                "scripts/vscripts/source/sv_remap_objects.source.nut"));
            StringAssert.Contains("entity function ReMap_CreateJumpPad", script);
            StringAssert.Contains("CreateEntity( \"trigger_cylinder_heavy\" )", script);
            StringAssert.Contains("trigger.SetLaunchScaleValues( launchVelocity, forwardScale )", script);
            StringAssert.Contains("file.jumpPadDoubleJump[trigger] <- doubleJump", script);
            StringAssert.Contains("void function ReMap_JumpPadPushEnt", script);
            StringAssert.Contains("ReMap_JumpPadPushEnt( trigger, ent", script);
            StringAssert.Contains("thread ReMap_GiveJumpPadDoubleJump( ent )", script);
            StringAssert.DoesNotContain("\tJumpPadPushEnt( trigger, ent", script);
            StringAssert.DoesNotContain("trigger.s.remapDoubleJump", script);
            StringAssert.DoesNotContain("MapEditor_", script);
        }

        [TestCase(99f, 1.7f, 45f)]
        [TestCase(1000f, 0.09f, 45f)]
        [TestCase(1000f, 1.7f, 0f)]
        public void RejectsInvalidLaunchSettings(float velocity, float forward, float radius)
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject {
                customType = "jump-pad", assetId = "custom:jump-pad", displayName = "Jump pad",
                jumpPadLaunchVelocity = velocity, jumpPadForwardScale = forward,
                jumpPadRadius = radius
            });
            Assert.Throws<System.ArgumentException>(document.Validate);
        }
    }
}
