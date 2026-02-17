using System.Collections.Generic;
using dc.en;
using dc.pr;


namespace DeadCellsMultiplayerMod.Mobs.Levelinit;

internal static class SyncMobIdRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<int, Mob> IdToMob = new();
    private static readonly Dictionary<Mob, int> MobToId = new(ReferenceEqualityComparer.Instance);
    private static Level? currentLevel;
    private static int nextRuntimeSyncId = 0;

    public static void RebuildForLevel(Level? level)
    {
        lock (Sync)
        {
            ClearLocked();
            currentLevel = level;
            if (level == null || level.entities == null)
                return;

            var entities = level.entities;
            var syncId = 0;
            for (int i = 0; i < entities.length; i++)
            {
                var mob = entities.array[i] as Mob;
                if (!IsUsableMob(mob))
                    continue;

                MobToId[mob!] = syncId;
                IdToMob[syncId] = mob!;
                syncId++;
            }

            nextRuntimeSyncId = syncId;
        }
    }

    public static void ClearForLevel(Level? level)
    {
        lock (Sync)
        {
            if (level != null && currentLevel != null && !ReferenceEquals(level, currentLevel))
                return;
            ClearLocked();
        }
    }

    public static bool TryGetSyncId(Mob? mob, out int syncId)
    {
        syncId = 0;
        if (!IsUsableMob(mob))
            return false;

        lock (Sync)
        {
            if (mob != null && MobToId.TryGetValue(mob, out syncId))
                return true;

            if (mob == null)
                return false;

            var assignedId = nextRuntimeSyncId++;
            MobToId[mob] = assignedId;
            IdToMob[assignedId] = mob;
            syncId = assignedId;
            return true;
        }
    }

    public static bool TryGetMobBySyncId(int syncId, out Mob? mob)
    {
        mob = null;
        if (syncId < 0)
            return false;

        lock (Sync)
        {
            if (!IdToMob.TryGetValue(syncId, out var mapped))
                return false;

            if (!IsUsableMob(mapped))
            {
                IdToMob.Remove(syncId);
                if (mapped != null)
                    MobToId.Remove(mapped);
                return false;
            }

            mob = mapped;
            return true;
        }
    }

    public static void BindSyncId(Mob? mob, int syncId)
    {
        if (!IsUsableMob(mob) || syncId < 0)
            return;

        lock (Sync)
        {
            if (mob != null && MobToId.TryGetValue(mob, out var oldId))
                IdToMob.Remove(oldId);

            if (IdToMob.TryGetValue(syncId, out var oldMob) && oldMob != null)
                MobToId.Remove(oldMob);

            if (mob != null)
            {
                MobToId[mob] = syncId;
                IdToMob[syncId] = mob;
            }

            if (syncId >= nextRuntimeSyncId)
                nextRuntimeSyncId = syncId + 1;
        }
    }

    public static void RemoveMob(Mob? mob)
    {
        if (mob == null)
            return;

        lock (Sync)
        {
            if (MobToId.TryGetValue(mob, out var syncId))
            {
                MobToId.Remove(mob);
                IdToMob.Remove(syncId);
            }
        }
    }

    private static void ClearLocked()
    {
        IdToMob.Clear();
        MobToId.Clear();
        currentLevel = null;
        nextRuntimeSyncId = 0;
    }

    private static bool IsUsableMob(Mob? mob)
    {
        if (mob == null)
            return false;

        try
        {
            if (mob.destroyed || mob._level == null)
                return false;
        }
        catch
        {
            return false;
        }

        return true;
    }
}
