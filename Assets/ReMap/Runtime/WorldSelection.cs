using System.Collections.Generic;
using System.Linq;
using ReMap.Standalone.Core;
using UnityEngine;
namespace ReMap.Standalone
{
    public sealed class SelectionPose
    {
        public MapObject Local, World;
        public bool PositionLocked;
    }
    public sealed partial class WorldView
    {
        private readonly List<string> outlinedIds = new List<string>();
        public Bounds? CombinedBounds(IReadOnlyList<string> ids)
        {
            if (!outlinedIds.SequenceEqual(ids)) { outlinedIds.Clear(); outlinedIds.AddRange(ids); selectionBounds.Remove("$selection"); }
            if (selectionBounds.TryGetValue("$selection", out var cached)) return cached;
            Bounds? result = null;
            foreach (var id in ids) if (instances.ContainsKey(id)) {
                var b = SelectionBounds(id); if (!result.HasValue) result = b; else { var merged = result.Value; merged.Encapsulate(b); result = merged; }
            }
            if (result.HasValue) selectionBounds["$selection"] = result.Value;
            return result;
        }
        public static float FrameDistance(Bounds bounds, Quaternion cameraRotation, float verticalFov, float aspect)
        {
            float verticalTangent=Mathf.Tan(Mathf.Clamp(verticalFov,1,179)*Mathf.Deg2Rad*.5f);
            float horizontalTangent=verticalTangent*Mathf.Max(.1f,aspect);
            var inverse=Quaternion.Inverse(cameraRotation);var extents=bounds.extents;
            float required=0;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
            {
                var corner=inverse*Vector3.Scale(extents,new Vector3(x,y,z));
                float projected=Mathf.Max(Mathf.Abs(corner.x)/horizontalTangent,Mathf.Abs(corner.y)/verticalTangent)*1.35f;
                required=Mathf.Max(required,projected-corner.z);
            }
            return required;
        }
        public void HighlightSelection(IReadOnlyList<string> ids) { if(ids.Count==1){Highlight(ids[0]);return;}var b = CombinedBounds(ids); outline.enabled = b.HasValue; if (b.HasValue) DrawSelectionBounds(b.Value); }
        public void FocusSelection(IReadOnlyList<string> ids)
        {
            var b = CombinedBounds(ids); if (!b.HasValue) { Focus(null); return; }
            CancelCameraMotion();focus=b.Value.center;orbitPoint=focus;
            distance=Mathf.Clamp(FrameDistance(b.Value,Camera.transform.rotation,Camera.fieldOfView,Camera.aspect),5,1500);
            UpdateCamera();
        }
        public bool SurfacePoint(Vector2 screen,float step,out Vector3 point) =>
            SurfacePoint(screen,step,false,out point);
        public bool SurfacePoint(Vector2 screen,float step,bool ignoreConstructionPlane,out Vector3 point) {
            point=Vector3.zero;var ray=Camera.ScreenPointToRay(screen);float nearest=Camera.farClipPlane;bool found=false;
            float constructionPlaneDistance=Camera.farClipPlane;Vector3 constructionPlanePoint=Vector3.zero;bool foundConstructionPlane=false;
            foreach(var hit in Physics.RaycastAll(ray,Camera.farClipPlane,~(1<<31),QueryTriggerInteraction.Ignore)) {
                if(hit.collider==constructionPlaneCollider||hit.collider.GetComponent<ConstructionPlaneSurface>()!=null) {
                    if(!ignoreConstructionPlane&&hit.distance<constructionPlaneDistance) {
                        constructionPlaneDistance=hit.distance;constructionPlanePoint=hit.point;foundConstructionPlane=true;
                    }
                    continue;
                }
                if(hit.distance>=nearest)continue;
                nearest=hit.distance;point=hit.point;found=true;
            }
            if(TryBspRaycast(ray,Camera.farClipPlane,out var bspPoint,out float bspDistance)&&bspDistance<nearest) {
                nearest=bspDistance;point=bspPoint;found=true;
            }
            if(!found&&foundConstructionPlane){point=constructionPlanePoint;found=true;}
            if(!found)return false;
            if(step>0){point.x=MapSession.Snap(point.x,step);point.z=MapSession.Snap(point.z,step);}return ApexCoordinates.ContainsUnity(ToData(point));
        }
        public List<string> InScreenRectangle(Rect rectangle)
        {
            var result = new List<string>();
            foreach (var pair in instances) {
                if (!pair.Value.activeInHierarchy || assetIds[pair.Key] == "group:") continue;
                var point = Camera.WorldToScreenPoint(SelectionBounds(pair.Key).center);
                if (point.z > Camera.nearClipPlane && rectangle.Contains(point)) result.Add(pair.Key);
            }
            return result;
        }
        public List<SelectionPose> CaptureSelection(IEnumerable<string> ids) => ids.Select(id => new SelectionPose {
            Local = LocalPose(id), World = WorldPose(id),
            PositionLocked = syncedZiplineDocument?.objects.Find(item => item.id == id)?.positionLocked == true
        }).ToList();
        // Only roots are passed here. All writes happen before a single physics synchronization.
        public bool PreviewSelection(List<SelectionPose> originals, Vector3 pivot, Quaternion basis, Vector3 move, Quaternion rotation, Vector3 factors,
            SelectionPose lockedPosition = null)
        {
            previousPreview.Clear();
            Transform lockedTransform = null;
            if (lockedPosition != null && instances.TryGetValue(lockedPosition.Local.id, out var lockedInstance)) {
                lockedTransform = lockedInstance.transform;
                previousPreview.Add(new PreviewPose(lockedTransform));
            }
            foreach (var original in originals) {
                var t = instances[original.Local.id].transform;previousPreview.Add(new PreviewPose(t));
                var relative = Quaternion.Inverse(basis) * (ToVector(original.World.position) - pivot);
                var position = pivot + move + rotation * (basis * Vector3.Scale(relative, factors));
                if (original.PositionLocked) position = ToVector(original.World.position);
                t.SetPositionAndRotation(position, rotation * Quaternion.Euler(ToVector(original.World.rotation)));
                t.localScale = Vector3.Scale(ToVector(original.Local.scale), factors);
            }
            if (lockedTransform != null)
                lockedTransform.position = ToVector(lockedPosition.World.position);
            bool valid=WithinWorldLimits();if(!valid)foreach(var before in previousPreview)before.Restore();
            selectionBounds.Clear(); Physics.SyncTransforms(); RefreshZiplineVisuals(); return valid;
        }
    }
}
