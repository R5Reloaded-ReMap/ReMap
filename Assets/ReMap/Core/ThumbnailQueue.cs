using System;
using System.Collections.Generic;
using System.Linq;
namespace ReMap.Standalone.Core
{
    public static class ThumbnailQueue
    {
        public static GameAssetRecord[] NextVisible(IEnumerable<GameAssetRecord> records,IEnumerable<GameAssetRecord> visible,ISet<string> ready,ISet<string> failed,string search,string[] targets,int limit=8,string preferredArchive=null) {
            var viewport=(visible??Enumerable.Empty<GameAssetRecord>()).ToArray();
            var ids=viewport.Select(r=>r.Id).ToHashSet();
            return Next((records??Enumerable.Empty<GameAssetRecord>()).Where(r=>ids.Contains(r.Id)),viewport,ready,failed,search,targets,limit,preferredArchive);
        }
        public static GameAssetRecord[] Next(IEnumerable<GameAssetRecord> records,IEnumerable<GameAssetRecord> visible,ISet<string> ready,ISet<string> failed,string search,string[] targets,int limit=8,string preferredArchive=null) {
            return Next(records,Enumerable.Empty<GameAssetRecord>(),visible,Enumerable.Empty<GameAssetRecord>(),ready,failed,search,targets,limit,preferredArchive);
        }
        public static GameAssetRecord[] Next(IEnumerable<GameAssetRecord> records,IEnumerable<GameAssetRecord> scene,IEnumerable<GameAssetRecord> visible,IEnumerable<GameAssetRecord> custom,ISet<string> ready,ISet<string> failed,string search,string[] targets,int limit=8,string preferredArchive=null) {
            var eligible=records.Where(r=>r.Supports(targets)&&!ready.Contains(r.Id)&&!failed.Contains(r.Id)).ToArray();
            var ids=eligible.Select(r=>r.Id).ToHashSet();
            var scenePending=(scene??Enumerable.Empty<GameAssetRecord>()).Where(r=>ids.Contains(r.Id)).ToArray();
            var visiblePending=(visible??Enumerable.Empty<GameAssetRecord>()).Where(r=>ids.Contains(r.Id)).ToArray();
            var customPending=(custom??Enumerable.Empty<GameAssetRecord>()).Where(r=>ids.Contains(r.Id)).ToArray();
            var ordered=scenePending.Concat(visiblePending).Concat(customPending).Concat(eligible.Where(r=>r.modelPath.IndexOf(search??"",StringComparison.OrdinalIgnoreCase)>=0)).Concat(eligible).GroupBy(r=>r.Id).Select(g=>g.First()).ToArray();
            if(ordered.Length==0)return Array.Empty<GameAssetRecord>();
            string Origin(GameAssetRecord r)=>r.origins.First(o=>o.mapId==""||targets.Contains(o.mapId)).archive;
            // Foreground priorities may switch archives. Once they are ready, finish the open archive.
            bool foregroundPending=scenePending.Length>0||visiblePending.Length>0||customPending.Length>0;
            string archive=!foregroundPending&&preferredArchive!=null&&ordered.Any(r=>Origin(r)==preferredArchive)?preferredArchive:Origin(ordered[0]);
            return ordered.Where(r=>Origin(r)==archive).GroupBy(r=>r.Name,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).Take(Math.Min(8,Math.Max(1,limit))).ToArray();
        }
    }
}
