using System;
using System.Collections.Generic;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace ReMap.Standalone
{
    internal sealed class ConstructionPlaneSurface : MonoBehaviour { }

    public sealed partial class WorldView : IDisposable
    {
        private readonly GameObject root;
        private readonly Dictionary<string, GameObject> instances = new Dictionary<string, GameObject>();
        private readonly Dictionary<GameObject, string> instanceIds = new Dictionary<GameObject, string>();
        private readonly Dictionary<string, Bounds> selectionBounds = new Dictionary<string, Bounds>();
        public int LastSyncTransformWrites { get; private set; }
        private readonly Dictionary<string, string> assetIds = new Dictionary<string, string>();
        public readonly WorkspaceModelProvider models;
        private readonly Material groundMaterial, lineMaterial;
        private readonly Collider constructionPlaneCollider;
        private readonly Shader objectShader;
        private readonly LineRenderer outline;
        private Vector3 focus = new Vector3(0, 0, 0);
        private float yaw = 35, pitch = 35, distance = 20;
        private GameObject ghost;
        private string ghostAsset;
        private GameObject selectionGhost;
        private MapDocument selectionGhostDocument;
        private readonly Dictionary<GameObject, string> selectionGhostAssets = new Dictionary<GameObject, string>();
        private GameObject toolGhost;
        private readonly Dictionary<GameObject, string> toolGhostAssets = new Dictionary<GameObject, string>();
        public Camera Camera { get; }
        public int LoadedModelCount => models.LoadedModelCount;
        public bool HasPreview => ghost != null || selectionGhost != null;
        public int PreviewObjectCount => selectionGhostAssets.Count;
        public bool HasToolPreview => toolGhost != null;
        public int ToolPreviewObjectCount => toolGhostAssets.Count;
        public float GridStep {get=>gridStep;set {gridStep=Mathf.Max(ApexCoordinates.MetersPerUnit,value);groundMaterial.SetFloat("_GridStep",gridStep);}}
        private float gridStep=ApexCoordinates.DefaultGridMeters;

        public WorldView(Shader objectShader, Shader gridShader, Shader lineShader)
        {
            this.objectShader = objectShader;
            root = new GameObject("Workspace view");
            models = new WorkspaceModelProvider(objectShader);
            Camera = new GameObject("Workspace camera", typeof(Camera)).GetComponent<Camera>();
            Camera.transform.SetParent(root.transform);
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(.055f, .073f, .10f);
            Camera.nearClipPlane = .05f;
            Camera.farClipPlane = ApexCoordinates.MaxUnityCoord*5;
            Camera.fieldOfView = 50;
            Camera.tag = "MainCamera";
            Camera.cullingMask &= ~(1 << 31);
            var sun = new GameObject("Workspace light", typeof(Light)).GetComponent<Light>();
            sun.transform.SetParent(root.transform);
            sun.type = LightType.Directional;
            sun.intensity = 1.8f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50, -35, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.5f, .55f, .65f);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Construction plane";
            ground.transform.SetParent(root.transform);
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = Vector3.one * (ApexCoordinates.MaxUnityCoord*2/10f);
            ground.AddComponent<ConstructionPlaneSurface>();
            constructionPlaneCollider = ground.GetComponent<Collider>();
            groundMaterial = new Material(gridShader);groundMaterial.SetFloat("_GridStep",gridStep);
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            outline = new GameObject("Selection bounds", typeof(LineRenderer)).GetComponent<LineRenderer>();
            outline.transform.SetParent(root.transform);
            lineMaterial = new Material(lineShader);
            lineMaterial.SetColor("_BaseColor", new Color(.35f, 1, .78f));
            outline.sharedMaterial = lineMaterial;
            outline.widthMultiplier = .025f;
            outline.useWorldSpace = true;
            outline.enabled = false;
            UpdateCamera();
        }

        public void SetViewport(Rect screenRect)
        {
            if (screenRect.width < 1 || screenRect.height < 1) return;
            Camera.pixelRect = new Rect(screenRect.x, Screen.height - screenRect.yMax, screenRect.width, screenRect.height);
        }

        private Vector2 middleStart;
        private bool middleMoved, middleHeld;
        private Vector3? middlePoint;
        private bool cameraCursorCaptured, restoreCameraCursorPosition;
        private bool previousCameraCursorVisible;
        private CursorLockMode previousCameraCursorLock;
        private Vector2 cameraCursorPosition;
        public static Vector3 FlyDirection(float yaw, float pitch, Vector3 local) => Quaternion.Euler(pitch, yaw, 0) * local;
        public static float WheelDistance(float current,float steps,bool fast=false) {
            if(!float.IsFinite(steps))return current;
            return Mathf.Clamp(current*Mathf.Exp(-Mathf.Clamp(steps,-20,20)*.18f*(fast?3:1)),.3f,1500);
        }
        public void Navigate(float deltaTime)
        {
            var mouse = Mouse.current; var keyboard = Keyboard.current;
            if (mouse == null) { ReleaseCameraCursor(); return; }
            if(Orbiting&&(!OrbitShortcut||!mouse.leftButton.isPressed))Orbiting=false;
            if(OrbitShortcut&&mouse.leftButton.wasPressedThisFrame)BeginOrbit();
            SetCameraCursorCaptured(!Orbiting&&mouse.rightButton.isPressed,mouse);
            if(Orbiting) {
                var delta=mouse.delta.ReadValue();yaw+=delta.x*.2f;pitch=Mathf.Clamp(pitch-delta.y*.2f,-89,89);
            }
            else if (mouse.rightButton.isPressed)
            {
                CancelCameraMotion();
                var cameraPosition = Camera.transform.position;
                var movement = mouse.delta.ReadValue(); yaw += movement.x * .2f; pitch = Mathf.Clamp(pitch - movement.y * .2f, -89, 89);
                focus = cameraPosition + FlyDirection(yaw, pitch, Vector3.forward) * distance;
                if (keyboard != null)
                {
                    float x = (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed || keyboard.qKey.isPressed ? 1 : 0);
                    float z = (keyboard.wKey.isPressed || keyboard.zKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
                    float y = (keyboard.rKey.isPressed ? 1 : 0) - (keyboard.fKey.isPressed ? 1 : 0);
                    float speed = Mathf.Max(2, distance * .5f) * (keyboard.shiftKey.isPressed ? 3 : 1);
                    focus += (FlyDirection(yaw, pitch, new Vector3(x, 0, z)) + Vector3.up * y) * deltaTime * speed;
                }
            }
            if (mouse.middleButton.wasPressedThisFrame)
            { middleStart = mouse.position.ReadValue(); middleMoved = false; middleHeld=true; middlePoint = SurfacePoint(middleStart,0,true,out var hitPoint)?hitPoint:(Vector3?)null; }
            if (middleHeld && mouse.middleButton.isPressed && !Orbiting)
            {
                if (Vector2.Distance(mouse.position.ReadValue(), middleStart) > 4) middleMoved = true;
                if (middleMoved)
                {
                    CancelCameraMotion();
                    var movement = mouse.delta.ReadValue();
                    focus += (-Camera.transform.right * movement.x - Camera.transform.up * movement.y) * distance * .0018f;
                }
            }
            if (mouse.middleButton.wasReleasedThisFrame && middleHeld) {
                if(!middleMoved&&middlePoint.HasValue&&!Orbiting&&!mouse.rightButton.isPressed)SmoothFocusPoint(middlePoint.Value);
                middleHeld=false;middlePoint=null;
            }
            float steps=mouse.scroll.ReadValue().y;
            if(InputSystem.settings.scrollDeltaBehavior==InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange && (Application.platform==RuntimePlatform.WindowsPlayer||Application.platform==RuntimePlatform.WindowsEditor))steps/=120f;
            if(steps!=0) {CancelCameraMotion();distance=WheelDistance(distance,steps,keyboard?.shiftKey.isPressed==true); }
            UpdateCamera();
        }
        private void SetCameraCursorCaptured(bool captured, Mouse mouse)
        {
            if(captured)
            {
                if(cameraCursorCaptured)return;
                cameraCursorCaptured=true;
                previousCameraCursorLock=UnityEngine.Cursor.lockState;
                previousCameraCursorVisible=UnityEngine.Cursor.visible;
                restoreCameraCursorPosition=mouse!=null;
                if(restoreCameraCursorPosition)cameraCursorPosition=mouse.position.ReadValue();
                UnityEngine.Cursor.lockState=CursorLockMode.Locked;
                UnityEngine.Cursor.visible=false;
                return;
            }
            ReleaseCameraCursor(mouse);
        }
        private void ReleaseCameraCursor(Mouse mouse=null)
        {
            if(!cameraCursorCaptured)return;
            cameraCursorCaptured=false;
            UnityEngine.Cursor.lockState=previousCameraCursorLock;
            UnityEngine.Cursor.visible=previousCameraCursorVisible;
            if(restoreCameraCursorPosition&&previousCameraCursorLock!=CursorLockMode.Locked&&mouse!=null)
                mouse.WarpCursorPosition(cameraCursorPosition);
            restoreCameraCursorPosition=false;
        }
        private Bounds SelectionBounds(string id)
        {
            if (!selectionBounds.TryGetValue(id, out var bounds)) { bounds = ObjectBounds(instances[id]); selectionBounds[id] = bounds; }
            return bounds;
        }
        public Vector3? Centre(string id) => id != null && instances.ContainsKey(id) ? SelectionBounds(id).center : (Vector3?)null;
        public void TransformPreview(string id, Vector3 position, Vector3 rotation)
        {
            if (!instances.TryGetValue(id, out var instance)) return;
            instance.transform.SetPositionAndRotation(position, Quaternion.Euler(rotation)); selectionBounds.Clear(); Physics.SyncTransforms(); RefreshZiplineVisuals(); Highlight(id);
        }
        private void UpdateCamera()
        {
            Camera.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            Camera.transform.position = focus - Camera.transform.forward * distance;
        }

        public void Focus(string id)
        {
            CancelCameraMotion();
            if (id != null && instances.TryGetValue(id, out var instance))
            {
                var bounds = SelectionBounds(id);
                focus = bounds.center;
                distance = Mathf.Clamp(bounds.size.magnitude * 3, 5, 150);
            }
            else { focus = Vector3.zero; distance = 20; }
            orbitPoint=focus; UpdateCamera();
        }

        public string Pick(Vector2 screenPosition)
        {
            string selected = null; float nearest = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(Camera.ScreenPointToRay(screenPosition), Camera.farClipPlane, ~(1 << 31), QueryTriggerInteraction.Ignore))
            {
                string id = null;
                for (var current = hit.collider.transform; current != null && id == null; current = current.parent)
                    instanceIds.TryGetValue(current.gameObject, out id);
                if (id != null && hit.distance < nearest) { selected = id; nearest = hit.distance; }
            }
            return selected;
        }

        public bool Placement(Vector2 screenPosition, CatalogEntry entry, bool snap, out Vector3 point)
        {
            point = default;
            if (!SurfacePoint(screenPosition, 0, out var surface)) return false;
            point = PlacementPosition(surface,entry,snap,GridStep);
            return ApexCoordinates.ContainsUnity(ToData(point));
        }

        public static Vector3 PlacementPosition(Vector3 surface,CatalogEntry entry,bool snap,float step=ApexCoordinates.DefaultGridMeters) {
            if (entry.CustomType == null) surface.y+=entry.GameAsset!=null?entry.PlacementLift:entry.Id=="demo:cylinder"?entry.Size.y:entry.Size.y*.5f;
            if(snap){surface.x=MapSession.Snap(surface.x,step);surface.z=MapSession.Snap(surface.z,step);}return surface;
        }
        public void Preview(CatalogEntry entry, Vector3? position)
        {
            if (entry?.CustomType != null) { PreviewCustom(entry, position); return; }
            if (entry == null || !position.HasValue) { ClearPreview(); return; }
            if (ghost == null || ghostAsset != entry.Id)
            {
                ClearPreview();
                ghost = models.Create(entry.Id, false);
                ghostAsset = entry.Id;
                ghost.name = "Placement preview";
                ghost.transform.SetParent(root.transform);
                foreach (var collider in ghost.GetComponents<Collider>()) collider.enabled = false;
                var tint = new MaterialPropertyBlock();
                tint.SetColor("_BaseColor", new Color(.3f, .95f, .65f));
                ghost.GetComponent<Renderer>().SetPropertyBlock(tint);
            }
            ghost.transform.position = position.Value;
            ghost.transform.localScale = entry.Size;
        }

        public void Preview(MapDocument document, Vector3? position)
        {
            if (document == null || !position.HasValue) { ClearPreview(); return; }
            if (selectionGhost == null || selectionGhostDocument != document)
            {
                ClearPreview();
                selectionGhostDocument = document;
                selectionGhost = CreateDocumentPreview(document, "Selection placement preview", selectionGhostAssets);
            }
            selectionGhost.transform.position = position.Value;
        }

        public void PreviewTool(MapDocument document)
        {
            ClearToolPreview();
            if (document != null) toolGhost = CreateDocumentPreview(document, "Construction tool preview", toolGhostAssets);
        }

        private GameObject CreateDocumentPreview(MapDocument document, string name, Dictionary<GameObject, string> assets)
        {
            var previewRoot = new GameObject(name);
            previewRoot.transform.SetParent(root.transform, false);
            var objects = new Dictionary<string, GameObject>();
            foreach (var item in document.objects)
            {
                var instance = item.isGroup ? new GameObject(item.displayName) : models.Create(item.assetId, false);
                instance.name = item.displayName + " preview";
                objects.Add(item.id, instance);
                assets.Add(instance, item.isGroup ? null : item.assetId);
            }
            foreach (var item in document.objects)
            {
                var instance = objects[item.id];
                var parent = string.IsNullOrEmpty(item.parentId) ? previewRoot.transform : objects[item.parentId].transform;
                instance.transform.SetParent(parent, false);
                instance.transform.localPosition = ToVector(item.position);
                instance.transform.localRotation = Quaternion.Euler(ToVector(item.rotation));
                instance.transform.localScale = ToVector(item.scale);
                instance.SetActive(!item.disabled);
                foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    var tint = new MaterialPropertyBlock();
                    tint.SetColor("_BaseColor", new Color(.3f, .95f, .65f));
                    renderer.SetPropertyBlock(tint);
                }
            }
            return previewRoot;
        }

        private void ReleaseDocumentPreview(ref GameObject preview, Dictionary<GameObject, string> assets)
        {
            foreach (var pair in assets)
            {
                if (pair.Key == null) continue;
                pair.Key.transform.SetParent(null, true);
                if (pair.Value == null) UnityEngine.Object.Destroy(pair.Key);
                else models.Release(pair.Value, pair.Key);
            }
            assets.Clear();
            if (preview != null) UnityEngine.Object.Destroy(preview);
            preview = null;
        }

        public void ClearPreview()
        {
            if (ghost != null) models.Release(ghostAsset, ghost);
            ghost = null;
            ghostAsset = null;
            ReleaseDocumentPreview(ref selectionGhost, selectionGhostAssets);
            selectionGhostDocument = null;
        }

        public void ClearToolPreview() => ReleaseDocumentPreview(ref toolGhost, toolGhostAssets);

        public void Reload(string assetId)
        {
            foreach (var id in new List<string>(instances.Keys))
                if (assetIds[id] == assetId) { ReleaseInstance(id); instances.Remove(id); assetIds.Remove(id); }
        }
        private void ReleaseInstance(string id)
        {
            var instance = instances[id];
            foreach (var child in instance.GetComponentsInChildren<Transform>(true)) instanceIds.Remove(child.gameObject); selectionBounds.Clear();
            if (assetIds[id].StartsWith("group:", StringComparison.Ordinal)) { instance.SetActive(false); UnityEngine.Object.Destroy(instance); }
            else models.Release(assetIds[id], instance);
        }
        public void Sync(MapDocument document, string selectedId)
        {
            syncedZiplineDocument = document;
            LastSyncTransformWrites = 0;
            var desired = new Dictionary<string, MapObject>(document.objects.Count);
            foreach (var item in document.objects) desired.Add(item.id, item);
            var retiring = new HashSet<GameObject>(); var retiredIds = new List<string>();
            foreach (var pair in instances) if (!desired.TryGetValue(pair.Key, out var item) || assetIds[pair.Key] != InstanceAssetKey(item)) { retiring.Add(pair.Value); retiredIds.Add(pair.Key); }
            bool changed = retiring.Count > 0;
            // Detach only changed parent links, before applying new links (including parent/child reversals).
            foreach (var pair in instances) {
                if (!desired.TryGetValue(pair.Key, out var item) || retiring.Contains(pair.Value)) continue;
                var t = pair.Value.transform; var expected = root.transform;
                if (!string.IsNullOrEmpty(item.parentId) && instances.TryGetValue(item.parentId, out var parent)) expected = parent.transform;
                if (t.parent != root.transform && (t.parent != expected || retiring.Contains(t.parent.gameObject))) { t.SetParent(root.transform, true); changed = true; LastSyncTransformWrites++; }
            }
            foreach (string id in retiredIds) { ReleaseInstance(id); instances.Remove(id); assetIds.Remove(id); }
            foreach (var item in document.objects) {
                if (instances.ContainsKey(item.id)) continue;
                var instance = item.isGroup ? new GameObject(item.displayName) : models.Create(item.assetId);
                instances.Add(item.id, instance); instanceIds.Add(instance, item.id); assetIds[item.id] = InstanceAssetKey(item); changed = true;
            }
            foreach (var item in document.objects) {
                var instance = instances[item.id]; var t = instance.transform;
                var parent = string.IsNullOrEmpty(item.parentId) ? root.transform : instances[item.parentId].transform;
                bool moved = false;
                if (t.parent != parent) { t.SetParent(parent, false); moved = true; }
                var p = ToVector(item.position); var r = Quaternion.Euler(ToVector(item.rotation)); var scale = ToVector(item.scale);
                if (t.localPosition != p) { t.localPosition = p; moved = true; }
                if (t.localRotation != r) { t.localRotation = r; moved = true; }
                if (t.localScale != scale) { t.localScale = scale; moved = true; }
                if (instance.name != item.displayName) instance.name = item.displayName;
                if (instance.activeSelf == item.disabled) { instance.SetActive(!item.disabled); changed = true; }
                if (moved) { changed = true; LastSyncTransformWrites++; }
            }
            if (changed) { selectionBounds.Clear(); Physics.SyncTransforms(); }
            UpdateZiplineVisuals(document);
            Highlight(selectedId);
        }
        private static Bounds ObjectBounds(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = default; bool found = false;
            foreach (var renderer in renderers)
            {
                if (!IsConstructionGeometryRenderer(renderer)) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return found ? bounds : new Bounds(instance.transform.position, Vector3.one * .4f);
        }
        private bool TryOrientedSelectionBounds(string id,out Bounds bounds,out Matrix4x4 localToWorld)
        {
            bounds=default;localToWorld=Matrix4x4.identity;
            if(!instances.TryGetValue(id,out var instance))return false;
            bool found=false;var worldToLocal=instance.transform.worldToLocalMatrix;
            foreach(var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if(!IsConstructionGeometryRenderer(renderer))continue;
                var filter=renderer.GetComponent<MeshFilter>();
                if(filter?.sharedMesh!=null)
                {
                    var source=filter.sharedMesh.bounds;
                    for(int i=0;i<8;i++)
                    {
                        var corner=source.center+Vector3.Scale(source.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                        var point=worldToLocal.MultiplyPoint3x4(renderer.transform.TransformPoint(corner));
                        if(!found){bounds=new Bounds(point,Vector3.zero);found=true;}else bounds.Encapsulate(point);
                    }
                }
                else
                {
                    var source=renderer.bounds;
                    for(int i=0;i<8;i++)
                    {
                        var corner=source.center+Vector3.Scale(source.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                        var point=worldToLocal.MultiplyPoint3x4(corner);
                        if(!found){bounds=new Bounds(point,Vector3.zero);found=true;}else bounds.Encapsulate(point);
                    }
                }
            }
            if(!found){bounds=new Bounds(Vector3.zero,Vector3.one*.4f);found=true;}
            localToWorld=instance.transform.localToWorldMatrix;return found;
        }
        public MapObject LocalPose(string id)
        {
            var t = instances[id].transform;
            return new MapObject { id = id, position = ToData(t.localPosition), rotation = ToData(t.localEulerAngles), scale = ToData(t.localScale) };
        }
        public MapObject WorldPose(string id)
        {
            var t = instances[id].transform;
            return new MapObject { id = id, position = ToData(t.position), rotation = ToData(t.eulerAngles), scale = ToData(t.lossyScale) };
        }
        public bool SetLocalPreview(string id, Vector3 position, Vector3 rotation, Vector3 scale,
            SelectionPose lockedPosition = null)
        {
            var t = instances[id].transform;var before=new PreviewPose(t);
            Transform lockedTransform = null; PreviewPose lockedBefore = default;
            if (lockedPosition != null && instances.TryGetValue(lockedPosition.Local.id, out var lockedInstance)) {
                lockedTransform = lockedInstance.transform; lockedBefore = new PreviewPose(lockedTransform);
            }
            t.localPosition = position; t.localRotation = Quaternion.Euler(rotation); t.localScale = scale;
            if (lockedTransform != null) lockedTransform.position = ToVector(lockedPosition.World.position);
            bool valid=WithinWorldLimits();if(!valid){before.Restore();if(lockedTransform!=null)lockedBefore.Restore();}
            selectionBounds.Clear(); Physics.SyncTransforms(); RefreshZiplineVisuals(); Highlight(id);return valid;
        }
        public void StoreWorldPose(MapObject item, Vector3 position, Vector3 rotation)
        {
            var parent = instances[item.id].transform.parent;
            item.position = ToData(parent.InverseTransformPoint(position));
            item.rotation = ToData((Quaternion.Inverse(parent.rotation) * Quaternion.Euler(rotation)).eulerAngles);
        }
        public void StoreWorldPoseForParent(MapObject item, Vector3 position, Vector3 rotation)
        {
            var parent = string.IsNullOrEmpty(item.parentId) ? root.transform : instances[item.parentId].transform;
            item.position = ToData(parent.InverseTransformPoint(position));
            item.rotation = ToData((Quaternion.Inverse(parent.rotation) * Quaternion.Euler(rotation)).eulerAngles);
        }
        public MapObject ReparentPose(string id, string parentId)
        {
            var t = instances[id].transform; var oldParent = t.parent;
            var old = LocalPose(id);
            t.SetParent(string.IsNullOrEmpty(parentId) ? root.transform : instances[parentId].transform, true);
            var result = LocalPose(id);
            t.SetParent(oldParent, false); t.localPosition = ToVector(old.position); t.localRotation = Quaternion.Euler(ToVector(old.rotation)); t.localScale = ToVector(old.scale);
            return result;
        }
        public List<MapObject> GenerationObjects(MapDocument document)
        {
            var result = new List<MapObject>();
            foreach (var item in document.objects)
            {
                if (item.isGroup && item.customType != "zipline" && item.customType != "zipline-endpoint" &&
                    item.customType != "door" && item.customType != "curved-zipline" &&
                    item.customType != "curved-zipline-point" && item.customType != "loot-bin" &&
                    item.customType != "jump-pad" && item.customType != "spawn-point" &&
                    item.customType != "trigger" && item.customType != "jump-tower" &&
                    item.customType != "weapon-rack" && item.customType != "respawn-heal" &&
                    item.customType != "button" && item.customType != "speed-boost" &&
                    item.customType != "bubble-shield" && item.customType != "camera-path" &&
                    item.customType != "camera-path-point" && item.customType != "camera-path-target" &&
                    item.customType != "animated-camera" && item.customType != "sound" &&
                    item.customType != "sound-point" && item.customType != "location-pair") continue;
                if (!MapHierarchy.IsEnabled(document, item.id)) continue;
                var copy = item.Copy(); var pose = WorldPose(item.id);
                copy.parentId = item.customType == "curved-zipline-point" ||
                    item.customType == "camera-path-point" || item.customType == "camera-path-target" ||
                    item.customType == "sound-point"
                    ? item.parentId : "";
                copy.position = pose.position; copy.rotation = pose.rotation; copy.scale = pose.scale; result.Add(copy);
            }
            return result;
        }
        public void Highlight(string id)
        {
            outline.enabled = id != null && instances.ContainsKey(id);
            if (!outline.enabled) return;
            if(TryOrientedSelectionBounds(id,out var bounds,out var transform))DrawSelectionBounds(bounds,transform);
            else DrawSelectionBounds(SelectionBounds(id));
        }
        private void DrawSelectionBounds(Bounds b)
        {
            DrawSelectionBounds(b,Matrix4x4.identity);
        }
        private void DrawSelectionBounds(Bounds b,Matrix4x4 transform)
        {
            b.Expand(.035f);
            var a = b.min; var z = b.max;
            var p = new[] { new Vector3(a.x,a.y,a.z), new Vector3(z.x,a.y,a.z), new Vector3(z.x,a.y,z.z), new Vector3(a.x,a.y,z.z),
                new Vector3(a.x,z.y,a.z), new Vector3(z.x,z.y,a.z), new Vector3(z.x,z.y,z.z), new Vector3(a.x,z.y,z.z) };
            var order = new[] {0,1,2,3,0,4,5,1,5,6,2,6,7,3,7,4};
            outline.positionCount = order.Length;
            for (int i = 0; i < order.Length; i++) outline.SetPosition(i, transform.MultiplyPoint3x4(p[order[i]]));
        }
        public Vector3[] SelectionOutlinePoints
        {
            get
            {
                var points=new Vector3[outline.positionCount];outline.GetPositions(points);return points;
            }
        }

        public static Vector3 ToVector(Float3 v) => new Vector3(v.x, v.y, v.z);
        public static Float3 ToData(Vector3 v) => new Float3(v.x, v.y, v.z);
        public void Dispose()
        {
            ReleaseCameraCursor(Mouse.current);
            ClearPreview();
            ClearToolPreview();
            ClearMapReference();
            models.Dispose();
            UnityEngine.Object.Destroy(groundMaterial);
            UnityEngine.Object.Destroy(lineMaterial);
            if (ziplineCableMaterial != null) UnityEngine.Object.Destroy(ziplineCableMaterial);
            if (ziplineDetachMaterial != null) UnityEngine.Object.Destroy(ziplineDetachMaterial);
            UnityEngine.Object.Destroy(root);
        }
    }
}
