using UnityEngine;
using UnityEngine.InputSystem;
namespace ReMap.Standalone {
    public sealed partial class WorldView {
        private Vector3 orbitPoint, focusFrom, focusTo;
        private float distanceFrom, distanceTo, focusElapsed;
        private const float FocusDuration=.28f;
        public bool CameraAnimating {get;private set;}
        public bool Orbiting {get;private set;}
        public Vector3 OrbitPoint => orbitPoint;
        public static bool OrbitShortcut => Keyboard.current?.altKey.isPressed==true;
        public void SmoothFocusPoint(Vector3 point) {
            if(!float.IsFinite(point.x)||!float.IsFinite(point.y)||!float.IsFinite(point.z))return;
            focusFrom=focus;distanceFrom=distance;focusTo=point;orbitPoint=point;
            distanceTo=Mathf.Clamp(Vector3.Distance(Camera.transform.position,point)*.65f,.5f,1500);
            focusElapsed=0;CameraAnimating=true;
        }
        public void TickCamera(float deltaTime) {
            if(!CameraAnimating||!float.IsFinite(deltaTime)||deltaTime<=0)return;
            focusElapsed+=deltaTime;float t=Mathf.Clamp01(focusElapsed/FocusDuration);float eased=t*t*(3-2*t);
            focus=Vector3.LerpUnclamped(focusFrom,focusTo,eased);distance=Mathf.LerpUnclamped(distanceFrom,distanceTo,eased);
            if(t>=1)CameraAnimating=false;
            UpdateCamera();
        }
        public void CancelCameraMotion() {CameraAnimating=false;}
        public void CancelNavigation() {CancelCameraMotion();Orbiting=false;middleHeld=false;middlePoint=null;ReleaseCameraCursor(Mouse.current);}
        public void CancelMiddleGesture() {middleHeld=false;middlePoint=null;}
        private void BeginOrbit() {
            CancelCameraMotion();var direction=orbitPoint-Camera.transform.position;
            if(direction.sqrMagnitude<.09f)direction=Camera.transform.forward*.3f;
            distance=direction.magnitude;direction/=distance;
            yaw=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
            pitch=Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(direction.y,-1,1))*Mathf.Rad2Deg,-89,89);
            focus=orbitPoint;Orbiting=true;
        }
    }
}
