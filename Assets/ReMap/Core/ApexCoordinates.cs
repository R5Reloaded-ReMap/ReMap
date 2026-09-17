using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
namespace ReMap.Standalone.Core {
    // CAST vertices retain Source units. Unity rendering uses metres and swaps the Y/Z axes.
    public static class ApexCoordinates {
        public const int MaxWorldCoord=(1<<16)-1;
        public const int CoordRange=MaxWorldCoord+MaxWorldCoord+1;
        public const float MetersPerUnit=.0254f;
        public const float MaxUnityCoord=MaxWorldCoord*MetersPerUnit;
        public const float DefaultGridUnits=64;
        public const float DefaultGridMeters=DefaultGridUnits*MetersPerUnit;
        public static string LimitMessage => L.T("#POSITION_OUTSIDE_APEX_WORLD_WORLD");
        public static Float3 ToUnity(Float3 source)=>new Float3(source.x*MetersPerUnit,source.z*MetersPerUnit,source.y*MetersPerUnit);
        public static Float3 ToApex(Float3 unity)=>new Float3(unity.x/MetersPerUnit,unity.z/MetersPerUnit,unity.y/MetersPerUnit);
        public static bool ContainsUnity(Float3 p)=>p.IsFinite&&Math.Abs(p.x)<=MaxUnityCoord&&Math.Abs(p.y)<=MaxUnityCoord&&Math.Abs(p.z)<=MaxUnityCoord;
        public static void ValidateWorld(MapDocument document) {
            var byId=document.objects.ToDictionary(o=>o.id);var matrices=new Dictionary<string,Matrix4x4>(byId.Count);
            Matrix4x4 World(MapObject item) {
                if(matrices.TryGetValue(item.id,out var cached))return cached;
                const float radians=(float)(Math.PI/180);
                var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,item.rotation.y*radians)*Quaternion.CreateFromAxisAngle(Vector3.UnitX,item.rotation.x*radians)*Quaternion.CreateFromAxisAngle(Vector3.UnitZ,item.rotation.z*radians);
                var m=Matrix4x4.CreateScale(item.scale.x,item.scale.y,item.scale.z)*Matrix4x4.CreateFromQuaternion(q)*Matrix4x4.CreateTranslation(item.position.x,item.position.y,item.position.z);
                if(!string.IsNullOrEmpty(item.parentId))m*=World(byId[item.parentId]);
                matrices.Add(item.id,m);return m;
            }
            foreach(var item in document.objects) {
                var m=World(item);
                var editorPosition=new Float3(m.M41,m.M42,m.M43);
                if(!ContainsUnity(editorPosition))throw new ArgumentException(item.displayName+" : "+LimitMessage);
                var gamePosition=new Float3(editorPosition.x+document.originOffset.x,editorPosition.y+document.originOffset.y,editorPosition.z+document.originOffset.z);
                if(!ContainsUnity(gamePosition))throw new ArgumentException(item.displayName+" : "+LimitMessage);
            }
            if(!ContainsUnity(document.originOffset))throw new ArgumentException(L.T("#SCENE")+" : "+LimitMessage);
        }
    }
}
