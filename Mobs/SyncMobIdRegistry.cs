using System;
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

    /// <summary>
    /// Rebuilds contiguous sync ids (0..N-1) for mobs included by <paramref name="includeMob"/>.
    /// Must match <see cref="DeadCellsMultiplayerMod.Mobs.MobsSynchronization.MobsSynchronization"/> tracked-mob enumeration (getDyn order + IsSyncMob).
    /// </summary>
    public static void RebuildForLevel(Level? level, Func<Mob?, bool> includeMob)
    {
        if (includeMob == null)
            throw new ArgumentNullException(nameof(includeMob));

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
                var mob = entities.getDyn(i) as Mob;
                if (mob == null || !includeMob(mob))
                    continue;

                MobToId[mob] = syncId;
                IdToMob[syncId] = mob;
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

    /// <summary>
    /// 获取怪物的 sync id。<paramref name="allowAssign"/>=true（仅主机）时，未映射的怪会被分配一个新 id；
    /// =false（客机）时绝不自创 id，只返回已存在的映射——客机的 id 必须来自主机数据包（按类型+位置绑定），
    /// 否则主客两端会因动态刷怪/到达顺序不同而编号错位，导致客机命中打到错误的怪、被打的怪卡在 0 血不死。
    /// </summary>
    public static bool TryGetSyncId(Mob? mob, bool allowAssign, out int syncId)
    {
        syncId = 0;
        if (!IsUsableMob(mob))
            return false;

        lock (Sync)
        {
            if (mob != null && MobToId.TryGetValue(mob, out syncId))
                return true;

            if (mob == null || !allowAssign)
                return false;

            var assignedId = nextRuntimeSyncId++;
            MobToId[mob] = assignedId;
            IdToMob[assignedId] = mob;
            syncId = assignedId;
            return true;
        }
    }

    public static bool TryGetExistingSyncId(Mob? mob, out int syncId)
    {
        syncId = 0;
        if (!IsUsableMob(mob))
            return false;

        lock (Sync)
        {
            return mob != null && MobToId.TryGetValue(mob, out syncId);
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
