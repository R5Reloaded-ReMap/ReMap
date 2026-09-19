using NUnit.Framework;
using ReMap.Standalone.Core;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.TestTools;

namespace ReMap.Standalone.Tests
{
    public sealed class JumpTowerTests
    {
        [Test]
        public void SavesValidatesAndExportsHeightAndModels()
        {
            var document = new MapDocument {
                name = "tower", editingMap = "mp_rr_desertlands_hu"
            };
            document.objects.Add(new MapObject {
                assetId = "custom:jump-tower", displayName = "Jump tower", customType = "jump-tower",
                isGroup = true, position = ApexCoordinates.ToUnity(new Float3(10, 20, 30)),
                jumpTowerHeight = 2400f
            });
            document.Validate();
            var restored = new UnityMapCodec().Decode(new UnityMapCodec().Encode(document));

            string code = ReMapGameScript.Generate(restored, restored.objects);
            StringAssert.Contains("PrecacheModel( $\"mdl/props/zipline_balloon/zipline_balloon_base.rmdl\" )", code);
            StringAssert.Contains("PrecacheModel( $\"mdl/props/zipline_balloon/zipline_balloon.rmdl\" )", code);
            StringAssert.Contains("ReMap_CreateJumpTower( <10, 20, 30>, <0, 0, 0>, 2400 )", code);
            StringAssert.Contains("script ReMap_CreateJumpTower( <10, 20, 30>, <0, 0, 0>, 2400 )",
                ReMapGameScript.GenerateLiveCommands(restored, restored.objects));
        }

        [Test]
        public void ApexImplementationBuildsEveryFunctionalPartWithoutLegacyWrapper()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string script = File.ReadAllText(Path.Combine(root,
                "scripts/vscripts/source/sv_remap_objects.source.nut"));
            StringAssert.Contains("void function ReMap_CreateJumpTower", script);
            StringAssert.Contains("REMAP_JUMP_TOWER_BASE_MODEL", script);
            StringAssert.Contains("const vector REMAP_JUMP_TOWER_CABLE_OFFSET = < -2.0, 2.65, 0 >", script);
            StringAssert.Contains("RotateVector( REMAP_JUMP_TOWER_CABLE_OFFSET, towerAngles )", script);
            StringAssert.Contains("ReMap_CreateZipline( topCable", script);
            StringAssert.Contains("ForcedSkydiveTriggerThink_EnterCallback", script);
            StringAssert.DoesNotContain("ReMapCreateJumpTower(", script);
            StringAssert.DoesNotContain("MapEditor_", script);
        }

        [TestCase(127f)]
        [TestCase(999f)]
        [TestCase(65536f)]
        public void RejectsInvalidHeight(float height)
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject {
                customType = "jump-tower", assetId = "custom:jump-tower",
                displayName = "Jump tower", isGroup = true, jumpTowerHeight = height
            });
            Assert.Throws<System.ArgumentException>(document.Validate);
        }

        [Test]
        public void AcceptsExactMinimumHeight()
        {
            var document = new MapDocument();
            document.objects.Add(new MapObject {
                customType = "jump-tower", assetId = "custom:jump-tower",
                displayName = "Jump tower", isGroup = true, jumpTowerHeight = 1000f
            });
            Assert.DoesNotThrow(document.Validate);
        }

        [Test]
        public void BalloonMovementClampsHeightAndKeepsItStrictlyAboveBase()
        {
            var tower = new MapObject {
                customType = "jump-tower", assetId = "custom:jump-tower",
                displayName = "Jump tower", isGroup = true, jumpTowerHeight = 2000f
            };
            var balloon = new MapObject {
                customType = "jump-tower-component", customRole = "balloon",
                parentId = tower.id, positionLocked = true,
                position = ApexCoordinates.ToUnity(new Float3(250f, -400f, 750f))
            };
            var document = new MapDocument();
            document.objects.Add(tower); document.objects.Add(balloon);
            var sync = typeof(ReMapApp).GetMethod("SyncJumpTowerHeightFromBalloon",
                BindingFlags.Static | BindingFlags.NonPublic);
            sync.Invoke(null, new object[] { document, balloon });

            Assert.That(tower.jumpTowerHeight, Is.EqualTo(1000f));
            var apexPosition = ApexCoordinates.ToApex(balloon.position);
            Assert.That(apexPosition.x, Is.EqualTo(0f).Within(.001f));
            Assert.That(apexPosition.y, Is.EqualTo(0f).Within(.001f));
            Assert.That(apexPosition.z, Is.EqualTo(1000f).Within(.001f));
            Assert.That(balloon.positionLocked, Is.True);
        }

        [Test]
        public void ComponentSyncPreservesBalloonIdentityAndHidesOnlyBase()
        {
            var owner = new GameObject("Jump tower test");
            try
            {
                var app = owner.AddComponent<ReMapApp>();
                var tower = new MapObject {
                    customType = "jump-tower", assetId = "custom:jump-tower",
                    displayName = "Jump tower", isGroup = true, jumpTowerHeight = 2000f
                };
                var document = new MapDocument(); document.objects.Add(tower);
                var sync = typeof(ReMapApp).GetMethod("SyncJumpTowerComponents",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                sync.Invoke(app, new object[] { document, tower });
                var firstBalloon = document.objects.Single(item =>
                    item.customType == "jump-tower-component" && item.customRole == "balloon");
                string balloonId = firstBalloon.id;
                firstBalloon.positionLocked = true;
                tower.jumpTowerHeight = 3200f;
                sync.Invoke(app, new object[] { document, tower });

                var towerBase = document.objects.Single(item =>
                    item.customType == "jump-tower-component" && item.customRole == "base");
                var balloon = document.objects.Single(item =>
                    item.customType == "jump-tower-component" && item.customRole == "balloon");
                Assert.That(balloon.id, Is.EqualTo(balloonId));
                Assert.That(balloon.positionLocked, Is.True);
                Assert.That(ApexCoordinates.ToApex(balloon.position).z,
                    Is.EqualTo(3200f).Within(.001f));

                var hidden = typeof(ReMapApp).GetMethod("HiddenHierarchyObject",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That((bool)hidden.Invoke(null, new object[] { towerBase }), Is.True);
                Assert.That((bool)hidden.Invoke(null, new object[] { balloon }), Is.False);
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void BalloonPreviewOnlyMovesVerticallyAndEnforcesMinimumHeight()
        {
            var world = new WorldView(
                Shader.Find("Universal Render Pipeline/Lit"),
                Shader.Find("ReMap/WorkspaceGrid"),
                Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                var tower = new MapObject {
                    assetId = "custom:jump-tower", displayName = "Jump tower",
                    customType = "jump-tower", isGroup = true, jumpTowerHeight = 2000f
                };
                var towerBase = new MapObject {
                    assetId = "custom:jump-tower-component:base", displayName = "Base",
                    customType = "jump-tower-component", customRole = "base",
                    parentId = tower.id, isGroup = true
                };
                var balloon = new MapObject {
                    assetId = "custom:jump-tower-component:balloon", displayName = "Balloon",
                    customType = "jump-tower-component", customRole = "balloon",
                    parentId = tower.id, isGroup = true,
                    position = ApexCoordinates.ToUnity(new Float3(0f, 0f, 2000f))
                };
                var document = new MapDocument();
                document.objects.Add(tower); document.objects.Add(towerBase);
                document.objects.Add(balloon);
                world.Sync(document, balloon.id);

                Assert.That(world.SetLocalPreview(balloon.id,
                    new Vector3(8f, 2f, -4f), new Vector3(20f, 30f, 40f),
                    Vector3.one * 3f), Is.True);
                var preview = world.LocalPose(balloon.id);
                var position = WorldView.ToVector(preview.position);
                Assert.That(position.x, Is.EqualTo(0f).Within(.0001f));
                Assert.That(position.z, Is.EqualTo(0f).Within(.0001f));
                Assert.That(position.y, Is.EqualTo(1000f *
                    ApexCoordinates.MetersPerUnit).Within(.0001f));
                Assert.That(WorldView.ToVector(preview.rotation), Is.EqualTo(Vector3.zero));
                Assert.That(WorldView.ToVector(preview.scale), Is.EqualTo(Vector3.one));
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
