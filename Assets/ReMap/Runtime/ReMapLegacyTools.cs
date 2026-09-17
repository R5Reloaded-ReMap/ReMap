using System;
using System.Collections.Generic;
using System.Linq;
using ReMap.Standalone.Core;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private DropdownField selectionMatchChoice, selectionScopeChoice;
        private readonly List<CatalogEntry> replacementModels = new List<CatalogEntry>();
        private VisualElement replacementModelList;
        private Toggle replacementRandom;
        private string parameterSourceId;
        private Label parameterSourceLabel;
        private Toggle transferObjectParameters, transferGameScripts;

        private void BuildSelectionByTypeTool()
        {
            var section = ToolSection(L.T("#SELECT_BY_TYPE"), "select-type");
            selectionMatchChoice = new DropdownField(L.T("#SELECTION_CRITERION"), new List<string>
            {
                L.T("#SAME_OBJECT_TYPE"),
                L.T("#SAME_MODEL"),
                L.T("#REGULAR_PROPS"),
                L.T("#FOLDERS"),
                L.T("#ZIPLINES")
            }, 0);
            selectionScopeChoice = new DropdownField(L.T("#SEARCH_SCOPE"), new List<string>
            {
                L.T("#WHOLE_SCENE"),
                L.T("#SELECTION_AND_DESCENDANTS")
            }, 0);
            section.Add(selectionMatchChoice);
            section.Add(selectionScopeChoice);
            var select = Button(L.T("#SELECT_MATCHING_OBJECTS"), SelectMatchingObjects, "primary");
            select.name = "tool-select-matching";
            section.Add(select);
            section.Add(Label(L.T("#SELECT_BY_TYPE_HELP"), "note"));
        }

        private static string BatchObjectType(MapObject item)
        {
            if (item == null) return "";
            if (item.customType == "zipline") return "zipline";
            if (item.isGroup) return "folder";
            return string.IsNullOrEmpty(item.customType) ? "prop" : item.customType;
        }

        private void BuildModelReplacementTool()
        {
            var section = ToolSection(L.T("#BATCH_MODEL_REPLACEMENT"), "replace-model");
            section.Add(Label(L.T("#BATCH_MODEL_REPLACEMENT_HELP"), "note"));
            var add = Button(L.T("#ADD_LIBRARY_MODEL"), AddReplacementModel, "primary");
            add.name = "tool-add-replacement-model";
            section.Add(add);
            replacementModelList = new VisualElement { name = "replacement-model-list" };
            section.Add(replacementModelList);
            replacementRandom = new Toggle(L.T("#RANDOM_MODEL_PER_OBJECT"));
            section.Add(replacementRandom);
            ToolButton(section, L.T("#REPLACE_SELECTED_MODELS"), ReplaceSelectedModels, "tool-replace-models");
            RefreshReplacementModelList();
        }

        private bool CanUseReplacement(CatalogEntry entry)
        {
            return entry != null && string.IsNullOrEmpty(entry.CustomType) && entry.SupportsGame(snapshot.gameTarget) &&
                (entry.GameAsset == null || entry.GameAsset.Supports(Targets));
        }

        private void AddReplacementModel()
        {
            if (!CanUseReplacement(previewEntry))
                throw new InvalidOperationException(L.T("#SELECT_COMPATIBLE_LIBRARY_MODEL"));
            if (replacementModels.All(entry => entry.Id != previewEntry.Id))
                replacementModels.Add(previewEntry);
            RefreshReplacementModelList();
            ToolMessage(L.F("#ARG0_ADDED_REPLACEMENT_MODELS", previewEntry.Name));
        }

        private void RefreshReplacementModelList()
        {
            if (replacementModelList == null) return;
            replacementModelList.Clear();
            if (replacementModels.Count == 0)
            {
                replacementModelList.Add(Label(L.T("#NO_REPLACEMENT_MODEL"), "note"));
                return;
            }
            foreach (var entry in replacementModels.ToArray())
            {
                var row = ToolRow(replacementModelList);
                row.Add(Label(entry.Name, "tool-model-name"));
                var remove = Button("×", () => { replacementModels.Remove(entry); RefreshReplacementModelList(); });
                remove.tooltip = L.F("#REMOVE_ARG0", entry.Name);
                row.Add(remove);
            }
        }

        private void ReplaceSelectedModels()
        {
            var choices = replacementModels.Where(CanUseReplacement).ToArray();
            if (choices.Length == 0) throw new InvalidOperationException(L.T("#ADD_REPLACEMENT_MODEL_FIRST"));
            var roots = SelectionRoots().ToArray();
            if (roots.Length == 0) throw new InvalidOperationException(L.T("#SELECT_OBJECT_GROUP_0ED54C"));
            int replaced = 0;
            ToolEdit(document =>
            {
                var ids = roots.SelectMany(id => ConstructionTools.Targets(document, id, true)).Distinct().ToArray();
                foreach (string id in ids)
                {
                    var item = document.objects.Find(candidate => candidate.id == id);
                    if (item == null || item.isGroup || !string.IsNullOrEmpty(item.customType)) continue;
                    var replacement = choices[replacementRandom.value && choices.Length > 1 ? toolRandom.Next(choices.Length) : 0];
                    item.assetId = replacement.Id;
                    item.displayName = replacement.Name;
                    item.gameModelPath = replacement.GameAsset?.modelPath ?? "";
                    item.commonAsset = replacement.GameAsset?.IsCommon ?? false;
                    item.availableMaps = replacement.GameAsset?.origins.Select(origin => origin.mapId)
                        .Where(map => map != "").Distinct().ToList() ?? new List<string>();
                    replaced++;
                }
            }, L.T("#SELECTED_MODELS_REPLACED"));
            ToolMessage(L.F("#ARG0_MODELS_REPLACED", replaced));
        }

        private void BuildParameterTransferTool()
        {
            var section = ToolSection(L.T("#PARAMETER_TRANSFER"), "transfer-parameters");
            parameterSourceLabel = Label(L.T("#NO_SOURCE_OBJECT"), "tool-info");
            section.Add(parameterSourceLabel);
            var source = Button(L.T("#USE_SELECTION_AS_SOURCE"), SetParameterTransferSource, "primary");
            source.name = "tool-set-parameter-source";
            section.Add(source);
            transferObjectParameters = new Toggle(L.T("#OBJECT_REMAP_PARAMETERS")) { value = true };
            transferGameScripts = new Toggle(L.T("#GAME_SCRIPT_PROPERTIES")) { value = true };
            section.Add(transferObjectParameters);
            section.Add(transferGameScripts);
            ToolButton(section, L.T("#TRANSFER_TO_SELECTION"), TransferParametersToSelection, "tool-transfer-parameters");
            section.Add(Label(L.T("#PARAMETER_TRANSFER_HELP"), "note"));
        }

        private static string ParameterCompatibility(MapObject item)
        {
            if (item == null || item.isGroup && string.IsNullOrEmpty(item.customType)) return "";
            if (string.IsNullOrEmpty(item.customType)) return "prop";
            return item.customType + ":" + (item.customRole ?? "");
        }

        private void SetParameterTransferSource()
        {
            if (selectedIds.Count != 1) throw new InvalidOperationException(L.T("#SELECT_ONE_SOURCE_OBJECT"));
            var source = snapshot.objects.Find(item => item.id == selectedId);
            if (string.IsNullOrEmpty(ParameterCompatibility(source)))
                throw new InvalidOperationException(L.T("#SOURCE_MUST_HAVE_PARAMETERS"));
            parameterSourceId = source.id;
            RefreshParameterTransferSource();
            ToolMessage(L.F("#ARG0_PARAMETER_SOURCE", source.displayName));
        }

        private void RefreshParameterTransferSource()
        {
            if (parameterSourceLabel == null) return;
            var source = snapshot?.objects.Find(item => item.id == parameterSourceId);
            if (source == null) parameterSourceId = null;
            parameterSourceLabel.text = source == null
                ? L.T("#NO_SOURCE_OBJECT")
                : L.F("#SOURCE_ARG0", source.displayName);
        }

        private static IEnumerable<string> ParameterTransferTargets(MapDocument document, IEnumerable<string> roots)
        {
            foreach (string id in roots)
            {
                var item = document.objects.Find(candidate => candidate.id == id);
                if (item == null) continue;
                if (item.isGroup && string.IsNullOrEmpty(item.customType))
                {
                    foreach (string childId in ConstructionTools.Targets(document, id, true)) yield return childId;
                }
                else if (MapHierarchy.IsEnabled(document, id)) yield return id;
            }
        }

        private void CopyObjectParameters(MapDocument document, MapObject source, MapObject target)
        {
            if (string.IsNullOrEmpty(source.customType))
            {
                target.allowMantle = source.allowMantle;
                target.fadeDistance = source.fadeDistance;
                target.realmId = source.realmId;
                target.clientSide = source.clientSide;
                return;
            }
            if (source.customType == "zipline")
            {
                target.ziplineMode = source.ziplineMode;
                target.ziplineWidth = source.ziplineWidth;
                target.ziplineSpeed = source.ziplineSpeed;
                target.ziplineLengthScale = source.ziplineLengthScale;
                target.ziplineFadeDistance = source.ziplineFadeDistance;
                target.ziplineScale = source.ziplineScale;
                target.ziplinePreserveVelocity = source.ziplinePreserveVelocity;
                target.ziplineDropToBottom = source.ziplineDropToBottom;
                target.ziplineAutoDetachStart = source.ziplineAutoDetachStart;
                target.ziplineAutoDetachEnd = source.ziplineAutoDetachEnd;
                target.ziplineRestPoint = source.ziplineRestPoint;
                target.ziplineDetachEndOnSpawn = source.ziplineDetachEndOnSpawn;
                target.ziplineDetachEndOnUse = source.ziplineDetachEndOnUse;
                target.ziplineAutomaticEnd = source.ziplineAutomaticEnd;
                target.ziplineLockEnd = source.ziplineLockEnd;
                target.ziplineEndOffset = source.ziplineEndOffset;
                target.ziplinePushOffInDirectionX = source.ziplinePushOffInDirectionX;
                target.ziplinePushOffAngle = source.ziplinePushOffAngle;
            }
            else if (source.customType == "zipline-endpoint")
            {
                target.customProfile = source.customProfile;
                target.ziplineArmHeight = source.ziplineArmHeight;
                SyncZiplineComponents(document, target);
            }
        }

        private void TransferParametersToSelection()
        {
            var source = snapshot.objects.Find(item => item.id == parameterSourceId);
            if (source == null) throw new InvalidOperationException(L.T("#CHOOSE_PARAMETER_SOURCE_FIRST"));
            if (!transferObjectParameters.value && !transferGameScripts.value)
                throw new InvalidOperationException(L.T("#CHOOSE_PARAMETERS_TO_TRANSFER"));
            var roots = SelectionRoots().ToArray();
            if (roots.Length == 0) throw new InvalidOperationException(L.T("#SELECT_OBJECT_GROUP_0ED54C"));
            int transferred = 0;
            ToolEdit(document =>
            {
                var documentSource = document.objects.Find(item => item.id == parameterSourceId);
                string compatibility = ParameterCompatibility(documentSource);
                foreach (string id in ParameterTransferTargets(document, roots).Distinct().ToArray())
                {
                    var target = document.objects.Find(item => item.id == id);
                    if (target == null || target.id == documentSource.id || ParameterCompatibility(target) != compatibility) continue;
                    if (transferObjectParameters.value) CopyObjectParameters(document, documentSource, target);
                    if (transferGameScripts.value)
                        target.scriptProperties = (documentSource.scriptProperties ?? new List<ScriptProperty>())
                            .ConvertAll(property => property?.Copy());
                    transferred++;
                }
            }, L.T("#PARAMETERS_TRANSFERRED"));
            ToolMessage(L.F("#ARG0_OBJECTS_UPDATED", transferred));
            RefreshParameterTransferSource();
        }

        private void SelectMatchingObjects()
        {
            CommitInspectorEdit();
            CancelPlacement();
            var reference = snapshot.objects.Find(item => item.id == selectedId);
            int criterion = selectionMatchChoice.index;
            if ((criterion == 0 || criterion == 1) && reference == null)
                throw new InvalidOperationException(L.T("#SELECT_REFERENCE_OBJECT_FIRST"));

            IEnumerable<MapObject> candidates = snapshot.objects.Where(item => !HiddenHierarchyObject(item));
            if (selectionScopeChoice.index == 1)
            {
                var scopedIds = new HashSet<string>();
                foreach (string rootId in SelectionRoots())
                    scopedIds.UnionWith(MapHierarchy.Subtree(snapshot, rootId));
                candidates = candidates.Where(item => scopedIds.Contains(item.id));
            }

            string referenceType = BatchObjectType(reference);
            IEnumerable<MapObject> matches;
            switch (criterion)
            {
                case 0: matches = candidates.Where(item => BatchObjectType(item) == referenceType); break;
                case 1: matches = candidates.Where(item => !item.isGroup && string.IsNullOrEmpty(item.customType) && item.assetId == reference.assetId); break;
                case 2: matches = candidates.Where(item => !item.isGroup && string.IsNullOrEmpty(item.customType)); break;
                case 3: matches = candidates.Where(item => item.isGroup && string.IsNullOrEmpty(item.customType)); break;
                default: matches = candidates.Where(item => item.customType == "zipline"); break;
            }

            string[] ids = matches.Select(item => item.id).ToArray();
            SetSelection(ids, ids.LastOrDefault());
            world.HighlightSelection(SelectionRoots());
            RefreshObjects();
            RefreshInspector();
            RefreshToolReadout();
            ToolMessage(L.F("#ARG0_MATCHING_ITEMS_SELECTED", ids.Length));
        }
    }
}
