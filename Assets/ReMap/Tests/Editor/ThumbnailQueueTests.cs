using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ReMap.Standalone.Core;
namespace ReMap.Standalone.Tests {
    public class ThumbnailQueueTests {
        private static GameAssetRecord Record(string id,string name,string archive="common.rpak")=>new GameAssetRecord{guid=id,modelPath="mdl/"+name+".rmdl",origins=new List<AssetOrigin>{new AssetOrigin{archive=archive}}};
        [Test] public void VisiblePageWinsOverEarlierSearchMatches() {
            var records=Enumerable.Range(0,20).Select(i=>Record(i.ToString(),"platform_"+i)).ToArray();
            var next=ThumbnailQueue.Next(records,records.Skip(10).Take(8),new HashSet<string>(),new HashSet<string>(),"platform",Array.Empty<string>());
            Assert.That(next.Select(r=>r.Id),Is.EqualTo(records.Skip(10).Take(8).Select(r=>r.Id)));
        }
        [Test] public void ReadyFailuresAndIncompatibleAssetsAreSkippedAndArchiveIsShared() {
            var a=Record("1","a");var b=Record("2","b");var c=Record("3","c","common_mp.rpak");var d=Record("4","d");var incompatible=Record("5","e");incompatible.origins[0].mapId="olympus";
            var result=ThumbnailQueue.Next(new[]{a,b,c,d,incompatible},new[]{c,d},new HashSet<string>{a.Id},new HashSet<string>{b.Id},"",new[]{"desert"});
            Assert.That(result.Select(r=>r.Id),Is.EqualTo(new[]{c.Id}));
        }
        [Test] public void SameFilenameDifferentGuidCannotOverwriteAnotherBatchModel() {
            var a=Record("1","box");var b=Record("2","box");var c=Record("3","crate");
            Assert.That(ThumbnailQueue.Next(new[]{a,b,c},new[]{a,b,c},new HashSet<string>(),new HashSet<string>(),"",Array.Empty<string>()).Length,Is.EqualTo(2));
        }
        [Test] public void BackgroundFinishesLoadedArchiveBeforeOpeningAnother() {
            var a=Record("1","a","other.rpak");var b=Record("2","b","loaded.rpak");
            var result=ThumbnailQueue.Next(new[]{a,b},Array.Empty<GameAssetRecord>(),new HashSet<string>(),new HashSet<string>(),"",Array.Empty<string>(),8,"loaded.rpak");
            Assert.That(result.Select(r=>r.Id),Is.EqualTo(new[]{b.Id}));
        }
        [Test] public void VisiblePageCanOverrideLoadedArchivePreference() {
            var a=Record("1","a","other.rpak");var b=Record("2","b","loaded.rpak");
            var result=ThumbnailQueue.Next(new[]{a,b},new[]{a},new HashSet<string>(),new HashSet<string>(),"",Array.Empty<string>(),8,"loaded.rpak");
            Assert.That(result.Select(r=>r.Id),Is.EqualTo(new[]{a.Id}));
        }
        [Test] public void VisiblePageFinishesItsLoadedArchiveBeforeSwitching() {
            var first=Record("1","first","other.rpak");var loaded=Record("2","loaded","loaded.rpak");
            var result=ThumbnailQueue.Next(new[]{first,loaded},new[]{first,loaded},new HashSet<string>(),new HashSet<string>(),"",Array.Empty<string>(),8,"loaded.rpak");
            Assert.That(result.Select(r=>r.Id),Is.EqualTo(new[]{loaded.Id}));
        }
        [Test] public void MissingSceneModelsWinOverVisibleAndCustomModels() {
            var scene=Record("1","scene","scene.rpak");var visible=Record("2","visible","visible.rpak");var custom=Record("3","custom","custom.rpak");
            var result=ThumbnailQueue.Next(new[]{custom,visible,scene},new[]{scene},new[]{visible},new[]{custom},new HashSet<string>(),new HashSet<string>(),"",Array.Empty<string>());
            Assert.That(result.Select(r=>r.Id),Is.EqualTo(new[]{scene.Id}));
        }
        [Test] public void VisibleAndCustomModelsOverrideLoadedArchivePreference() {
            var background=Record("1","background","loaded.rpak");var custom=Record("2","custom","custom.rpak");
            var result=ThumbnailQueue.Next(new[]{background,custom},Array.Empty<GameAssetRecord>(),Array.Empty<GameAssetRecord>(),new[]{custom},new HashSet<string>(),new HashSet<string>(),"",Array.Empty<string>(),8,"loaded.rpak");
            Assert.That(result.Select(r=>r.Id),Is.EqualTo(new[]{custom.Id}));
        }
        [Test] public void OnDemandQueueNeverFillsFromOffscreenAssets() {
            var visible=Record("1","visible");var offscreen=Record("2","offscreen");
            var result=ThumbnailQueue.NextVisible(new[]{visible,offscreen},new[]{visible},new HashSet<string>(),new HashSet<string>(),"",Array.Empty<string>());
            Assert.That(result.Select(r=>r.Id),Is.EqualTo(new[]{visible.Id}));
            result=ThumbnailQueue.NextVisible(new[]{visible,offscreen},new[]{visible},new HashSet<string>{visible.Id},new HashSet<string>(),"",Array.Empty<string>());
            Assert.That(result,Is.Empty);
        }
    }
}
