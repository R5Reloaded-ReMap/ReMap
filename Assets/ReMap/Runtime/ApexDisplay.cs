using ReMap.Standalone.Core;
using UnityEngine;
namespace ReMap.Standalone {
    public static class ApexDisplay {
        public static Vector3 Position(Vector3 unity)=>WorldView.ToVector(ApexCoordinates.ToApex(WorldView.ToData(unity)));
        public static Vector3 GamePosition(Vector3 editorWorld,Float3 originOffset)=>Position(editorWorld+WorldView.ToVector(originOffset));
        public static Vector3 UnityPosition(Vector3 apex)=>WorldView.ToVector(ApexCoordinates.ToUnity(WorldView.ToData(apex)));
        public static Vector3 Axes(Vector3 v)=>new Vector3(v.x,v.z,v.y);
        // Source angles are pitch (Y), yaw (Z), roll (X), composed Rz * Ry * Rx.
        // Swapping Y/Z changes handedness, so quaternion vector components also change sign.
        public static Vector3 Angles(Vector3 unityEuler) {
            if(!WorldView.ToData(unityEuler).IsFinite)return unityEuler;
            var u=Quaternion.Euler(unityEuler);var source=new Quaternion(-u.x,-u.z,-u.y,u.w);
            var f=source*Vector3.right;var side=source*Vector3.up;var up=source*Vector3.forward;
            float planar=Mathf.Sqrt(f.x*f.x+f.y*f.y);
            return new Vector3(Mathf.Atan2(-f.z,planar),planar>1e-5f?Mathf.Atan2(f.y,f.x):Mathf.Atan2(-side.x,side.y),planar>1e-5f?Mathf.Atan2(side.z,up.z):0)*Mathf.Rad2Deg;
        }
        public static Vector3 UnityAngles(Vector3 angles) {
            if(!WorldView.ToData(angles).IsFinite)return angles;
            var source=Quaternion.AngleAxis(angles.y,Vector3.forward)*Quaternion.AngleAxis(angles.x,Vector3.up)*Quaternion.AngleAxis(angles.z,Vector3.right);
            return new Quaternion(-source.x,-source.z,-source.y,source.w).eulerAngles;
        }
    }
}
