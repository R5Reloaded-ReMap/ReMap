using System;
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
    }
}
