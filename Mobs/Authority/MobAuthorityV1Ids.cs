using System.Runtime.CompilerServices;
using dc.en;
using dc.pr;

namespace DeadCellsMultiplayerMod.Mobs.Authority;

internal static class MobAuthorityV1Ids
{
    private sealed class ReferenceComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static readonly ReferenceComparer<T> Instance = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private static readonly object Sync = new();
    private static readonly Dictionary<Mob, int> IdByMob = new(ReferenceComparer<Mob>.Instance);
    private static readonly Dictionary<int, Mob> MobById = new();
    private static int _nextId = 1;
    private static Level? _level;

    public static int GetOrCreate(Mob mob, Level level)
    {
        if (mob == null)
            return 0;

        lock (Sync)
        {
            if (!ReferenceEquals(_level, level))
            {
                IdByMob.Clear();
                MobById.Clear();
                _level = level;
            }

            if (IdByMob.TryGetValue(mob, out var existing))
                return existing;

            var id = _nextId++;
            if (_nextId <= 0)
                _nextId = 1;

            IdByMob[mob] = id;
            MobById[id] = mob;
            return id;
        }
    }

    public static void Prune(Level? level)
    {
        lock (Sync)
        {
            if (level == null || !ReferenceEquals(_level, level))
            {
                IdByMob.Clear();
                MobById.Clear();
                _level = level;
                return;
            }

            List<int>? staleIds = null;
            foreach (var pair in MobById)
            {
                var mob = pair.Value;
                var remove = false;
                try
                {
                    remove = mob.destroyed || mob._level == null || !ReferenceEquals(mob._level, level) || mob.life <= 0;
                }
                catch
                {
                    remove = true;
                }

                if (!remove)
                    continue;

                staleIds ??= new List<int>();
                staleIds.Add(pair.Key);
            }

            if (staleIds == null)
                return;

            for (int i = 0; i < staleIds.Count; i++)
            {
                var id = staleIds[i];
                if (MobById.TryGetValue(id, out var mob))
                    IdByMob.Remove(mob);
                MobById.Remove(id);
            }
        }
    }

    public static bool TryGetMob(int netMobId, out Mob? mob)
    {
        lock (Sync)
        {
            if (MobById.TryGetValue(netMobId, out mob))
                return true;
        }

        mob = null;
        return false;
    }

    public static void Clear()
    {
        lock (Sync)
        {
            IdByMob.Clear();
            MobById.Clear();
            _level = null;
        }
    }
}
