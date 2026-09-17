using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class WindowHintTests
    {
        [Test]
        public void SavesValidatesAndExportsDimensionsAndRightVector()
        {
            var document = new MapDocument { name = "window", editingMap = "mp_rr_desertlands_hu" };
            document.objects.Add(new MapObject {
                assetId = "custom:window-hint", displayName = "Window", customType = "window-hint",
                isGroup = true, position = ApexCoordinates.ToUnity(new Float3(10, 20, 30)),
                rotation = WorldView.ToData(ApexDisplay.UnityAngles(new Vector3(0, 0, 0))),
                windowHintHalfHeight = 80f, windowHintHalfWidth = 96f
            });
            document.Validate();
            var restored = new UnityMapCodec().Decode(new UnityMapCodec().Encode(document));
            string code = ReMapGameScript.Generate(restored, restored.objects);
            StringAssert.Contains("ReMap_CreateWindowHint( <10, 20, 30>, 80, 96, <1, 0, 0> )", code);
            StringAssert.Contains("script ReMap_CreateWindowHint( <10, 20, 30>",
                ReMapGameScript.GenerateLiveCommands(restored, restored.objects));
        }

        [Test]
        public void RightVectorFollowsTheDisplayedLocalWidthAxis()
        {
            var document = new MapDocument { editingMap = "mp_rr_desertlands_hu" };
            document.objects.Add(new MapObject {
                assetId = "custom:window-hint", displayName = "Window", customType = "window-hint",
                isGroup = true, rotation = WorldView.ToData(ApexDisplay.UnityAngles(new Vector3(0, 90, 0)))
            });
            StringAssert.Contains("<0, 1, 0> )", ReMapGameScript.Generate(document, document.objects));
        }

        [Test]
        public void ApexImplementationCreatesNativeWindowHintWithoutLegacyHelpers()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = File.ReadAllText(Path.Combine(root, "scripts/vscripts/source/sv_remap_objects.source.nut"));
            StringAssert.Contains("entity function ReMap_CreateWindowHint", script);
            StringAssert.Contains("CreateEntity( \"func_window_hint\" )", script);
            StringAssert.Contains("hint.kv.right", script);
            StringAssert.DoesNotContain("MapEditor_CreateFuncWindowHint", script);
        }

        [Test]
        public void RejectsInvalidWindowDimensions()
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject {
                assetId = "custom:window-hint", displayName = "Window", customType = "window-hint",
                isGroup = true, windowHintHalfHeight = 0f
            });
            Assert.Throws<System.ArgumentException>(document.Validate);
        }
    }
}
