using System.Globalization;

namespace DeadCellsMultiplayerMod.Mobs.Authority;

internal static class MobAuthorityV1ProxyRegistry
{
    private sealed class ProxyState
    {
        public int HostUserId;
        public int NetMobId;
        public string LevelId = string.Empty;
        public double X;
        public double Y;
    }

    private const double ForwardRangePx = 130.0;
    private const double BackwardGracePx = 24.0;
    private const double VerticalRangePx = 80.0;

    private static readonly object Sync = new();
    private static readonly Dictionary<string, ProxyState> Proxies = new(StringComparer.Ordinal);

    public static void Upsert(int hostUserId, string levelId, int netMobId, double x, double y)
    {
        if (hostUserId <= 0 || netMobId <= 0)
            return;

        var key = BuildKey(hostUserId, netMobId);
        lock (Sync)
        {
            if (!Proxies.TryGetValue(key, out var state))
            {
                state = new ProxyState { HostUserId = hostUserId, NetMobId = netMobId };
                Proxies[key] = state;
            }

            state.LevelId = levelId ?? string.Empty;
            state.X = x;
            state.Y = y;
        }
    }

    public static void Remove(int hostUserId, int netMobId)
    {
        if (hostUserId <= 0 || netMobId <= 0)
            return;

        lock (Sync)
            Proxies.Remove(BuildKey(hostUserId, netMobId));
    }

    public static void Clear()
    {
        lock (Sync)
            Proxies.Clear();
    }

    public static bool TryFindAttackTarget(string levelId, double heroX, double heroY, int dir, out int netMobId, out double x, out double y)
    {
        netMobId = 0;
        x = 0.0;
        y = 0.0;

        var facing = dir < 0 ? -1 : 1;
        var bestScore = double.MaxValue;
        var normalizedLevelId = (levelId ?? string.Empty).Trim();

        lock (Sync)
        {
            foreach (var proxy in Proxies.Values)
            {
                if (!string.IsNullOrWhiteSpace(normalizedLevelId) &&
                    !string.IsNullOrWhiteSpace(proxy.LevelId) &&
                    !string.Equals(normalizedLevelId, proxy.LevelId, StringComparison.Ordinal))
                {
                    continue;
                }

                var dx = proxy.X - heroX;
                var dy = proxy.Y - heroY;
                var forward = dx * facing;
                if (forward < -BackwardGracePx || forward > ForwardRangePx)
                    continue;
                if (System.Math.Abs(dy) > VerticalRangePx)
                    continue;

                var score = forward * forward + dy * dy;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                netMobId = proxy.NetMobId;
                x = proxy.X;
                y = proxy.Y;
            }
        }

        return netMobId > 0;
    }

    private static string BuildKey(int hostUserId, int netMobId)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{hostUserId}:{netMobId}");
    }
}
