using System;
using System.Collections.Generic;
using System.Linq;
namespace ReMap.Standalone.Core
{
    public static class MapAssembly
    {
        public static string Insert(MapDocument destination, MapDocument assembly, Float3 position)
        {
            assembly.Validate();
            var roots=assembly.objects.Where(o=>string.IsNullOrEmpty(o.parentId)).ToArray();
            if(roots.Length!=1||!roots[0].isGroup)throw new ArgumentException(L.T("#ASSEMBLY_HAVE_EXACTLY_ONE_ROOT"));
            if(destination.objects.Count+assembly.objects.Count>10000)throw new ArgumentException(L.T("#ASSEMBLY_EXCEED_10_000_ITEM"));
            var mapping=assembly.objects.ToDictionary(o=>o.id,o=>Guid.NewGuid().ToString("N"));
            var copies=new List<MapObject>();
            foreach(var original in assembly.objects) {
                var copy=original.Copy();copy.id=mapping[original.id];
                copy.parentId=string.IsNullOrEmpty(original.parentId)?"":mapping[original.parentId];
                MapHierarchy.RemapInternalReferences(copy,mapping);
                if(copy.parentId=="")copy.position=new Float3(original.position.x+position.x,original.position.y+position.y,original.position.z+position.z);
                copies.Add(copy);
            }
            var check=destination.Copy();check.objects.AddRange(copies);check.Validate();
            destination.objects.AddRange(copies);return mapping[roots[0].id];
        }
    }
}
