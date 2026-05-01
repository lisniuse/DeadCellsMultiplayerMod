using System;
using System.Collections.Generic;
using dc.en;
using dc.pr;
using Serilog;


namespace DeadCellsMultiplayerMod.Mobs.Levelinit;

internal static class SyncMobIdRegistry
{
    internal readonly struct DebugMapping
    {
        public readonly int SyncId;
        public readonly Mob Mob;

        public DebugMapping(int syncId, Mob mob)
        {
            SyncId = syncId;
            Mob = mob;
        }
    }

    internal readonly struct Stats
    {
        public readonly int Count;
        public readonly int MinSyncId;
        public readonly int MaxSyncId;
        public readonly int NextRuntimeSyncId;

        public Stats(int count, int minSyncId, int maxSyncId, int nextRuntimeSyncId)
        {
            Count = count;
            MinSyncId = minSyncId;
            MaxSyncId = maxSyncId;
            NextRuntimeSyncId = nextRuntimeSyncId;
        }
    }

    private static readonly object Sync = new();
    private static readonly Dictionary<int, Mob> IdToMob = new();
    private static readonly Dictionary<Mob, int> MobToId = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<int> IssuedIdsForIdentity = new();
    private static Level? currentLevel;
    private static int currentIdentityToken;
    private static int nextRuntimeSyncId = 0;

    /// <summary>
    /// Rebuilds contiguous sync ids (0..N-1) for mobs included by <paramref name="includeMob"/>.
    /// Must match <see cref="DeadCellsMultiplayerMod.Mobs.MobsSynchronization.MobsSynchronization"/> tracked-mob enumeration (getDyn order + IsSyncMob).
    /// </summary>
    public static void RebuildForLevel(Level? level, Func<Mob?, bool> includeMob, int identityToken, bool isHostAuthoritative)
    {
        if (includeMob == null)
            throw new ArgumentNullException(nameof(includeMob));

        lock (Sync)
        {
            var candidateMobCount = 0;
            var preservedIdCount = 0;
            var newlyAssignedIdCount = 0;
            var sameIdentity = identityToken > 0 && currentIdentityToken > 0 && identityToken == currentIdentityToken;
            var previousAssignments = new Dictionary<Mob, int>(MobToId, ReferenceEqualityComparer.Instance);
            var previousIdToMob = new Dictionary<int, Mob>(IdToMob);
            ClearLocked(resetIdentityState: !sameIdentity);
            currentLevel = level;
            currentIdentityToken = identityToken > 0 ? identityToken : 0;
            if (level == null || level.entities == null)
                return;

            var entities = level.entities;
            for (int i = 0; i < entities.length; i++)
            {
                var mob = entities.getDyn(i) as Mob;
                if (mob == null || !includeMob(mob))
                    continue;

                candidateMobCount++;
                if (previousAssignments.TryGetValue(mob, out var existingSyncId) &&
                    existingSyncId >= 0 &&
                    !IdToMob.ContainsKey(existingSyncId))
                {
                    MobToId[mob] = existingSyncId;
                    IdToMob[existingSyncId] = mob;
                    IssuedIdsForIdentity.Add(existingSyncId);
                    if (existingSyncId >= nextRuntimeSyncId)
                        nextRuntimeSyncId = existingSyncId + 1;
                    preservedIdCount++;
                    continue;
                }

                var assignedId = AllocateNextSyncIdLocked();
                if (previousIdToMob.TryGetValue(assignedId, out var previousMob) &&
                    previousMob != null &&
                    !ReferenceEquals(previousMob, mob))
                {
                    Log.Warning(
                        "[MobSync] syncId reuse detected identityToken={IdentityToken} syncId={SyncId} previousMob={PreviousMob} newMob={NewMob}",
                        currentIdentityToken,
                        assignedId,
                        DescribeMob(previousMob),
                        DescribeMob(mob));
                }

                MobToId[mob] = assignedId;
                IdToMob[assignedId] = mob;
                IssuedIdsForIdentity.Add(assignedId);
                newlyAssignedIdCount++;
            }

            Log.Information(
                "[MobSync] rebuild invariant summary role={Role} levelId={LevelId} identityToken={IdentityToken} candidateMobs={CandidateMobs} preservedIds={PreservedIds} newlyAssignedIds={NewlyAssignedIds} skippedClientMobs={SkippedClientMobs} registryCount={RegistryCount}",
                isHostAuthoritative ? "host" : "client",
                GetLevelIdSafe(level),
                currentIdentityToken,
                candidateMobCount,
                preservedIdCount,
                newlyAssignedIdCount,
                0,
                IdToMob.Count);
        }
    }

    public static void ClearForLevel(Level? level)
    {
        lock (Sync)
        {
            if (level != null && currentLevel != null && !ReferenceEquals(level, currentLevel))
                return;
            if (currentIdentityToken > 0 && level == null)
            {
                Log.Warning(
                    "[MobSync] registry cleared while identity still active identityToken={IdentityToken}",
                    currentIdentityToken);
            }

            ClearLocked(resetIdentityState: true);
        }
    }

    public static bool TryGetSyncId(Mob? mob, out int syncId, bool isHostAuthoritative)
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

            if (!isHostAuthoritative)
            {
                Log.Warning(
                    "[MobSync] client-side attempted syncId creation identityToken={IdentityToken} mob={Mob}",
                    currentIdentityToken,
                    DescribeMob(mob));
                return false;
            }

            var assignedId = AllocateNextSyncIdLocked();
            if (IssuedIdsForIdentity.Contains(assignedId))
            {
                Log.Warning(
                    "[MobSync] syncId reuse detected identityToken={IdentityToken} syncId={SyncId} mob={Mob}",
                    currentIdentityToken,
                    assignedId,
                    DescribeMob(mob));
                return false;
            }

            IssuedIdsForIdentity.Add(assignedId);
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
            if (mob != null && MobToId.TryGetValue(mob, out var oldId) && oldId != syncId)
            {
                Log.Warning(
                    "[MobSync] syncId remap attempt blocked identityToken={IdentityToken} mob={Mob} oldSyncId={OldSyncId} newSyncId={NewSyncId}",
                    currentIdentityToken,
                    DescribeMob(mob),
                    oldId,
                    syncId);
                return;
            }

            if (IdToMob.TryGetValue(syncId, out var oldMob) &&
                oldMob != null &&
                !ReferenceEquals(oldMob, mob))
            {
                Log.Warning(
                    "[MobSync] syncId assigned to different mob identityToken={IdentityToken} syncId={SyncId} previousMob={PreviousMob} attemptedMob={AttemptedMob}",
                    currentIdentityToken,
                    syncId,
                    DescribeMob(oldMob),
                    DescribeMob(mob));
                return;
            }

            if (mob != null)
            {
                MobToId[mob] = syncId;
                IdToMob[syncId] = mob;
            }

            IssuedIdsForIdentity.Add(syncId);
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

    public static Stats GetStats()
    {
        lock (Sync)
        {
            if (IdToMob.Count <= 0)
                return new Stats(0, -1, -1, nextRuntimeSyncId);

            var minSyncId = int.MaxValue;
            var maxSyncId = int.MinValue;
            foreach (var syncId in IdToMob.Keys)
            {
                if (syncId < minSyncId)
                    minSyncId = syncId;
                if (syncId > maxSyncId)
                    maxSyncId = syncId;
            }

            if (minSyncId == int.MaxValue)
            {
                minSyncId = -1;
                maxSyncId = -1;
            }

            return new Stats(IdToMob.Count, minSyncId, maxSyncId, nextRuntimeSyncId);
        }
    }

    public static List<DebugMapping> GetDebugMappings()
    {
        lock (Sync)
        {
            var snapshot = new List<DebugMapping>(IdToMob.Count);
            foreach (var pair in IdToMob)
            {
                if (pair.Value == null)
                    continue;

                snapshot.Add(new DebugMapping(pair.Key, pair.Value));
            }

            return snapshot;
        }
    }

    private static int AllocateNextSyncIdLocked()
    {
        while (IssuedIdsForIdentity.Contains(nextRuntimeSyncId))
            nextRuntimeSyncId++;

        return nextRuntimeSyncId++;
    }

    private static void ClearLocked(bool resetIdentityState)
    {
        IdToMob.Clear();
        MobToId.Clear();
        currentLevel = null;
        if (resetIdentityState)
        {
            currentIdentityToken = 0;
            nextRuntimeSyncId = 0;
            IssuedIdsForIdentity.Clear();
        }
    }

    private static string DescribeMob(Mob? mob)
    {
        if (mob == null)
            return "null";

        try
        {
            return mob.GetType().FullName ?? mob.GetType().Name;
        }
        catch
        {
            return "unknown";
        }
    }

    private static string GetLevelIdSafe(Level? level)
    {
        if (level == null)
            return string.Empty;

        try
        {
            return level.map?.id?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
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
