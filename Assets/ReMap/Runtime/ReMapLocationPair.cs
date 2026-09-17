using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        internal static MapObject CreateLocationPair(Vector3 position, string parent = "") =>
            new MapObject {
                assetId = "custom:new-location-pair", displayName = L.T("#NEW_LOCATION_PAIR"),
                customType = "location-pair", parentId = parent ?? "", position = WorldView.ToData(position),
                isGroup = true
            };

        private void InsertLocationPair(Vector3 position, string parent = "")
        {
            var pair = CreateLocationPair(position, parent);
            session.Edit(document => document.objects.Add(pair));
            selectedId = pair.id; RevealHierarchy(pair.id); Refresh();
            SetStatus(L.T("#LOCATION_PAIR_CREATED"));
        }

        private void BuildLocationPairInspector(VisualElement section)
        {
            section.Add(Label(L.T("#NEW_LOCATION_PAIR"), "inspector-subsection-title"));
            section.Add(Label(L.T("#LOCATION_PAIR_HELP"), "note"));
        }
    }
}
