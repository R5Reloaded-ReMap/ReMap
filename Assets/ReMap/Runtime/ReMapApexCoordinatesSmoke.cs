using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.UIElements;
namespace ReMap.Standalone {
    public sealed partial class ReMapApp {
        private async Task CheckApexCoordinates() {
            await TreeFrames(12);enabled=false;
            try {
                string local=RsxAssetLibrary.FindLocalRoot();string fixtures=Path.Combine(local,"Logs","ScaleQA","models");
                var names=new[]{"thunderdome_cage_ceiling_256x256_02","thunderdome_cage_ceiling_256x128_02","thunderdome_cage_ceiling_128x128_03"};
                var expected=new[]{new Vector3(256,256,16),new Vector3(128,256,16),new Vector3(256,256,16.751f)};
                for(int i=0;i<names.Length;i++) {
                    string path=Directory.GetFiles(fixtures,names[i]+"_LOD0.cast",SearchOption.AllDirectories).Single();
                    string id="qa:scale:"+i;world.models.Prepare(id,path);var instance=world.models.Create(id,false);
                    try {
                        var size=ApexDisplay.Position(instance.GetComponent<MeshFilter>().sharedMesh.bounds.size);
                        if(Vector3.Distance(size,expected[i])>.01f)throw new Exception("Model scale differs from Source vertices: "+size);
                        Debug.Log("REMAP_APEX_REAL_SCALE_OK: "+names[i]+" = "+size.ToString("F3"));
                    }finally{world.models.Release(id,instance);}
                }
                var item=new MapObject{assetId="qa:scale:0",displayName="Dalle 256 × 256 Apex"};
                var doc=new MapDocument();doc.objects.Add(item);session.Replace(doc);Refresh();Select(item.id);
                await TreeFrames();
                var draggedField=positionInput.Query<EndlessFloatField>().First();
                var dragStart=draggedField.labelElement.worldBound.center;
                var positionBeforeDrag=WorldView.ToVector(world.WorldPose(item.id).position);
                using(var down=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,mousePosition=dragStart}))draggedField.labelElement.SendEvent(down);
                if(draggedField.isDelayed)throw new Exception("Transform label drag still uses delayed updates.");
                using(var move=PointerMoveEvent.GetPooled(new Event{type=EventType.MouseDrag,button=0,mousePosition=dragStart+Vector2.right*20,delta=Vector2.right*20}))draggedField.labelElement.SendEvent(move);
                await TreeFrames();
                if(Vector3.Distance(positionBeforeDrag,WorldView.ToVector(world.WorldPose(item.id).position))<.0001f)throw new Exception("Transform label drag did not move the model live.");
                using(var up=PointerUpEvent.GetPooled(new Event{type=EventType.MouseUp,button=0,mousePosition=dragStart+Vector2.right*20}))draggedField.labelElement.SendEvent(up);
                if(!draggedField.isDelayed)throw new Exception("Transform field did not restore delayed keyboard input after dragging.");
                var fields=positionInput.Query<FloatField>().ToList();fields[0].value=256;fields[1].value=512;fields[2].value=128;
                if(fields.Any(field=>!field.isDelayed))throw new Exception("Transform fields recalculate before typed input is committed.");
                var p=world.WorldPose(item.id).position;
                if(Vector3.Distance(WorldView.ToVector(p),new Vector3(6.5024f,3.2512f,13.0048f))>.0001f)throw new Exception("Displayed Apex input did not convert to the world");
                CommitInspectorEdit();if(Vector3.Distance(positionInput.DisplayedValue,new Vector3(256,512,128))>.001f)throw new Exception("Inspector coordinates did not round trip");
                var angles=rotationInput.Query<FloatField>().ToList();angles[1].value=90;CommitInspectorEdit();
                if(Vector3.Distance(Quaternion.Euler(WorldView.ToVector(world.WorldPose(item.id).rotation))*Vector3.right,Vector3.forward)>.0001f)throw new Exception("Apex yaw axis incorrect");
                angles=rotationInput.Query<FloatField>().ToList();angles[1].value=-45;CommitInspectorEdit();
                if(Mathf.Abs(rotationInput.DisplayedValue.y+45)>.001f)throw new Exception("Negative Apex angles were normalized to a positive turn.");
                angles=rotationInput.Query<FloatField>().ToList();if(angles.Any(field=>!field.isDelayed))throw new Exception("Rotation fields are not delayed.");
                long revision=session.Revision;fields=positionInput.Query<FloatField>().ToList();fields[0].value=65535;CommitInspectorEdit();
                if(Mathf.Abs(world.WorldPose(item.id).position.x-ApexCoordinates.MaxUnityCoord)>.0001f)throw new Exception("Inclusive edge rejected");
                long atEdge=session.Revision;fields=positionInput.Query<FloatField>().ToList();fields[0].value=65536;
                if(!world.WithinWorldLimits())throw new Exception("Live inspector escaped the world");
                bool rejected=false;try{CommitInspectorEdit();}catch(ArgumentException){rejected=true;}
                if(!rejected||session.Revision!=atEdge)throw new Exception("Invalid coordinate entered history");
                session.Undo();Refresh();if(session.Revision!=revision)throw new Exception("Invalid edit corrupted undo");
                var group=new MapObject{isGroup=true,position=ApexCoordinates.ToUnity(new Float3(65500,0,0))};
                var child=new MapObject{parentId=group.id,position=ApexCoordinates.ToUnity(new Float3(16,0,0))};
                doc=new MapDocument();doc.objects.AddRange(new[]{group,child});session.Replace(doc);Refresh();
                var before=world.WorldPose(group.id);var originals=world.CaptureSelection(new[]{group.id});
                if(world.PreviewSelection(originals,WorldView.ToVector(before.position),Quaternion.identity,Vector3.right,Quaternion.identity,Vector3.one))throw new Exception("Parent movement allowed an out-of-range descendant");
                if(Vector3.Distance(WorldView.ToVector(before.position),WorldView.ToVector(world.WorldPose(group.id).position))>.00001f||!world.WithinWorldLimits())throw new Exception("Rejected parent preview was not rolled back");
                if(world.SetLocalPreview(group.id,WorldView.ToVector(group.position),Vector3.zero,new Vector3(4,1,1)))throw new Exception("Scaling parent escaped the world");
                if(!world.WithinWorldLimits())throw new Exception("Invalid parent scale persisted");
                // Transactional validation should remain inexpensive for the target scene size.
                doc=new MapDocument();for(int i=0;i<3000;i++)doc.objects.Add(new MapObject{position=ApexCoordinates.ToUnity(new Float3((i%60)*256-7680,(i/60)*256-6400,0))});
                var timer=System.Diagnostics.Stopwatch.StartNew();doc.Validate();Debug.Log("REMAP_APEX_VALIDATE_3000_MS: "+timer.Elapsed.TotalMilliseconds.ToString("F2"));
                // Finish on the measured model and a readable Apex position.
                doc=new MapDocument();item.position=ApexCoordinates.ToUnity(new Float3(256,512,128));item.rotation=default;doc.objects.Add(item);session.Replace(doc);Refresh();Select(item.id);world.Focus(item.id);
                if(Mathf.Abs(world.GridStep/ApexCoordinates.MetersPerUnit-64)>.001f)throw new Exception("Default grid is not 64 Source units");
                SetStatus("Échelle réelle 256 × 256, coordonnées Apex et limites ±65 535 vérifiées, y compris dans les dossiers.");
                Debug.Log("REMAP_APEX_COORDINATES_OK");
            }finally{enabled=true;}
        }
        private IEnumerator ApexCoordinatesSmoke() {
            var task=CheckApexCoordinates();while(!task.IsCompleted)yield return null;
            if(task.IsFaulted){Debug.LogException(task.Exception);Application.Quit(1);yield break;}
            yield return new WaitForEndOfFrame();var shot=ScreenCapture.CaptureScreenshotAsTexture();if(shot!=null){File.WriteAllBytes(Path.Combine(Application.dataPath,"..","apex-coordinates-preview.png"),shot.EncodeToPNG());Destroy(shot);}Application.Quit(0);
        }
    }
}
