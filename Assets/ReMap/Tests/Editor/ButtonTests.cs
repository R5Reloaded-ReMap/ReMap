using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class ButtonTests
    {
        [Test]
        public void SavesAndExportsVisibleButtonCallback()
        {
            var document = new MapDocument { name = "button", editingMap = "mp_rr_desertlands_hu" };
            document.objects.Add(new MapObject {
                assetId = "custom:button", displayName = "Button", customType = "button",
                customProfile = "wall",
                gameModelPath = "mdl/props/global_access_panel_button/global_access_panel_button_wall.rmdl",
                buttonMode = "visible", buttonUseText = "%use% Launch",
                buttonCallback = "ent.SetVelocity( <0, 0, 500> )"
            });
            document.Validate();
            var restored = new UnityMapCodec().Decode(new UnityMapCodec().Encode(document));
            string code = ReMapGameScript.Generate(restored, restored.objects);
            StringAssert.Contains("PrecacheModel( $\"mdl/props/global_access_panel_button/global_access_panel_button_wall.rmdl\" )", code);
            StringAssert.Contains("ReMap_CreateButton( <0, 0, 0>, <0, 0, 0>, true, true, \"%use% Launch\", $\"mdl/props/global_access_panel_button/global_access_panel_button_wall.rmdl\" )", code);
            StringAssert.Contains("AddCallback_OnUseEntity( remapButton0", code);
            StringAssert.Contains("ent.SetVelocity( <0, 0, 500> )", code);
        }

        [Test]
        public void VisibleButtonTeleportsToChildAndKeepsCallback()
        {
            var document = new MapDocument { name = "button", editingMap = "mp_rr_desertlands_hu" };
            var button = new MapObject {
                assetId = "custom:button", displayName = "Button", customType = "button",
                gameModelPath = "mdl/props/global_access_panel_button/global_access_panel_button_console_w_stand.rmdl",
                buttonMode = "visible", buttonTeleportEnabled = true, buttonTeleportPlaySound = true,
                buttonCallback = "ent.SetVelocity( <0, 0, 500> )"
            };
            document.objects.Add(button);
            document.objects.Add(new MapObject {
                assetId = "custom:button-teleport-target", displayName = "Teleport destination",
                customType = "button-teleport-target", customRole = "destination",
                parentId = button.id, isGroup = true,
                position = ApexCoordinates.ToUnity(new Float3(100, 200, 300)),
                rotation = WorldView.ToData(ApexDisplay.UnityAngles(new Vector3(0, 90, 0)))
            });
            document.Validate();
            var restored = new UnityMapCodec().Decode(new UnityMapCodec().Encode(document));
            string code = ReMapGameScript.Generate(restored, restored.objects);
            StringAssert.Contains("ReMap_AddButtonTeleport( remapButton0, <100, 200, 300>, <0, 90, 0>, true )", code);
            StringAssert.Contains("AddCallback_OnUseEntity( remapButton0", code);
            StringAssert.Contains("ent.SetVelocity( <0, 0, 500> )", code);
        }

        [Test]
        public void ExportsInvisibleTeleportRelativeToButton()
        {
            var document = new MapDocument { name = "button", editingMap = "mp_rr_desertlands_hu" };
            document.objects.Add(new MapObject {
                assetId = "custom:button", displayName = "Button", customType = "button",
                gameModelPath = "mdl/weapons/bullets/damage_arrow.rmdl", buttonMode = "invisible",
                position = ApexCoordinates.ToUnity(new Float3(100, 200, 300)),
                buttonDestination = ApexCoordinates.ToUnity(new Float3(10, 20, 30)),
                buttonDirection = WorldView.ToData(ApexDisplay.UnityAngles(new Vector3(0, 90, 0))),
                buttonMessage = "Go!", buttonSubMessage = "Fast", buttonToken = "#FS_STRING_VAR",
                buttonMessageType = 4, buttonMessageDuration = 3f
            });
            document.Validate();
            string code = ReMapGameScript.Generate(document, document.objects);
            StringAssert.Contains("PrecacheModel( $\"mdl/weapons/bullets/damage_arrow.rmdl\" )", code);
            StringAssert.Contains("ReMap_CreateTeleportButton( <100, 200, 300>", code);
            StringAssert.Contains("<110, 220, 330>", code);
            StringAssert.Contains("\"Go!\", \"Fast\", 4, 3, \"#FS_STRING_VAR\", false )", code);
        }

        [Test]
        public void ApexImplementationIsStandalone()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = File.ReadAllText(Path.Combine(root, "scripts/vscripts/source/sv_remap_objects.source.nut"));
            StringAssert.Contains("entity function ReMap_CreateButton", script);
            StringAssert.Contains("void function ReMap_AddButtonTeleport", script);
            StringAssert.Contains("void function ReMap_CreateTeleportButton", script);
            StringAssert.Contains("EmitSoundOnEntityOnlyToPlayer( player, player, \"PhaseGate_Enter_1p\" )", script);
            StringAssert.Contains("EmitSoundOnEntityExceptToPlayer( player, player, \"PhaseGate_Enter_3p\" )", script);
            StringAssert.DoesNotContain("Wraith_phasegate_Travel", script);
            StringAssert.DoesNotContain("EmitDifferentSoundsOnEntityForPlayerAndWorld", script);
            StringAssert.Contains("panel.MakeInvisible()", script);
            StringAssert.Contains("player.SetVelocity( ZERO_VECTOR )", script);
            StringAssert.DoesNotContain("Invis_Button(", script);
            StringAssert.DoesNotContain("MapEditor_CreateButton", script);
        }

        [TestCase("console", "mdl/props/global_access_panel_button/global_access_panel_button_console.rmdl")]
        [TestCase("wall", "mdl/props/global_access_panel_button/global_access_panel_button_wall.rmdl")]
        [TestCase("console-stand", "mdl/props/global_access_panel_button/global_access_panel_button_console_w_stand.rmdl")]
        public void VisibleButtonProfilesResolveToTheirNativeModels(string profile, string expected)
        {
            var resolver = typeof(ReMapApp).GetMethod("ButtonModelPath", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(resolver.Invoke(null, new object[] { "visible", profile }), Is.EqualTo(expected));
            Assert.That(resolver.Invoke(null, new object[] { "invisible", profile }), Is.EqualTo("mdl/weapons/bullets/damage_arrow.rmdl"));
        }

        [TestCase("other", 4, 5f)]
        [TestCase("visible", -1, 5f)]
        [TestCase("invisible", 4, -1f)]
        public void RejectsInvalidSettings(string mode, int type, float duration)
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject {
                customType = "button", assetId = "custom:button", displayName = "Button",
                buttonMode = mode, buttonMessageType = type, buttonMessageDuration = duration
            });
            Assert.Throws<System.ArgumentException>(document.Validate);
        }

        [Test]
        public void RejectsTeleportTargetWithoutButtonParent()
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject {
                assetId = "custom:button-teleport-target", displayName = "Teleport destination",
                customType = "button-teleport-target", customRole = "destination", isGroup = true
            });
            Assert.Throws<System.ArgumentException>(document.Validate);
        }
    }
}
