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
            StringAssert.Contains("trigger.SetAboveHeight( halfHeight )", script);
            StringAssert.DoesNotContain("MapEditor_", script);
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
