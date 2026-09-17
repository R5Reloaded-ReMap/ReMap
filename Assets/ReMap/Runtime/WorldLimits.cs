using System.Collections.Generic;
using ReMap.Standalone.Core;
using UnityEngine;
namespace ReMap.Standalone {
    public sealed partial class WorldView {
        private struct PreviewPose {
            public Transform t; public Vector3 p,s;public Quaternion r;
            public PreviewPose(Transform t){this.t=t;p=t.localPosition;r=t.localRotation;s=t.localScale;}
            public void Restore(){t.localPosition=p;t.localRotation=r;t.localScale=s;}
        }
        private readonly List<PreviewPose> previousPreview=new List<PreviewPose>();
        public bool WithinWorldLimits() {
            foreach(var instance in instances.Values)if(!ApexCoordinates.ContainsUnity(ToData(instance.transform.position)))return false;
            return true;
        }
    }
}
