using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class TextInfoPanelTests
    {
        [Test]
        public void SavesValidatesAndExportsAllLegacySettings()
        {
            var document = new MapDocument { name = "panel", editingMap = "mp_rr_desertlands_hu" };
            document.objects.Add(new MapObject {
                assetId = "custom:text-info-panel", displayName = "Panel", customType = "text-info-panel",
                isGroup = true, position = ApexCoordinates.ToUnity(new Float3(10, 20, 30)),
                rotation = WorldView.ToData(ApexDisplay.UnityAngles(new Vector3(5, 90, 0))),
                textInfoPanelTitle = "Movement", textInfoPanelDescription = "Jump here",
                textInfoPanelShowPin = false, textInfoPanelScale = 2f
            });
            document.Validate();
            var restored = new UnityMapCodec().Decode(new UnityMapCodec().Encode(document));
            string code = ReMapGameScript.Generate(restored, restored.objects);
            StringAssert.Contains("ReMap_CreateTextInfoPanel( \"Movement\", \"Jump here\", <10, 20, 30>, <5, 90, 0>, false, 2 )", code);
            StringAssert.Contains("script ReMap_CreateTextInfoPanel( \"Movement\"",
                ReMapGameScript.GenerateLiveCommands(restored, restored.objects));
        }

        [Test]
        public void ApexImplementationHandlesCurrentLateAndClearedPanelsWithoutLegacyHelpers()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = File.ReadAllText(Path.Combine(root, "scripts/vscripts/source/sv_remap_objects.source.nut"));
            string clientScript = File.ReadAllText(Path.Combine(root, "scripts/vscripts/source/cl_remap_objects.source.nut"));
            StringAssert.Contains("void function ReMap_CreateTextInfoPanel", script);
            StringAssert.Contains("AddCallback_OnClientConnected( ReMap_SendTextInfoPanelsToPlayer )", script);
            StringAssert.Contains("while ( IsValid( player ) && ( !player.IsPlayer() || !player.p.isConnected ) )", script);
            StringAssert.Contains("int nextTextInfoPanelId = 500", script);
            StringAssert.Contains("Dev_CreateTextInfoPanelWithID", script);
            StringAssert.Contains("Dev_DestroyTextInfoPanelWithID", script);
            StringAssert.DoesNotContain("MapEditor_CreateTextInfoPanel", script);
            StringAssert.Contains("void function ReMap_CreateClientTextInfoPanel", clientScript);
            StringAssert.Contains("dev_infoPanelTitleString = title", clientScript);
            StringAssert.Contains("Dev_CreateTextInfoPanelWithID( origin, angles, showPin, textScale, panelId )", clientScript);
        }

        [Test]
        public void FlowstateExportsTextInfoPanelsOnTheClient()
        {
            var document = new MapDocument
            {
                name = "panel", editingMap = "mp_rr_desertlands_hu",
                gameTarget = GameTargets.R5Flowstate
            };
            var panel = new MapObject
            {
                customType = "text-info-panel", isGroup = true,
                textInfoPanelTitle = "Movement", textInfoPanelDescription = "Jump here"
            };

            string code = ReMapGameScript.Generate(document, new[] { panel });
            string live = ReMapGameScript.GenerateLiveCommands(document, new[] { panel });

            StringAssert.DoesNotContain("\tReMap_CreateTextInfoPanel( ", code);
            StringAssert.Contains("\tReMap_CreateClientTextInfoPanel( \"Movement\", \"Jump here\"", code);
            StringAssert.Contains("script_client ReMap_CreateClientTextInfoPanel( \"Movement\", \"Jump here\"", live);
        }

        [Test]
        public void RejectsTextPastNativePanelLimit()
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject {
                assetId = "custom:text-info-panel", displayName = "Panel", customType = "text-info-panel",
                isGroup = true, textInfoPanelTitle = new string('x', 599)
            });
            Assert.Throws<System.ArgumentException>(document.Validate);
        }
    }
}
