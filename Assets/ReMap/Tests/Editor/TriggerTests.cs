using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class TriggerTests
    {
        [Test]
        public void SavesValidatesAndExportsCallbacks()
        {
            var document = new MapDocument {
                name = "trigger", editingMap = "mp_rr_desertlands_hu"
            };
            document.objects.Add(new MapObject {
                assetId = "custom:trigger", displayName = "Trigger", customType = "trigger",
                isGroup = true, position = ApexCoordinates.ToUnity(new Float3(10, 20, 30)),
                triggerRadius = 120f, triggerHalfHeight = 60f, triggerDebug = true,
                realmId = 4, triggerEnterCallback = "if ( ent.IsPlayer() )\n\tprintt( \"enter\" )",
                triggerLeaveCallback = "printt( \"leave\" )"
            });
            document.Validate();
            var restored = new UnityMapCodec().Decode(new UnityMapCodec().Encode(document));

            string code = ReMapGameScript.Generate(restored, restored.objects);
            StringAssert.Contains("ReMap_CreateTrigger( <10, 20, 30>, <0, 0, 0>, 120, 60, true, 4 )", code);
            StringAssert.Contains("remapTrigger0.SetEnterCallback( void function( entity trigger, entity ent )", code);
            StringAssert.Contains("printt( \"enter\" )", code);
            StringAssert.Contains("remapTrigger0.SetLeaveCallback", code);
            StringAssert.Contains("DispatchSpawn( remapTrigger0 )", code);
            StringAssert.DoesNotContain("ReMap_CreateTrigger", ReMapGameScript.GenerateLiveCommands(restored, restored.objects));
        }

        [Test]
        public void ApexImplementationIsStandaloneAndUnspawnedUntilCallbacksAreSet()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = File.ReadAllText(Path.Combine(root,
                "scripts/vscripts/source/sv_remap_objects.source.nut"));
            StringAssert.Contains("entity function ReMap_CreateTrigger", script);
            StringAssert.Contains("CreateEntity( \"trigger_cylinder\" )", script);
            StringAssert.Contains("global function ReMap_TeleportPlayer", script);
            StringAssert.Contains("trigger.SetAboveHeight( halfHeight )", script);
            StringAssert.DoesNotContain("MapEditor_", script);
        }

        [Test]
        public void TeleportTriggerUsesItsDestinationAndKeepsCustomEnterCode()
        {
            var document = new MapDocument { name = "trigger", editingMap = "mp_rr_desertlands_hu" };
            var trigger = new MapObject {
                assetId = "custom:trigger", displayName = "Fall recovery", customType = "trigger",
                isGroup = true, triggerRadius = 120f, triggerHalfHeight = 60f,
                triggerTeleportEnabled = true, triggerTeleportPlaySound = true,
                triggerEnterCallback = "printt( \"recovered\" )"
            };
            document.objects.Add(trigger);
            document.objects.Add(new MapObject {
                assetId = "custom:trigger-teleport-target", displayName = "Destination",
                customType = "trigger-teleport-target", customRole = "destination", parentId = trigger.id,
                isGroup = true, position = ApexCoordinates.ToUnity(new Float3(100, 200, 300)),
                rotation = WorldView.ToData(ApexDisplay.UnityAngles(new Vector3(0, 90, 0)))
            });

            document.Validate();
            string code = ReMapGameScript.Generate(document, document.objects);

            StringAssert.Contains("ReMap_TeleportPlayer( ent, <100, 200, 300>, <0, 90, 0>, true )", code);
            StringAssert.Contains("printt( \"recovered\" )", code);
            Assert.That(code.Split(new[] { "remapTrigger0.SetEnterCallback" }, System.StringSplitOptions.None).Length - 1, Is.EqualTo(1));
        }

        [Test]
        public void RejectsTeleportTargetWithoutTriggerParent()
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject {
                assetId = "custom:trigger-teleport-target", displayName = "Destination",
                customType = "trigger-teleport-target", customRole = "destination", isGroup = true
            });
            Assert.Throws<System.ArgumentException>(document.Validate);
        }

        [TestCase(0f, 50f)]
        [TestCase(100f, 0f)]
        public void RejectsInvalidDimensions(float radius, float halfHeight)
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject {
                customType = "trigger", assetId = "custom:trigger", displayName = "Trigger",
                isGroup = true, triggerRadius = radius, triggerHalfHeight = halfHeight
            });
            Assert.Throws<System.ArgumentException>(document.Validate);
        }
    }
}
