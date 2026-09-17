using System;
using System.Collections.Generic;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    // Runtime handles drawn above the scene. Screen hit testing matches the visible lines.
    public sealed class GizmoOverlay : VisualElement
    {
        private readonly List<Vector2[]> strokes = new List<Vector2[]>();
        private readonly List<int> axes = new List<int>();
        private readonly List<bool> filled = new List<bool>();
        private readonly Label mirrorLabel;
        private readonly Label sideLabel;
        private readonly Label axisLabel;
        private readonly Label objectsLabel;
        private readonly Label placementZeroLabel;
        private static readonly Color[] Colors = { new Color(1,.3f,.3f), new Color(.35f,.65f,1), new Color(.4f,1,.45f) };
        public Vector3 Pivot { get; private set; }
        public float WorldLength { get; private set; }
        public bool Rotation { get; private set; }
        public int ActiveAxis = -1;
        private Rect cameraRect;
        private bool cacheValid;
        private Vector3 previousCameraPosition;
        private Quaternion previousCameraRotation;
        private Vector3? previousPivot;
        private bool previousRotation, previousScale, previousDuplicate, previousDirectionalMirror, previousPlacement;
        private Vector3[] previousSymmetryCorners, previousCornerGuide;
        private Quaternion previousBasis;
        private int previousActiveAxis, previousAxisMask=7, previousCornerTarget=-1, previousCornerAxis, previousCornerSide, previousCornerObjects;
        private float previousFov;
        private Rect previousContentRect;
        public GizmoOverlay()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute; style.left = style.right = style.top = style.bottom = 0;
            generateVisualContent += Draw;
            mirrorLabel = CreateControlLabel("directional-mirror");
            sideLabel = CreateControlLabel("corner-side");
            axisLabel = CreateControlLabel("corner-axis");
            objectsLabel = CreateControlLabel("corner-objects");
            placementZeroLabel = CreateControlLabel("placement-zero");
        }
        public static Vector3 Axis(int axis) => axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
        public static bool PlaneAxes(int handle,out int first,out int second)
        {
            first=second=-1;
            if(handle==4){first=0;second=1;return true;}
            if(handle==5){first=0;second=2;return true;}
            if(handle==6){first=1;second=2;return true;}
            return false;
        }
        private void AddStroke(Vector2[] points,int handle,bool fill=false){strokes.Add(points);axes.Add(handle);filled.Add(fill);}
        public void Rebuild(Camera camera, Vector3? pivot, bool rotation) => Rebuild(camera,pivot,rotation,Quaternion.identity,false,false);
        public void Rebuild(Camera camera, Vector3? pivot, bool rotation, Quaternion basis, bool scale)
            => Rebuild(camera,pivot,rotation,basis,scale,false,null);
        public void Rebuild(Camera camera, Vector3? pivot, bool rotation, Quaternion basis, bool scale, bool duplicate)
            => Rebuild(camera,pivot,rotation,basis,scale,duplicate,null);
        public void Rebuild(Camera camera, Vector3? pivot, bool rotation, Quaternion basis, bool scale, bool duplicate, Vector3[] symmetryCorners, int cornerTarget=-1, bool directionalMirror=false, Vector3[] cornerGuide=null, int cornerAxis=0, int cornerSide=0, int cornerObjects=0, int axisMask=7, bool placement=false)
        {
            if (cacheValid && previousBasis == basis && previousScale == scale && previousDuplicate == duplicate && previousPlacement == placement && previousAxisMask == axisMask && previousCornerTarget == cornerTarget && previousDirectionalMirror == directionalMirror && previousCornerAxis == cornerAxis && previousCornerSide == cornerSide && previousCornerObjects == cornerObjects && CornersEqual(previousSymmetryCorners,symmetryCorners) && CornersEqual(previousCornerGuide,cornerGuide) && previousPivot == pivot && previousRotation == rotation && previousActiveAxis == ActiveAxis && cameraRect == camera.pixelRect && previousContentRect == contentRect && previousCameraPosition == camera.transform.position && previousCameraRotation == camera.transform.rotation && previousFov == camera.fieldOfView) return;
            cacheValid = true; previousBasis = basis; previousScale = scale; previousDuplicate = duplicate; previousPlacement = placement; previousAxisMask = axisMask; previousCornerTarget = cornerTarget; previousDirectionalMirror = directionalMirror; previousCornerAxis = cornerAxis; previousCornerSide = cornerSide; previousCornerObjects = cornerObjects; previousSymmetryCorners = symmetryCorners == null ? null : (Vector3[])symmetryCorners.Clone(); previousCornerGuide = cornerGuide == null ? null : (Vector3[])cornerGuide.Clone(); previousPivot = pivot; previousRotation = rotation; previousActiveAxis = ActiveAxis;
            previousCameraPosition = camera.transform.position; previousCameraRotation = camera.transform.rotation; previousFov = camera.fieldOfView; previousContentRect = contentRect;
            strokes.Clear(); axes.Clear(); filled.Clear(); Rotation = rotation; cameraRect = camera.pixelRect;
            HideControlLabels();
            if (!pivot.HasValue || camera.WorldToScreenPoint(pivot.Value).z <= camera.nearClipPlane) { MarkDirtyRepaint(); return; }
            Pivot = pivot.Value;
            float depth = Vector3.Dot(Pivot - camera.transform.position, camera.transform.forward);
            WorldLength = 95 * 2 * depth * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f) / camera.pixelHeight;
            Vector2 centre = camera.WorldToScreenPoint(Pivot);
            if (placement)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector2 projectedTip = camera.WorldToScreenPoint(Pivot + Vector3.up * sign * WorldLength);
                    Vector2 direction = projectedTip - centre;
                    if (direction.magnitude < 14) direction = Vector2.up * sign * 78;
                    else direction = direction.normalized * Mathf.Clamp(direction.magnitude, 64, 95);
                    Vector2 perpendicular = new Vector2(-direction.y, direction.x).normalized;
                    Vector2 origin = centre;
                    Vector2 tip = origin + direction;
                    Vector2 side = perpendicular * 9;
                    Vector2 back = tip - direction.normalized * 18;
                    int handle = sign < 0 ? 7 : 8;
                    AddStroke(new[] { origin + direction.normalized * 10, tip, back + side, tip, back - side }, handle);
                }
                AddPlacementZeroControl(centre + new Vector2(-78, 0));
                MarkDirtyRepaint(); return;
            }

            if (duplicate)
            {
                if (symmetryCorners != null && symmetryCorners.Length > 0)
                {
                    AddCornerGuide(camera, cornerGuide);
                    for (int index = 0; index < symmetryCorners.Length; index++)
                    {
                        Vector3 worldCorner = symmetryCorners[index]; var projected = camera.WorldToScreenPoint(worldCorner);
                        if (projected.z <= camera.nearClipPlane) continue;
                        Vector2 point = projected; const float radius = 9;
                        AddStroke(new[] { point+Vector2.up*radius, point+Vector2.right*radius, point+Vector2.down*radius, point+Vector2.left*radius, point+Vector2.up*radius }, 20+index, true);
                    }
                    int targetIndex=cornerTarget-20;
                    if(targetIndex>=0&&targetIndex<symmetryCorners.Length)
                    {
                        var target=camera.WorldToScreenPoint(symmetryCorners[targetIndex]);if(target.z>camera.nearClipPlane)AddCornerControls(target,cornerAxis,cornerSide,cornerObjects);
                    }
                    MarkDirtyRepaint(); return;
                }
                for (int axis = 0; axis < 3; axis++) for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector2 tip = camera.WorldToScreenPoint(Pivot + basis * Axis(axis) * sign * WorldLength);
                    Vector2 direction = tip - centre;
                    if (direction.magnitude < 14) continue;
                    Vector2 side = new Vector2(-direction.y, direction.x).normalized * 6;
                    Vector2 back = tip - direction.normalized * 14;
                    int handle = 10 + axis * 2 + (sign > 0 ? 1 : 0);
                    AddStroke(new[] { centre + direction.normalized * 12, tip, back + side, tip, back - side }, handle);
                }
                AddDirectionalMirrorControl(centre + new Vector2(-60, 44));
                MarkDirtyRepaint(); return;
            }
            for (int axis = 0; axis < 3; axis++)
            {
                if ((axisMask & (1 << axis)) == 0) continue;
                if (rotation)
                {
                    Vector3 u = basis * Axis((axis + 1) % 3), v = basis * Axis((axis + 2) % 3);
                    var ring = new Vector2[73];
                    for (int i = 0; i < ring.Length; i++) { float a = i * Mathf.PI * 2 / 72; ring[i] = camera.WorldToScreenPoint(Pivot + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * WorldLength); }
                    AddStroke(ring,axis);
                }
                else
                {
                    Vector2 tip = camera.WorldToScreenPoint(Pivot + basis * Axis(axis) * WorldLength);
                    Vector2 direction = tip - centre;
                    if (direction.magnitude < 14) continue;
                    Vector2 side = new Vector2(-direction.y, direction.x).normalized * 5;
                    Vector2 back = tip - direction.normalized * 12;
                    if(scale) { var d=direction.normalized*5; AddStroke(new[]{centre,tip,tip+side+d,tip-side+d,tip-side-d,tip+side-d,tip+side+d},axis); }
                    else AddStroke(new[] { centre, tip, back + side, tip, back - side },axis);
                }
            }
            if(!rotation&&!scale)
            {
                for(int handle=4;handle<=6;handle++)
                {
                    PlaneAxes(handle,out int first,out int second);
                    if ((axisMask & (1 << first)) == 0 || (axisMask & (1 << second)) == 0) continue;
                    Vector3 u=basis*Axis(first)*WorldLength,v=basis*Axis(second)*WorldLength;
                    const float near=.18f,far=.34f;
                    var square=new[]{(Vector2)camera.WorldToScreenPoint(Pivot+u*near+v*near),(Vector2)camera.WorldToScreenPoint(Pivot+u*far+v*near),(Vector2)camera.WorldToScreenPoint(Pivot+u*far+v*far),(Vector2)camera.WorldToScreenPoint(Pivot+u*near+v*far),(Vector2)camera.WorldToScreenPoint(Pivot+u*near+v*near)};
                    float area=Mathf.Abs((square[1].x-square[0].x)*(square[3].y-square[0].y)-(square[1].y-square[0].y)*(square[3].x-square[0].x));
                    if(area>18)AddStroke(square,handle,true);
                }
            }
            if(scale)AddStroke(new[]{centre+new Vector2(-6,-6),centre+new Vector2(6,-6),centre+new Vector2(6,6),centre+new Vector2(-6,6),centre+new Vector2(-6,-6)},3);
            MarkDirtyRepaint();
        }
        private Label CreateControlLabel(string name)
        {
            var label = new Label { name = name, pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 11;
            label.style.color = Color.white;
            label.style.display = DisplayStyle.None;
            Add(label);
            return label;
        }
        private void HideControlLabels()
        {
            mirrorLabel.style.display = DisplayStyle.None;
            sideLabel.style.display = DisplayStyle.None;
            axisLabel.style.display = DisplayStyle.None;
            objectsLabel.style.display = DisplayStyle.None;
            placementZeroLabel.style.display = DisplayStyle.None;
        }
        private Vector2 ScreenToContent(Vector2 screen)
            => new Vector2((screen.x-cameraRect.x)/cameraRect.width*contentRect.width, (cameraRect.yMax-screen.y)/cameraRect.height*contentRect.height);
        private void ShowControlLabel(Label label, string text, Vector2 centre, float width, float height, int handle)
        {
            Vector2 local = ScreenToContent(centre);
            float localWidth = width / cameraRect.width * contentRect.width;
            float localHeight = height / cameraRect.height * contentRect.height;
            label.text = text;
            label.style.left = local.x - localWidth * .5f;
            label.style.top = local.y - localHeight * .5f - 1.5f;
            label.style.width = localWidth;
            label.style.height = localHeight;
            label.style.color = ActiveAxis == handle ? Color.yellow : Color.white;
            label.style.display = DisplayStyle.Flex;
        }
        private Vector2 ClampControlCenter(Vector2 centre, float halfWidth, float halfHeight)
        {
            centre.x = Mathf.Clamp(centre.x, cameraRect.xMin + halfWidth + 5, cameraRect.xMax - halfWidth - 5);
            centre.y = Mathf.Clamp(centre.y, cameraRect.yMin + halfHeight + 5, cameraRect.yMax - halfHeight - 5);
            return centre;
        }
        private static Vector2[] ControlShape(Vector2 centre, float halfWidth, float halfHeight)
        {
            const float cut = 6;
            return new[] { centre + new Vector2(-halfWidth + cut, -halfHeight), centre + new Vector2(halfWidth - cut, -halfHeight), centre + new Vector2(halfWidth, -halfHeight + cut), centre + new Vector2(halfWidth, halfHeight - cut), centre + new Vector2(halfWidth - cut, halfHeight), centre + new Vector2(-halfWidth + cut, halfHeight), centre + new Vector2(-halfWidth, halfHeight - cut), centre + new Vector2(-halfWidth, -halfHeight + cut), centre + new Vector2(-halfWidth + cut, -halfHeight) };
        }
        private void AddDirectionalMirrorControl(Vector2 centre)
        {
            const float width = 98, height = 30;
            centre = ClampControlCenter(centre, width * .5f, height * .5f);
            AddStroke(ControlShape(centre, width * .5f, height * .5f), 16, true);
            ShowControlLabel(mirrorLabel, L.T("#MIRROR"), centre, width, height, 16);
        }
        private void AddPlacementZeroControl(Vector2 centre)
        {
            const float width = 72, height = 30;
            centre = ClampControlCenter(centre, width * .5f, height * .5f);
            AddStroke(ControlShape(centre, width * .5f, height * .5f), 9, true);
            ShowControlLabel(placementZeroLabel, L.T("#GO_TO_ZERO"), centre, width, height, 9);
        }
        private void AddCornerGuide(Camera camera, Vector3[] guide)
        {
            if (guide == null || guide.Length < 7) return;
            var rectangle = new Vector2[5];
            for (int i = 0; i < rectangle.Length; i++)
            {
                var projected = camera.WorldToScreenPoint(guide[i]); if (projected.z <= camera.nearClipPlane) return;
                rectangle[i] = projected;
            }
            AddStroke(rectangle, 27, true);
            var start3 = camera.WorldToScreenPoint(guide[5]); var end3 = camera.WorldToScreenPoint(guide[6]);
            if (start3.z <= camera.nearClipPlane || end3.z <= camera.nearClipPlane) return;
            Vector2 start = start3, end = end3, direction = end - start;
            if (direction.magnitude < 12) return;
            Vector2 side = new Vector2(-direction.y, direction.x).normalized * 7; Vector2 back = end - direction.normalized * 16;
            AddStroke(new[] { start, end, back + side, end, back - side }, 28);
        }        private void AddCornerControls(Vector2 target, int cornerAxis, int cornerSide, int cornerObjects)
        {
            const float width = 108, height = 30, gap = 7;
            float groupHalfWidth = (width * 3 + gap * 2) * .5f;
            float y = target.y + 44;
            if (y + height * .5f + 5 > cameraRect.yMax) y = target.y - 44;
            var group = ClampControlCenter(new Vector2(target.x, y), groupHalfWidth, height * .5f);
            var side = group + Vector2.left * (width + gap);
            var axis = group;
            var objects = group + Vector2.right * (width + gap);
            AddStroke(ControlShape(side, width * .5f, height * .5f), 24, true);
            AddStroke(ControlShape(axis, width * .5f, height * .5f), 25, true);
            AddStroke(ControlShape(objects, width * .5f, height * .5f), 26, true);
            ShowControlLabel(sideLabel, L.T(cornerSide == 0 ? "#CORNER_RIGHT" : "#CORNER_LEFT"), side, width, height, 24);
            string[] axes = { "#NORTH", "#EAST", "#SOUTH", "#WEST" };
            ShowControlLabel(axisLabel, L.T(axes[(cornerAxis % 4 + 4) % 4]), axis, width, height, 25);
            ShowControlLabel(objectsLabel, L.T(cornerObjects == 0 ? "#OBJECTS_0_DEG" : "#OBJECTS_180_DEG"), objects, width, height, 26);
        }
        private static bool CornersEqual(Vector3[] a, Vector3[] b)
        {
            if (ReferenceEquals(a,b)) return true; if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true;
        }
        public Vector2? HandleCenter(int handle)
        {
            int index=axes.IndexOf(handle);if(index<0)return null;
            var points=strokes[index];var sum=Vector2.zero;int count=points.Length>1&&points[0]==points[points.Length-1]?points.Length-1:points.Length;
            for(int i=0;i<count;i++)sum+=points[i];return sum/Mathf.Max(1,count);
        }
        public static Vector3 DuplicateDirection(int handle)
        {
            if (handle < 10 || handle > 15) return Vector3.zero;
            return Axis((handle - 10) / 2) * (((handle - 10) & 1) == 0 ? -1 : 1);
        }
        public static Vector3 PlacementDirection(int handle) =>
            handle == 7 ? Vector3.down : handle == 8 ? Vector3.up : Vector3.zero;
        public static Color StrokeColor(int handle, bool directionalMirror = false)
        {
            if(handle==28)return new Color(1f,.85f,.2f);
            if(handle==16&&directionalMirror)return new Color(.45f,1f,.5f);
            if(handle==16||(handle>=20&&handle<=27))return new Color(.35f,.9f,1f);
            if(handle==7)return new Color(1f,.68f,.12f);
            if(handle==8)return new Color(.3f,1f,.52f);
            if(handle==9)return new Color(.35f,.9f,1f);
            if(handle>=10&&handle<=15)return Colors[(handle-10)/2];
            if(handle>=0&&handle<3)return Colors[handle];
            if(handle==3)return Color.white;
            if(PlaneAxes(handle,out int first,out int second))return (Colors[first]+Colors[second])*.5f;
            return Color.white;
        }
        public int Hit(Vector2 screen)
        {
            if(previousScale && strokes.Count>0 && Vector2.Distance(screen, (strokes[strokes.Count-1][0]+strokes[strokes.Count-1][2])*.5f)<10)return 3;
            for(int i=0;i<strokes.Count;i++)if(axes[i]<27&&filled[i]&&PointInPolygon(screen,strokes[i]))return axes[i];
            float best = 9; int found = -1;
            for (int i = 0; i < strokes.Count; i++)
            {
                if (axes[i] >= 27) continue;
                for (int j = 1; j < strokes[i].Length; j++)
                { float distance = SegmentDistance(screen, strokes[i][j - 1], strokes[i][j]); if (distance < best) { best = distance; found = axes[i]; } }
            }
            return found;
        }
        private static bool PointInPolygon(Vector2 point,Vector2[] polygon)
        {
            bool inside=false;int count=polygon.Length>1&&polygon[0]==polygon[polygon.Length-1]?polygon.Length-1:polygon.Length;
            for(int i=0,j=count-1;i<count;j=i++)
            {
                var a=polygon[i];var b=polygon[j];
                if((a.y>point.y)!=(b.y>point.y)&&point.x<(b.x-a.x)*(point.y-a.y)/(b.y-a.y)+a.x)inside=!inside;
            }
            return inside;
        }
        public static float SegmentDistance(Vector2 point, Vector2 a, Vector2 b)
        { var d = b - a; float t = d.sqrMagnitude < .001f ? 0 : Mathf.Clamp01(Vector2.Dot(point - a, d) / d.sqrMagnitude); return Vector2.Distance(point, a + d * t); }
        private void Draw(MeshGenerationContext context)
        {
            if (cameraRect.width <= 0 || cameraRect.height <= 0) return;
            var painter = context.painter2D;
            for (int i = 0; i < strokes.Count; i++)
            {
                Color color=StrokeColor(axes[i],previousDirectionalMirror);
                bool textControl = axes[i] == 9 || axes[i] == 16 || (axes[i] >= 24 && axes[i] <= 26);
                bool controlIcon = textControl && !filled[i];
                painter.strokeColor = ActiveAxis == axes[i] ? Color.yellow : controlIcon ? Color.white : color;
                painter.lineWidth = ActiveAxis == axes[i] ? 4.5f : axes[i] == 7 || axes[i] == 8 ? 4f : controlIcon ? 2f : 2.5f;
                if(filled[i])
                {
                    if (ActiveAxis == axes[i]) color = new Color(.42f,.35f,.03f,.94f);
                    else if (textControl && axes[i] == 16 && previousDirectionalMirror) color = new Color(.08f,.35f,.13f,.94f);
                    else if (textControl) color = new Color(.025f,.11f,.15f,.94f);
                    else color = new Color(color.r,color.g,color.b,.25f);
                    painter.fillColor=color;
                    painter.BeginPath();
                    for(int j=0;j<strokes[i].Length;j++){Vector2 screen=strokes[i][j];var p=new Vector2((screen.x-cameraRect.x)/cameraRect.width*contentRect.width,(cameraRect.yMax-screen.y)/cameraRect.height*contentRect.height);if(j==0)painter.MoveTo(p);else painter.LineTo(p);}
                    painter.ClosePath();painter.Fill();
                }
                painter.BeginPath();
                for (int j = 0; j < strokes[i].Length; j++)
                {
                    Vector2 screen = strokes[i][j];
                    var p = new Vector2((screen.x - cameraRect.x) / cameraRect.width * contentRect.width, (cameraRect.yMax - screen.y) / cameraRect.height * contentRect.height);
                    if (j == 0) painter.MoveTo(p); else painter.LineTo(p);
                }
                painter.Stroke();
            }
        }
    }
}
