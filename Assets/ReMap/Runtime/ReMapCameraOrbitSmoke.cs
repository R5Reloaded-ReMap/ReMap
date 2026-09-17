using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace ReMap.Standalone {
    public sealed partial class ReMapApp {
        private async Task CheckCameraOrbit() {
            await TreeFrames(12);enabled=false;
            var previousMouse=Mouse.current;var previousKeys=Keyboard.current;
            var mouse=InputSystem.AddDevice<Mouse>();var keys=InputSystem.AddDevice<Keyboard>();
            void Input(Vector2 position,ushort buttons,Vector2 delta=default,bool alt=false) {
                InputSystem.QueueStateEvent(keys,alt?new KeyboardState(Key.LeftAlt):new KeyboardState());
                InputSystem.QueueStateEvent(mouse,new MouseState{position=position,buttons=buttons,delta=delta});InputSystem.Update();mouse.MakeCurrent();keys.MakeCurrent();
            }
            try {
                var floor=snapshot.objects.Find(o=>o.assetId=="demo:floor");Select(floor.id);world.Focus(floor.id);
                Vector2 screen=world.Camera.WorldToScreenPoint(new Vector3(1,.25f,1));
                if(!world.SurfacePoint(screen,0,out var hit))throw new Exception("Test surface missing");
                if(Vector3.Distance(hit,world.Centre(floor.id).Value)<.5f)throw new Exception("Test needs an off-centre surface point");
                var before=world.Camera.transform.position;float desiredRadius=Vector3.Distance(before,hit)*.65f;
                Input(screen,4);world.Navigate(0);Input(screen,0);world.Navigate(0);
                if(!world.CameraAnimating||Vector3.Distance(before,world.Camera.transform.position)>.0001f)throw new Exception("Middle click jumped instead of starting animation");
                world.TickCamera(.07f);if(!world.CameraAnimating||Vector3.Distance(before,world.Camera.transform.position)<.001f)throw new Exception("Animation did not advance gradually");
                world.TickCamera(.3f);
                if(world.CameraAnimating||Vector3.Distance(world.OrbitPoint,hit)>.0001f||Vector3.Dot(world.Camera.transform.forward,(hit-world.Camera.transform.position).normalized)<.9999f)throw new Exception("Focus missed clicked point");
                if(Mathf.Abs(Vector3.Distance(world.Camera.transform.position,hit)-desiredRadius)>.001f)throw new Exception("Focus approached wrong distance");
                Debug.Log("REMAP_SMOOTH_POINT_FOCUS_OK");
                // Route through the real app input dispatcher, including while an item is being placed.
                int count=snapshot.objects.Count;var original=world.WorldPose(floor.id);var entry=catalog.Entries[0];BeginPlacement(entry);
                screen=world.Camera.pixelRect.center;Input(screen,1,alt:true);Update();
                if(!world.Orbiting||snapshot.objects.Count!=count||draggingGizmo||sceneSelectionPending||placing!=entry)throw new Exception("Orbit started placement or object editing");
                before=world.Camera.transform.position;float radius=Vector3.Distance(before,hit);
                Input(screen+new Vector2(100,30),1,new Vector2(100,30),true);Update();
                if(Vector3.Distance(before,world.Camera.transform.position)<.1f||Mathf.Abs(Vector3.Distance(world.Camera.transform.position,hit)-radius)>.001f)throw new Exception("Camera did not orbit at constant radius");
                if(Vector3.Dot(world.Camera.transform.forward,(hit-world.Camera.transform.position).normalized)<.9999f||snapshot.objects.Count!=count)throw new Exception("Orbit lost its pivot or placed an object");
                Input(screen,0);Update();if(world.Orbiting)throw new Exception("Orbit gesture did not release");
                CancelPlacement();Select(floor.id);UpdateGizmoVisual();
                // Alt-click directly on a gizmo must remain camera input.
                screen=world.Camera.WorldToScreenPoint(gizmoVisual.Pivot);Input(screen,1,alt:true);Update();
                Input(screen+Vector2.right*40,1,Vector2.right*40,true);Update();Input(screen,0);Update();
                if(draggingGizmo||sceneSelectionPending||Vector3.Distance(WorldView.ToVector(world.WorldPose(floor.id).position),WorldView.ToVector(original.position))>.0001f)throw new Exception("Orbit moved a selected object");
                Debug.Log("REMAP_ORBIT_PIVOT_AND_PLACEMENT_OK");
                bool cursorVisible=Cursor.visible;var cursorLock=Cursor.lockState;
                before=world.Camera.transform.position;Input(screen,2,new Vector2(30,10));world.Navigate(0);
                if(Cursor.visible||Cursor.lockState!=CursorLockMode.Locked)throw new Exception("Right look did not hide and lock the cursor");
                if(Vector3.Distance(before,world.Camera.transform.position)>.001f)throw new Exception("Right look moved the camera position");
                if(Vector3.Distance(world.OrbitPoint,hit)>.0001f)throw new Exception("Free look lost the chosen orbit point");
                Input(screen,0);world.Navigate(0);
                if(Cursor.visible!=cursorVisible||Cursor.lockState!=cursorLock)throw new Exception("Right look did not restore the cursor");
                world.SmoothFocusPoint(hit+Vector3.right);world.TickCamera(.1f);before=world.Camera.transform.position;
                Input(screen,2);world.Navigate(0);world.TickCamera(1);
                if(world.CameraAnimating||Vector3.Distance(before,world.Camera.transform.position)>.001f)throw new Exception("Manual navigation failed to cancel animation");
                Input(screen,0);world.Navigate(0);before=world.Camera.transform.position;
                Input(screen,4);world.Navigate(0);Input(screen+Vector2.right*30,4,Vector2.right*30);world.Navigate(0);Input(screen+Vector2.right*30,0);world.Navigate(0);
                if(world.CameraAnimating||Vector3.Distance(before,world.Camera.transform.position)<.01f)throw new Exception("Middle drag recentered instead of panning");
                // Ground works too; no object identifier is required for the middle-click pivot.
                world.Focus(null);screen=world.Camera.WorldToScreenPoint(new Vector3(12,-.015f,0));
                if(world.Pick(screen)!=null||!world.SurfacePoint(screen,0,false,out hit))throw new Exception("Ground focus fixture is not empty ground");
                Input(screen,4);world.Navigate(0);Input(screen,0);world.Navigate(0);world.TickCamera(.3f);
                if(Vector3.Distance(world.OrbitPoint,hit)>.001f)throw new Exception("Middle click could not focus empty ground");
                Debug.Log("REMAP_CAMERA_ORBIT_OK: surface focus, ground, orbit, placement, gizmo, pan, free look and interruption");
            } finally {
                world.CancelNavigation();InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keys);previousMouse?.MakeCurrent();previousKeys?.MakeCurrent();enabled=true;
            }
        }
        private IEnumerator CameraOrbitSmoke() {
            var task=CheckCameraOrbit();while(!task.IsCompleted)yield return null;
            if(task.IsFaulted){Debug.LogException(task.Exception);Application.Quit(1);}else Application.Quit(0);
        }
    }
}
