using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using UnityEngine;

namespace ReMap.Standalone.Tests
{
    public sealed class BubbleShieldTests
    {
        [Test]
        public void SavesValidatesAndExportsColorAndScale()
        {
            var document = new MapDocument { name = "shield", editingMap = "mp_rr_desertlands_hu" };
            document.objects.Add(new MapObject {
                assetId = "custom:bubble-shield", displayName = "Bubble shield",
                customType = "bubble-shield", gameModelPath = "mdl/fx/bb_shield.rmdl",
                position = ApexCoordinates.ToUnity(new Float3(10, 20, 30)),
                scale = new Float3(1.5f, 1.5f, 1.5f),
                bubbleShieldColor = new Float3(64, 128, 255)
            });
            document.Validate();
            var restored = new UnityMapCodec().Decode(new UnityMapCodec().Encode(document));
            string code = ReMapGameScript.Generate(restored, restored.objects);
            StringAssert.Contains("PrecacheModel( $\"mdl/fx/bb_shield.rmdl\" )", code);
            StringAssert.Contains("ReMap_CreateBubbleShield( <10, 20, 30>, <0, 0, 0>, 1.5, <64, 128, 255> )", code);
            StringAssert.DoesNotContain("MapEditor_CreateBubbleShieldWithSettings", code);
        }

        [Test]
        public void ApexImplementationIsStandalone()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = File.ReadAllText(Path.Combine(root, "scripts/vscripts/source/sv_remap_objects.source.nut"));
            StringAssert.Contains("entity function ReMap_CreateBubbleShield", script);
            StringAssert.Contains("TRACE_COLLISION_GROUP_BLOCK_WEAPONS", script);
            StringAssert.Contains("CONTENTS_NOGRAPPLE", script);
            StringAssert.DoesNotContain("MapEditor_CreateBubbleShieldWithSettings", script);
        }

        [Test]
        public void RejectsNonUniformScaleOrInvalidColor()
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject {
                assetId = "custom:bubble-shield", displayName = "Bubble shield",
                customType = "bubble-shield", scale = new Float3(1, 2, 1),
                bubbleShieldColor = new Float3(256, 0, 0)
            });
            Assert.Throws<System.ArgumentException>(document.Validate);
        }
    }
}
