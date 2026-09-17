using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
using UnityEngine;
namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        // Synthetic static CAST exercises the same loading/cooking path as extracted Apex models.
        private static string CollisionFixture()
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            void Quad(float x0, float x1, float z0, float z1, float y0 = 0, float y1 = 0) {
                int n = vertices.Count; vertices.AddRange(new[] { new Vector3(x0,y0,z0), new Vector3(x0,y0,z1), new Vector3(x1,y1,z1), new Vector3(x1,y1,z0) });
                triangles.AddRange(new[] {n,n+1,n+2,n,n+2,n+3});
            }
            Quad(-3,-1,-3,3); Quad(1,3,-3,3); Quad(-1,1,-3,-1); Quad(-1,1,1,3); Quad(5,9,-2,2,0,2);
            string path = Path.Combine(Application.temporaryCachePath, "remap-collision-fixture.cast");
            using (var buffer = new MemoryStream()) using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true)) {
                writer.Write(0x74736163u); writer.Write(1u); writer.Write(1u); writer.Write(0u);
                writer.Write(CastReader.Mesh); long size = buffer.Position; writer.Write(0u); writer.Write(1ul); writer.Write(2u); writer.Write(0u);
                writer.Write(Encoding.ASCII.GetBytes("3v")); writer.Write((ushort)2); writer.Write((uint)vertices.Count); writer.Write(Encoding.ASCII.GetBytes("vp"));
                foreach (var p in vertices) { writer.Write(p.x / .0254f); writer.Write(p.z / .0254f); writer.Write(p.y / .0254f); }
                writer.Write((byte)'i'); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((uint)triangles.Count); writer.Write((byte)'f');
                for (int i = 0; i < triangles.Count; i += 3) { writer.Write((uint)triangles[i]); writer.Write((uint)triangles[i+2]); writer.Write((uint)triangles[i+1]); }
                long end = buffer.Position; buffer.Position = size; writer.Write((uint)(end - 16)); writer.Flush(); File.WriteAllBytes(path, buffer.ToArray());
            }
            return path;
        }
        private async Task CheckPreciseCollisions()
        {
            await TreeFrames(12); session.Replace(new MapDocument()); Refresh();
            const string asset = "qa:collision-frame"; world.models.Prepare(asset, CollisionFixture());
            var preview = world.models.Create(asset, false);
            ToolCheck(preview.GetComponent<Collider>() == null && world.models.CollisionModelCount == 0, "Thumbnail prepared physics geometry.");
            var thumbnail = ModelThumbnail.Render(preview); Destroy(thumbnail); world.models.Release(asset, preview);
            ToolCheck(world.models.LoadedGameModelCount == 0, "Preview resource leak.");
            var frame = new MapObject { assetId = asset, displayName = "Cadre ouvert + rampe", position = new Float3(0,2,0) };
            var below = new MapObject { displayName = "Objet sous le trou", position = new Float3(0,.5f,0) };
            var underground = new MapObject { displayName = "Objet sous le plan zéro", position = new Float3(-5,-2,0) };
            session.Edit(d => d.objects.AddRange(new[] { frame, below, underground })); Refresh(); Select(frame.id);
            ToolCheck(world.models.CollisionModelCount == 1, "Scene model has no prepared collision.");
            session.Edit(d=>d.objects.Find(o=>o.id==frame.id).rotation.y=35);Refresh();Select(frame.id);
            var outline=world.SelectionOutlinePoints;
            ToolCheck(outline.Length>1&&Mathf.Abs(outline[1].z-outline[0].z)>.1f,"Single-object selection outline stayed world-aligned while the object rotated.");
            session.Undo();Refresh();Select(frame.id);
            world.Camera.transform.SetPositionAndRotation(new Vector3(0,12,0), Quaternion.Euler(90,0,0));
            var hole = world.Camera.WorldToScreenPoint(Vector3.zero); var rim = world.Camera.WorldToScreenPoint(new Vector3(2,2,0));
            ToolCheck(world.Pick(hole) == below.id, "Selection was blocked by an invisible box across the opening.");
            ToolCheck(world.Pick(rim) == frame.id, "Selection missed actual frame geometry.");
            ToolCheck(world.Pick(world.Camera.WorldToScreenPoint(new Vector3(-5,-2,0))) == underground.id, "Construction plane blocked selection below zero.");
            var rampPoint = world.Camera.WorldToScreenPoint(new Vector3(7,3,0));
            ToolCheck(world.Placement(rampPoint, catalog.Entries[0], false, out var placed) && Mathf.Abs(placed.y - 3.5f) < .002f, "Placement does not follow sloped triangles.");
            // A box above the hole stays in place; one above the rim stops at Y=2.
            session.Edit(d => { d.objects.Find(o => o.id == below.id).position.y = 6; }); Refresh(); Select(below.id); ShowConstructionTools(true);
            groundClearance.value = 0; float holeHeight = world.GeometryBounds(below.id).Value.min.y; GroundSelection(true);
            ToolCheck(Mathf.Abs(world.GeometryBounds(below.id).Value.min.y - holeHeight) < .002f, "Grounding used the construction plane through the hole.");
            session.Edit(d => d.objects.Find(o => o.id == below.id).position = new Float3(2,6,0)); Refresh(); GroundSelection(true);
            ToolCheck(Mathf.Abs(world.GeometryBounds(below.id).Value.min.y - 2) < .002f, "Grounding missed the rim.");
            Select(frame.id); Duplicate(); ToolCheck(world.models.CollisionModelCount == 1, "A duplicate prepared another model resource.");
            session.Undo(); Refresh();
            session.Edit(d => d.objects.Find(o => o.id == frame.id).disabled = true); Refresh();
            world.Camera.transform.SetPositionAndRotation(new Vector3(0,12,0), Quaternion.Euler(90,0,0));
            ToolCheck(world.Pick(world.Camera.WorldToScreenPoint(new Vector3(-2,2,0))) == null, "Disabled mesh still collides.");
            session.Undo(); Refresh(); Select(frame.id); world.Focus(frame.id);
            // Validate actual cached assets without requesting any extraction or loading the whole catalogue into physics.
            if (!assetLibrary.Configured) throw new Exception("Real asset source is not configured.");
            session.Edit(d => d.targetMaps = new List<string> { "mp_rr_desertlands_hu", "mp_rr_olympus_mu2" }); Refresh();
            await IndexAssets(); int verified = 0;
            foreach (string guid in new[] { "43f9958ba65ae981", "a32899530e86bd40", "b7f4185bbf83a8e8", "fc0ebc1ace084ec5" }) {
                var record = assetLibrary.Records.FirstOrDefault(r => r.guid == guid); string path = record == null ? null : assetLibrary.CachedModel(record);
                if (path == null) throw new Exception("Cached collision fixture unavailable: " + guid);
                world.models.Prepare(record.Id, path); int previous = world.models.CollisionModelCount;
                var a = world.models.Create(record.Id); var b = world.models.Create(record.Id);
                try {
                    var collider = a.GetComponent<MeshCollider>();
                    ToolCheck(collider != null && !collider.convex && collider.sharedMesh == a.GetComponent<MeshFilter>().sharedMesh && b.GetComponent<MeshCollider>().sharedMesh == collider.sharedMesh, "Real mesh collision/shared resource mismatch.");
                    ToolCheck(world.models.CollisionModelCount == previous + 1, "Collision resource was duplicated.");
                    Physics.SyncTransforms();
                    // Probe above triangle centres whose face points upward; at least one must be hittable.
                    var vertices = collider.sharedMesh.vertices; var indices = collider.sharedMesh.triangles; bool hit = false;
                    for (int i = 0; i < indices.Length && !hit; i += 3) {
                        var p = vertices[indices[i]]; var q = vertices[indices[i+1]]; var r = vertices[indices[i+2]];
                        if (Vector3.Cross(q-p,r-p).normalized.y < .4f) continue;
                        var center = (p+q+r)/3; hit = collider.Raycast(new Ray(center + Vector3.up * .05f, Vector3.down), out _, .1f);
                    }
                    ToolCheck(hit, "Real mesh has no hittable upward triangle: " + record.Name);
                    Debug.Log("REMAP_REAL_COLLISION_OK: " + record.Name + " triangles=" + indices.Length/3); verified++;
                }
                finally { world.models.Release(record.Id, a); world.models.Release(record.Id, b); }
                ToolCheck(world.models.CollisionModelCount == previous, "Released collision resource remains resident.");
            }
            ToolCheck(verified == 4, "Not all cached models verified.");
            SetStatus("Collisions vérifiées : trou traversable, rampe précise, pose au sol et 4 modèles Apex.");
            await TreeFrames(4);
        }
        private IEnumerator CollisionSmoke()
        {
            var check = CheckPreciseCollisions(); while (!check.IsCompleted) yield return null;
            if (check.IsFaulted) Debug.LogException(check.Exception);
            yield return new WaitForEndOfFrame(); var capture = ScreenCapture.CaptureScreenshotAsTexture();
            if (capture != null) { File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "collision-preview.png"), capture.EncodeToPNG()); Destroy(capture); }
            if (!check.IsFaulted) Debug.Log("REMAP_PRECISE_COLLISIONS_OK");
            Application.Quit(check.IsFaulted ? 1 : 0);
        }
    }
}
