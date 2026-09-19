using ReMap.Standalone.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ReMap.Standalone
{
    public sealed partial class WorldView
    {
        private const string ZiplineMarkerName = "__remap_zipline_endpoint";
        private const string ZiplineDetachStartName = "__remap_zipline_detach_start";
        private const string ZiplineDetachEndName = "__remap_zipline_detach_end";
        private const string ZiplineCableSelectionName = "__remap_zipline_cable_selection";
        private const string ZiplineModelSelectionName = "__remap_zipline_model_selection";
        private const string TriggerPreviewName = "__remap_trigger_volume";
        private const string CameraPathMarkerName = "__remap_camera_path_marker";
        private const string ButtonTeleportTargetMarkerName = "__remap_button_teleport_target_marker";
        private const string ButtonTeleportGuideName = "__remap_button_teleport_guide";
        private const string TriggerTeleportTargetMarkerName = "__remap_trigger_teleport_target_marker";
        private const string TriggerTeleportGuideName = "__remap_trigger_teleport_guide";
        private const string TeleportDirectionArrowName = "direction";
        private const string LocationPairMarkerName = "__remap_location_pair_marker";
        private const string TextInfoPanelMarkerName = "__remap_text_info_panel_marker";
        private const string TextInfoPanelBackgroundName = "background";
        private const string TextInfoPanelTitleName = "title";
        private const string TextInfoPanelDescriptionName = "description";
        private const string TextInfoPanelPinName = "pin";
        private const float TextInfoPanelNativeVisibleSizeApex = 120f;
        private const float TextInfoPanelNativeVisibleSize =
            TextInfoPanelNativeVisibleSizeApex * ApexCoordinates.MetersPerUnit;
        private const string WindowHintMarkerName = "__remap_window_hint_marker";
        private MapDocument syncedZiplineDocument;
        private Material ziplineCableMaterial, ziplineDetachMaterial;

        private static string InstanceAssetKey(MapObject item) => item.isGroup ? "group:" + (item.customType ?? "") : item.assetId;

        private void RefreshZiplineVisuals()
        {
            if (syncedZiplineDocument != null) UpdateZiplineVisuals(syncedZiplineDocument);
        }

        private void UpdateZiplineVisuals(MapDocument document)
        {
            foreach (var item in document.objects)
            {
                if (!instances.TryGetValue(item.id, out var instance)) continue;
                if (item.customType == "zipline-endpoint" || item.customType == "curved-zipline-point" ||
                    item.customType == "ziprail-point")
                    EnsureZiplineMarker(instance, item);
                if (item.customType == "curved-zipline-component" || item.customType == "ziprail-component")
                {
                    ConfigureZiplineComponentColliders(instance, item);
                    EnsureZiplineModelSelection(instance, item.id);
                }
                if (item.customType == "trigger") EnsureTriggerVisual(instance, item);
                if (item.customType == "camera-path-point" || item.customType == "camera-path-target")
                    EnsureCameraPathMarker(instance, item);
                if (item.customType == "button-teleport-target")
                    EnsureButtonTeleportTargetMarker(instance, item);
                if (item.customType == "trigger-teleport-target")
                    EnsureTriggerTeleportTargetMarker(instance, item);
                if (item.customType == "sound" || item.customType == "sound-point")
                    EnsureSoundMarker(instance, item);
                if (item.customType == "location-pair") EnsureLocationPairMarker(instance, item);
                if (item.customType == "text-info-panel") EnsureTextInfoPanelMarker(instance, item);
                if (item.customType == "window-hint") EnsureWindowHintMarker(instance, item);
            }
            foreach (var item in document.objects)
            {
                if (item.customType != "button" || !instances.TryGetValue(item.id, out var instance))
                    continue;
                var target = document.objects.FirstOrDefault(candidate => candidate.parentId == item.id &&
                    candidate.customType == "button-teleport-target");
                var existing = instance.transform.Find(ButtonTeleportGuideName);
                LineRenderer guide = existing?.GetComponent<LineRenderer>();
                GameObject targetInstance = null;
                bool visible = target != null && (item.buttonMode == "invisible" || item.buttonTeleportEnabled) &&
                    instances.TryGetValue(target.id, out targetInstance);
                if (visible && guide == null)
                {
                    guide = new GameObject(ButtonTeleportGuideName, typeof(LineRenderer)).GetComponent<LineRenderer>();
                    guide.transform.SetParent(instance.transform, false);
                    guide.useWorldSpace = true; guide.positionCount = 2; guide.numCapVertices = 4;
                    guide.sharedMaterial = lineMaterial; guide.widthMultiplier = .025f;
                    var tint = new MaterialPropertyBlock();
                    tint.SetColor("_BaseColor", new Color(1f, .25f, .75f));
                    guide.SetPropertyBlock(tint);
                }
                if (guide != null)
                {
                    guide.enabled = visible;
                    if (visible)
                    {
                        guide.SetPosition(0, instance.transform.position);
                        guide.SetPosition(1, targetInstance.transform.position);
                    }
                }
            }
            foreach (var item in document.objects)
            {
                if (item.customType != "trigger" || !instances.TryGetValue(item.id, out var instance)) continue;
                var target = document.objects.FirstOrDefault(candidate => candidate.parentId == item.id && candidate.customType == "trigger-teleport-target");
                var existing = instance.transform.Find(TriggerTeleportGuideName);
                LineRenderer guide = existing?.GetComponent<LineRenderer>();
                GameObject targetInstance = null;
                bool visible = item.triggerTeleportEnabled && target != null && instances.TryGetValue(target.id, out targetInstance);
                if (visible && guide == null)
                {
                    guide = new GameObject(TriggerTeleportGuideName, typeof(LineRenderer)).GetComponent<LineRenderer>();
                    guide.transform.SetParent(instance.transform, false);
                    guide.useWorldSpace = true; guide.positionCount = 2; guide.numCapVertices = 4;
                    guide.sharedMaterial = lineMaterial; guide.widthMultiplier = .025f;
                    var tint = new MaterialPropertyBlock();
                    tint.SetColor("_BaseColor", new Color(.15f, .7f, 1f));
                    guide.SetPropertyBlock(tint);
                }
                if (guide != null)
                {
                    guide.enabled = visible;
                    if (visible)
                    {
                        guide.SetPosition(0, instance.transform.position);
                        guide.SetPosition(1, targetInstance.transform.position);
                    }
                }
            }
            foreach (var item in document.objects)
            {
                if (item.customType != "zipline" || !instances.TryGetValue(item.id, out var instance)) continue;
                var line = instance.GetComponent<LineRenderer>();
                if (line == null)
                {
                    line = instance.AddComponent<LineRenderer>();
                    line.useWorldSpace = true; line.numCapVertices = 4;
                }
                line.sharedMaterial = ZiplineCableMaterial();
                line.widthMultiplier = Mathf.Max(.1f, item.ziplineWidth) * ApexCoordinates.MetersPerUnit;
                GameObject start = null, end = null;
                bool visible = instances.TryGetValue(item.ziplineStartId, out start) && instances.TryGetValue(item.ziplineEndId, out end);
                line.enabled = visible;
                if (visible)
                {
                    var startItem = document.objects.Find(o => o.id == item.ziplineStartId);
                    var endItem = document.objects.Find(o => o.id == item.ziplineEndId);
                    Vector3 startAnchor = ZiplineCableAnchor(start, startItem);
                    Vector3 endAnchor = ZiplineCableAnchor(end, endItem);
                    if (item.ziplineMode == "vertical")
                    {
                        endAnchor = VerticalEndAnchor(startAnchor, endAnchor);
                        SetZiplineMarkerPosition(end, endAnchor);
                    }
                    var fullCablePoints = ZiplinePreviewPoints(startAnchor, endAnchor,
                        item.ziplineLengthScale, item.ziplineMode == "vertical");
                    bool hasDetachGuides = TryZiplineDetachGuidePoints(fullCablePoints,
                        item.ziplineAutoDetachStart, item.ziplineAutoDetachEnd,
                        out var startGuidePoints, out var cablePoints, out var endGuidePoints);
                    if (!hasDetachGuides) cablePoints = fullCablePoints;
                    line.positionCount = cablePoints.Length;
                    line.SetPositions(cablePoints);
                    UpdateZiplineCableSelection(instance, item.id, fullCablePoints);
                    UpdateZiplineDetachGuides(instance, startGuidePoints, endGuidePoints);
                    Vector3 pushDirection = ZiplinePushDirection(item.ziplinePushOffAngle);
                    UpdateZiplinePushArrow(instance, item.ziplineMode == "vertical" && item.ziplinePushOffInDirectionX,
                        startAnchor, pushDirection);
                }
                else
                {
                    UpdateZiplineCableSelection(instance, item.id, Array.Empty<Vector3>());
                    UpdateZiplineDetachGuides(instance, Array.Empty<Vector3>(), Array.Empty<Vector3>());
                    UpdateZiplinePushArrow(instance, false, Vector3.zero, Vector3.right);
                }
            }
            foreach (var item in document.objects)
            {
                if (item.customType != "curved-zipline" || !instances.TryGetValue(item.id, out var instance))
                    continue;
                var line = instance.GetComponent<LineRenderer>();
                if (line == null)
                {
                    line = instance.AddComponent<LineRenderer>();
                    line.useWorldSpace = true; line.numCapVertices = 4;
                }
                line.sharedMaterial = ZiplineCableMaterial();
                line.widthMultiplier = Mathf.Max(.1f, item.ziplineWidth) * ApexCoordinates.MetersPerUnit;
                var controls = document.objects.Where(candidate => candidate.parentId == item.id &&
                    candidate.customType == "curved-zipline-point")
                    .OrderBy(candidate => int.TryParse(candidate.customRole, out int index) ? index : int.MaxValue)
                    .Select(candidate => instances.TryGetValue(candidate.id, out var point)
                        ? point.transform.TransformPoint(ReMapZiplineProfiles.UnityOffset(
                            ReMapZiplineProfiles.CableOffsetApex(
                                candidate.customProfile, candidate.ziplineArmHeight)))
                        : Vector3.zero).ToArray();
                line.enabled = controls.Length >= 2;
                if (!line.enabled) { UpdateZiplineCableSelection(instance, item.id,
                    Array.Empty<Vector3>()); continue; }
                var curve = CurvedZiplinePreviewPoints(controls, item.curvedZiplineSegments);
                line.positionCount = curve.Length;
                line.SetPositions(curve);
                UpdateZiplineCableSelection(instance, item.id, curve);
            }
            foreach (var item in document.objects)
            {
                if (item.customType != "ziprail" || !instances.TryGetValue(item.id, out var instance))
                    continue;
                var line = instance.GetComponent<LineRenderer>();
                if (line == null)
                {
                    line = instance.AddComponent<LineRenderer>();
                    line.useWorldSpace = true; line.numCapVertices = 4;
                }
                line.sharedMaterial = ZiplineCableMaterial();
                line.widthMultiplier = Mathf.Max(.1f, item.ziplineWidth) * ApexCoordinates.MetersPerUnit;
                var points = document.objects.Where(candidate => candidate.parentId == item.id &&
                    candidate.customType == "ziprail-point")
                    .OrderBy(candidate => int.TryParse(candidate.customRole, out int index)
                        ? index : int.MaxValue).ToArray();
                var controls = points
                    .Select(candidate => instances.TryGetValue(candidate.id, out var point)
                        ? ZiprailCableAnchor(point, candidate) : Vector3.zero).ToArray();
                line.enabled = controls.Length >= 2;
                if (!line.enabled) { UpdateZiplineCableSelection(instance, item.id,
                    Array.Empty<Vector3>()); continue; }
                UpdateZiprailEndModels(document, points, controls);
                var curve = ZiprailPreviewPoints(controls, item.curvedZiplineSegments);
                line.positionCount = curve.Length;
                line.SetPositions(curve);
                UpdateZiplineCableSelection(instance, item.id, curve);
            }
            foreach (var item in document.objects)
            {
                if (item.customType != "camera-path" || !instances.TryGetValue(item.id, out var instance))
                    continue;
                var line = instance.GetComponent<LineRenderer>();
                if (line == null)
                {
                    line = instance.AddComponent<LineRenderer>();
                    line.useWorldSpace = true; line.numCapVertices = 4;
                }
                line.sharedMaterial = ZiplineCableMaterial(); line.widthMultiplier = .035f;
                var points = document.objects.Where(candidate => candidate.parentId == item.id &&
                    candidate.customType == "camera-path-point")
                    .OrderBy(candidate => int.TryParse(candidate.customRole, out int index) ? index : int.MaxValue)
                    .Select(candidate => instances.TryGetValue(candidate.id, out var point) ? point.transform.position : Vector3.zero)
                    .ToArray();
                line.enabled = points.Length >= 2;
                if (line.enabled) { line.positionCount = points.Length; line.SetPositions(points); }
            }
            foreach (var item in document.objects)
            {
                if (item.customType != "sound" || !instances.TryGetValue(item.id, out var instance))
                    continue;
                var line = instance.GetComponent<LineRenderer>();
                if (line == null)
                {
                    line = instance.AddComponent<LineRenderer>();
                    line.useWorldSpace = true; line.numCapVertices = 4;
                }
                line.sharedMaterial = ZiplineCableMaterial(); line.widthMultiplier = .025f;
                var points = document.objects.Where(candidate => candidate.parentId == item.id &&
                    candidate.customType == "sound-point")
                    .OrderBy(candidate => int.TryParse(candidate.customRole, out int index) ? index : int.MaxValue)
                    .Select(candidate => instances.TryGetValue(candidate.id, out var point)
                        ? point.transform.position : Vector3.zero).ToList();
                points.Insert(0, instance.transform.position);
                line.enabled = item.soundShowPolyline && points.Count >= 2;
                if (line.enabled) { line.positionCount = points.Count; line.SetPositions(points.ToArray()); }
            }
            foreach (var item in document.objects)
            {
                if (item.customType != "jump-tower" || !instances.TryGetValue(item.id, out var instance))
                    continue;
                var line = instance.GetComponent<LineRenderer>();
                if (line == null)
                {
                    line = instance.AddComponent<LineRenderer>();
                    line.useWorldSpace = true; line.numCapVertices = 4;
                }
                line.sharedMaterial = ZiplineCableMaterial(); line.widthMultiplier = .05f;
                line.positionCount = 2;
                var towerBase = document.objects.Find(candidate => candidate.parentId == item.id &&
                    candidate.customType == "jump-tower-component" && candidate.customRole == "base");
                var balloon = document.objects.Find(candidate => candidate.parentId == item.id &&
                    candidate.customType == "jump-tower-component" && candidate.customRole == "balloon");
                var baseTransform = towerBase != null && instances.TryGetValue(towerBase.id, out var baseInstance)
                    ? baseInstance.transform : instance.transform;
                var balloonTransform = balloon != null && instances.TryGetValue(balloon.id, out var balloonInstance)
                    ? balloonInstance.transform : instance.transform;
                var topCableOffset = ReMapApp.JumpTowerCableOffsetApex;
                topCableOffset.z = balloonTransform == instance.transform ? item.jumpTowerHeight : 0f;
                var bottomCableOffset = ReMapApp.JumpTowerCableOffsetApex;
                bottomCableOffset.z = 64f;
                var topOffset = ReMapZiplineProfiles.UnityOffset(topCableOffset);
                line.SetPosition(0, balloonTransform.TransformPoint(topOffset));
                line.SetPosition(1, baseTransform.TransformPoint(ReMapZiplineProfiles.UnityOffset(bottomCableOffset)));
            }
        }

        private void EnsureCameraPathMarker(GameObject instance, MapObject item)
        {
            var existing = instance.transform.Find(CameraPathMarkerName);
            GameObject marker;
            if (existing == null)
            {
                marker = GameObject.CreatePrimitive(item.customType == "camera-path-target" ? PrimitiveType.Cube : PrimitiveType.Sphere);
                marker.name = CameraPathMarkerName; marker.transform.SetParent(instance.transform, false);
                marker.transform.localScale = Vector3.one * (item.customType == "camera-path-target" ? .28f : .2f);
                marker.GetComponent<Collider>().enabled = false;
                marker.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                instanceIds[marker] = item.id;
            }
            else marker = existing.gameObject;
            var tint = new MaterialPropertyBlock();
            tint.SetColor("_BaseColor", item.customType == "camera-path-target" ?
                new Color(1f, .25f, .65f) : new Color(1f, .82f, .18f));
            marker.GetComponent<Renderer>().SetPropertyBlock(tint);
        }

        private void EnsureButtonTeleportTargetMarker(GameObject instance, MapObject item)
        {
            EnsureTeleportTargetMarker(instance, item, ButtonTeleportTargetMarkerName, new Color(1f, .25f, .75f));
        }

        private void EnsureTriggerTeleportTargetMarker(GameObject instance, MapObject item)
        {
            EnsureTeleportTargetMarker(instance, item, TriggerTeleportTargetMarkerName, new Color(.15f, .7f, 1f));
        }

        private void EnsureTeleportTargetMarker(GameObject instance, MapObject item, string markerName, Color color)
        {
            var existing = instance.transform.Find(markerName);
            GameObject marker;
            if (existing == null)
            {
                marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = markerName;
                marker.transform.SetParent(instance.transform, false);
                var selection = marker.GetComponent<BoxCollider>();
                selection.isTrigger = true; selection.enabled = true;
                marker.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                instanceIds[marker] = item.id;
            }
            else marker = existing.gameObject;
            marker.transform.localPosition = Vector3.zero;
            marker.transform.localRotation = Quaternion.identity;
            marker.transform.localScale = Vector3.one * .18f;
            var tint = new MaterialPropertyBlock();
            tint.SetColor("_BaseColor", color);
            marker.GetComponent<Renderer>().SetPropertyBlock(tint);

            var arrowTransform = marker.transform.Find(TeleportDirectionArrowName);
            LineRenderer arrow;
            if (arrowTransform == null)
            {
                arrow = new GameObject(TeleportDirectionArrowName, typeof(LineRenderer)).GetComponent<LineRenderer>();
                arrow.transform.SetParent(marker.transform, false);
                arrow.useWorldSpace = true; arrow.positionCount = 5; arrow.numCapVertices = 4;
                arrow.sharedMaterial = lineMaterial; arrow.widthMultiplier = .035f;
            }
            else arrow = arrowTransform.GetComponent<LineRenderer>();
            arrow.SetPropertyBlock(tint);
            float length = 64f * ApexCoordinates.MetersPerUnit;
            float head = 12f * ApexCoordinates.MetersPerUnit;
            Vector3 origin = instance.transform.position;
            Vector3 direction = instance.transform.right.normalized;
            Vector3 side = instance.transform.forward.normalized;
            Vector3 tip = origin + direction * length;
            arrow.SetPosition(0, origin);
            arrow.SetPosition(1, tip);
            arrow.SetPosition(2, tip - direction * head + side * head * .55f);
            arrow.SetPosition(3, tip);
            arrow.SetPosition(4, tip - direction * head - side * head * .55f);
        }

        private void EnsureLocationPairMarker(GameObject instance, MapObject item)
        {
            var existing = instance.transform.Find(LocationPairMarkerName);
            GameObject marker;
            if (existing == null)
            {
                marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = LocationPairMarkerName; marker.transform.SetParent(instance.transform, false);
                marker.GetComponent<Collider>().enabled = false;
                marker.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                instanceIds[marker] = item.id;
            }
            else marker = existing.gameObject;
            marker.transform.localPosition = new Vector3(0f, 0f, .25f);
            marker.transform.localRotation = Quaternion.identity;
            marker.transform.localScale = new Vector3(.16f, .16f, .5f);
            var tint = new MaterialPropertyBlock();
            tint.SetColor("_BaseColor", new Color(1f, .4f, .85f));
            marker.GetComponent<Renderer>().SetPropertyBlock(tint);
        }

        private void EnsureTextInfoPanelMarker(GameObject instance, MapObject item)
        {
            var existing = instance.transform.Find(TextInfoPanelMarkerName);
            GameObject marker;
            if (existing == null)
            {
                marker = new GameObject(TextInfoPanelMarkerName);
                marker.transform.SetParent(instance.transform, false);
                instanceIds[marker] = item.id;

                var background = GameObject.CreatePrimitive(PrimitiveType.Cube);
                background.name = TextInfoPanelBackgroundName;
                background.transform.SetParent(marker.transform, false);
                background.GetComponent<Collider>().enabled = false;
                background.GetComponent<Renderer>().sharedMaterial = textInfoPanelBackgroundMaterial;
                instanceIds[background] = item.id;

                CreateTextInfoPanelText(marker.transform, TextInfoPanelTitleName, true);
                CreateTextInfoPanelText(marker.transform, TextInfoPanelDescriptionName, false);

                var pin = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pin.name = TextInfoPanelPinName;
                pin.transform.SetParent(marker.transform, false);
                pin.GetComponent<Collider>().enabled = false;
                pin.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                instanceIds[pin] = item.id;
            }
            else marker = existing.gameObject;

            float scale = Mathf.Max(.01f, item.textInfoPanelScale);
            marker.transform.localPosition = Vector3.zero;
            marker.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            marker.transform.localScale = Vector3.one * scale;

            bool hasTitle = !string.IsNullOrWhiteSpace(item.textInfoPanelTitle);
            bool hasDescription = !string.IsNullOrWhiteSpace(item.textInfoPanelDescription);

            var title = marker.transform.Find(TextInfoPanelTitleName).GetComponent<TextMesh>();
            title.text = item.textInfoPanelTitle ?? "";
            title.gameObject.SetActive(hasTitle);
            title.characterSize = .24f;
            title.transform.localScale = Vector3.one;

            var description = marker.transform.Find(TextInfoPanelDescriptionName).GetComponent<TextMesh>();
            description.text = item.textInfoPanelDescription ?? "";
            description.gameObject.SetActive(hasDescription);
            description.characterSize = .13f;
            description.transform.localScale = Vector3.one;

            Vector2 panelSize = TextInfoPanelPreviewSize(item.textInfoPanelTitle,
                item.textInfoPanelDescription, item.textInfoPanelShowPin);
            float width = panelSize.x, height = panelSize.y;
            float textWidth = width - .5f;
            Vector2 titleSize = hasTitle ? FitTextInfoPanelText(title, textWidth) : Vector2.zero;
            if (hasDescription) FitTextInfoPanelText(description, textWidth);

            // Apex world-space RUI topologies use the supplied origin as their centre.
            // Keep the preview content inset from the panel's top edge while leaving
            // the object/gizmo origin at the centre of the native 120-unit panel.
            float top = height * .5f - .32f;
            title.transform.localPosition = new Vector3(.026f, top, 0f);
            if (hasTitle) top -= titleSize.y + (hasDescription ? .16f : 0f);
            description.transform.localPosition = new Vector3(.026f, top, 0f);

            var backgroundTransform = marker.transform.Find(TextInfoPanelBackgroundName);
            backgroundTransform.localPosition = Vector3.zero;
            backgroundTransform.localRotation = Quaternion.identity;
            backgroundTransform.localScale = new Vector3(.045f, height, width);

            var pinTransform = marker.transform.Find(TextInfoPanelPinName);
            pinTransform.gameObject.SetActive(item.textInfoPanelShowPin);
            pinTransform.localPosition = new Vector3(.027f, 0f, 0f);
            pinTransform.localRotation = Quaternion.Euler(45f, 0f, 0f);
            float pinDiameter = 8f * ApexCoordinates.MetersPerUnit;
            pinTransform.localScale = new Vector3(.035f, pinDiameter, pinDiameter);
            var pinTint = new MaterialPropertyBlock();
            pinTint.SetColor("_BaseColor", new Color(1f, .78f, .05f));
            pinTransform.GetComponent<Renderer>().SetPropertyBlock(pinTint);
        }

        private static void CreateTextInfoPanelText(Transform parent, string name, bool title)
        {
            var textObject = new GameObject(name, typeof(TextMesh));
            textObject.transform.SetParent(parent, false);
            textObject.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            var text = textObject.GetComponent<TextMesh>();
            text.anchor = TextAnchor.UpperCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 64;
            text.fontStyle = title ? FontStyle.Bold : FontStyle.Normal;
            text.richText = false;
            text.color = title ? new Color(1f, .78f, .05f) : new Color(.92f, .94f, .96f);
        }

        internal static Vector2 TextInfoPanelPreviewSize(string title, string description, bool showPin)
        {
            return new Vector2(TextInfoPanelNativeVisibleSize, TextInfoPanelNativeVisibleSize);
        }

        private static Vector2 FitTextInfoPanelText(TextMesh text, float maximumWidth)
        {
            Vector2 size = TextInfoPanelRenderedSize(text);
            float fit = size.x > maximumWidth ? maximumWidth / size.x : 1f;
            text.transform.localScale = Vector3.one * fit;
            return size * fit;
        }

        private static Vector2 TextInfoPanelRenderedSize(TextMesh text)
        {
            var renderer = text.GetComponent<Renderer>();
            Vector3 size = renderer != null ? renderer.localBounds.size : Vector3.zero;
            if (size.x <= .001f || size.y <= .001f)
                return new Vector2(TextInfoPanelTextWidth(text.text, text.characterSize),
                    TextInfoPanelLineCount(text.text) * text.characterSize);
            return new Vector2(size.x, size.y);
        }

        private static float TextInfoPanelTextWidth(string text, float characterSize)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            float widest = 0f;
            foreach (string line in text.Replace("\r", "").Split('\n'))
            {
                float width = 0f;
                foreach (char character in line)
                    width += character == ' ' ? .42f : "ilI.,:;!'|".IndexOf(character) >= 0 ? .36f :
                        "MW@#%&".IndexOf(character) >= 0 ? .92f : .66f;
                widest = Mathf.Max(widest, width * characterSize);
            }
            return widest;
        }

        private static int TextInfoPanelLineCount(string text) =>
            string.IsNullOrEmpty(text) ? 0 : text.Replace("\r", "").Split('\n').Length;

        private void EnsureWindowHintMarker(GameObject instance, MapObject item)
        {
            var existing = instance.transform.Find(WindowHintMarkerName);
            GameObject marker;
            if (existing == null)
            {
                marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = WindowHintMarkerName; marker.transform.SetParent(instance.transform, false);
                marker.GetComponent<Collider>().enabled = false;
                marker.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                instanceIds[marker] = item.id;
            }
            else marker = existing.gameObject;
            marker.transform.localPosition = Vector3.zero;
            marker.transform.localRotation = Quaternion.identity;
            marker.transform.localScale = new Vector3(
                item.windowHintHalfWidth * 2f * ApexCoordinates.MetersPerUnit,
                item.windowHintHalfHeight * 2f * ApexCoordinates.MetersPerUnit, .08f);
            var tint = new MaterialPropertyBlock();
            tint.SetColor("_BaseColor", new Color(1f, .55f, .15f, .35f));
            marker.GetComponent<Renderer>().SetPropertyBlock(tint);
        }

        private void EnsureZiplineMarker(GameObject instance, MapObject item)
        {
            var markerTransform = instance.transform.Find(ZiplineMarkerName);
            GameObject marker;
            if (markerTransform == null)
            {
                marker = GameObject.CreatePrimitive(PrimitiveType.Sphere); marker.name = ZiplineMarkerName;
                marker.transform.SetParent(instance.transform, false); marker.transform.localScale = Vector3.one * .18f;
                var selection = marker.GetComponent<SphereCollider>();
                selection.radius = 1.2f; selection.isTrigger = true; selection.enabled = true;
                marker.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                instanceIds[marker] = item.id;
            }
            else marker = markerTransform.gameObject;
            marker.transform.position = item.customType == "ziprail-point"
                ? ZiprailCableAnchor(instance, item)
                : ZiplineCableAnchor(instance, item);
            var tint = new MaterialPropertyBlock();
            bool railPoint = item.customType == "curved-zipline-point" || item.customType == "ziprail-point";
            bool start = item.customRole == "start" || railPoint && item.customRole == "0";
            tint.SetColor("_BaseColor", start ? new Color(.25f, 1f, .55f) :
                railPoint ? new Color(.2f, .75f, 1f) : new Color(1f, .58f, .2f));
            marker.GetComponent<Renderer>().SetPropertyBlock(tint);
        }

        private void EnsureZiplineModelSelection(GameObject instance, string id)
        {
            var selection = instance.transform.Find(ZiplineModelSelectionName);
            GameObject hitbox;
            if (selection == null)
            {
                hitbox = new GameObject(ZiplineModelSelectionName);
                hitbox.transform.SetParent(instance.transform, false);
                var collider = hitbox.AddComponent<BoxCollider>(); collider.isTrigger = true;
                instanceIds[hitbox] = id;
            }
            else hitbox = selection.gameObject;

            Bounds localBounds = default; bool found = false;
            Matrix4x4 worldToLocal = instance.transform.worldToLocalMatrix;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.gameObject == hitbox || renderer is LineRenderer) continue;
                Bounds worldBounds = renderer.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 world = worldBounds.center + Vector3.Scale(worldBounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1,
                            (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    Vector3 local = worldToLocal.MultiplyPoint3x4(world);
                    if (!found) { localBounds = new Bounds(local, Vector3.zero); found = true; }
                    else localBounds.Encapsulate(local);
                }
            }
            hitbox.SetActive(found);
            if (!found) return;
            var box = hitbox.GetComponent<BoxCollider>(); box.enabled = true; box.isTrigger = true;
            box.center = localBounds.center;
            box.size = Vector3.Max(localBounds.size, Vector3.one * .15f);
        }

        internal static void ConfigureZiplineComponentColliders(GameObject instance, MapObject item)
        {
            bool solid = item.customType == "ziprail-component"
                ? item.customRole != "cord-end"
                : item.customRole == "support" ||
                    (item.customRole?.StartsWith("support-", StringComparison.Ordinal) ?? false);
            Transform selection = instance.transform.Find(ZiplineModelSelectionName);
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
            {
                if (selection != null && collider.transform == selection) continue;
                collider.enabled = solid;
                if (solid) collider.isTrigger = false;
            }
        }

        private void UpdateZiplineCableSelection(GameObject instance, string id,
            IReadOnlyList<Vector3> points)
        {
            var holderTransform = instance.transform.Find(ZiplineCableSelectionName);
            GameObject holder;
            if (holderTransform == null)
            {
                holder = new GameObject(ZiplineCableSelectionName);
                holder.transform.SetParent(instance.transform, false);
            }
            else holder = holderTransform.gameObject;
            int segmentCount = Math.Max(0, (points?.Count ?? 0) - 1);
            holder.SetActive(segmentCount > 0);
            Matrix4x4 worldToLocal = instance.transform.worldToLocalMatrix;
            for (int index = 0; index < segmentCount; index++)
            {
                GameObject segment;
                if (index < holder.transform.childCount)
                    segment = holder.transform.GetChild(index).gameObject;
                else
                {
                    segment = new GameObject("segment_" + index);
                    segment.transform.SetParent(holder.transform, false);
                    var collider = segment.AddComponent<CapsuleCollider>();
                    collider.direction = 2; collider.isTrigger = true;
                    instanceIds[segment] = id;
                }
                segment.SetActive(true);
                Vector3 start = worldToLocal.MultiplyPoint3x4(points[index]);
                Vector3 end = worldToLocal.MultiplyPoint3x4(points[index + 1]);
                Vector3 delta = end - start;
                segment.transform.localPosition = (start + end) * .5f;
                segment.transform.localRotation = delta.sqrMagnitude > .000001f
                    ? Quaternion.FromToRotation(Vector3.forward, delta) : Quaternion.identity;
                var capsule = segment.GetComponent<CapsuleCollider>();
                capsule.enabled = true; capsule.isTrigger = true; capsule.radius = .12f;
                capsule.height = Mathf.Max(.24f, delta.magnitude + .24f);
            }
            for (int index = segmentCount; index < holder.transform.childCount; index++)
                holder.transform.GetChild(index).gameObject.SetActive(false);
        }

        public static Vector3[] ZiprailPreviewPoints(IReadOnlyList<Vector3> controls, int segmentsPerSpan)
        {
            if (controls == null || controls.Count < 2) return Array.Empty<Vector3>();
            segmentsPerSpan = Mathf.Clamp(segmentsPerSpan, 2, 32);
            var result = new List<Vector3>((controls.Count - 1) * segmentsPerSpan + 1) {
                controls[0]
            };
            for (int span = 0; span < controls.Count - 1; span++)
            {
                Vector3 p0 = span == 0 ? controls[span] : controls[span - 1];
                Vector3 p1 = controls[span];
                Vector3 p2 = controls[span + 1];
                Vector3 p3 = span + 2 < controls.Count ? controls[span + 2] : p2;
                for (int segment = 1; segment <= segmentsPerSpan; segment++)
                {
                    float t = segment / (float)segmentsPerSpan;
                    float t2 = t * t, t3 = t2 * t;
                    result.Add(.5f * ((2f * p1) + (-p0 + p2) * t +
                        (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                        (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            return result.ToArray();
        }

        public static Vector3[] CurvedZiplinePreviewPoints(IReadOnlyList<Vector3> controls, int segmentsPerSpan)
        {
            if (controls == null || controls.Count < 2) return Array.Empty<Vector3>();
            segmentsPerSpan = Mathf.Clamp(segmentsPerSpan, 2, 32);
            var tangents = new Vector3[controls.Count];
            tangents[0] = (controls[1] - controls[0]) * .5f;
            tangents[tangents.Length - 1] = (controls[controls.Count - 1] -
                controls[controls.Count - 2]) * .5f;
            for (int index = 1; index < controls.Count - 1; index++)
            {
                Vector3 direction = controls[index + 1] - controls[index - 1];
                float length = Mathf.Min(Vector3.Distance(controls[index - 1], controls[index]),
                    Vector3.Distance(controls[index], controls[index + 1])) * .5f;
                tangents[index] = direction.sqrMagnitude < .000001f
                    ? Vector3.zero : direction.normalized * length;
            }

            var result = new List<Vector3>((controls.Count - 1) * segmentsPerSpan + 1) { controls[0] };
            for (int span = 0; span < controls.Count - 1; span++)
            {
                Vector3 p0 = controls[span], p1 = p0 + tangents[span];
                Vector3 p3 = controls[span + 1], p2 = p3 - tangents[span + 1];
                for (int segment = 1; segment <= segmentsPerSpan; segment++)
                {
                    float t = segment / (float)segmentsPerSpan;
                    Vector3 a = Vector3.Lerp(p0, p1, t), b = Vector3.Lerp(p1, p2, t);
                    Vector3 c = Vector3.Lerp(p2, p3, t);
                    result.Add(Vector3.Lerp(Vector3.Lerp(a, b, t), Vector3.Lerp(b, c, t), t));
                }
            }
            return result.ToArray();
        }

        private static Vector3 ZiplineCableAnchor(GameObject instance, MapObject item)
        {
            if (instance == null || item == null) return instance == null ? Vector3.zero : instance.transform.position;
            var apexOffset = ReMapZiplineProfiles.CableOffsetApex(item.customProfile, item.ziplineArmHeight);
            return instance.transform.TransformPoint(ReMapZiplineProfiles.UnityOffset(apexOffset));
        }

        private static Vector3 ZiprailCableAnchor(GameObject instance, MapObject item)
        {
            if (instance == null || item == null) return instance == null ? Vector3.zero : instance.transform.position;
            var profile = ReMapZiprailProfiles.Find(item.customProfile);
            return instance.transform.TransformPoint(ApexDisplay.UnityPosition(ReMapZiprailProfiles.CableOffsetApex(profile)));
        }

        private void UpdateZiprailEndModels(MapDocument document, MapObject[] points, Vector3[] anchors)
        {
            UpdateZiprailEndModel(document, points[0], anchors[0], anchors[1]);
            UpdateZiprailEndModel(document, points[points.Length - 1], anchors[points.Length - 1], anchors[points.Length - 2]);
        }

        private void UpdateZiprailEndModel(MapDocument document, MapObject point, Vector3 anchor, Vector3 adjacentAnchor)
        {
            var component = document.objects.FirstOrDefault(candidate => candidate.parentId == point.id && candidate.customType == "ziprail-component" && candidate.customRole == "cord-end");
            if (component == null || !instances.TryGetValue(component.id, out var instance)) return;
            Vector3 outward = anchor - adjacentAnchor;
            outward.y = 0f;
            if (outward.sqrMagnitude < .000001f) return;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.right, outward.normalized);
            instance.transform.position = anchor + rotation * ApexDisplay.UnityPosition(new Vector3(ReMapZiprailProfiles.CordEndOriginOffsetApex, 0f, 0f));
            instance.transform.rotation = rotation;
        }

        public Vector3 ZiplineCableAnchorPosition(string endpointId)
        {
            if (!instances.TryGetValue(endpointId, out var instance) || syncedZiplineDocument == null)
                return Vector3.zero;
            return ZiplineCableAnchor(instance,
                syncedZiplineDocument.objects.Find(item => item.id == endpointId));
        }

        private static Vector3 VerticalEndAnchor(Vector3 startAnchor, Vector3 endAnchor) =>
            new Vector3(startAnchor.x, endAnchor.y, startAnchor.z);

        public static Vector3[] ZiplinePreviewPoints(Vector3 start, Vector3 end,
            float lengthScale, bool vertical, int segments = 24)
        {
            segments = Mathf.Clamp(segments, 1, 128);
            var points = new Vector3[segments + 1];
            Vector3 delta = end - start;
            float distance = delta.magnitude;
            float horizontal = new Vector2(delta.x, delta.z).magnitude;
            // Apex computes its cable shape natively. Approximate a uniformly loaded cable with
            // y = gL² / 8H: Earth gravity supplies the load and lengthScale controls an empirical
            // horizontal tension. Squaring it makes loose values visibly softer while 1 stays taut,
            // but never perfectly straight as it is in game.
            const float gravity = 9.81f;
            const float referenceTension = 1200f;
            float tension = Mathf.Max(.1f, Mathf.Clamp(lengthScale, 0f, 1.2f));
            float sag = vertical || distance < .0001f ? 0f :
                gravity * horizontal * horizontal / (8f * referenceTension * tension * tension);
            sag = Mathf.Min(sag, distance * .35f);
            for (int index = 0; index <= segments; index++)
            {
                float t = index / (float)segments;
                points[index] = Vector3.Lerp(start, end, t) + Vector3.down * (sag * 4f * t * (1f - t));
            }
            return points;
        }

        private static void SetZiplineMarkerPosition(GameObject endpoint, Vector3 position)
        {
            var marker = endpoint?.transform.Find(ZiplineMarkerName);
            if (marker != null) marker.position = position;
        }

        public Vector3 ZiplineGizmoPivot(string endpointId)
        {
            if (!instances.TryGetValue(endpointId, out var instance) || syncedZiplineDocument == null)
                return Vector3.zero;
            var item = syncedZiplineDocument.objects.Find(o => o.id == endpointId);
            if (item == null) return instance.transform.position;
            var zipline = syncedZiplineDocument.objects.Find(o => o.id == item.parentId);
            if (item.customRole == "end" && zipline?.customType == "zipline" &&
                zipline.ziplineMode == "vertical" &&
                instances.TryGetValue(zipline.ziplineStartId, out var start))
            {
                var startItem = syncedZiplineDocument.objects.Find(o => o.id == zipline.ziplineStartId);
                return VerticalEndAnchor(ZiplineCableAnchor(start, startItem),
                    ZiplineCableAnchor(instance, item));
            }
            return ReMapZiplineProfiles.Find(item.customProfile).HasSupport
                ? instance.transform.position : ZiplineCableAnchor(instance, item);
        }

        public Vector3 ZiprailGizmoPivot(string objectId)
        {
            if (syncedZiplineDocument == null) return Vector3.zero;
            var item = syncedZiplineDocument.objects.Find(candidate => candidate.id == objectId);
            if (item?.customType == "ziprail")
                item = syncedZiplineDocument.objects.Where(candidate =>
                    candidate.parentId == item.id && candidate.customType == "ziprail-point")
                    .OrderBy(candidate => int.TryParse(candidate.customRole, out int index)
                        ? index : int.MaxValue).FirstOrDefault();
            if (item == null || !instances.TryGetValue(item.id, out var instance))
                return instances.TryGetValue(objectId, out var fallback)
                    ? fallback.transform.position : Vector3.zero;
            if (item.customType != "ziprail-point") return instance.transform.position;

            var profile = ReMapZiprailProfiles.Find(item.customProfile);
            return instance.transform.TransformPoint(ApexDisplay.UnityPosition(
                ReMapZiprailProfiles.PivotOffsetApex(profile, item.ziplineArmHeight)));
        }

        private static bool TryLocalGeometryBounds(GameObject instance, out Bounds bounds)
        {
            bounds = default; bool found = false; var worldToLocal = instance.transform.worldToLocalMatrix;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.gameObject.name == ZiplineMarkerName) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter?.sharedMesh == null) continue;
                var source = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = source.center + Vector3.Scale(source.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var point = worldToLocal.MultiplyPoint3x4(renderer.transform.TransformPoint(corner));
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                    else bounds.Encapsulate(point);
                }
            }
            return found;
        }

        private Material ZiplineCableMaterial()
        {
            if (ziplineCableMaterial != null) return ziplineCableMaterial;
            ziplineCableMaterial = new Material(lineMaterial) { name = "cable/zipline.vmt preview" };
            ziplineCableMaterial.SetColor("_BaseColor", new Color(1f, .82f, .05f));
            return ziplineCableMaterial;
        }

        private Material ZiplineDetachMaterial()
        {
            if (ziplineDetachMaterial != null) return ziplineDetachMaterial;
            ziplineDetachMaterial = new Material(lineMaterial) { name = "zipline auto-detach preview" };
            ziplineDetachMaterial.SetColor("_BaseColor", new Color(1f, .18f, .12f));
            return ziplineDetachMaterial;
        }

        public static bool TryZiplineDetachGuides(Vector3 start, Vector3 end,
            float startApex, float endApex, out Vector3 startGuideEnd, out Vector3 endGuideStart)
        {
            bool visible = TryZiplineDetachGuidePoints(new[] { start, end }, startApex, endApex,
                out var startPoints, out _, out var endPoints);
            startGuideEnd = startPoints.Length > 0 ? startPoints[startPoints.Length - 1] : start;
            endGuideStart = endPoints.Length > 0 ? endPoints[0] : end;
            return visible;
        }

        public static bool TryZiplineDetachGuidePoints(IReadOnlyList<Vector3> curve,
            float startApex, float endApex, out Vector3[] startGuidePoints,
            out Vector3[] cablePoints, out Vector3[] endGuidePoints)
        {
            startGuidePoints = Array.Empty<Vector3>();
            cablePoints = Array.Empty<Vector3>();
            endGuidePoints = Array.Empty<Vector3>();
            if (curve == null || curve.Count < 2) return false;

            var distances = new float[curve.Count];
            for (int index = 1; index < curve.Count; index++)
                distances[index] = distances[index - 1] + Vector3.Distance(curve[index - 1], curve[index]);
            float total = distances[distances.Length - 1];
            float startDistance = Mathf.Max(0f, startApex) * ApexCoordinates.MetersPerUnit;
            float endDistance = Mathf.Max(0f, endApex) * ApexCoordinates.MetersPerUnit;
            if (total < .0001f || total <= startDistance + endDistance) return false;
            if (startDistance <= .0001f && endDistance <= .0001f) return false;

            float cableEnd = total - endDistance;
            if (startDistance > .0001f)
                startGuidePoints = SlicePolyline(curve, distances, 0f, startDistance);
            cablePoints = SlicePolyline(curve, distances, startDistance, cableEnd);
            if (endDistance > .0001f)
                endGuidePoints = SlicePolyline(curve, distances, cableEnd, total);
            if (cablePoints.Length >= 2) return true;
            startGuidePoints = Array.Empty<Vector3>();
            cablePoints = Array.Empty<Vector3>();
            endGuidePoints = Array.Empty<Vector3>();
            return false;
        }

        private static Vector3[] SlicePolyline(IReadOnlyList<Vector3> points, float[] distances,
            float from, float to)
        {
            var result = new List<Vector3> { PointOnPolyline(points, distances, from) };
            for (int index = 1; index < points.Count - 1; index++)
                if (distances[index] > from + .0001f && distances[index] < to - .0001f)
                    result.Add(points[index]);
            Vector3 last = PointOnPolyline(points, distances, to);
            if (Vector3.Distance(result[result.Count - 1], last) > .0001f) result.Add(last);
            return result.ToArray();
        }

        private static Vector3 PointOnPolyline(IReadOnlyList<Vector3> points, float[] distances,
            float distance)
        {
            distance = Mathf.Clamp(distance, 0f, distances[distances.Length - 1]);
            for (int index = 1; index < points.Count; index++)
            {
                if (distance > distances[index]) continue;
                float segment = distances[index] - distances[index - 1];
                if (segment < .0001f) continue;
                return Vector3.Lerp(points[index - 1], points[index],
                    (distance - distances[index - 1]) / segment);
            }
            return points[points.Count - 1];
        }

        public static Vector3 ZiplinePushDirection(float worldYaw) =>
            Quaternion.AngleAxis(-worldYaw, Vector3.up) * Vector3.right;

        private LineRenderer ZiplineDetachGuide(GameObject zipline, string name)
        {
            var child = zipline.transform.Find(name);
            if (child != null) return child.GetComponent<LineRenderer>();
            var line = new GameObject(name, typeof(LineRenderer)).GetComponent<LineRenderer>();
            line.transform.SetParent(zipline.transform, false);
            line.useWorldSpace = true; line.positionCount = 2; line.numCapVertices = 4;
            line.widthMultiplier = .055f; line.sharedMaterial = ZiplineDetachMaterial();
            return line;
        }

        private void UpdateZiplineDetachGuides(GameObject zipline,
            IReadOnlyList<Vector3> startPoints, IReadOnlyList<Vector3> endPoints)
        {
            var startGuide = ZiplineDetachGuide(zipline, ZiplineDetachStartName);
            var endGuide = ZiplineDetachGuide(zipline, ZiplineDetachEndName);
            startGuide.enabled = startPoints != null && startPoints.Count >= 2;
            endGuide.enabled = endPoints != null && endPoints.Count >= 2;
            if (startGuide.enabled)
            {
                startGuide.positionCount = startPoints.Count;
                for (int index = 0; index < startPoints.Count; index++)
                    startGuide.SetPosition(index, startPoints[index]);
            }
            if (endGuide.enabled)
            {
                endGuide.positionCount = endPoints.Count;
                for (int index = 0; index < endPoints.Count; index++)
                    endGuide.SetPosition(index, endPoints[index]);
            }
        }

        private void UpdateZiplinePushArrow(GameObject zipline, bool visible, Vector3 origin, Vector3 direction)
        {
            const string name = "__remap_zipline_push_direction";
            var child = zipline.transform.Find(name);
            LineRenderer arrow;
            if (child == null)
            {
                arrow = new GameObject(name, typeof(LineRenderer)).GetComponent<LineRenderer>();
                arrow.transform.SetParent(zipline.transform, false);
                arrow.useWorldSpace = true;
                arrow.positionCount = 5;
                arrow.numCapVertices = 4;
                arrow.sharedMaterial = lineMaterial;
                arrow.widthMultiplier = .035f;
            }
            else arrow = child.GetComponent<LineRenderer>();
            arrow.enabled = visible;
            if (!visible) return;
            direction = direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.right;
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            if (side.sqrMagnitude < .0001f) side = Vector3.forward;
            side.Normalize();
            float length = 48f * ApexCoordinates.MetersPerUnit;
            float head = 14f * ApexCoordinates.MetersPerUnit;
            Vector3 tip = origin + direction * length;
            arrow.SetPosition(0, origin);
            arrow.SetPosition(1, tip);
            arrow.SetPosition(2, tip - direction * head + side * head * .55f);
            arrow.SetPosition(3, tip);
            arrow.SetPosition(4, tip - direction * head - side * head * .55f);
        }

        public bool TryZiplineSurfaceBelow(string endpointId, HashSet<string> excludedIds, out float surfaceHeight)
        {
            surfaceHeight = 0f;
            if (!instances.TryGetValue(endpointId, out var endpoint)) return false;
            return TryZiplineSurfaceBelow(endpoint.transform.position, excludedIds, out surfaceHeight);
        }

        public bool TryZiplineSurfaceBelow(Vector3 origin, HashSet<string> excludedIds, out float surfaceHeight)
        {
            surfaceHeight = 0f;
            origin += Vector3.up * .02f;
            float nearest = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(origin, Vector3.down, ApexCoordinates.MaxUnityCoord * 2f,
                ~(1 << 31), QueryTriggerInteraction.Ignore))
            {
                string id = null;
                for (var current = hit.collider.transform; current != null && id == null; current = current.parent)
                    instanceIds.TryGetValue(current.gameObject, out id);
                if (id == null || excludedIds.Contains(id) || hit.distance >= nearest) continue;
                nearest = hit.distance;
                surfaceHeight = hit.point.y;
            }
            if (TryBspSurfaceBelow(origin, out float bspHeight))
            {
                float distance = origin.y - bspHeight;
                if (distance >= 0f && distance < nearest)
                {
                    nearest = distance;
                    surfaceHeight = bspHeight;
                }
            }
            return !float.IsPositiveInfinity(nearest);
        }

        private void PreviewCustom(CatalogEntry entry, Vector3? position)
        {
            if (entry == null || !position.HasValue) { ClearPreview(); return; }
            if (ghost == null || ghostAsset != entry.Id)
            {
                ClearPreview(); ghostAsset = entry.Id;
                if (entry.CustomType == "loot-bin" || entry.CustomType == "jump-pad" ||
                    entry.CustomType == "spawn-point" || entry.CustomType == "weapon-rack" ||
                    entry.CustomType == "respawn-heal")
                {
                    ghost = GameObject.CreatePrimitive(entry.CustomType == "spawn-point"
                        ? PrimitiveType.Capsule : PrimitiveType.Cube);
                    ghost.name = entry.Name + " placement preview";
                    ghost.transform.SetParent(root.transform);
                    ghost.transform.localScale = entry.Size;
                    ghost.GetComponent<Collider>().enabled = false;
                    ghost.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                    ghost.transform.position = position.Value + Vector3.up * entry.Size.y * .5f;
                    return;
                }
                if (entry.CustomType == "curved-zipline")
                {
                    ghost = new GameObject("Curved zipline placement preview");
                    ghost.transform.SetParent(root.transform);
                    var controls = new[] { Vector3.zero, new Vector3(3.8f, 1.2f, 1.5f), new Vector3(7.6f, 0f, 0f) };
                    foreach (var control in controls)
                    {
                        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        marker.transform.SetParent(ghost.transform, false);
                        marker.transform.localPosition = control;
                        marker.transform.localScale = Vector3.one * .18f;
                        marker.GetComponent<Collider>().enabled = false;
                        marker.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                    }
                    var curveLine = ghost.AddComponent<LineRenderer>();
                    curveLine.sharedMaterial = lineMaterial; curveLine.useWorldSpace = false;
                    var curve = CurvedZiplinePreviewPoints(controls, 8);
                    curveLine.positionCount = curve.Length; curveLine.SetPositions(curve);
                    curveLine.widthMultiplier = .04f; curveLine.numCapVertices = 4;
                    ghost.transform.position = position.Value;
                    return;
                }
                if (entry.CustomType == "ziprail")
                {
                    ghost = new GameObject("Ziprail placement preview");
                    ghost.transform.SetParent(root.transform);
                    var controls = new[] { Vector3.zero, new Vector3(3.8f, 1.2f, 1.5f),
                        new Vector3(7.6f, 0f, 0f) };
                    foreach (var control in controls)
                    {
                        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        marker.transform.SetParent(ghost.transform, false);
                        marker.transform.localPosition = control;
                        marker.transform.localScale = Vector3.one * .18f;
                        marker.GetComponent<Collider>().enabled = false;
                        marker.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                    }
                    var railLine = ghost.AddComponent<LineRenderer>();
                    railLine.sharedMaterial = lineMaterial; railLine.useWorldSpace = false;
                    var curve = ZiprailPreviewPoints(controls, 8);
                    railLine.positionCount = curve.Length; railLine.SetPositions(curve);
                    railLine.widthMultiplier = .04f; railLine.numCapVertices = 4;
                    ghost.transform.position = position.Value;
                    return;
                }
                if (entry.CustomType == "door")
                {
                    ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    ghost.name = "Door placement preview";
                    ghost.transform.SetParent(root.transform);
                    ghost.transform.localScale = entry.Size;
                    ghost.GetComponent<Collider>().enabled = false;
                    ghost.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                    var doorTint = new MaterialPropertyBlock();
                    doorTint.SetColor("_BaseColor", new Color(.3f, .95f, .65f));
                    ghost.GetComponent<Renderer>().SetPropertyBlock(doorTint);
                    ghost.transform.position = position.Value + Vector3.up * entry.Size.y * .5f;
                    return;
                }
                if (entry.CustomType == "trigger")
                {
                    ghost = CreateTriggerWireframe("Trigger placement preview",
                        root.transform, null);
                    UpdateTriggerWireframe(ghost, entry.Size.x * .5f,
                        entry.Size.y * .5f);
                    ghost.transform.position = position.Value;
                    return;
                }
                if (entry.CustomType == "sound")
                {
                    ghost = CreateSoundRangeWireframe("Sound placement preview",
                        root.transform, null);
                    UpdateSoundRangeWireframe(ghost, entry.Size.x * .5f);
                    ghost.transform.position = position.Value;
                    return;
                }
                if (entry.CustomType == "text-info-panel")
                {
                    ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    ghost.name = "Text info panel placement preview";
                    ghost.transform.SetParent(root.transform);
                    ghost.transform.localScale = new Vector3(.08f, entry.Size.y, entry.Size.x);
                    ghost.GetComponent<Collider>().enabled = false;
                    ghost.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                    var panelTint = new MaterialPropertyBlock();
                    panelTint.SetColor("_BaseColor", new Color(.25f, .75f, 1f, .65f));
                    ghost.GetComponent<Renderer>().SetPropertyBlock(panelTint);
                    ghost.transform.position = position.Value;
                    return;
                }
                if (entry.CustomType == "window-hint")
                {
                    ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    ghost.name = "Window hint placement preview";
                    ghost.transform.SetParent(root.transform);
                    ghost.transform.localScale = entry.Size;
                    ghost.GetComponent<Collider>().enabled = false;
                    ghost.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                    var hintTint = new MaterialPropertyBlock();
                    hintTint.SetColor("_BaseColor", new Color(1f, .55f, .15f, .35f));
                    ghost.GetComponent<Renderer>().SetPropertyBlock(hintTint);
                    ghost.transform.position = position.Value;
                    return;
                }
                if (entry.CustomType == "jump-tower")
                {
                    ghost = new GameObject("Jump tower placement preview");
                    ghost.transform.SetParent(root.transform);
                    var towerBase = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    towerBase.transform.SetParent(ghost.transform, false);
                    towerBase.transform.localScale = new Vector3(1.2f, .5f, 1.2f);
                    var balloon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    balloon.transform.SetParent(ghost.transform, false);
                    balloon.transform.localPosition = Vector3.up * entry.Size.y;
                    balloon.transform.localScale = Vector3.one * 2.5f;
                    foreach (var part in new[] { towerBase, balloon })
                    {
                        part.GetComponent<Collider>().enabled = false;
                        part.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                    }
                    var towerLine = ghost.AddComponent<LineRenderer>();
                    towerLine.sharedMaterial = lineMaterial; towerLine.useWorldSpace = false;
                    Vector3 cableOffset = ReMapZiplineProfiles.UnityOffset(ReMapApp.JumpTowerCableOffsetApex);
                    towerLine.positionCount = 2; towerLine.SetPosition(0, cableOffset + Vector3.up * 1.6f);
                    towerLine.SetPosition(1, cableOffset + Vector3.up * entry.Size.y); towerLine.widthMultiplier = .05f;
                    ghost.transform.position = position.Value;
                    return;
                }
                ghost = new GameObject("Zipline placement preview"); ghost.transform.SetParent(root.transform);
                float length = Mathf.Max(1, entry.Size.y);
                var start = GameObject.CreatePrimitive(PrimitiveType.Cylinder); start.transform.SetParent(ghost.transform, false);
                start.transform.localPosition = Vector3.up * .5f; start.transform.localScale = new Vector3(.12f, .5f, .12f);
                var arm = GameObject.CreatePrimitive(PrimitiveType.Cube); arm.transform.SetParent(ghost.transform, false);
                arm.transform.localPosition = new Vector3(.35f, 1f, 0); arm.transform.localScale = new Vector3(.7f, .1f, .1f);
                var end = GameObject.CreatePrimitive(PrimitiveType.Sphere); end.transform.SetParent(ghost.transform, false);
                end.transform.localPosition = Vector3.down * length; end.transform.localScale = Vector3.one * .18f;
                foreach (var part in new[] { start, arm, end })
                {
                    part.GetComponent<Collider>().enabled = false; part.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                    var tint = new MaterialPropertyBlock(); tint.SetColor("_BaseColor", new Color(.3f, .95f, .65f));
                    part.GetComponent<Renderer>().SetPropertyBlock(tint);
                }
                var line = ghost.AddComponent<LineRenderer>(); line.sharedMaterial = lineMaterial; line.useWorldSpace = false;
                line.positionCount = 2; line.SetPosition(0, Vector3.up); line.SetPosition(1, Vector3.down * length);
                line.widthMultiplier = .04f; line.numCapVertices = 4;
            }
            ghost.transform.position = position.Value + (entry.CustomType == "door" || entry.CustomType == "loot-bin" ||
                entry.CustomType == "jump-pad" || entry.CustomType == "spawn-point" ||
                entry.CustomType == "weapon-rack" || entry.CustomType == "respawn-heal"
                ? Vector3.up * entry.Size.y * .5f : Vector3.zero);
        }
    }
}
