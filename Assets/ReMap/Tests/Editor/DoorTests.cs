using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class DoorTests
    {
        private static MapDocument Document(string type = "single")
        {
            var document = new MapDocument {
                name = "doors", gameTarget = GameTargets.R5Flowstate,
                editingMap = "mp_rr_desertlands_hu"
            };
            var door = new MapObject {
                assetId = "custom:door", displayName = "Door", isGroup = true,
                customType = "door", doorType = type, doorGold = true, doorSpawnOpen = true,
                position = ApexCoordinates.ToUnity(new Float3(10, 20, 30)),
                rotation = WorldView.ToData(ApexDisplay.UnityAngles(new Vector3(5, 90, 0)))
            };
            int count = type == "double" ? 2 : 1;
            string model = type == "vertical"
                ? "mdl/door/door_canyonlands_large_01_animated.rmdl"
                : type == "horizontal"
                    ? "mdl/door/door_256x256x8_elevatorstyle02_animated.rmdl"
                    : "mdl/door/canyonlands_door_single_02.rmdl";
            for (int index = 0; index < count; index++)
                document.objects.Add(new MapObject {
                    assetId = "custom:door-component:" + index, displayName = "Door panel",
                    customType = "door-component", customRole = index == 0 ? "left" : "right",
                    parentId = door.id, gameModelPath = model
                });
            document.objects.Insert(0, door);
            return document;
        }

        [Test]
        public void NewDoorIsOneSingleConfigurableObject()
        {
            var factory = typeof(ReMapApp).GetMethod("CreateDefaultDoorObjects",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(factory, Is.Not.Null);
            var objects = (MapObject[])factory.Invoke(null,
                new object[] { new Vector3(1, 2, 3), "" });

            Assert.That(objects.Length, Is.EqualTo(2));
            Assert.That(objects[0].customType, Is.EqualTo("door"));
            Assert.That(objects[0].doorType, Is.EqualTo("single"));
            Assert.That(objects[0].doorSpawnOpen, Is.False);
            Assert.That(objects[1].customType, Is.EqualTo("door-component"));
            Assert.That(objects[1].parentId, Is.EqualTo(objects[0].id));
        }

        [TestCase("single", "REMAP_DOOR_SINGLE", "mdl/door/canyonlands_door_single_02.rmdl")]
        [TestCase("double", "REMAP_DOOR_DOUBLE", "mdl/door/canyonlands_door_single_02.rmdl")]
        [TestCase("vertical", "REMAP_DOOR_VERTICAL", "mdl/door/door_canyonlands_large_01_animated.rmdl")]
        [TestCase("horizontal", "REMAP_DOOR_HORIZONTAL", "mdl/door/door_256x256x8_elevatorstyle02_animated.rmdl")]
        public void ValidatesSavesAndExportsEveryDoorType(string type, string constant, string model)
        {
            var document = Document(type);
            document.Validate();
            var restored = new UnityMapCodec().Decode(new UnityMapCodec().Encode(document));
            var door = restored.objects.Single(item => item.customType == "door");
            Assert.That(door.doorType, Is.EqualTo(type));
            Assert.That(door.doorGold, Is.True);
            Assert.That(door.doorSpawnOpen, Is.True);

            string code = ReMapGameScript.Generate(restored, restored.objects);
            StringAssert.Contains("PrecacheModel( $\"" + model + "\" )", code);
            StringAssert.Contains("ReMap_CreateDoor( <10, 20, 30>, <5, 90, 0>, " + constant, code);
            StringAssert.Contains(type == "vertical" || type == "horizontal"
                ? constant + ", false, true )" : constant + ", true, true )", code);
            StringAssert.DoesNotContain("ReMap_CreateProp( $\"" + model + "\"", code);

            string live = ReMapGameScript.GenerateLiveCommands(restored, restored.objects);
            StringAssert.Contains("script ReMap_CreateDoor( <10, 20, 30>", live);
        }

        [Test]
        public void RejectsInvalidTypeAndWrongPanelCount()
        {
            var invalidType = Document();
            invalidType.objects[0].doorType = "garage";
            Assert.Throws<System.ArgumentException>(invalidType.Validate);

            var missingPanel = Document("double");
            missingPanel.objects.RemoveAt(missingPanel.objects.Count - 1);
            Assert.Throws<System.ArgumentException>(missingPanel.Validate);
        }

        [Test]
        public void ApexDoorScriptIsStandaloneFromLegacyMapEditorFunctions()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            foreach (string relative in new[] {
                "scripts/vscripts/source/sv_remap_objects.source.nut",
                "scripts/vscripts/remap_r5r/sv_remap_objects.nut",
                "scripts/vscripts/remap_r5f/sv_remap_objects.nut"
            })
            {
                string script = File.ReadAllText(Path.Combine(root, relative));
                StringAssert.Contains("void function ReMap_CreateDoor", script);
                StringAssert.Contains("entity function ReMap_CreateDoorEntity", script);
                StringAssert.DoesNotContain("MapEditor_", script);
            }
        }

        [Test]
        public void OpeningGuideIsAClearBidirectionalQuarterTurnArrow()
        {
            Vector3 hinge = new Vector3(2f, 3f, 4f);
            var method = typeof(WorldView).GetMethod("DoorOpeningArc", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            Vector3[] points = (Vector3[])method.Invoke(null, new object[] { hinge, Vector3.right, Vector3.forward, Vector3.up, 2f, 18 });

            Assert.That(points.Length, Is.EqualTo(25));
            Assert.That(Vector3.Distance(points[1], hinge + Vector3.right * 2f), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(points[21], hinge + Vector3.forward * 2f), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(points[1], points[0]), Is.GreaterThan(.5f));
            Assert.That(Vector3.Distance(points[1], points[2]), Is.GreaterThan(.5f));
            Assert.That(Vector3.Distance(points[21], points[22]), Is.GreaterThan(.5f));
            Assert.That(Vector3.Distance(points[21], points[24]), Is.GreaterThan(.5f));
        }
    }
}
