using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class SoundTests
    {
        [Test]
        public void SavesValidatesAndExportsAmbientGenericWithPolyline()
        {
            var document = new MapDocument { name = "sound", editingMap = "mp_rr_desertlands_hu" };
            var sound = new MapObject {
                assetId = "custom:sound", displayName = "Sound", customType = "sound", isGroup = true,
                position = ApexCoordinates.ToUnity(new Float3(100, 200, 300)), soundName = "Music_Test",
                soundRadius = 1200f, soundWaveAmbient = true, soundEnabled = false
            };
            document.objects.Add(sound);
            document.objects.Add(new MapObject {
                assetId = "custom:sound-point:0", displayName = "Point", customType = "sound-point",
                customRole = "0", parentId = sound.id, isGroup = true,
                position = ApexCoordinates.ToUnity(new Float3(400, 0, 0))
            });
            document.Validate();
            var restored = new UnityMapCodec().Decode(new UnityMapCodec().Encode(document));
            string code = ReMapGameScript.Generate(restored, restored.objects);
            StringAssert.Contains("ReMap_CreateSound( <100, 200, 300>, \"Music_Test\", 1200, true, false, [ <400, 0, 0> ] )", code);
            StringAssert.Contains("script ReMap_CreateSound( <100, 200, 300>",
                ReMapGameScript.GenerateLiveCommands(restored, restored.objects));
        }

        [Test]
        public void ApexImplementationCreatesAmbientGenericWithoutLegacyHelpers()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = File.ReadAllText(Path.Combine(root, "scripts/vscripts/source/sv_remap_objects.source.nut"));
            StringAssert.Contains("entity function ReMap_CreateSound", script);
            StringAssert.Contains("CreateEntity( \"ambient_generic\" )", script);
            StringAssert.Contains("polyline_segment_", script);
            StringAssert.Contains("sound.SetSoundName( soundName )", script);
            StringAssert.DoesNotContain("MapEditor_", script);
        }

        [Test]
        public void RejectsInvalidSoundSettings()
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject {
                assetId = "custom:sound", displayName = "Sound", customType = "sound",
                isGroup = true, soundRadius = -1f
            });
            Assert.Throws<System.ArgumentException>(document.Validate);
        }
    }
}
