using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class LocationPairTests
    {
        [Test]
        public void SavesValidatesAndExportsNativeWrapperCall()
        {
            var document = new MapDocument { name = "locations", editingMap = "mp_rr_desertlands_hu" };
            document.objects.Add(new MapObject {
                assetId = "custom:new-location-pair", displayName = "Location pair",
                customType = "location-pair", isGroup = true,
                position = ApexCoordinates.ToUnity(new Float3(10, 20, 30)),
                rotation = WorldView.ToData(ApexDisplay.UnityAngles(new Vector3(5, 90, 0)))
            });
            document.Validate();
            var restored = new UnityMapCodec().Decode(new UnityMapCodec().Encode(document));
            string code = ReMapGameScript.Generate(restored, restored.objects);
            StringAssert.Contains("ReMap_NewLocPair( <10, 20, 30>, <5, 90, 0> )", code);
            StringAssert.Contains("script ReMap_NewLocPair( <10, 20, 30>",
                ReMapGameScript.GenerateLiveCommands(restored, restored.objects));
        }

        [Test]
        public void ApexImplementationOnlyWrapsNativeNewLocPair()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = File.ReadAllText(Path.Combine(root, "scripts/vscripts/source/sv_remap_objects.source.nut"));
            StringAssert.Contains("LocPair function ReMap_NewLocPair( vector origin, vector angles )", script);
            StringAssert.Contains("return NewLocPair( origin, angles )", script);
        }

        [Test]
        public void RejectsNonGroupLocationPair()
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject {
                assetId = "custom:new-location-pair", displayName = "Location pair",
                customType = "location-pair", isGroup = false
            });
            Assert.Throws<System.ArgumentException>(document.Validate);
        }
    }
}
