using System;
using ReMap.Standalone.Core;
using UnityEngine;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private const string DiscordApplicationId = "1551148854475497512";
        private const int DiscordTextLimit = 128;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private DiscordIpcClient discordPresence;
#endif

        private void InitializeDiscordPresence()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (discordPresence == null) discordPresence = new DiscordIpcClient(DiscordApplicationId);
#endif
        }

        private void RefreshDiscordPresence()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (discordPresence == null || snapshot == null) return;
            string project = string.IsNullOrWhiteSpace(snapshot.name) ? "ReMap" : snapshot.name.Trim();
            string details = LimitDiscordText(L.F("#DISCORD_EDITING_ARG0", project));
            string state = LimitDiscordText(L.F("#DISCORD_TARGET_OBJECTS_VERSION_ARG0_ARG1_ARG2",
                GameTargets.DisplayName(snapshot.gameTarget), snapshot.objects?.Count ?? 0, Application.version));
            discordPresence.SetActivity(details, state);
#endif
        }

        private void DisposeDiscordPresence()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            discordPresence?.Dispose();
            discordPresence = null;
#endif
        }

        internal static string LimitDiscordText(string value)
        {
            value = string.IsNullOrWhiteSpace(value) ? "ReMap" : value.Trim();
            return value.Length <= DiscordTextLimit ? value : value.Substring(0, DiscordTextLimit);
        }
    }
}
