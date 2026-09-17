using System;
using System.Collections;
using System.IO;
using System.Linq;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private IEnumerator InteractionSmoke()
        {
            for (int i = 0; i < 10; i++) yield return null;
            ShowSettings(true);
            for (int i = 0; i < 3; i++) yield return null;
            if (Environment.GetCommandLineArgs().Contains("-remapSettingsShot"))
            {
                yield return new WaitForEndOfFrame();
                var image = ScreenCapture.CaptureScreenshotAsTexture();
                if (image != null) { File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "settings-preview.png"), image.EncodeToPNG()); Destroy(image); }
            }
            bool success = false;
            Mouse testMouse = null; Keyboard testKeys = null;
            var originalMouse = Mouse.current; var originalKeys = Keyboard.current;
            try
            {
                if (viewport.worldBound.height < 150 || libraryDock.worldBound.y < viewport.worldBound.yMax - 2) throw new Exception("Dock overlaps viewport.");
                ShowIndexing(true);
                if (!IndexingOpen || mapChoices.worldBound.width < 100) throw new Exception("Indexing modal missing.");
                ShowIndexing(false);
                testMouse = InputSystem.AddDevice<Mouse>(); testKeys = InputSystem.AddDevice<Keyboard>();
                void MouseAt(Vector2 point, ushort buttons) { InputSystem.QueueStateEvent(testMouse, new MouseState { position = point, buttons = buttons }); InputSystem.Update(); testMouse.MakeCurrent(); testKeys.MakeCurrent(); }
                moveSnap=1;world.GridStep=moveSnap;Select(snapshot.objects[0].id); UpdateGizmoVisual();
                string id = selectedId;
                Vector3 original = WorldView.ToVector(snapshot.objects[0].position);
                Vector3 pivot = gizmoVisual.Pivot; float length = gizmoVisual.WorldLength;
                Vector2 a = world.Camera.WorldToScreenPoint(pivot), b = world.Camera.WorldToScreenPoint(pivot + Vector3.right * length);
                Vector2 hit = Vector2.Lerp(a, b, .78f);
                MouseAt(hit, 1); if (!HandleGizmoInput(hit, true) || !draggingGizmo || dragAxis != 0) throw new Exception("X arrow could not be picked: hit=" + gizmoVisual.Hit(hit) + " selected=" + selectedId + " dragging=" + draggingGizmo + " axis=" + dragAxis + " mouse=" + Mouse.current.position.ReadValue() + " pressed=" + Mouse.current.leftButton.wasPressedThisFrame + " viewport=" + world.Camera.pixelRect);
                Vector2 moved = hit + (b - a) / length * 2;
                MouseAt(moved, 1); HandleGizmoInput(moved, true);
                if (Vector3.Distance(positionInput.value, original + Vector3.right * 2) > .02f) throw new Exception("Inspector did not follow live gizmo drag.");
                MouseAt(moved, 0); HandleGizmoInput(moved, true);
                var translated = WorldView.ToVector(snapshot.objects.Find(o => o.id == id).position);
                if (Vector3.Distance(translated, original + Vector3.right * 2) > .02f) throw new Exception("X drag failed.");
                session.Undo(); Refresh(); if (Vector3.Distance(WorldView.ToVector(snapshot.objects.Find(o => o.id == id).position), original) > .001f) throw new Exception("One-step drag undo failed.");
                UpdateGizmoVisual();var planeCentre=gizmoVisual.HandleCenter(5);if(!planeCentre.HasValue)throw new Exception("XZ plane handle was not drawn.");
                MouseAt(planeCentre.Value,1);HandleGizmoInput(planeCentre.Value,true);if(!draggingGizmo||dragAxis!=5)throw new Exception("XZ plane square could not be picked: "+gizmoVisual.Hit(planeCentre.Value));
                var planeFinish=(Vector2)world.Camera.WorldToScreenPoint(dragPlaneStart+Vector3.right*2+Vector3.forward*3);
                MouseAt(planeFinish,1);HandleGizmoInput(planeFinish,true);MouseAt(planeFinish,0);HandleGizmoInput(planeFinish,true);
                var planeMoved=WorldView.ToVector(snapshot.objects.Find(o=>o.id==id).position);if(Vector3.Distance(planeMoved,original+Vector3.right*2+Vector3.forward*3)>.03f)throw new Exception("XZ plane drag failed: "+planeMoved);
                session.Undo();Refresh();
                SetGizmoMode(true); UpdateGizmoVisual(); pivot = gizmoVisual.Pivot; length = gizmoVisual.WorldLength;
                Vector2 start = world.Camera.WorldToScreenPoint(pivot + (Vector3.right + Vector3.forward).normalized * length);
                Vector3 startOffset = (Vector3.right + Vector3.forward).normalized * length;
                Vector2 finish = world.Camera.WorldToScreenPoint(pivot + Quaternion.AngleAxis(30, Vector3.up) * startOffset);
                MouseAt(start, 1); HandleGizmoInput(start, true);
                if (!draggingGizmo || dragAxis != 1) throw new Exception("Y rotation ring could not be picked.");
                MouseAt(finish, 1); HandleGizmoInput(finish, true); MouseAt(finish, 0); HandleGizmoInput(finish, true);
                var rotation = snapshot.objects.Find(o => o.id == id).rotation;
                if (Mathf.Abs(Mathf.DeltaAngle(0, rotation.y) - 30) > .1f) throw new Exception("Rotation drag failed: " + rotation.y);
                session.Undo(); Refresh();
                float beforeY = world.Camera.transform.position.y;
                InputSystem.QueueStateEvent(testKeys, new KeyboardState(Key.S)); MouseAt(world.Camera.pixelRect.center, 2); world.Navigate(.1f);
                if (world.Camera.transform.position.y <= beforeY) throw new Exception("Backward flight did not rise while looking down.");
                InputSystem.QueueStateEvent(testKeys, new KeyboardState()); MouseAt(world.Camera.pixelRect.center, 0);
                Vector3 target;
                Vector2 objectScreen = world.Camera.WorldToScreenPoint(new Vector3(1, .25f, -1));
                if (world.Pick(objectScreen) != id) throw new Exception("Middle-click target ray missed.");
                if(!world.SurfacePoint(objectScreen,0,out target))throw new Exception("Middle click surface absent");
                MouseAt(objectScreen, 4); world.Navigate(0); MouseAt(objectScreen, 0); world.Navigate(0);world.TickCamera(.3f);
                if (Vector3.Dot(world.Camera.transform.forward, (target - world.Camera.transform.position).normalized) < .999f) throw new Exception("Middle click did not centre the hit point.");
                SetGizmoMode(false); UpdateGizmoVisual();
                SetStatus("Poignées XYZ et plans 2D, rotation, annulation, caméra 3D et clic molette vérifiés."); success = true;
            }
            catch (Exception ex) { Debug.LogException(ex); }
            finally
            {
                if (testMouse != null) InputSystem.RemoveDevice(testMouse); if (testKeys != null) InputSystem.RemoveDevice(testKeys);
                originalMouse?.MakeCurrent(); originalKeys?.MakeCurrent();
            }
            for (int i = 0; i < 3; i++) yield return null;
            if (success)
            {
                yield return new WaitForEndOfFrame(); var texture = ScreenCapture.CaptureScreenshotAsTexture();
                if (texture != null) { File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "interaction-preview.png"), texture.EncodeToPNG()); Destroy(texture); }
                Debug.Log("REMAP_INTERACTION_SMOKE_OK");
            }
            Application.Quit(success ? 0 : 1);
        }
    }
}
