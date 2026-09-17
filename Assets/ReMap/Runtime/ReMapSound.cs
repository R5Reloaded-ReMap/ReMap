using ReMap.Standalone.Core;
using System;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        internal static MapObject[] CreateDefaultSoundObjects(Vector3 position, string parent = "")
        {
            var sound = new MapObject {
                assetId = "custom:sound", displayName = L.T("#SOUND"), customType = "sound",
                parentId = parent ?? "", position = WorldView.ToData(position), isGroup = true,
                soundName = "", soundRadius = 0f, soundEnabled = true, soundShowPolyline = true
            };
            return new[] { sound };
        }

        private static MapObject CreateSoundPoint(string parentId, int index, Vector3 position) =>
            new MapObject {
                assetId = "custom:sound-point:" + index.ToString(CultureInfo.InvariantCulture),
                displayName = L.F("#SOUND_POINT_ARG0", index + 1), customType = "sound-point",
                customRole = index.ToString(CultureInfo.InvariantCulture), parentId = parentId,
                isGroup = true, position = WorldView.ToData(position)
            };

        private MapObject[] SoundPoints(MapDocument document, string soundId) =>
            document.objects.Where(candidate => candidate.parentId == soundId &&
                candidate.customType == "sound-point")
                .OrderBy(candidate => int.TryParse(candidate.customRole, out int index) ? index : int.MaxValue)
                .ToArray();

        private void InsertSound(Vector3 position, string parent = "")
        {
            var created = CreateDefaultSoundObjects(position, parent);
            session.Edit(document => document.objects.AddRange(created));
            selectedId = created[0].id; RevealHierarchy(selectedId); Refresh();
            SetStatus(L.T("#SOUND_CREATED"));
        }

        private void AddSoundPoint(string soundId)
        {
            session.Edit(document => {
                var points = SoundPoints(document, soundId);
                Vector3 position;
                if (points.Length == 0) position = ApexDisplay.UnityPosition(new Vector3(200f, 0f, 0f));
                else if (points.Length == 1)
                    position = WorldView.ToVector(points[0].position) + ApexDisplay.UnityPosition(new Vector3(200f, 0f, 0f));
                else
                {
                    Vector3 last = WorldView.ToVector(points[points.Length - 1].position);
                    Vector3 previous = WorldView.ToVector(points[points.Length - 2].position);
                    position = last + last - previous;
                }
                document.objects.Add(CreateSoundPoint(soundId, points.Length, position));
            });
            Refresh();
        }

        private void RemoveSoundPoint(string soundId)
        {
            session.Edit(document => {
                var points = SoundPoints(document, soundId);
                if (points.Length == 0) return;
                var removed = MapHierarchy.Subtree(document, points[points.Length - 1].id);
                document.objects.RemoveAll(candidate => removed.Contains(candidate.id));
            });
            selectedId = soundId; Refresh();
        }

        private void BuildSoundInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#SOUND"), "inspector-subsection-title"));
            var points = SoundPoints(snapshot, item.id);
            section.Add(Label(L.F("#ARG0_SOUND_POINTS", points.Length), "inspector-inline-help"));
            var actions = new VisualElement(); actions.AddToClassList("inspector-actions");
            actions.Add(Button(L.T("#ADD_POINT"), () => AddSoundPoint(item.id)));
            var remove = Button(L.T("#REMOVE_LAST_POINT"), () => RemoveSoundPoint(item.id));
            remove.SetEnabled(points.Length > 0); actions.Add(remove);
            if (points.Length > 0)
                actions.Add(Button(L.T("#SELECT_LAST_POINT"), () => Select(points[points.Length - 1].id)));
            section.Add(actions);

            var name = CompactInspectorField(new TextField(L.T("#SOUND_NAME")) { value = item.soundName ?? "", isDelayed = true });
            var radius = CompactInspectorField(new FloatField(L.T("#SOUND_RADIUS")) { value = item.soundRadius, isDelayed = true });
            var wave = CompactInspectorField(new Toggle(L.T("#SOUND_WAVE_AMBIENT")) { value = item.soundWaveAmbient });
            var enabled = CompactInspectorField(new Toggle(L.T("#SOUND_ENABLED")) { value = item.soundEnabled });
            var show = CompactInspectorField(new Toggle(L.T("#SOUND_SHOW_POLYLINE")) { value = item.soundShowPolyline });
            section.Add(name); section.Add(radius); section.Add(wave); section.Add(enabled); section.Add(show);
            section.Add(Label(L.T("#SOUND_HELP"), "note"));

            void Change(Action<MapObject> change)
            {
                CommitInspectorEdit();
                session.Edit(document => change(document.objects.Find(candidate => candidate.id == item.id)));
                Refresh();
            }
            name.RegisterValueChangedCallback(change => Change(edited => edited.soundName = change.newValue ?? ""));
            radius.RegisterValueChangedCallback(change => Change(edited => edited.soundRadius = Mathf.Clamp(change.newValue, 0f, 65535f)));
            wave.RegisterValueChangedCallback(change => Change(edited => edited.soundWaveAmbient = change.newValue));
            enabled.RegisterValueChangedCallback(change => Change(edited => edited.soundEnabled = change.newValue));
            show.RegisterValueChangedCallback(change => Change(edited => edited.soundShowPolyline = change.newValue));
        }

        private void BuildSoundPointInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#SOUND_POLYLINE_POINT"), "inspector-subsection-title"));
            section.Add(Label(L.T("#SOUND_POINT_HELP"), "note"));
            section.Add(Button(L.T("#SELECT_SOUND"), () => Select(item.parentId)));
        }
    }
}
