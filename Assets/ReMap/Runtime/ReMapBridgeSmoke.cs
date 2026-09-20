using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private IEnumerator BridgeSmoke()
        {
            Task<int> send = Task.Run(() => ReMapBridge.Send(new[] { "script print(\"REMAP_BRIDGE_OK\")" }));
            while (!send.IsCompleted) yield return null;
            if (send.IsFaulted) { Debug.LogException(send.Exception); Application.Quit(1); yield break; }
            Debug.Log("REMAP_BRIDGE_OK: " + send.Result + " command sent");
            Application.Quit(0);
        }
    }
}
