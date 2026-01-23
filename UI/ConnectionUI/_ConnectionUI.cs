using dc.pr;
using dc.ui;
using DeadCellsMultiplayerMod.Interface.ModuleInitializing;
using ModCore.Events;
using ModCore.Utitities;
using System.Collections.Generic;

namespace DeadCellsMultiplayerMod.MultiplayerModUI.Connection
{
    public static class _ConnectionUI
    {
        public static List<string> GetAllPlayerNames()
        {
            var playerNames = new List<string>();

            var net = ModEntry._net;
            if (net == null) return playerNames;

            var localName = GameMenu.Username;
            if (string.IsNullOrWhiteSpace(localName))
                localName = "Guest";

            if (!net.TryGetRemoteUserSnapshots(out var snapshots))
                snapshots = new List<NetNode.RemoteUserSnapshot>();

            var localId = net.id;
            const int hostId = 1;

            if (!net.HasRemote && !net.IsHost)
            {
                playerNames.Add("connecting...");
                return playerNames;
            }

            string? hostName = null;
            if (localId == hostId)
            {
                hostName = localName;
            }
            else
            {
                for (int i = 0; i < snapshots.Count; i++)
                {
                    var remote = snapshots[i];
                    if (remote.Id == hostId)
                    {
                        hostName = GetPlayerName(localId, remote.Id, remote.Username ?? string.Empty);
                        break;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(hostName))
            {
                var hostSuffix = localId == hostId ? " (Host) (you)" : " (Host)";
                playerNames.Add(hostName + hostSuffix);
            }

            if (localId != hostId)
                playerNames.Add(localName + " (you)");

            for (int i = 0; i < snapshots.Count; i++)
            {
                var remote = snapshots[i];
                if (remote.Id == hostId || remote.Id == localId)
                    continue;

                string displayName = GetPlayerName(localId, remote.Id, remote.Username ?? string.Empty);
                playerNames.Add(displayName);
            }

            return playerNames;
        }


        public static string GetPlayerName(int localId, int remoteId, string remoteUsername)
        {
            if (ModEntry.TryGetClientIndex(localId, remoteId, out var slotIndex))
            {
                var displayName = ModEntry.GetClientLabel(slotIndex);

                if (string.IsNullOrWhiteSpace(displayName) ||
                    string.Equals(displayName, "Guest", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(remoteUsername))
                        displayName = remoteUsername.Trim();
                }

                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = "Guest";

                return displayName;
            }
            return string.IsNullOrWhiteSpace(remoteUsername) ? "Guest" : remoteUsername.Trim();
        }
        public static bool ShouldAutoHideConnectionUI(this TitleScreen titleScreen, bool visible)
        {
            return ConnectionUI.set_visible = visible;
        }

    }
}
