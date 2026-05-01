using DeadCellsMultiplayerMod.Network;

public sealed partial class NetNode
{
    private void CloseClientConnection()
    {
        DisposeLiteNetService();
    }

    private static bool IsRealtimeSteamLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var trimmed = line.TrimStart();
        if (trimmed.Length == 0)
            return false;

        if (IsPositionLine(trimmed))
            return true;

        return trimmed.StartsWith("ANIM|", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("HEADANIM|", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("HP|", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("MOBSTATE|", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("MOBSTATE2|", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("MOBMOVE|", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("MOBCHARGE|", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("MOBDRAW|", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPositionLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var separatorIndex = line.IndexOf('|');
        if (separatorIndex <= 0)
            return false;

        for (var i = 0; i < separatorIndex; i++)
        {
            if (!char.IsDigit(line[i]))
                return false;
        }

        return true;
    }

    private bool HasAnyConnection()
    {
        if (_role == NetRole.Host)
        {
            lock (_clientsLock)
            {
                return _clients.Count > 0;
            }
        }
        if (_useSteamTransport)
        {
            lock (_sync) return _hasRemote;
        }

        return _binaryNetwork?.HasPeers == true;
    }

    /// <summary>Sends a pre-encoded mob protocol line (used by MobSyncWorker). Line must start with MOB.</summary>
    public Task SendMobWireLine(string line)
    {
        if (string.IsNullOrEmpty(line))
            return Task.CompletedTask;

        if (!line.StartsWith("MOB", StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        if (_role != NetRole.Host && _role != NetRole.Client)
            return Task.CompletedTask;

        if (!HasAnyConnection())
            return Task.CompletedTask;

        return SendLineSafe(line);
    }

    private Task SendLineSafe(string line)
    {
        if (_role == NetRole.Host)
            return BroadcastLineSafe(line);

        TrySendLegacyLine(line, ResolveLegacyDelivery(line));
        return Task.CompletedTask;
    }

    private Task BroadcastLineSafe(string line)
    {
        TrySendLegacyLine(line, ResolveLegacyDelivery(line));
        return Task.CompletedTask;
    }

    private static NetworkDelivery ResolveLegacyDelivery(string line)
    {
        return IsRealtimeSteamLine(line)
            ? NetworkDelivery.Unreliable
            : NetworkDelivery.ReliableOrdered;
    }
}
