using NUnit.Framework;
using ReMap.Standalone.Core;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.TestTools;

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
            string live = ReMapGameScript.GenerateLiveCommands(restored, restored.objects);
            StringAssert.Contains("script ReMap_CreateTextInfoPanel( \"Movement\"", live);
            StringAssert.Contains("<5, 90, 0>", live);
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
            StringAssert.Contains("table<entity, int> textInfoPanelDeliveries", script);
            StringAssert.Contains("thread ReMap_SendTextInfoPanelsToPlayerWhenReady", script);
            StringAssert.Contains("wait 0.25", script);
            StringAssert.Contains("ReMap_IsTextInfoPanelDeliveryCurrent", script);
            StringAssert.Contains("WaitFrame()\n\t}", script.Replace("\r", ""));
            StringAssert.Contains("int nextTextInfoPanelId = 500", script);
            StringAssert.Contains("int nextTextInfoPanelId = 1000000", script);
            StringAssert.Contains("file.nextTextInfoPanelId += 1000", script);
            StringAssert.Contains("Dev_CreateTextInfoPanelWithID", script);
            StringAssert.Contains("Dev_DestroyTextInfoPanelWithID", script);
            StringAssert.DoesNotContain("MapEditor_CreateTextInfoPanel", script);
            StringAssert.DoesNotContain("ReMap_CreateClientTextInfoPanel", clientScript);
            StringAssert.DoesNotContain("textInfoPanelIds", clientScript);
        }

        [Test]
        public void FlowstateExportsPersistentTextInfoPanelsOnTheServer()
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

            StringAssert.Contains("\tReMap_CreateTextInfoPanel( \"Movement\", \"Jump here\"", code);
            StringAssert.DoesNotContain("ReMap_CreateClientTextInfoPanel", code);
            StringAssert.Contains("script ReMap_CreateTextInfoPanel( \"Movement\", \"Jump here\"", live);
            StringAssert.DoesNotContain("script_client ReMap_CreateClientTextInfoPanel", live);
            StringAssert.Contains("<0, 0, 0>", code);
            StringAssert.Contains("<0, 0, 0>", live);
        }

        [Test]
        public void ReloadedKeepsLegacyPanelDeliveryWhileFlowstateUsesTheConnectionQueue()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string r5r = File.ReadAllText(Path.Combine(root, "scripts/vscripts/remap_r5r/sv_remap_objects.nut"));
            string r5f = File.ReadAllText(Path.Combine(root, "scripts/vscripts/remap_r5f/sv_remap_objects.nut"));

            StringAssert.DoesNotContain("textInfoPanelDeliveries", r5r);
            StringAssert.DoesNotContain("wait 0.25", r5r);
            StringAssert.Contains("thread ReMap_SendTextInfoPanelToPlayerWhenReady( player, panel )", r5r);

            StringAssert.Contains("textInfoPanelDeliveries", r5f);
            StringAssert.Contains("wait 0.25", r5f);
            StringAssert.Contains("thread ReMap_SendTextInfoPanelsToPlayerWhenReady( player, delivery )", r5f);
        }

        [Test]
        public void InstallerSynchronizesFlowstatePanelIdRuntime()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string source = Path.Combine(root, "scripts", "vscripts", "remap_r5f");
            string installed = Path.Combine(Path.GetTempPath(),
                "ReMapPanelRuntime-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(installed);
            try
            {
                foreach (string file in new[] { "sv_remap_objects.nut", "sv_remap_ziplines.nut", "cl_remap_objects.nut" })
                    File.WriteAllText(Path.Combine(installed, file), "outdated");

                var synchronize = typeof(ReMapGameScriptInstaller).GetMethod(
                    "SynchronizeRuntimeScripts", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(synchronize, Is.Not.Null);
                synchronize.Invoke(null, new object[] { installed, GameTargets.R5Flowstate, source });

                string server = File.ReadAllText(Path.Combine(installed, "sv_remap_objects.nut"));
                string client = File.ReadAllText(Path.Combine(installed, "cl_remap_objects.nut"));
                StringAssert.Contains("int nextTextInfoPanelId = 1000000", server);
                StringAssert.Contains("file.nextTextInfoPanelId += 1000", server);
                StringAssert.Contains("ReMap_SendTextInfoPanelsToPlayerWhenReady", server);
                StringAssert.DoesNotContain("ReMap_CreateClientTextInfoPanel", client);
            }
            finally
            {
                if (Directory.Exists(installed)) Directory.Delete(installed, true);
            }
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

        [Test]
        public void EditorPreviewUsesTheNativeFixedPanelSize()
        {
            var world = new WorldView(
                Shader.Find("Universal Render Pipeline/Lit"),
                Shader.Find("ReMap/WorkspaceGrid"),
                Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                var panel = new MapObject {
                    assetId = "custom:text-info-panel", displayName = "Panel",
                    customType = "text-info-panel", isGroup = true,
                    textInfoPanelTitle = "Go", textInfoPanelDescription = "Jump",
                    textInfoPanelShowPin = true
                };
                var document = new MapDocument();
                document.objects.Add(panel);
                world.Sync(document, null);

                var instancesField = typeof(WorldView).GetField("instances",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var instances = (Dictionary<string, GameObject>)instancesField.GetValue(world);
                var marker = instances[panel.id].transform.Find("__remap_text_info_panel_marker");
                var background = marker.Find("background");
                float shortWidth = background.localScale.z;
                float nativeVisibleSize = 120f * ApexCoordinates.MetersPerUnit;

                Assert.That(Mathf.Abs(Mathf.DeltaAngle(marker.localEulerAngles.y, 180f)), Is.LessThan(.001f));
                Assert.That(background.localScale.z, Is.EqualTo(nativeVisibleSize).Within(.001f));
                Assert.That(background.localScale.y, Is.EqualTo(nativeVisibleSize).Within(.001f));
                Assert.That(background.localPosition.y,
                    Is.EqualTo(-nativeVisibleSize * .5f).Within(.001f));
                Assert.That(marker.Find("title").GetComponent<TextMesh>().text, Is.EqualTo("Go"));
                Assert.That(marker.Find("description").GetComponent<TextMesh>().text, Is.EqualTo("Jump"));
                Assert.That(marker.Find("pin").gameObject.activeSelf, Is.True);
                Assert.That(background.GetComponent<Renderer>().sharedMaterial.renderQueue,
                    Is.EqualTo((int)UnityEngine.Rendering.RenderQueue.Transparent));
                Assert.That(background.GetComponent<Renderer>().sharedMaterial.GetColor("_BaseColor").a,
                    Is.EqualTo(.7f).Within(.001f));

                panel.textInfoPanelTitle = "A considerably longer information panel title";
                panel.textInfoPanelShowPin = false;
                world.Sync(document, null);

                Assert.That(marker.Find("background").localScale.z,
                    Is.EqualTo(shortWidth).Within(.001f));
                Assert.That(marker.Find("background").GetComponent<Renderer>().bounds.size.z,
                    Is.GreaterThanOrEqualTo(marker.Find("title").GetComponent<Renderer>().bounds.size.z));
                Assert.That(marker.Find("pin").gameObject.activeSelf, Is.False);
            }
            finally
            {
                bool previous = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;
                try { world.Dispose(); }
                finally { LogAssert.ignoreFailingMessages = previous; }
            }
        }
    }
}
