using System;
using System.Collections;
using System.IO;
using System.Linq;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;
namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private IEnumerator WorkspaceFeaturesSmoke()
        {
            for (int i = 0; i < 12; i++) yield return null;
            bool success = false; var oldMouse = Mouse.current; Mouse mouse = null;
            string testRoot = Path.Combine(Application.temporaryCachePath, "SharedTextureQA", Guid.NewGuid().ToString("N"));
            string modelRoot = Path.Combine(testRoot, "generation", "models", "test");
            string materialRoot = Path.Combine(modelRoot, "textured", "mdl", "test"); Directory.CreateDirectory(materialRoot);
            var png = new Texture2D(512, 256, TextureFormat.RGBA32, false);
            var pixels = Enumerable.Repeat(new Color32(187, 75, 36, 255), 512 * 256).ToArray(); png.SetPixels32(pixels); png.Apply();
            var bytes = png.EncodeToPNG(); Destroy(png);
            File.WriteAllBytes(Path.Combine(materialRoot, "first.png"), bytes); File.WriteAllBytes(Path.Combine(materialRoot, "same-other-name.png"), bytes);
            var normalize = SharedTextureCache.Normalize(modelRoot, 256); while (!normalize.IsCompleted) yield return null;
            try
            {
                if (normalize.IsFaulted) throw normalize.Exception;
                var sharedFiles = Directory.GetFiles(Path.Combine(testRoot, "Textures"), "*.png");
                if (sharedFiles.Length != 1 || File.Exists(Path.Combine(materialRoot, "first.png"))) throw new Exception("Shared texture deduplication failed.");
                string resolved = SharedTextureCache.Resolve(Path.Combine(materialRoot, "test.cast"), "first.png");
                if (resolved == null || !string.Equals(Path.GetFullPath(resolved), Path.GetFullPath(sharedFiles[0]), StringComparison.OrdinalIgnoreCase)) throw new Exception("CAST manifest lookup failed.");
                var texture = SharedTextureCache.Acquire(resolved); var texture2 = SharedTextureCache.Acquire(resolved);
                if (texture != texture2 || texture.width != 256 || texture.height != 128 || SharedTextureCache.ResidentCount != 1) throw new Exception("Texture resize or shared GPU resource failed.");
                SharedTextureCache.Release(resolved); SharedTextureCache.Release(resolved);
                if (SharedTextureCache.ResidentCount != 0) throw new Exception("Texture reference leak.");
                var first = snapshot.objects.First(); Select(null); CreateGroup(); string group = selectedId;
                ReparentObject(first.id, group); Select(group);
                Vector3 before = WorldView.ToVector(world.WorldPose(first.id).position);
                positionInput.Change(new Vector3(3, 2, 1));
                if (Vector3.Distance(WorldView.ToVector(world.WorldPose(first.id).position), before + new Vector3(3, 2, 1)) > .001f) throw new Exception("Live group inspector movement failed.");
                if (snapshot.objects.Find(o => o.id == group).position.x != 0) throw new Exception("Live inspector added undo entries before commit.");
                CommitInspectorEdit(); session.Undo(); Refresh();
                if (Vector3.Distance(WorldView.ToVector(world.WorldPose(first.id).position), before) > .001f) throw new Exception("Inspector gesture undo failed.");
                session.Edit(d => d.objects.Find(o => o.id == group).disabled = true); Refresh();
                if (world.GenerationObjects(snapshot).Any(o => o.id == first.id)) throw new Exception("Disabled group generation exclusion failed.");
                session.Edit(d => d.objects.Find(o => o.id == group).disabled = false); Refresh();
                ReparentObject(first.id, "");
                if (Vector3.Distance(WorldView.ToVector(world.WorldPose(first.id).position), before) > .001f) throw new Exception("Reparent did not preserve world transform.");
                Select(group); ReparentObject(first.id, group); Select(group); positionInput.Change(new Vector3(2, 0, 0)); CommitInspectorEdit(); world.Focus(group);
                SetStatus("Dossiers, mouvement en direct, désactivation, focus et textures partagées vérifiés."); success = true;
            }
            catch (Exception ex) { Debug.LogException(ex); }
            finally { if (mouse != null) InputSystem.RemoveDevice(mouse); oldMouse?.MakeCurrent(); }
            if (success)
            {
                mouse = InputSystem.AddDevice<Mouse>(); search.Focus();
                for (int i = 0; i < 2; i++) yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = world.Camera.pixelRect.center, buttons = 2 }); InputSystem.Update(); Update();
                for (int i = 0; i < 2; i++) yield return null;
                if (root.panel.focusController.focusedElement != viewport)
                { success = false; Debug.LogError("Camera focus failed: " + root.panel.focusController.focusedElement + " mouse=" + Mouse.current.position.ReadValue() + " rect=" + world.Camera.pixelRect); }
                InputSystem.QueueStateEvent(mouse, new MouseState { position = world.Camera.pixelRect.center }); InputSystem.Update();
                InputSystem.RemoveDevice(mouse); mouse = null; oldMouse?.MakeCurrent();
            }
            // Exercise the actual drop handler without game extraction, then validate the resulting parent.
            if (success)
            {
                int beforeCount = snapshot.objects.Count; string folder = selectedId;
                var drop = DropLibraryItem(catalog.Entries[0], null, null, folder, Vector2.zero, false); while (!drop.IsCompleted) yield return null;
                if (snapshot.objects.Count != beforeCount + 1 || snapshot.objects.Last().parentId != folder) { success = false; Debug.LogError("Library drop into folder failed."); }
            }
            for (int i = 0; i < 4; i++) yield return null;
            if (success)
            {
                yield return new WaitForEndOfFrame(); var image = ScreenCapture.CaptureScreenshotAsTexture();
                if (image != null) { File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "hierarchy-preview.png"), image.EncodeToPNG()); Destroy(image); }
                Debug.Log("REMAP_WORKSPACE_FEATURES_OK");
            }
            Application.Quit(success ? 0 : 1);
        }
    }
}
