using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace ReMap.Standalone.Tests
{
    public sealed class MapReferenceFormatTests
    {
        [Test]
        public void MprtRoundTripPreservesNativePlacement()
        {
            string path = Path.Combine(Path.GetTempPath(), "remap-" + Guid.NewGuid().ToString("N") + ".mprt");
            try
            {
                var source = new List<MprtPlacement> { new MprtPlacement { ModelPath = "mdl/props/crate.rmdl",
                    Position = new Float3(10, 20, 30), Angles = new Float3(4, 5, 6), Scale = 1.25f } };
                MprtReader.Write(path, source);
                var loaded = MprtReader.Read(path);
                Assert.That(loaded, Has.Count.EqualTo(1));
                Assert.That(loaded[0].ModelPath, Is.EqualTo("mdl/props/crate.rmdl"));
                Assert.That(loaded[0].Position.x, Is.EqualTo(10));
                Assert.That(loaded[0].Angles.x, Is.EqualTo(4));
                Assert.That(loaded[0].Angles.y, Is.EqualTo(5));
                Assert.That(loaded[0].Angles.z, Is.EqualTo(6));
                Assert.That(loaded[0].Scale, Is.EqualTo(1.25f));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        public void ReadsExternalApexRenderLumps()
        {
            string root = Path.Combine(Path.GetTempPath(), "remap-bsp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); string bsp = Path.Combine(root, "mp_test.bsp");
            try
            {
                var lumps = new Dictionary<int, byte[]>();
                byte[] models = new byte[64]; PutInt(models, 28, 1); lumps[0x0e] = models;
                byte[] meshes = new byte[28]; PutShort(meshes, 4, 1); lumps[0x50] = meshes;
                lumps[0x52] = new byte[12]; lumps[0x02] = new byte[16];
                lumps[0x0f] = Encoding.UTF8.GetBytes("terrain/test\0");
                byte[] faces = new byte[6]; PutShort(faces, 2, 1); PutShort(faces, 4, 2); lumps[0x4f] = faces;
                byte[] vertices = new byte[36]; PutFloat(vertices, 12, 100); PutFloat(vertices, 28, 100); lumps[0x03] = vertices;
                byte[] normals = new byte[36]; PutFloat(normals, 4, 1); PutFloat(normals, 16, 1); PutFloat(normals, 28, 1); lumps[0x1e] = normals;
                byte[] lit = new byte[60];
                for (int i = 0; i < 3; i++) { PutInt(lit, i * 20, i); PutInt(lit, i * 20 + 4, i); }
                lumps[0x48] = lit;
                WriteExternalBsp(bsp, lumps);

                var map = ApexBspReader.Read(bsp, false);
                Assert.That(map.Surfaces, Has.Count.EqualTo(1));
                Assert.That(map.Surfaces[0].MaterialPath, Is.EqualTo("terrain/test"));
                Assert.That(map.Surfaces[0].Positions, Has.Length.EqualTo(3));
                Assert.That(map.Surfaces[0].Indices, Is.EqualTo(new[] { 0, 1, 2 }));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void ReadsStaticPropGameLumpAndCanWriteMprt()
        {
            byte[] bytes = new byte[24 + 4 + 128 + 4 + 64];
            PutInt(bytes, 0, 1); PutInt(bytes, 4, 0x73707270); PutInt(bytes, 12, 24); PutInt(bytes, 24, 1);
            Encoding.UTF8.GetBytes("mdl/props/crate.rmdl").CopyTo(bytes, 28);
            int props = 28 + 128; PutInt(bytes, props, 1); int record = props + 4;
            PutFloat(bytes, record + 8, 10); PutFloat(bytes, record + 12, 20); PutFloat(bytes, record + 16, 30);
            PutFloat(bytes, record + 20, 4); PutFloat(bytes, record + 24, 5); PutFloat(bytes, record + 28, 6);
            PutFloat(bytes, record + 32, 2); PutShort(bytes, record + 36, 0);
            var propsRead = ApexBspReader.ReadStaticProps(bytes);
            Assert.That(propsRead, Has.Count.EqualTo(1));
            Assert.That(propsRead[0].ModelPath, Is.EqualTo("mdl/props/crate.rmdl"));
            Assert.That(propsRead[0].Position.z, Is.EqualTo(30));
            Assert.That(propsRead[0].Angles.y, Is.EqualTo(5));
            Assert.That(propsRead[0].Scale, Is.EqualTo(2));
        }

        [Test]
        public void ReadsServerBspCollisionBvh()
        {
            string root = Path.Combine(Path.GetTempPath(), "remap-bvh-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); string bsp = Path.Combine(root, "mp_collision.bsp");
            try
            {
                var lumps = new Dictionary<int, byte[]>();
                byte[] models = new byte[64];
                PutFloat(models, 0, -1); PutFloat(models, 4, -1); PutFloat(models, 8, -1);
                PutFloat(models, 12, 2); PutFloat(models, 16, 2); PutFloat(models, 20, 2);
                lumps[0x0e] = models;
                byte[] vertices = new byte[48];
                PutFloat(vertices, 12, 0); PutFloat(vertices, 16, 0); PutFloat(vertices, 20, 0);
                PutFloat(vertices, 24, 1); PutFloat(vertices, 28, 0); PutFloat(vertices, 32, 0);
                PutFloat(vertices, 36, 0); PutFloat(vertices, 40, 1); PutFloat(vertices, 44, 0);
                lumps[0x03] = vertices;
                byte[] nodes = new byte[64]; PutInt(nodes, 56, 4); lumps[0x12] = nodes;
                byte[] leaves = new byte[8]; PutInt(leaves, 4, 1 << 20); lumps[0x13] = leaves;
                WriteExternalBsp(bsp, lumps);

                var map = ApexBspReader.Read(bsp, false);
                Assert.That(map.Surfaces, Has.Count.EqualTo(1));
                Assert.That(map.Surfaces[0].MaterialPath, Is.EqualTo("__bsp_collision"));
                Assert.That(map.Surfaces[0].Positions, Has.Length.EqualTo(3));
                Assert.That(map.Surfaces[0].Indices, Has.Length.EqualTo(3));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void UsesCollisionWhenServerHeaderReferencesMissingRenderLumps()
        {
            string root = Path.Combine(Path.GetTempPath(), "remap-server-bvh-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); string bsp = Path.Combine(root, "mp_server.bsp");
            try
            {
                var lumps = new Dictionary<int, byte[]>();
                byte[] models = new byte[64];
                PutFloat(models, 0, -1); PutFloat(models, 4, -1); PutFloat(models, 8, -1);
                PutFloat(models, 12, 2); PutFloat(models, 16, 2); PutFloat(models, 20, 2);
                lumps[0x0e] = models;
                byte[] vertices = new byte[48];
                PutFloat(vertices, 12, 0); PutFloat(vertices, 16, 0); PutFloat(vertices, 20, 0);
                PutFloat(vertices, 24, 1); PutFloat(vertices, 28, 0); PutFloat(vertices, 32, 0);
                PutFloat(vertices, 36, 0); PutFloat(vertices, 40, 1); PutFloat(vertices, 44, 0);
                lumps[0x03] = vertices;
                byte[] nodes = new byte[64]; PutInt(nodes, 56, 4); lumps[0x12] = nodes;
                byte[] leaves = new byte[8]; PutInt(leaves, 4, 1 << 20); lumps[0x13] = leaves;
                WriteExternalBsp(bsp, lumps, new Dictionary<int, int> { [0x4f] = 6, [0x50] = 28, [0x52] = 12 });

                var map = ApexBspReader.Read(bsp, false);
                Assert.That(map.Surfaces, Has.Count.EqualTo(1));
                Assert.That(map.Surfaces[0].MaterialPath, Is.EqualTo("__bsp_collision"));
                Assert.That(map.Surfaces[0].Indices, Has.Length.EqualTo(3));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void FiltersInvisibleToolSurfacesFromServerCollisionBvh()
        {
            string root = Path.Combine(Path.GetTempPath(), "remap-bvh-tools-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); string bsp = Path.Combine(root, "mp_collision_tools.bsp");
            try
            {
                var lumps = new Dictionary<int, byte[]>();
                byte[] models = new byte[64];
                PutFloat(models, 0, -1); PutFloat(models, 4, -1); PutFloat(models, 8, -1);
                PutFloat(models, 12, 2); PutFloat(models, 16, 2); PutFloat(models, 20, 2);
                lumps[0x0e] = models;
                byte[] vertices = new byte[48];
                PutFloat(vertices, 12, 0); PutFloat(vertices, 16, 0); PutFloat(vertices, 20, 0);
                PutFloat(vertices, 24, 1); PutFloat(vertices, 28, 0); PutFloat(vertices, 32, 0);
                PutFloat(vertices, 36, 0); PutFloat(vertices, 40, 1); PutFloat(vertices, 44, 0);
                lumps[0x03] = vertices;

                byte[] nodes = new byte[64];
                PutInt(nodes, 52, 2 << 8);
                PutInt(nodes, 56, 0x44);
                lumps[0x12] = nodes;
                byte[] leaves = new byte[16];
                PutInt(leaves, 4, 1 << 20);
                PutInt(leaves, 8, 1);
                PutInt(leaves, 12, 1 << 20);
                lumps[0x13] = leaves;

                byte[] surfaceNames = Encoding.UTF8.GetBytes("TOOLS\\TOOLSCLIP\0WORLD\\GROUND\0");
                lumps[0x0f] = surfaceNames;
                byte[] surfaceProperties = new byte[16];
                PutInt(surfaceProperties, 4, 0);
                PutInt(surfaceProperties, 12, Encoding.UTF8.GetByteCount("TOOLS\\TOOLSCLIP") + 1);
                lumps[0x11] = surfaceProperties;
                WriteExternalBsp(bsp, lumps);

                var map = ApexBspReader.Read(bsp, false);

                Assert.That(map.Surfaces, Has.Count.EqualTo(1));
                Assert.That(map.Surfaces[0].Indices, Has.Length.EqualTo(3),
                    "The visible surface must remain while the TOOLS surface is removed.");
                Assert.That(map.Warnings, Has.Some.Contains("Filtered 1 invisible BSP tool triangles."));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void MprtProfilesUseBoundedPerformanceBudgets()
        {
            var settings = new AssetSourceSettings();
            MprtReferenceProfiles.Apply(settings, 0);
            Assert.That(settings.mprtDistance, Is.EqualTo(300));
            Assert.That(settings.mprtMaxActive, Is.EqualTo(5000));
            Assert.That(settings.mprtMinimumSize, Is.EqualTo(1));
            Assert.That(settings.mprtDensityPercent, Is.EqualTo(50));

            MprtReferenceProfiles.Apply(settings, 1);
            Assert.That(settings.mprtDistance, Is.EqualTo(750));
            Assert.That(settings.mprtMaxActive, Is.EqualTo(15000));
            Assert.That(settings.mprtMinimumSize, Is.EqualTo(.5f));
            Assert.That(settings.mprtDensityPercent, Is.EqualTo(100));

            MprtReferenceProfiles.Apply(settings, 2);
            Assert.That(settings.mprtDistance, Is.EqualTo(1500));
            Assert.That(settings.mprtMaxActive, Is.EqualTo(30000));
            Assert.That(settings.mprtMinimumSize, Is.EqualTo(.25f));
            Assert.That(settings.mprtDensityPercent, Is.EqualTo(100));
        }

        [Test]
        public void MprtOptionsClampUnsafeCustomValues()
        {
            var settings = new AssetSourceSettings { mprtDistance = 99999, mprtMaxActive = 999999,
                mprtMinimumSize = -2, mprtDensityPercent = 0 };
            MprtReferenceOptions options = MprtReferenceOptions.From(settings);
            Assert.That(options.Distance, Is.EqualTo(5000));
            Assert.That(options.MaxActive, Is.EqualTo(50000));
            Assert.That(options.MinimumSize, Is.EqualTo(0));
            Assert.That(options.DensityPercent, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator BspReferenceFiltersTrianglesThatBridgeDistantRegions()
        {
            WorldView world = CreateWorld();
            try
            {
                var surface = new BspSurface {
                    MaterialPath = "__edge_filter_test",
                    Positions = new[] {
                        new Float3(0, 0, 0), new Float3(64, 0, 0), new Float3(0, 64, 0),
                        new Float3(0, 0, 0), new Float3(25000, 0, 0), new Float3(0, 25000, 0)
                    },
                    Normals = Enumerable.Repeat(new Float3(0, 0, 1), 6).ToArray(),
                    TextureCoordinates = Enumerable.Repeat(new BspVector2(), 6).ToArray(),
                    Indices = new[] { 0, 1, 2, 3, 4, 5 }
                };
                var map = new ApexBspMap();
                map.Surfaces.Add(surface);

                world.LoadBspReference(map, "", new Float3());

                Assert.That(world.BspReferenceMeshCount, Is.EqualTo(1));
                Assert.That(world.BspReferenceTriangleCount, Is.EqualTo(1),
                    "The normal face must remain while the >500 Unity-unit bridge is removed.");
            }
            finally { DisposeWorld(world); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator BspReferenceSupportsVerticalSurfaceQueriesWithoutPhysXColliders()
        {
            WorldView world = CreateWorld();
            try
            {
                const float surfaceApexZ = 128f;
                var surface = new BspSurface {
                    MaterialPath = "__vertical_query_test",
                    Positions = new[] {
                        new Float3(-256, -256, surfaceApexZ), new Float3(256, -256, surfaceApexZ),
                        new Float3(256, 256, surfaceApexZ), new Float3(-256, 256, surfaceApexZ)
                    },
                    Normals = Enumerable.Repeat(new Float3(0, 0, 1), 4).ToArray(),
                    TextureCoordinates = Enumerable.Repeat(new BspVector2(), 4).ToArray(),
                    Indices = new[] { 0, 1, 2, 0, 2, 3 }
                };
                var map = new ApexBspMap(); map.Surfaces.Add(surface);
                world.LoadBspReference(map, "", new Float3());

                bool found = world.TryZiplineSurfaceBelow(new Vector3(0, 10, 0),
                    new HashSet<string>(), out float height);

                Assert.That(world.BspReferenceColliderCount, Is.Zero);
                Assert.That(found, Is.True);
                Assert.That(height, Is.EqualTo(surfaceApexZ * ApexCoordinates.MetersPerUnit).Within(.0001f));
            }
            finally { DisposeWorld(world); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator BspReferenceSupportsCameraRaysWithoutPhysXColliders()
        {
            WorldView world = CreateWorld();
            try
            {
                const float surfaceApexZ = -128f;
                var surface = new BspSurface {
                    MaterialPath = "__camera_ray_test",
                    Positions = new[] {
                        new Float3(-256, -256, surfaceApexZ), new Float3(256, -256, surfaceApexZ),
                        new Float3(256, 256, surfaceApexZ), new Float3(-256, 256, surfaceApexZ)
                    },
                    Normals = Enumerable.Repeat(new Float3(0, 0, 1), 4).ToArray(),
                    TextureCoordinates = Enumerable.Repeat(new BspVector2(), 4).ToArray(),
                    Indices = new[] { 0, 1, 2, 0, 2, 3 }
                };
                var map = new ApexBspMap(); map.Surfaces.Add(surface);
                world.LoadBspReference(map, "", new Float3());
                Vector3 expected = Vector3.up * (surfaceApexZ * ApexCoordinates.MetersPerUnit);
                var ray = new Ray(expected + new Vector3(0, 10, -10), new Vector3(0, -1, 1).normalized);

                bool found = world.TryBspRaycast(ray, 100f, out Vector3 point, out float distance);

                Assert.That(world.BspReferenceColliderCount, Is.Zero);
                Assert.That(found, Is.True);
                Assert.That(Vector3.Distance(point, expected), Is.LessThan(.0001f));
                Assert.That(distance, Is.EqualTo(Mathf.Sqrt(200f)).Within(.0001f));
            }
            finally { DisposeWorld(world); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlacementPrefersBspBelowConstructionPlane()
        {
            WorldView world = CreateWorld();
            try
            {
                const float surfaceApexZ = -128f;
                var surface = new BspSurface {
                    MaterialPath = "__placement_ray_test",
                    Positions = new[] {
                        new Float3(-256, -256, surfaceApexZ), new Float3(256, -256, surfaceApexZ),
                        new Float3(256, 256, surfaceApexZ), new Float3(-256, 256, surfaceApexZ)
                    },
                    Normals = Enumerable.Repeat(new Float3(0, 0, 1), 4).ToArray(),
                    TextureCoordinates = Enumerable.Repeat(new BspVector2(), 4).ToArray(),
                    Indices = new[] { 0, 1, 2, 0, 2, 3 }
                };
                var map = new ApexBspMap(); map.Surfaces.Add(surface);
                world.LoadBspReference(map, "", new Float3());
                Vector3 expected = Vector3.up * (surfaceApexZ * ApexCoordinates.MetersPerUnit);
                Vector3 cameraPosition = expected + new Vector3(0, 10, -10);
                world.Camera.pixelRect = new Rect(0, 0, 800, 600);
                world.Camera.transform.SetPositionAndRotation(cameraPosition,
                    Quaternion.LookRotation(expected - cameraPosition));
                Physics.SyncTransforms();
                var entry = new CatalogEntry("test:surface", "Surface test", "Test", Vector3.one) {
                    CustomType = "test"
                };

                bool found = world.Placement(world.Camera.pixelRect.center, entry, false,
                    out Vector3 point);

                Assert.That(found, Is.True);
                Assert.That(point.y, Is.LessThan(0f));
                Assert.That(Vector3.Distance(point, expected), Is.LessThan(.001f));
            }
            finally { DisposeWorld(world); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ConstructionToolsUseBspAboveAndBelowWithoutPhysXColliders()
        {
            WorldView world = CreateWorld();
            try
            {
                const float surfaceApexZ = -128f;
                var surface = new BspSurface {
                    MaterialPath = "__construction_tool_ray_test",
                    Positions = new[] {
                        new Float3(-256, -256, surfaceApexZ), new Float3(256, -256, surfaceApexZ),
                        new Float3(256, 256, surfaceApexZ), new Float3(-256, 256, surfaceApexZ)
                    },
                    Normals = Enumerable.Repeat(new Float3(0, 0, 1), 4).ToArray(),
                    TextureCoordinates = Enumerable.Repeat(new BspVector2(), 4).ToArray(),
                    Indices = new[] { 0, 1, 2, 0, 2, 3 }
                };
                var map = new ApexBspMap(); map.Surfaces.Add(surface);
                world.LoadBspReference(map, "", new Float3());
                Vector3 expected = Vector3.up * (surfaceApexZ * ApexCoordinates.MetersPerUnit);
                var supports = new HashSet<Collider>();

                bool foundBelow = world.TryConstructionSurface(
                    new Ray(expected + Vector3.up * 10f, Vector3.down), supports, out Vector3 below);
                bool foundAbove = world.TryConstructionSurface(
                    new Ray(expected + Vector3.down * 10f, Vector3.up), supports, out Vector3 above);

                Assert.That(world.BspReferenceColliderCount, Is.Zero);
                Assert.That(foundBelow, Is.True);
                Assert.That(foundAbove, Is.True);
                Assert.That(Vector3.Distance(below, expected), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(above, expected), Is.LessThan(.001f));
            }
            finally { DisposeWorld(world); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlacementFallsBackToConstructionPlaneWhenNoWorldSurfaceExists()
        {
            WorldView world = CreateWorld();
            try
            {
                world.Camera.pixelRect = new Rect(0, 0, 800, 600);
                world.Camera.transform.SetPositionAndRotation(new Vector3(0, 10, -10),
                    Quaternion.LookRotation(new Vector3(0, -10, 10)));
                Physics.SyncTransforms();
                var entry = new CatalogEntry("test:no-surface", "No surface", "Test", Vector3.one) {
                    CustomType = "test"
                };

                bool found = world.Placement(world.Camera.pixelRect.center, entry, false, out Vector3 point);

                Assert.That(found, Is.True);
                Assert.That(point.y, Is.EqualTo(0f).Within(.001f));
            }
            finally { DisposeWorld(world); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator BspReferenceBuildYieldsAndHonorsCancellation()
        {
            WorldView world = CreateWorld();
            using var cancellation = new CancellationTokenSource();
            try
            {
                var surface = new BspSurface {
                    MaterialPath = "__cancel_test",
                    Positions = new[] { new Float3(0, 0, 0), new Float3(64, 0, 0), new Float3(0, 64, 0) },
                    Normals = new[] { new Float3(0, 0, 1), new Float3(0, 0, 1), new Float3(0, 0, 1) },
                    TextureCoordinates = new[] { new BspVector2(), new BspVector2(), new BspVector2() },
                    Indices = Enumerable.Range(0, 1200000).Select(index => index % 3).ToArray()
                };
                var map = new ApexBspMap();
                map.Surfaces.Add(surface);
                Task loading = world.LoadBspReferenceAsync(map, "", new Float3(), null, cancellation.Token);
                var timeout = System.Diagnostics.Stopwatch.StartNew();
                while (!loading.IsCompleted && world.BspReferenceMeshCount == 0 &&
                    timeout.Elapsed < TimeSpan.FromSeconds(5))
                    yield return null;
                Assert.That(world.BspReferenceMeshCount, Is.GreaterThan(0));
                Assert.That(world.BspReferenceColliderCount, Is.Zero,
                    "Locked BSP visualization must not cook unsafe raw collision through PhysX.");
                Assert.That(loading.IsCompleted, Is.False, "BSP creation did not yield between bounded chunks.");
                cancellation.Cancel();
                timeout.Restart();
                while (!loading.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(5)) yield return null;
                Assert.That(loading.IsCanceled ||
                    loading.Exception?.GetBaseException() is OperationCanceledException, Is.True);
            }
            finally { DisposeWorld(world); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator MprtReferenceIndexYieldsAndHonorsCancellation()
        {
            WorldView world = CreateWorld();
            using var cancellation = new CancellationTokenSource();
            try
            {
                var references = Enumerable.Range(0, 20000).Select(index => new MapReferenceModel {
                    AssetId = "cancel-test",
                    Placement = new MprtPlacement {
                        ModelPath = "mdl/cancel_test.rmdl", Position = new Float3(index, 0, index), Scale = 1
                    }
                }).ToArray();
                Task loading = world.ConfigureMprtReferenceAsync(references, new MprtReferenceOptions(),
                    new Float3(), null, cancellation.Token);
                Assert.That(loading.IsCompleted, Is.False, "MPRT indexing did not yield after its first batch.");
                Assert.That(world.MprtReferenceAvailableCount, Is.LessThan(references.Length));
                cancellation.Cancel();
                int frames = 0;
                while (!loading.IsCompleted && frames++ < 120) yield return null;
                Assert.That(loading.IsCanceled, Is.True);
            }
            finally { DisposeWorld(world); }
            yield return null;
        }

        [Test]
        public void MprtNeverCreatesFallbackBlocksForModelsNoLongerPrepared()
        {
            WorldView world = CreateWorld();
            try
            {
                const string id = "apex:missing-mprt";
                world.models.Prepare(id, "missing.cast");
                world.ConfigureMprtReference(new[] { new MapReferenceModel {
                    AssetId = id,
                    Placement = new MprtPlacement { ModelPath = "mdl/props/missing.rmdl", Scale = 1 }
                } }, new MprtReferenceOptions { Distance = 750, MaxActive = 100, MinimumSize = 0 }, new Float3());
                Assert.That(world.MprtReferenceAvailableCount, Is.EqualTo(1));

                world.models.ForgetPrepared(id);
                world.TickMprtReference(1);

                Assert.That(world.MprtReferenceActiveCount, Is.Zero);
                Assert.That(world.models.LoadedModelCount, Is.Zero,
                    "An unavailable MPRT model must not fall back to the magenta demo cube.");
            }
            finally { DisposeWorld(world); }
        }

        private static WorldView CreateWorld() => new WorldView(
            Shader.Find("Universal Render Pipeline/Lit"),
            Shader.Find("ReMap/WorkspaceGrid"),
            Shader.Find("Universal Render Pipeline/Unlit"));

        private static void DisposeWorld(WorldView world)
        {
            bool previous = LogAssert.ignoreFailingMessages;
            try { LogAssert.ignoreFailingMessages = true; world.Dispose(); }
            finally { LogAssert.ignoreFailingMessages = previous; }
        }

        private static void WriteExternalBsp(string path, Dictionary<int, byte[]> lumps,
            Dictionary<int, int> declaredLengths = null)
        {
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0x50534272u); writer.Write(47); writer.Write(0); writer.Write(127);
                for (int i = 0; i < 128; i++)
                {
                    writer.Write(0);
                    writer.Write(lumps.TryGetValue(i, out var data) ? data.Length :
                        declaredLengths != null && declaredLengths.TryGetValue(i, out int length) ? length : 0);
                    writer.Write(0); writer.Write(0);
                }
            }
            foreach (var pair in lumps) File.WriteAllBytes(path + "." + pair.Key.ToString("x4") + ".bsp_lump", pair.Value);
        }
        private static void PutInt(byte[] bytes, int offset, int value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
        private static void PutShort(byte[] bytes, int offset, short value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
        private static void PutFloat(byte[] bytes, int offset, float value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
    }
}
