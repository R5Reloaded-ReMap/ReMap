using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.UIElements;
namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private async Task DragDockHandle(VisualElement handle, Vector2 delta)
        {
            Vector2 start = handle.worldBound.center;
            using (var e = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = start })) handle.SendEvent(e);
            if (!layoutResizing) throw new Exception("Resize handle did not capture: " + handle.name);
            using (var e = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = start + delta, delta = delta })) handle.SendEvent(e);
            await TreeFrames();
            using (var e = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = start + delta })) handle.SendEvent(e);
            if (layoutResizing) throw new Exception("Resize capture was not released.");
            await TreeFrames();
        }
        private async Task CheckDockLayout()
        {
            await TreeFrames(12); ResetLayout(); await TreeFrames(); string layoutFixtureId = snapshot.objects[0].id; Select(layoutFixtureId); await TreeFrames();
            if(inspectorPanel.worldBound.width<370)throw new Exception("Properties panel is too narrow for transform vectors and game-property selectors.");
            void CheckVerticalScrollbar(ScrollView view, string name)
            {
                var scroller = view?.verticalScroller;
                if (scroller == null || scroller.worldBound.height < 1) return;
                float top = scroller.worldBound.yMin - view.worldBound.yMin;
                float bottom = view.worldBound.yMax - scroller.worldBound.yMax;
                if (top > 2 || bottom > 2)
                    throw new Exception(name + " scrollbar is vertically inset. top=" + top + " bottom=" + bottom);
            }
            var propertyScope = inspector.Q<DropdownField>("script-property-add-scope");
            var propertyField = inspector.Q<DropdownField>("script-property-add-field");
            if (propertyScope == null || propertyField == null || propertyScope.value != ".kv" || !propertyField.choices.Any(choice => choice.StartsWith("solid · ")))
                throw new Exception("The two-stage game property selector does not expose .kv.solid.");
            var propertySection = inspector.Q("script-properties");
            var information = inspector.Q("inspector-information");
            if (propertyScope.worldBound.width < 78 || propertyField.worldBound.height < 20 || propertySection == null || information == null || information.worldBound.yMin <= propertySection.worldBound.yMin)
                throw new Exception("The property selector is clipped or the technical information is not below the editable controls.");
            var transformSection = inspector.Q("inspector-transform");
            var gameScriptsSection = inspector.Q("game-scripts");
            if (transformSection == null || gameScriptsSection == null || inspector.Q("remap-settings") != null || positionInput.worldBound.height > 28)
                throw new Exception("Classic prop properties are not split into compact Transform, Game Scripts and information sections.");
            var nameValue = objectNameInput.Q(className: "unity-base-text-field__input");
            if (scaleInput.Query<FloatField>().ToList().Count != 1 || positionInput.Query<FloatField>().ToList().Count != 3 ||
                rotationInput.Query<FloatField>().ToList().Count != 3)
                throw new Exception("Transform does not expose Origin/Angles vectors and one uniform Scale value.");
            if (nameValue == null || nameValue.worldBound.width < objectNameInput.labelElement.worldBound.width * 2.7f ||
                positionInput.FieldsRow.worldBound.width < positionInput.TitleRow.worldBound.width * 2.7f)
                throw new Exception("Transform vectors do not reserve 3/4 of the row for wide coordinate values. name label="+
                    objectNameInput.labelElement.worldBound+" value="+nameValue?.worldBound+" vector label="+
                    positionInput.TitleRow.worldBound+" value="+positionInput.FieldsRow.worldBound);
            if (Mathf.Abs(nameValue.worldBound.xMin - positionInput.FieldsRow.worldBound.xMin) > 2f ||
                Mathf.Abs(nameValue.worldBound.xMax - positionInput.FieldsRow.worldBound.xMax) > 2f ||
                scaleInput.TitleRow.worldBound.width < scaleInput.FieldsRow.worldBound.width * 2.1f)
                throw new Exception("Name is not aligned with transform vectors or Scale does not use the compact 70/30 layout. name="+
                    nameValue.worldBound+" vector="+positionInput.FieldsRow.worldBound+" scale title="+
                    scaleInput.TitleRow.worldBound+" scale value="+scaleInput.FieldsRow.worldBound);
            CheckVerticalScrollbar(inspector as ScrollView, "Properties");
            var copyableFields = information.Query<TextField>(className: "inspector-info-field").ToList();
            if (copyableFields.Count < 5 || copyableFields.Any(field => !field.isReadOnly) ||
                copyableFields.Where(field => field.label != L.T("#SCALE") && field.label != L.T("#OBJECT_TYPE") && field.label != L.T("#MODEL_PATH"))
                    .Any(field => !field.value.StartsWith("< ") || !field.value.EndsWith(" >") || !field.value.Contains(", ")))
                throw new Exception("Technical property information is not exposed as compact copyable read-only fields.");
            var classicSettings = inspector.Q("classic-prop-settings");
            var classicToggleInput = classicSettings?.Q<Toggle>()?.Q(className: "unity-toggle__input");
            var classicNumberField = classicSettings?.Q<FloatField>();
            var classicNumberInput = classicNumberField?.Q(className: "unity-base-text-field__input");
            if (classicToggleInput == null || classicNumberInput == null || Mathf.Abs(classicToggleInput.worldBound.xMax - classicNumberInput.worldBound.xMax) > 1.5f)
                throw new Exception("Property toggles and numeric fields do not share the same right edge.");
            if (classicNumberField.labelElement.worldBound.width < classicNumberInput.worldBound.width * 2.1f)
                throw new Exception("Compact scalar properties do not use the requested 70/30 label/value layout.");
            propertyScope.value = ".e";
            if (propertyField.choices.Any(choice => choice.StartsWith("solid · ")) || !propertyField.choices.Any(choice => choice.StartsWith("preventStickyEnts · ")))
                throw new Exception("Game property fields are not filtered by .kv/.e scope.");
            propertyScope.value = ".kv";
            ChangeScriptProperties(selectedId, list => list.Add(ManualProperty("kv"))); await TreeFrames();
            var manualEquals = inspector.Q<Label>(className: "script-property-equals");
            var manualName = inspector.Q<TextField>(className: "script-property-name");
            var manualValue = inspector.Q<TextField>(className: "script-property-value");
            if (manualEquals?.text != "=" || manualName == null || manualValue == null ||
                Mathf.Abs(manualName.worldBound.width - manualValue.worldBound.width) > 3)
                throw new Exception("Manual game fields are not arranged as balanced field = value controls.");
            session.Undo(); Refresh(); await TreeFrames();
            SelectSceneRoot(); await TreeFrames();
            if (!positionInput.ClassListContains("scene-origin-field") || positionInput.Query<FloatField>().ToList().Count != 3 || positionInput.FieldsRow.worldBound.height < 20 || positionInput.Query<FloatField>().ToList().Any(field => field.worldBound.width < 60))
                throw new Exception("World Spawn starting origin does not expose readable X/Y/Z fields.");
            Select(layoutFixtureId); await TreeFrames();
            if(slot.worldBound.width<175)throw new Exception("Project selector is still too narrow.");
            if (catalogMode.choices.Count != 3 || catalogMode.choices.Any(choice => choice.IndexOf("Demo", StringComparison.OrdinalIgnoreCase) >= 0)) throw new Exception("Demo Shapes is still available in the catalog.");
            ShowNewMapDialog(true); await TreeFrames();
            if (!NewMapDialogOpen || newMapName == null || newMapPrimary == null) throw new Exception("New map dialog did not open.");
            CheckVerticalScrollbar(newMapOverlay.Q<ScrollView>(className: "new-map-content"), "New project");
            ShowNewMapDialog(false); ShowAssemblySave(true); await TreeFrames();
            if (!AssemblySaveOpen || assemblyName == null) throw new Exception("Assembly save dialog did not open.");
            ShowAssemblySave(false);
            if (libraryDock.Q(className: "library-controls") is ScrollView) throw new Exception("Library controls still use a horizontal scroll bar.");
            if (assetDetail.resolvedStyle.display != DisplayStyle.None) throw new Exception("Library details should be collapsed by default.");
            SetLibraryDetails(true); await TreeFrames();
            if (assetDetail.resolvedStyle.display == DisplayStyle.None) throw new Exception("Library details could not be reopened.");
            if(assetDetailSplitter.resolvedStyle.display==DisplayStyle.None)throw new Exception("Library details resize handle is hidden.");
            var saveSelection=libraryDock.Q<Button>("save-selection-button");
            if(saveSelection==null||libraryDetailsButton.parent!=saveSelection.parent||libraryDetailsButton.worldBound.xMin<saveSelection.worldBound.xMax-1)throw new Exception("Details is not beside Save selection in the library toolbar.");
            if(pageState.parent==null||!pageState.parent.ClassListContains("dock-title"))throw new Exception("Model count is not in the Library title bar.");
            float detailsBefore=assetDetail.worldBound.width;
            await DragDockHandle(assetDetailSplitter,new Vector2(-45,0));
            if(Mathf.Abs(assetDetail.worldBound.width-detailsBefore-45)>2)throw new Exception("Library details resize failed.");
            if(catalogList.worldBound.xMax>assetDetailSplitter.worldBound.xMin+1)throw new Exception("Library details overlap the catalog.");
            SetLibraryDetails(false); await TreeFrames();
            TreeClick(fileMenuButton); await TreeFrames();
            if (commandMenu.resolvedStyle.display == DisplayStyle.None) throw new Exception("File menu did not open.");
            HideCommandMenu();
            ShowConstructionTools(true); ShowTool("grid"); await TreeFrames();
            if (toolsWindow.parent != root || toolsWindow.resolvedStyle.display == DisplayStyle.None || toolSections.Count(section => section.resolvedStyle.display != DisplayStyle.None) != 1)
                throw new Exception("Floating tools window does not display exactly one selected tool.");
            var toolActivation = toolsWindow.Q<Button>("construction-tool-activation");
            if (toolActivation == null || toolActivation.worldBound.width < 65 || toolActivation.worldBound.yMin < toolsWindow.worldBound.yMin)
                throw new Exception("Construction tool activation control is missing or clipped.");
            var toolsScrollBar = toolsPanel.Q(className: "unity-scroller--vertical");
            var toolsScrollThumb = toolsScrollBar?.Q(className: "unity-base-slider__dragger");
            if (toolsScrollBar == null || toolsScrollThumb == null || toolsScrollBar.worldBound.width > 9 || toolsScrollThumb.worldBound.width > 7)
                throw new Exception("Construction tools scroll bar is too wide.");
            if (toolChoice.worldBound.width < toolsWindow.worldBound.width - 20 || toolsScrollBar.worldBound.yMin < toolsPanel.worldBound.yMin - 1 || toolsScrollBar.worldBound.yMax > toolsPanel.worldBound.yMax + 1 || toolsPanel.worldBound.height - toolsScrollBar.worldBound.height > 6)
                throw new Exception("Tool selector is narrow or the scroll bar does not fill the text rectangle. choice=" + toolChoice.worldBound + " window=" + toolsWindow.worldBound + " scroller=" + toolsScrollBar.worldBound + " panel=" + toolsPanel.worldBound);
            ShowConstructionTools(false);
            var codeToolbarButton = root.Q<Button>("code-preview-button");
            if (codeToolbarButton == null || !codeToolbarButton.enabledInHierarchy || !codeToolbarButton.parent.ClassListContains("toolbar")) throw new Exception("Direct generated-code toolbar button is missing or disabled.");
            TreeClick(codeToolbarButton); await TreeFrames();
            if (codeWindow.parent != root || codeWindow.resolvedStyle.display == DisplayStyle.None || string.IsNullOrWhiteSpace(codePreview.text))
                throw new Exception("Generated code floating window did not open.");
            if (codePreviewAdditionalTab == null || additionalCodePane == null || additionalSharedCodeEditor == null || additionalServerCodeEditor == null || additionalClientCodeEditor == null)
                throw new Exception("Project additional-code editors are missing.");
            var codeExportPanel = codeWindow.Q(className: "code-export-panel");
            if (codeExportPanel == null || entExportUseNative == null || entExportMode == null || entExportRestartMap == null)
                throw new Exception("Generated code window does not contain its map build options.");
            var exportModeInput = entExportMode.Q(className: "unity-base-popup-field__input");
            if (codeExportPanel.worldBound.width < 310 || exportModeInput == null || exportModeInput.worldBound.width < 180 || codeExportPanel.worldBound.xMin < codePreviewScroll.worldBound.xMax - 1 || codeExportPanel.worldBound.xMax > codeWindow.worldBound.xMax + 1)
                throw new Exception("Map build options are not laid out to the right of the generated code preview.");
            var codeResize = codeWindow.Q("resize-game-code");
            if (codeResize == null) throw new Exception("Generated code resize handle is missing.");
            if (codePreviewScroll.verticalScroller.worldBound.width > 9 || codePreviewScroll.horizontalScroller.worldBound.height > 9 || codeResize.worldBound.width > 14)
                throw new Exception("Generated code scroll bars or resize corner are oversized.");
            if (codePreviewScroll.verticalScroller.worldBound.yMin < codePreviewScroll.worldBound.yMin - 1 || codePreviewScroll.verticalScroller.worldBound.yMax > codePreviewScroll.worldBound.yMax + 1 || codePreviewScroll.worldBound.height - codePreviewScroll.verticalScroller.worldBound.height > 6)
                throw new Exception("Generated code scroll bar does not fill its text rectangle.");
            TreeClick(codePreviewAdditionalTab); await TreeFrames();
            if (additionalCodePane.resolvedStyle.display == DisplayStyle.None || additionalServerCodeEditor.worldBound.width < 250 || additionalServerCodeEditor.worldBound.height < 90)
                throw new Exception("Project additional-code editor is hidden or clipped.");
            SetCodePreviewMode(false); await TreeFrames();
            Vector2 codeSize = codeWindow.worldBound.size, codeResizeStart = codeResize.worldBound.center, codeResizeDelta = new Vector2(55, 35);
            using (var e = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = codeResizeStart })) codeResize.SendEvent(e);
            using (var e = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = codeResizeStart + codeResizeDelta, delta = codeResizeDelta })) codeResize.SendEvent(e);
            await TreeFrames();
            using (var e = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = codeResizeStart + codeResizeDelta })) codeResize.SendEvent(e);
            await TreeFrames();
            if (Vector2.Distance(codeWindow.worldBound.size, codeSize + codeResizeDelta) > 2 ||
                codeWindow.worldBound.xMax > root.worldBound.xMax - 7 || codeWindow.worldBound.yMax > root.worldBound.yMax - 7)
                throw new Exception("Generated code resize failed or left the workspace bounds.");
            string previewCode = codePreview.text;
            codePreview.text = string.Join("\n", Enumerable.Range(0, 80).Select(i => previewCode + i + new string('x', 220))); await TreeFrames();
            codePreviewScroll.scrollOffset = new Vector2(codePreviewScroll.horizontalScroller.highValue, codePreviewScroll.verticalScroller.highValue); await TreeFrames();
            bool horizontalOverflow = codePreviewScroll.horizontalScroller.highValue > 1;
            if (codePreviewScroll.horizontalScrollerVisibility != ScrollerVisibility.Auto ||
                codePreviewScroll.verticalScrollerVisibility != ScrollerVisibility.AlwaysVisible ||
                codePreviewScroll.scrollOffset.y < 1 || horizontalOverflow && codePreviewScroll.scrollOffset.x < 1)
                throw new Exception("Generated code preview does not scroll to its long content.");
            codePreview.text = previewCode; codePreviewScroll.scrollOffset = Vector2.zero;
            ShowCodePreview(false);
            if (LiveMapEnabled)
            {
                ShowLiveConsole(); await TreeFrames();
                if (liveWindow.resolvedStyle.display == DisplayStyle.None || liveCommand == null || liveSendButton == null || liveRebuildButton == null)
                    throw new Exception("Live game command window did not open.");
                ShowLiveConsole(false);
            }
            else if (liveWindow != null) throw new Exception("Disabled Live Map UI was created.");
            string document = codec.Encode(snapshot);
            float sideBefore = inspectorPanel.worldBound.width;
            await DragDockHandle(sideSplitter, new Vector2(-70, 0));
            if (Mathf.Abs(inspectorPanel.worldBound.width - sideBefore - 70) > 2) throw new Exception("Side panel resize failed.");
            float libraryBefore = libraryDock.worldBound.height;
            await DragDockHandle(librarySplitter, new Vector2(0, -55));
            if (Mathf.Abs(libraryDock.worldBound.height - libraryBefore - 55) > 2) throw new Exception("Library resize failed.");
            float treeBefore = hierarchyPane.worldBound.height;
            await DragDockHandle(treeSplitter, new Vector2(0, 40));
            if (Mathf.Abs(hierarchyPane.worldBound.height - treeBefore - 40) > 2) throw new Exception("Hierarchy/properties split failed.");
            float viewHeight = viewport.worldBound.height;
            TreeClick(libraryDock.Q<Button>(className: "dock-close")); await TreeFrames();
            if (layout.library || viewport.worldBound.height < viewHeight + 100) throw new Exception("Closing library did not free scene space.");
            TreeClick(hierarchyPane.Q<Button>(className: "dock-close")); await TreeFrames();
            if (layout.hierarchy || propertiesPane.worldBound.height < editorBody.worldBound.height - 3) throw new Exception("Closing hierarchy did not expand properties.");
            TreeClick(propertiesPane.Q<Button>(className: "dock-close")); await TreeFrames();
            if (inspectorPanel.resolvedStyle.display != DisplayStyle.None || viewport.worldBound.width < editorBody.worldBound.width - 3) throw new Exception("Closing the side panels did not expand the scene.");
            TreeClick(root.Q<Button>("view-menu-button")); await TreeFrames();
            if (viewMenu.resolvedStyle.display == DisplayStyle.None) throw new Exception("Affichage menu did not open.");
            if(fullscreenMenuButton==null||!fullscreenMenuButton.text.Contains("F11"))throw new Exception("Fullscreen command is missing from View.");
            ToggleFullscreen();await TreeFrames(12);
            if(!IsFullscreen||Screen.fullScreenMode!=FullScreenMode.FullScreenWindow)throw new Exception("F11 did not enter borderless fullscreen: "+Screen.fullScreenMode);
            ToggleFullscreen();await TreeFrames(12);
            if(IsFullscreen||Screen.fullScreenMode!=FullScreenMode.Windowed)throw new Exception("F11 did not restore windowed mode: "+Screen.fullScreenMode);
            foreach (var toggle in panelToggles.Values) toggle.value = true;
            viewMenu.style.display = DisplayStyle.None; await TreeFrames();
            if (!layout.library || !layout.hierarchy || !layout.properties) throw new Exception("Panel reopening failed.");
            float fullWidth = viewport.worldBound.width, fullHeight = viewport.worldBound.height;
            TreeClick(maximizeSceneButton); await TreeFrames();
            if (viewport.worldBound.width <= fullWidth || viewport.worldBound.height <= fullHeight) throw new Exception("Scene maximize failed.");
            TreeClick(maximizeSceneButton); await TreeFrames();
            if (Mathf.Abs(viewport.worldBound.width - fullWidth) > 2) throw new Exception("Scene layout was not restored.");
            ShowSettings(true); await TreeFrames();
            CheckVerticalScrollbar(settingsPanel.Q<ScrollView>(className: "settings-scroll"), "Settings");
            var before = settingsPanel.worldBound;
            await DragDockHandle(settingsPanel.Q("resize-settings"), new Vector2(-60, 35));
            if (Mathf.Abs(settingsPanel.worldBound.width - before.width + 60) > 2) throw new Exception("Settings resize failed.");
            var position = settingsPanel.worldBound.position;
            await DragDockHandle(settingsPanel.Q("settings-drag-title"), new Vector2(-25, -20));
            if (Vector2.Distance(settingsPanel.worldBound.position, position + new Vector2(-25, -20)) > 2) throw new Exception("Settings title drag failed.");
            string saved = JsonUtility.ToJson(layout); SaveLayout(); layout = new WorkspaceLayout(); LoadLayout();
            if (JsonUtility.ToJson(layout) != saved) throw new Exception("Panel sizes/visibility did not survive saving and reloading preferences.");
            ShowSettings(false); await TreeFrames();
            if (codec.Encode(snapshot) != document) throw new Exception("Panel operations changed map content.");
            layout.sideWidth = 9999; layout.libraryHeight = 9999; layout.treeHeight = 9999; AdaptLayout(); await TreeFrames();
            if (viewport.worldBound.width < 275 || viewport.worldBound.height < 95) throw new Exception("Extreme panel sizes hid the scene.");
            ResetLayout(); await TreeFrames();
            if (assetLibrary.Configured)
            {
                await IndexAssets();
                search.value="";RefreshCatalog();await TreeFrames();
                int available=assetLibrary.Records.Count(r=>r.Supports(Targets));
                if(available>0&&!pageState.text.Contains(available.ToString()))throw new Exception("Catalog does not expose its full result count immediately.");
                if(available>100&&catalogList.Q<Button>(className:"game-card")==null)throw new Exception("Virtual catalog did not render its visible window.");
                if(available>100)
                {
                    SetLibraryDetails(true);await TreeFrames();RenderVisibleCatalog(true);await TreeFrames();
                    float scrollbarGap=assetDetailSplitter.worldBound.xMin-catalogList.verticalScroller.worldBound.xMax;
                    if(catalogList.verticalScroller.worldBound.width<1||Mathf.Abs(scrollbarGap)>1.5f)throw new Exception("Catalog scrollbar is not flush with the details divider. Gap="+scrollbarGap+" scroller="+catalogList.verticalScroller.worldBound+" divider="+assetDetailSplitter.worldBound);
                    var scrollbarDragger=catalogList.verticalScroller.Q(className:"unity-base-slider__dragger");
                    if(catalogList.verticalScroller.worldBound.width<10||scrollbarDragger==null||scrollbarDragger.worldBound.width>catalogList.verticalScroller.worldBound.width+1)throw new Exception("Catalog scrollbar thumb is clipped or hidden. Scroller="+catalogList.verticalScroller.worldBound+" thumb="+scrollbarDragger?.worldBound);
                    SetLibraryDetails(false);await TreeFrames();
                }
                if(catalogList.Q(className:"card-origin")!=null)throw new Exception("Catalog cards still use a third RPAK line.");
                if(available>100){catalogList.scrollOffset=new Vector2(0,catalogList.verticalScroller.highValue);await TreeFrames();if(renderedCatalogLast!=available)throw new Exception("Virtual catalog cannot reach the final indexed model.");}
                SetScaleGizmo();
                if(!mode.text.Contains("prop_dynamic")||GizmoScaleFactors(2,0)!=Vector3.one*2||GizmoScaleFactors(2,2)!=Vector3.one*2)
                    throw new Exception("The Scale gizmo is not uniformly scaling all three axes.");
                var record = assetLibrary.Records.FirstOrDefault(r => r.guid == "43f9958ba65ae981");
                if (record != null && assetLibrary.CachedModel(record) != null) { search.value = "death_box"; await PreviewGameAsset(record);if(!previewText.text.Contains("Dimensions X / Y / Z"))throw new Exception("Model details omit Apex dimensions."); }
                if(thumbnailTotal>0&&thumbnailDone>=thumbnailTotal&&thumbnailStatusRow.resolvedStyle.display!=DisplayStyle.None)throw new Exception("Completed thumbnail progress still consumes a library row.");
            }
            SetStatus("Disposition vérifiée : redimensionner, fermer, rouvrir et restaurer les panneaux.");
        }
        private IEnumerator LayoutSmoke()
        {
            var check = CheckDockLayout(); while (!check.IsCompleted) yield return null;
            if (check.IsFaulted) Debug.LogException(check.Exception);
            yield return new WaitForEndOfFrame(); var image = ScreenCapture.CaptureScreenshotAsTexture();
            if (image != null) { File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "editor-layout-preview.png"), image.EncodeToPNG()); Destroy(image); }
            if (!check.IsFaulted)
            {
                ShowSettings(true); for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame(); image = ScreenCapture.CaptureScreenshotAsTexture();
                if (image != null) { File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "editor-settings-preview.png"), image.EncodeToPNG()); Destroy(image); }
                Debug.Log("REMAP_DOCK_LAYOUT_OK");
            }
            PlayerPrefs.DeleteKey(LayoutPreference + ".qa"); PlayerPrefs.Save();
            Application.Quit(check.IsFaulted ? 1 : 0);
        }
    }
}
