using System;
using System.Collections.Generic;
using System.Linq;
namespace ReMap.Standalone.Core
{
    public static class ThumbnailQueue
    {
        public static GameAssetRecord[] Next(IEnumerable<GameAssetRecord> records,IEnumerable<GameAssetRecord> visible,ISet<string> ready,ISet<string> failed,string search,string[] targets,int limit=8,string preferredArchive=null) {
            var eligible=records.Where(r=>r.Supports(targets)&&!ready.Contains(r.Id)&&!failed.Contains(r.Id)).ToArray();
            var ids=eligible.Select(r=>r.Id).ToHashSet();
            var ordered=visible.Where(r=>ids.Contains(r.Id)).Concat(eligible.Where(r=>r.modelPath.IndexOf(search??"",StringComparison.OrdinalIgnoreCase)>=0)).Concat(eligible).GroupBy(r=>r.Id).Select(g=>g.First()).ToArray();
            if(ordered.Length==0)return Array.Empty<GameAssetRecord>();
            string Origin(GameAssetRecord r)=>r.origins.First(o=>o.mapId==""||targets.Contains(o.mapId)).archive;
            // Visible models remain first. Once they are ready, finish the open archive before switching.
            bool visiblePending=visible.Any(r=>ids.Contains(r.Id));
            string archive=!visiblePending&&preferredArchive!=null&&ordered.Any(r=>Origin(r)==preferredArchive)?preferredArchive:Origin(ordered[0]);
            return ordered.Where(r=>Origin(r)==archive).GroupBy(r=>r.Name,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).Take(Math.Min(8,Math.Max(1,limit))).ToArray();
        }
    }
}
