using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.TestTools;
namespace ReMap.Standalone.Tests
{
    public sealed class AlbedoTests
    {
        [Test]
        public void TextureNormalizationHonorsPreCanceledRequest()
        {
            string root = Path.Combine(Path.GetTempPath(), "remap-texture-cancel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                Assert.ThrowsAsync<System.Threading.Tasks.TaskCanceledException>(async () =>
                    await SharedTextureCache.Normalize(root, 256, cancellation.Token));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void TextureGarbageCollectionPreservesReferencedHashes()
        {
            string root=Path.Combine(Path.GetTempPath(),"remap-texture-gc-"+Guid.NewGuid().ToString("N"));
            string cache=Path.Combine(root,"AssetCache"),model=Path.Combine(cache,"Models","test"),legacyModel=Path.Combine(cache,"Models","legacy"),textures=Path.Combine(cache,"Textures");
            string kept=new string('a',64),orphaned=new string('b',64),legacy=new string('c',64);
            Directory.CreateDirectory(model);Directory.CreateDirectory(legacyModel);Directory.CreateDirectory(textures);
            try
            {
                File.WriteAllBytes(Path.Combine(textures,kept+".png"),new byte[]{1});
                File.WriteAllBytes(Path.Combine(textures,orphaned+".png"),new byte[]{2});
                File.WriteAllBytes(Path.Combine(textures,legacy+".png"),new byte[]{3});
                File.WriteAllText(Path.Combine(model,"textures.manifest.json"),JsonUtility.ToJson(new SharedTextureCache.Manifest{
                    maximumSize=SharedTextureCache.PreviewMaximumSize,entries=new List<SharedTextureCache.Entry>{new SharedTextureCache.Entry{source="color.png",hash=kept}}},true));
                File.WriteAllText(Path.Combine(legacyModel,"textures.manifest.json"),JsonUtility.ToJson(new SharedTextureCache.Manifest{
                    entries=new List<SharedTextureCache.Entry>{new SharedTextureCache.Entry{source="old.png",hash=legacy}}},true));
                Assert.That(SharedTextureCache.ManifestMatchesMaximum(model,SharedTextureCache.PreviewMaximumSize),Is.True);
                File.Delete(Path.Combine(textures,kept+".png"));
                Assert.That(SharedTextureCache.ManifestMatchesMaximum(model,SharedTextureCache.PreviewMaximumSize),Is.False,
                    "A manifest must not keep a model cached when its shared texture was removed.");
                File.WriteAllBytes(Path.Combine(textures,kept+".png"),new byte[]{1});
                Assert.That(SharedTextureCache.ManifestMatchesMaximum(legacyModel,SharedTextureCache.PreviewMaximumSize),Is.False);
                Assert.That(SharedTextureCache.CollectGarbage(cache),Is.EqualTo(2));
                Assert.That(File.Exists(Path.Combine(textures,kept+".png")),Is.True);
                Assert.That(File.Exists(Path.Combine(textures,orphaned+".png")),Is.False);
                Assert.That(File.Exists(Path.Combine(textures,legacy+".png")),Is.False);
            }
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }

        [UnityTest]
        public IEnumerator ExistingTextureManifestMigratesWithoutRsxExtraction()
        {
            string root = Path.Combine(Path.GetTempPath(), "remap-texture-migration-" + Guid.NewGuid().ToString("N"));
            string modelRoot = Path.Combine(root, "AssetCache", "Models", "test");
            string materialRoot = Path.Combine(modelRoot, "export", "mdl", "test");
            Directory.CreateDirectory(materialRoot);
            Texture2D source = null;
            try
            {
                source = new Texture2D(1024, 512, TextureFormat.RGBA32, false);
                source.SetPixels32(Enumerable.Repeat(new Color32(90, 140, 210, 255), 1024 * 512).ToArray());
                source.Apply();
                File.WriteAllBytes(Path.Combine(materialRoot, "color.png"), source.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(source); source = null;

                var initial = SharedTextureCache.Normalize(modelRoot, 1024);
                while (!initial.IsCompleted) yield return null;
                if (initial.IsFaulted) throw initial.Exception;
                string cast = Path.Combine(materialRoot, "test.cast");
                string original = SharedTextureCache.Resolve(cast, "color.png");
                Assert.That(original, Is.Not.Null);

                var migration = SharedTextureCache.Normalize(modelRoot, 0);
                while (!migration.IsCompleted) yield return null;
                if (migration.IsFaulted) throw migration.Exception;
                string migrated = SharedTextureCache.Resolve(cast, "color.png");
                Assert.That(migrated, Is.Not.Null.And.Not.EqualTo(original));
                Assert.That(File.Exists(original), Is.True, "Shared textures still referenced by another manifest must be preserved.");
                var texture = SharedTextureCache.Acquire(migrated);
                try
                {
                    Assert.That(texture.width, Is.EqualTo(512));
                    Assert.That(texture.height, Is.EqualTo(256));
                }
                finally { SharedTextureCache.Release(migrated); }
                var manifest = JsonUtility.FromJson<SharedTextureCache.Manifest>(File.ReadAllText(Path.Combine(modelRoot, "textures.manifest.json")));
                Assert.That(manifest.maximumSize, Is.EqualTo(SharedTextureCache.PreviewMaximumSize));
            }
            finally
            {
                if (source != null) UnityEngine.Object.DestroyImmediate(source);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test] public void MissingBindingOffersOnlyExactMaterialColorNames()
        {
            var material=new CastNode();material.Properties["n"]="metal_floor_holes_02";
            var paths=CastAlbedo.Candidates(material,"canyonland_thunderdome_ground_cap_01").ToArray();
            Assert.That(paths.Last(),Is.EqualTo("canyonland_thunderdome_ground_cap_01/metal_floor_holes_02_col.png"));
            Assert.That(paths.Length,Is.EqualTo(3));Assert.That(paths.Any(p=>p.Contains("normal")||p.Contains("gloss")),Is.False);
        }
        [Test] public void ExplicitCastLinkHasPriorityOverNameFallback()
        {
            var material=new CastNode();material.Properties["n"]="wall";material.Properties["albedo"]=new ulong[]{17};
            var file=new CastNode{Type=CastReader.FileNode,Hash=17};file.Properties["p"]="textures/actual.png";material.Children.Add(file);
            Assert.That(CastAlbedo.Candidates(material,"model").First(),Is.EqualTo("textures/actual.png"));
        }
        [Test] public void RenderPassSuffixMatchesRsxNaming()
        {
            var material=new CastNode();material.Properties["n"]="materials/metal_prepass";
            Assert.That(CastAlbedo.Candidates(material,"model").Last(),Is.EqualTo("model/metal_col.png"));
        }
        [TestCase("metal_col.png",true)] [TestCase("metal_albedo2Texture.png",true)]
        [TestCase("metal_normalTexture.png",false)] [TestCase("metal_specTexture.png",false)]
        public void ColorClassificationDoesNotKeepNormalOrSpecularMaps(string name,bool expected)
            =>Assert.That(CastAlbedo.IsColorTextureName(name),Is.EqualTo(expected));
        [Test] public void ReportedGroundCapFindsItsExistingUnboundAlbedo()
        {
            string root=RsxAssetLibrary.FindExistingModelDirectory(Path.GetFullPath(Path.Combine(Application.dataPath,"..")),"04c630f398c9d040");
            if(root==null)Assert.Ignore("Local game fixture not available.");
            if(!File.Exists(Path.Combine(root,"complete.txt")))Assert.Ignore("Local game fixture not available.");
            string cast=Path.Combine(root,File.ReadLines(Path.Combine(root,"complete.txt")).First());
            var material=CastReader.Read(cast).SelectMany(n=>n.Descendants(CastReader.Material)).Single(m=>m.Text("n")=="metal_floor_holes_02");
            Assert.That(material.Link("albedo"),Is.EqualTo(0));
            string resolved=SharedTextureCache.ResolveAlbedo(cast,material);
            Assert.That(resolved,Is.Not.Null);Assert.That(Path.GetFileName(resolved),Is.EqualTo("ae95161d8281da6155f537b93aed162161cadb45c1f42ea9087d9f01b2d3c223.png"));
        }
        [Test] public void ReportedSpaceElevatorRampFitsTheGlobalTextureBudget()
        {
            string root=RsxAssetLibrary.FindExistingModelDirectory(Path.GetFullPath(Path.Combine(Application.dataPath,"..")),"8ffa8882a95d0f40");
            if(root==null||!File.Exists(Path.Combine(root,"complete.txt")))Assert.Ignore("Local game fixture not available.");
            string cast=Path.Combine(root,File.ReadLines(Path.Combine(root,"complete.txt")).First());
            Shader shader=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");
            Assert.That(shader,Is.Not.Null);
            var model=StaticCastModel.Load(cast,shader);
            try
            {
                Assert.That(model.TexturePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count(),Is.GreaterThan(16));
            }
            finally
            {
                bool ignored=LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages=true;
                try{model.Dispose();}
                finally{LogAssert.ignoreFailingMessages=ignored;}
            }
        }
        [Test] public void RandomDecoderNoiseTriggersOfficialFallbackSignal()
        {
            const int width=128,height=128;var pixels=new Color32[width*height];var random=new System.Random(9137);
            for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32((byte)random.Next(256),(byte)random.Next(256),(byte)random.Next(256),255);
            Assert.That(SharedTextureCache.LooksLikeRandomCorruption(pixels,width,height,width*height*3),Is.True);
        }
        [Test] public void SmoothColorTextureDoesNotTriggerOfficialFallbackSignal()
        {
            const int width=128,height=128;var pixels=new Color32[width*height];
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)pixels[y*width+x]=new Color32((byte)(x*255/(width-1)),(byte)(y*255/(height-1)),96,255);
            Assert.That(SharedTextureCache.LooksLikeRandomCorruption(pixels,width,height,width*height),Is.False);
        }
    }
}
