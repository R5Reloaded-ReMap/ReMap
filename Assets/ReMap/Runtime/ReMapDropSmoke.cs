using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Debug=UnityEngine.Debug;
namespace ReMap.Standalone {
    public sealed partial class ReMapApp {
        private async Task CheckDropAndWheel() {
            await TreeFrames(12);await IndexAssets();
            var record=assetLibrary.Records.Single(r=>r.guid=="43f9958ba65ae981");
            if(assetLibrary.CachedModel(record)==null)throw new Exception("Cached death box required for drop test");
            await PreviewGameAsset(record);var prepared=previewEntry;if(prepared?.Id!=record.Id)throw new Exception("Initial preparation failed");
            var thumbnailPath=Path.Combine(assetLibrary.ModelDirectory(record),"thumbnail.png");var stamp=File.GetLastWriteTimeUtc(thumbnailPath);
            int count=snapshot.objects.Count;var point=new Vector2(world.Camera.pixelRect.center.x,world.Camera.pixelRect.yMin+50);
            if(!world.Placement(point,prepared,snap,out var expected))throw new Exception("Drop surface missing");
            assetBusy=true; // Simulate an unrelated extraction. A prepared drop must complete synchronously.
            var clock=Stopwatch.StartNew();var drop=DropLibraryItem(null,record,null,"",point,true);
            if(!drop.IsCompleted)throw new Exception("Prepared drag waited behind an unrelated extraction");await drop;
            Debug.Log("REMAP_READY_DROP_MS: "+clock.Elapsed.TotalMilliseconds);
            if(snapshot.objects.Count!=count+1||snapshot.objects.Last().assetId!=record.Id)throw new Exception("Ready drop failed");
            if(Vector3.Distance(WorldView.ToVector(snapshot.objects.Last().position),expected)>.001f)throw new Exception("Drag and Place use different positions");
            if(!ReferenceEquals(previewEntry,prepared)||File.GetLastWriteTimeUtc(thumbnailPath)!=stamp||!assetBusy)throw new Exception("Drop regenerated preview or changed unrelated worker state");
            session.Undo();Refresh();
            // A thumbnail-prepared model also drops instantly when it is not the current preview.
            previewEntry=null;var next=DropLibraryItem(null,record,null,"",point,true);if(!next.IsCompleted)throw new Exception("Thumbnail-prepared model waited");await next;
            if(snapshot.objects.Count!=count+1)throw new Exception("Thumbnail-prepared drop missing");session.Undo();Refresh();
            // A disk-cached model bypasses an unrelated RSX job without rendering a thumbnail.
            preparedPlacementEntries.Clear();var disk=DropLibraryItem(null,record,null,"",point,true);await disk;
            if(snapshot.objects.Count!=count+1||File.GetLastWriteTimeUtc(thumbnailPath)!=stamp)throw new Exception("Disk cache drop failed or regenerated image");
            assetBusy=false;Select(selectedId);FocusSelection();
            var previousMouse=Mouse.current;var previousKeyboard=Keyboard.current;var testMouse=InputSystem.AddDevice<Mouse>();var keys=InputSystem.AddDevice<Keyboard>();
            try {
                BeginPlacement(prepared);float scroll=InputSystem.settings.scrollDeltaBehavior==InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms?-1:-120;
                var before=world.Camera.transform.position;
                InputSystem.QueueStateEvent(testMouse,new MouseState{position=point,scroll=new Vector2(0,scroll)});InputSystem.Update();world.Navigate(0);
                float normal=Vector3.Distance(before,world.Camera.transform.position);if(normal<.5f)throw new Exception("Wheel retreat remains too small during placement: "+normal);
                if(placing!=prepared)throw new Exception("Wheel canceled placement");
                before=world.Camera.transform.position;InputSystem.QueueStateEvent(keys,new KeyboardState(Key.LeftShift));InputSystem.QueueStateEvent(testMouse,new MouseState{position=point,scroll=new Vector2(0,scroll)});InputSystem.Update();world.Navigate(0);
                float fast=Vector3.Distance(before,world.Camera.transform.position);if(fast<=normal*2)throw new Exception("Shift wheel acceleration failed");
                Debug.Log("REMAP_WHEEL_METERS: normal="+normal+" accelerated="+fast);
            }finally{InputSystem.RemoveDevice(testMouse);InputSystem.RemoveDevice(keys);previousMouse?.MakeCurrent();previousKeyboard?.MakeCurrent();}
            CancelPlacement();Debug.Log("REMAP_DROP_WHEEL_OK");
        }
        private IEnumerator DropWheelSmoke() {
            var task=CheckDropAndWheel();while(!task.IsCompleted)yield return null;
            if(task.IsFaulted){Debug.LogException(task.Exception);Application.Quit(1);}else Application.Quit(0);
        }
    }
}
