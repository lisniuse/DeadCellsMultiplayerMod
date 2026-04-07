using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using dc;
using dc.en;
using dc.h2d;
using dc.libs.heaps.slib;
using dc.libs.heaps.slib._AnimManager;
using dc.pr;
using dc.tool.atk;
using dc.tool.skill;
using DeadCellsMultiplayerMod.Interface.ModuleInitializing;
using DeadCellsMultiplayerMod.Mobs.Bosses;
using DeadCellsMultiplayerMod.Mobs.Levelinit;
using Hashlink.Virtuals;
using ModCore.Events;
using ModCore.Events.Interfaces.Game;
using ModCore.Utilities;
using Serilog;

namespace DeadCellsMultiplayerMod.Mobs.MobsSynchronization
{
    public partial class MobsSynchronization :
    IOnAdvancedModuleInitializing,
    IOnFrameUpdate,
    IEventReceiver
    {

        private static void TrySendHostMobStateDeltaBatchPreUpdate(NetNode net)
        {
            if (!IsHost(net))
                return;

            var trackedMobCount = 0;
            lock (Sync)
            {
                trackedMobCount = trackedMobs.Count;
            }

            if (trackedMobCount <= 0)
                return;

            var now = Stopwatch.GetTimestamp();

            if (!TryCaptureTrackedMobsForBatch(out trackedMobCount))
                return;

            s_batchSnapshotsScratch.Clear();
            for (int i = 0; i < s_batchMobsScratch.Count; i++)
            {
                var mob = s_batchMobsScratch[i];
                if (!TryGetMobSyncId(mob, out var mobSyncId) || mobSyncId < 0)
                    continue;
                if (!IsMobOnScreenForSync(mob) && !ShouldForceHostOffScreenMobStateForHpOrDie(mob, mobSyncId))
                    continue;
                var priority = GetHostMobSyncPriority(mob);
                var forceAnimTransitionSend = ShouldForceHostAnimStateSend(mob, mobSyncId, out var prebuiltAnim);
                if (!ShouldEvaluateMobBySyncId(
                        hostLastStateEvalTickBySyncId,
                        mobSyncId,
                        now,
                        forceAnimTransitionSend ? 0.0 : GetHostMobStateEvalSeconds(priority, trackedMobCount)))
                {
                    continue;
                }

                if (TryBuildHostMobStateDeltaSnapshot(mob, mobSyncId, now, forceAnimTransitionSend, out var snapshot, priority, prebuiltAnim))
                    s_batchSnapshotsScratch.Add(snapshot);
            }

            // Mob sync encoding is in-process.
            if (s_batchSnapshotsScratch.Count > 0)
            {
                MobSyncTrace.LogSendStatesBatch("host", s_batchSnapshotsScratch);
                net.SendMobStates(s_batchSnapshotsScratch);
            }
        }

        private static void TrySendClientMobBatchesNetFrame(NetNode net, long now)
        {
            if (!IsClient(net))
                return;

            var keepAliveSeconds = ClientDrawKeepAliveSeconds;
            s_drawsScratch.Clear();
            s_batchSnapshotsScratch.Clear();

            for (int i = 0; i < s_batchMobsScratch.Count; i++)
            {
                var mob = s_batchMobsScratch[i];
                if (mob == null)
                    continue;
                if (!TryGetMobSyncId(mob, out var mobSyncId) || mobSyncId < 0)
                    continue;

                GetClientMobDrawAndAffectEvalSeconds(mob, out var drawEvalSec, out var affectEvalSec);

                if (ShouldEvaluateMobBySyncId(
                        clientLastDrawEvalTickBySyncId,
                        mobSyncId,
                        now,
                        drawEvalSec))
                {
                    try
                    {
                        var isOutOfGame = mob.isOutOfGame;
                        var isOnScreen = mob.isOnScreen;
                        var shouldSendDraw = false;
                        lock (Sync)
                        {
                            var changed = !clientLastSentDrawStateBySyncId.TryGetValue(mobSyncId, out var lastDraw) ||
                                          lastDraw.IsOutOfGame != isOutOfGame ||
                                          lastDraw.IsOnScreen != isOnScreen;
                            var periodicRefresh = !isOutOfGame &&
                                                  (!clientLastSentDrawStateBySyncId.TryGetValue(mobSyncId, out var lastActiveDraw) ||
                                                   ElapsedSeconds(lastActiveDraw.Tick, now) >= keepAliveSeconds);
                            if (changed || periodicRefresh)
                            {
                                clientLastSentDrawStateBySyncId[mobSyncId] = new ClientDrawSentState(isOutOfGame, isOnScreen, now);
                                shouldSendDraw = true;
                            }
                        }

                        if (shouldSendDraw)
                            s_drawsScratch.Add(new NetNode.MobDraw(net.id, mobSyncId, isOutOfGame, isOnScreen));
                    }
                    catch
                    {
                    }
                }

                if (!IsMobOnScreenForSync(mob) && !ShouldForceClientOffScreenMobStateForHpOrDie(mob))
                    continue;

                if (!ShouldEvaluateMobBySyncId(
                        clientLastAffectEvalTickBySyncId,
                        mobSyncId,
                        now,
                        affectEvalSec))
                {
                    continue;
                }

                var statePayload = GetClientAffectPayloadForSend(mob, mobSyncId, now);
                var shouldSendAffect = false;

                lock (Sync)
                {
                    var changed = !clientLastSentAffectPayloadBySyncId.TryGetValue(mobSyncId, out var lastPayload) ||
                                  !string.Equals(lastPayload, statePayload, StringComparison.Ordinal);
                    if (changed)
                    {
                        clientLastSentAffectPayloadBySyncId[mobSyncId] = statePayload;
                        clientLastSentAffectTickBySyncId[mobSyncId] = now;
                        shouldSendAffect = true;
                    }
                }

                if (!shouldSendAffect)
                    continue;

                s_batchSnapshotsScratch.Add(new NetNode.MobStateSnapshot(
                    mobSyncId,
                    0.0,
                    0.0,
                    0,
                    0,
                    0,
                    string.Empty,
                    string.Empty,
                    statePayload));
            }

            if (s_drawsScratch.Count > 0)
            {
                MobSyncTrace.LogSendDrawBatch("client", s_drawsScratch);
                net.SendMobDrawBatch(s_drawsScratch);
            }

            if (s_batchSnapshotsScratch.Count > 0)
            {
                MobSyncTrace.LogSendStatesBatch("client", s_batchSnapshotsScratch);
                net.SendMobStates(s_batchSnapshotsScratch);
            }

            s_drawsScratch.Clear();
        }

        private static bool IsMobOnScreenForSync(Mob mob)
        {
            if (mob == null)
                return false;

            var hasVisibility = TryGetMobVisibilityState(mob, out var isOnScreen, out _, out _);
            if (hasVisibility && isOnScreen)
                return true;

            if (IsHost(GameMenu.NetRef) && TryGetMobSyncId(mob, out var mobSyncId) && mobSyncId >= 0)
            {
                var now = Stopwatch.GetTimestamp();
                if (HasActiveHostClientVisibilityLease(mobSyncId, now, pruneExpired: true))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// When a mob is off-screen we still must push state for HP changes and death (host → clients).
        /// </summary>
        private static bool ShouldForceHostOffScreenMobStateForHpOrDie(Mob mob, int mobSyncId)
        {
            if (mob == null)
                return false;

            int life;
            int maxLife;
            try
            {
                life = mob.life;
                maxLife = mob.maxLife;
            }
            catch
            {
                return false;
            }

            if (life <= 0)
                return true;

            if (maxLife > 0 && life < maxLife)
                return true;

            lock (Sync)
            {
                if (hostLastSentMobStatesBySyncId.TryGetValue(mobSyncId, out var prev) && prev.Life != life)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Client → host affect payloads must still send when HP/death matters while off-screen.
        /// </summary>
        private static bool ShouldForceClientOffScreenMobStateForHpOrDie(Mob mob)
        {
            if (mob == null)
                return false;

            try
            {
                if (mob.life <= 0)
                    return true;

                var max = mob.maxLife;
                if (max <= 0)
                    return false;

                return mob.life < max;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasActiveHostClientVisibilityLease(int mobSyncId, long nowTick, bool pruneExpired)
        {
            if (mobSyncId < 0)
                return false;

            lock (Sync)
            {
                if (!hostClientVisibleUntilTickBySyncId.TryGetValue(mobSyncId, out var visibleUntilTick))
                    return false;

                if (nowTick <= visibleUntilTick)
                    return true;

                if (pruneExpired)
                    hostClientVisibleUntilTickBySyncId.Remove(mobSyncId);

                return false;
            }
        }

        private static void TryRecoverClientSyncMobLifeAfterLocalDamage(Mob? mob, int fallbackLife)
        {
            if (mob == null || mob.destroyed)
                return;
            if (BossSyncHelpers.IsBossMob(mob))
                return;

            var net = GameMenu.NetRef;
            if (!IsClient(net) || !IsSyncMob(mob))
                return;

            try
            {
                if (mob.life <= 0)
                {
                    // Was alive before this damage (fallbackLife > 0) and dead after — keep dead so lethal
                    // hit|0 can be sent and host can apply kill; do not resurrect here.
                    if (fallbackLife > 0)
                        return;

                    mob.life = System.Math.Max(1, fallbackLife);
                }
            }
            catch
            {
            }
        }

        private static void TryApplyHostClientVisibilityLease(Mob mob)
        {
            if (mob == null)
                return;
            if (!TryGetMobSyncId(mob, out var syncId) || syncId < 0)
                return;

            var now = Stopwatch.GetTimestamp();
            if (!HasActiveHostClientVisibilityLease(syncId, now, pruneExpired: true))
                return;

            try
            {
                var wasOutOfGame = mob.isOutOfGame;
                mob.isOnScreen = true;
                if (mob.onScreenRecent < 180.0)
                    mob.onScreenRecent = 180.0;
                mob.isOutOfGame = false;
                mob.lastOutOfGame = false;
                if (wasOutOfGame)
                    mob.onOutOfGameChange();
            }
            catch
            {
            }
        }

        private static bool TryBuildHostMobStateDeltaSnapshot(
            Mob mob,
            int mobSyncId,
            long nowTick,
            bool forcePayloadRefresh,
            out NetNode.MobStateSnapshot snapshot,
            HostMobSyncPriority? priorityHint = null,
            string? prebuiltAnimPayload = null)
        {
            snapshot = default;
            if (mob == null)
                return false;

            var x = GetWorldX(mob);
            var y = GetWorldY(mob);
            var dir = NormalizeDir(mob.dir);
            var life = mob.life;
            var maxLife = mob.maxLife;
            var animPayload = string.Empty;
            var mobType = string.Empty;
            var statePayload = string.Empty;

            HostMobSentState previous;
            var hadPrevious = false;
            var lastSentTick = 0L;
            CachedHostMobPayload cachedPayload = default;
            var hasCachedPayload = false;

            lock (Sync)
            {
                hadPrevious = hostLastSentMobStatesBySyncId.TryGetValue(mobSyncId, out previous);
                hostLastStateSentTickBySyncId.TryGetValue(mobSyncId, out lastSentTick);
                hasCachedPayload = hostCachedPayloadBySyncId.TryGetValue(mobSyncId, out cachedPayload);
            }

            var shouldRefreshPayload = forcePayloadRefresh ||
                                       !hasCachedPayload ||
                                       !hadPrevious ||
                                       ElapsedSeconds(cachedPayload.Tick, nowTick) >= HostPayloadRefreshSeconds;
            if (BossSyncHelpers.IsBossMob(mob))
                shouldRefreshPayload = true;

            if (shouldRefreshPayload)
            {
                animPayload = prebuiltAnimPayload ?? BuildAnimPayload(mob);
                mobType = BuildMobStateTypeSignature(mob);
                statePayload = BuildHostMobStatePayload(mob);
                cachedPayload = new CachedHostMobPayload(animPayload, mobType, statePayload, nowTick);
                hasCachedPayload = true;
                lock (Sync)
                {
                    hostCachedPayloadBySyncId[mobSyncId] = cachedPayload;
                }
            }
            else
            {
                animPayload = cachedPayload.AnimPayload;
                mobType = cachedPayload.Type;
                statePayload = cachedPayload.StatePayload;
            }

            var current = new HostMobSentState(x, y, dir, life, maxLife, animPayload, mobType, statePayload);
            var resolvedPriority = priorityHint ?? GetHostMobSyncPriority(mob);
            var positionEpsilon = GetHostStatePositionEpsilon(resolvedPriority);
            if (hadPrevious && HostMobSentStateEquals(previous, current, positionEpsilon))
            {
                if (lastSentTick != 0 && ElapsedSeconds(lastSentTick, nowTick) < HostUnchangedStateResendGateSeconds)
                    return false;
            }
            else if (resolvedPriority == HostMobSyncPriority.Dormant &&
                     hadPrevious &&
                     life == previous.Life &&
                     maxLife == previous.MaxLife &&
                     lastSentTick != 0 &&
                     ElapsedSeconds(lastSentTick, nowTick) < HostDormantDuplicateLifeMinSeconds)
            {
                return false;
            }

            lock (Sync)
            {
                hostLastSentMobStatesBySyncId[mobSyncId] = current;
                hostLastStateSentTickBySyncId[mobSyncId] = nowTick;
                if (!hasCachedPayload)
                    hostCachedPayloadBySyncId[mobSyncId] = new CachedHostMobPayload(animPayload, mobType, statePayload, nowTick);
            }

            var snapshotAnimPayload = hadPrevious &&
                                      string.Equals(previous.AnimPayload, animPayload, StringComparison.Ordinal)
                ? string.Empty
                : animPayload;
            var snapshotMobType = hadPrevious &&
                                  string.Equals(previous.Type, mobType, StringComparison.Ordinal)
                ? string.Empty
                : mobType;
            var snapshotStatePayload = hadPrevious &&
                                       string.Equals(previous.StatePayload, statePayload, StringComparison.Ordinal)
                ? string.Empty
                : statePayload;

            snapshot = new NetNode.MobStateSnapshot(
                mobSyncId,
                x,
                y,
                dir,
                life,
                maxLife,
                snapshotAnimPayload,
                snapshotMobType,
                snapshotStatePayload);
            return true;
        }

        private static bool ShouldForceHostAnimStateSend(Mob mob, int mobSyncId) =>
            ShouldForceHostAnimStateSend(mob, mobSyncId, out _);

        private static bool ShouldForceHostAnimStateSend(Mob mob, int mobSyncId, out string? currentAnimPayload)
        {
            currentAnimPayload = null;

            if (mob == null || mobSyncId < 0)
                return false;

            string cachedAnimPayload;
            lock (Sync)
            {
                if (!hostCachedPayloadBySyncId.TryGetValue(mobSyncId, out var cachedPayload))
                    return false;

                cachedAnimPayload = cachedPayload.AnimPayload ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(cachedAnimPayload))
                return false;

            var built = BuildAnimPayload(mob);
            if (string.IsNullOrWhiteSpace(built) ||
                string.Equals(built, cachedAnimPayload, StringComparison.Ordinal))
            {
                return false;
            }

            // Treat pure speed jitter as non-transitional; force only on group/reverse changes.
            if (TryParseAnimPayload(built, out var currentParsed) &&
                TryParseAnimPayload(cachedAnimPayload, out var cachedParsed))
            {
                if (string.Equals(currentParsed.Group, cachedParsed.Group, StringComparison.Ordinal) &&
                    currentParsed.Reverse == cachedParsed.Reverse)
                {
                    return false;
                }
            }

            currentAnimPayload = built;
            return true;
        }

        private static bool HostMobSentStateEquals(HostMobSentState a, HostMobSentState b, double positionEpsilon)
        {
            var posEpsilon = positionEpsilon <= 0.0 ? MobStatePositionEpsilon : positionEpsilon;
            return IsApproximatelyEqual(a.X, b.X, posEpsilon) &&
                   IsApproximatelyEqual(a.Y, b.Y, posEpsilon) &&
                   a.Dir == b.Dir &&
                   a.Life == b.Life &&
                   a.MaxLife == b.MaxLife &&
                   string.Equals(a.AnimPayload, b.AnimPayload, StringComparison.Ordinal) &&
                   string.Equals(a.Type, b.Type, StringComparison.Ordinal) &&
                   string.Equals(a.StatePayload, b.StatePayload, StringComparison.Ordinal);
        }

        private static void EnsurePlayerInterestPointsForFrame(Level? level)
        {
            if (level == null)
            {
                s_playerInterestPointsScratch.Clear();
                lastPlayerInterestLevel = null;
                lastPlayerInterestFrame = double.NaN;
                return;
            }

            double frame;
            try
            {
                frame = level.ftime;
            }
            catch
            {
                frame = double.NaN;
            }

            if (ReferenceEquals(lastPlayerInterestLevel, level) && lastPlayerInterestFrame == frame)
                return;

            s_playerInterestPointsScratch.Clear();
            lastPlayerInterestLevel = level;
            lastPlayerInterestFrame = frame;

            TryAddPlayerInterestPoint(level, ModEntry.me ?? ModCore.Modules.Game.Instance?.HeroInstance);

            for (int i = 0; i < ModEntry.clients.Length; i++)
                TryAddPlayerInterestPoint(level, ModEntry.clients[i]);
        }

        private static void TryAddPlayerInterestPoint(Level level, Entity? entity)
        {
            if (entity == null)
                return;
            if (ModEntry.IsEntityDownedForCombat(entity))
                return;

            try
            {
                if (entity.destroyed || entity.life <= 0)
                    return;
                if (entity._level != null && !ReferenceEquals(entity._level, level))
                    return;
            }
            catch
            {
                return;
            }

            for (int i = 0; i < s_playerInterestPointsScratch.Count; i++)
            {
                if (ReferenceEquals(s_playerInterestPointsScratch[i].Entity, entity))
                    return;
            }

            try
            {
                s_playerInterestPointsScratch.Add(new PlayerInterestPoint(entity, GetWorldX(entity), GetWorldY(entity)));
            }
            catch
            {
            }
        }

        private static bool TryGetNearestPlayerDistanceSq(Mob mob, out double distanceSq)
        {
            distanceSq = double.PositiveInfinity;
            if (mob == null)
                return false;

            Level? level;
            try
            {
                level = mob._level ?? currentLevel;
            }
            catch
            {
                level = currentLevel;
            }

            EnsurePlayerInterestPointsForFrame(level);
            if (s_playerInterestPointsScratch.Count == 0)
                return false;

            double frameKey;
            try
            {
                frameKey = level?.ftime ?? double.NaN;
            }
            catch
            {
                frameKey = double.NaN;
            }

            if (!double.IsNaN(frameKey) && !double.Equals(frameKey, s_distanceSqCacheFrameKey))
            {
                s_localIndexToNearestDistanceSq.Clear();
                s_distanceSqCacheFrameKey = frameKey;
            }

            if (TryGetTrackedIndex(mob, out var cacheIdx) &&
                s_localIndexToNearestDistanceSq.TryGetValue(cacheIdx, out var cachedSq))
            {
                distanceSq = cachedSq;
                return double.IsFinite(cachedSq);
            }

            double mx;
            double my;
            try
            {
                mx = GetWorldX(mob);
                my = GetWorldY(mob);
            }
            catch
            {
                return false;
            }

            var best = double.PositiveInfinity;
            for (int i = 0; i < s_playerInterestPointsScratch.Count; i++)
            {
                var point = s_playerInterestPointsScratch[i];
                var dx = point.X - mx;
                var dy = point.Y - my;
                var candidate = dx * dx + dy * dy;
                if (candidate < best)
                    best = candidate;
            }

            distanceSq = best;
            if (double.IsFinite(best) && TryGetTrackedIndex(mob, out var idxForCache))
                s_localIndexToNearestDistanceSq[idxForCache] = best;

            return double.IsFinite(best);
        }

        private static bool TryGetMobVisibilityState(Mob mob, out bool isOnScreen, out bool isOutOfGame, out double onScreenRecent)
        {
            isOnScreen = false;
            isOutOfGame = true;
            onScreenRecent = 0.0;
            if (mob == null)
                return false;

            try
            {
                isOnScreen = mob.isOnScreen;
                isOutOfGame = mob.isOutOfGame;
                onScreenRecent = mob.onScreenRecent;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryCanMobUpdate(Mob mob, out bool canUpdate)
        {
            canUpdate = false;
            if (mob == null)
                return false;

            try
            {
                canUpdate = mob.canUpdate();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static HostMobSyncPriority GetHostMobSyncPriority(Mob? mob)
        {
            if (mob == null)
                return HostMobSyncPriority.Dormant;
            if (BossSyncHelpers.IsBossMob(mob) || HasValidLivingPlayerCombatTarget(mob))
                return HostMobSyncPriority.Active;

            var hasDistance = TryGetNearestPlayerDistanceSq(mob, out var distanceSq);
            TryGetMobVisibilityState(mob, out var isOnScreen, out var isOutOfGame, out var onScreenRecent);

            if (isOnScreen || onScreenRecent > 0.0 || !isOutOfGame)
                return HostMobSyncPriority.Active;
            if (hasDistance && distanceSq <= MobSyncDistanceSq)
                return HostMobSyncPriority.Active;
            if (hasDistance)
                return HostMobSyncPriority.MidRange;

            return HostMobSyncPriority.FarRange;
        }

        private static double GetHostStatePositionEpsilon(Mob mob) =>
            GetHostStatePositionEpsilon(GetHostMobSyncPriority(mob));

        private static double GetHostStatePositionEpsilon(HostMobSyncPriority priority) =>
            priority switch
            {
                HostMobSyncPriority.Active => MobStatePositionEpsilon,
                HostMobSyncPriority.MidRange => HostMobStateMidPositionEpsilon,
                HostMobSyncPriority.FarRange => HostMobStateFarPositionEpsilon,
                _ => HostMobStateDormantPositionEpsilon
            };

        private static double GetHostMobStateEvalSeconds(Mob mob) =>
            GetHostMobStateEvalSeconds(GetHostMobSyncPriority(mob), trackedMobCount: -1);

        private static double GetHostMobStateEvalSeconds(HostMobSyncPriority priority, int trackedMobCount)
        {
            var seconds = priority switch
            {
                HostMobSyncPriority.Active => HostActiveStateEvalSeconds,
                HostMobSyncPriority.MidRange => HostFarStateEvalSeconds,
                HostMobSyncPriority.FarRange => HostDormantStateEvalSeconds,
                _ => HostDormantStateEvalSeconds * 1.6
            };

            if (seconds <= 0.0)
                return seconds;

            var count = trackedMobCount >= 0 ? trackedMobCount : TrackedMobCountLocked();
            if (count >= HostCrowdMobCountThreshold)
            {
                if (priority == HostMobSyncPriority.Active)
                    return seconds * HostCrowdActiveEvalStretchMultiplier;

                return seconds * HostCrowdEvalStretchMultiplier;
            }

            return seconds;
        }

        private static int TrackedMobCountLocked()
        {
            lock (Sync) { return trackedMobs.Count; }
        }

        private static void GetClientMobDrawAndAffectEvalSeconds(Mob? mob, out double drawSeconds, out double affectSeconds)
        {
            if (mob == null)
            {
                drawSeconds = ClientDormantDrawEvalSeconds;
                affectSeconds = ClientDormantAffectEvalSeconds;
                return;
            }

            if (BossSyncHelpers.IsBossMob(mob) || HasValidLivingPlayerCombatTarget(mob))
            {
                drawSeconds = 0.0;
                affectSeconds = 0.0;
                return;
            }

            TryGetMobVisibilityState(mob, out var isOnScreen, out var isOutOfGame, out var onScreenRecent);

            if (isOnScreen || onScreenRecent > 0.0 || !isOutOfGame)
                affectSeconds = 0.0;
            else
                affectSeconds = double.NaN;

            if (isOnScreen)
                drawSeconds = 0.0;
            else if (!isOutOfGame)
                drawSeconds = ClientFarDrawEvalSeconds;
            else
                drawSeconds = double.NaN;

            if (!double.IsNaN(affectSeconds) && !double.IsNaN(drawSeconds))
                return;

            var hasDistance = TryGetNearestPlayerDistanceSq(mob, out var distanceSq);

            if (double.IsNaN(affectSeconds))
            {
                if (hasDistance && distanceSq <= MobSyncDistanceSq)
                    affectSeconds = 0.0;
                else if (hasDistance)
                    affectSeconds = ClientFarAffectEvalSeconds;
                else
                    affectSeconds = ClientDormantAffectEvalSeconds * 1.6;
            }

            if (double.IsNaN(drawSeconds))
            {
                if (hasDistance && distanceSq <= MobDrawNearDistanceSq)
                    drawSeconds = 0.0;
                else if (hasDistance && distanceSq <= MobSyncDistanceSq)
                    drawSeconds = ClientFarDrawEvalSeconds;
                else
                    drawSeconds = ClientDormantDrawEvalSeconds * 1.35;
            }
        }

        private static bool ShouldEvaluateMobBySyncId(
            Dictionary<int, long> lastEvalTickBySyncId,
            int syncId,
            long nowTick,
            double intervalSeconds)
        {
            if (syncId < 0)
                return false;

            if (intervalSeconds <= 0.0)
            {
                lock (Sync)
                {
                    lastEvalTickBySyncId.Remove(syncId);
                }

                return true;
            }

            lock (Sync)
            {
                if (lastEvalTickBySyncId.TryGetValue(syncId, out var lastTick) &&
                    ElapsedSeconds(lastTick, nowTick) < intervalSeconds)
                    return false;

                lastEvalTickBySyncId[syncId] = nowTick;
                return true;
            }
        }

        private static string GetClientAffectPayloadForSend(Mob mob, int mobSyncId, long nowTick)
        {
            lock (Sync)
            {
                if (clientAffectSampleBySyncId.TryGetValue(mobSyncId, out var cached) &&
                    ElapsedSeconds(cached.Tick, nowTick) < ClientAffectSampleSeconds)
                {
                    return cached.Payload;
                }
            }

            // Client->host affect sync sends presence only; duration ticks are too noisy and create packet spam.
            var payload = BuildMobAffectPresencePayload(mob);
            lock (Sync)
            {
                clientAffectSampleBySyncId[mobSyncId] = new TimedStringPayload(payload, nowTick);
            }

            return payload;
        }

        private static string BuildHostMobStatePayload(Mob mob)
        {
            if (mob == null)
                return string.Empty;

            var presencePayload = BuildMobAffectPresencePayload(mob);
            return BossStateSync.AppendBossState(presencePayload, mob);
        }

        private static bool IsApproximatelyEqual(double a, double b, double epsilon)
        {
            return System.Math.Abs(a - b) <= epsilon;
        }

        private static bool TryCaptureTrackedMobsForBatch(out int trackedMobCount)
        {
            lock (Sync)
            {
                PruneInvalidTrackedMobsLocked();
                trackedMobCount = trackedMobs.Count;
                s_batchMobsScratch.Clear();
                if (trackedMobCount <= 0)
                    return false;

                s_batchMobsScratch.AddRange(trackedMobs);
                return true;
            }
        }

        private static string BuildMobAffectStatePayload(Mob mob, bool includeBossStateForHost = false)
        {
            if (mob == null)
                return string.Empty;
            if (BossSyncHelpers.IsBossMob(mob))
                return includeBossStateForHost ? BossStateSync.AppendBossState(string.Empty, mob) : string.Empty;

            try
            {
                var affects = mob.getAllAffects();
                if (affects == null || affects.length <= 0)
                    return string.Empty;

                StringBuilder? builder = null;
                for (int i = 0; i < affects.length; i++)
                {
                    var affectList = affects.getDyn(i);
                    var affectCount = TryGetDynLength(affectList);
                    if (affectCount <= 0)
                        continue;

                    var maxFrames = 0;
                    for (int j = 0; j < affectCount; j++)
                    {
                        var affect = TryGetDynAffectEntry(affectList, j);
                        if (affect == null)
                            continue;

                        var frames = NormalizeAffectFrames(affect.t);
                        if (frames > maxFrames)
                            maxFrames = frames;
                    }

                    if (maxFrames <= 0)
                        maxFrames = ClientAffectSyncDefaultFrames;

                    builder ??= new StringBuilder(affects.length * 6);
                    if (builder.Length > 0)
                        builder.Append('.');

                    builder.Append(i.ToString(CultureInfo.InvariantCulture));
                    builder.Append(':');
                    builder.Append(maxFrames.ToString(CultureInfo.InvariantCulture));
                }

                if (builder == null || builder.Length == 0)
                    return string.Empty;

                var basePayload = builder.ToString();
                return includeBossStateForHost ? BossStateSync.AppendBossState(basePayload, mob) : basePayload;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string BuildMobAffectPresencePayload(Mob mob)
        {
            if (mob == null)
                return string.Empty;

            try
            {
                var affects = mob.getAllAffects();
                if (affects == null || affects.length <= 0)
                    return string.Empty;

                StringBuilder? builder = null;
                for (int i = 0; i < affects.length; i++)
                {
                    if (TryGetDynLength(affects.getDyn(i)) <= 0)
                        continue;

                    builder ??= new StringBuilder(affects.length * 3);
                    if (builder.Length > 0)
                        builder.Append('.');

                    builder.Append(i.ToString(CultureInfo.InvariantCulture));
                }

                return builder?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ExtractAffectPresenceSignature(string? payload)
        {
            var parsed = ParseAffectStatePayload(payload);
            if (parsed.Count == 0)
                return string.Empty;

            var ids = new List<int>(parsed.Count);
            foreach (var key in parsed.Keys)
                ids.Add(key);
            ids.Sort();
            return string.Join(".", ids);
        }

        private void Hook_Mob_contactAttack(Hook_Mob.orig_contactAttack orig, Mob self, Entity pow)
        {
            var net = GameMenu.NetRef;
            if (IsHost(net) && ModEntry.IsLocalPlayerDowned() && IsPlayerCombatTargetEntity(pow))
                return;

            orig(self, pow);

            if (!IsHost(net) || !IsPlayerCombatTargetEntity(pow))
                return;

            if (TryGetTrackedIndex(self, out var mobIndex) && ShouldSendHostContactPacket(mobIndex))
                TrySendHostMobAttack(self, ContactAttackPacketSkillId, false, null, pow);
        }

        private void Hook_Mob_onTouch(Hook_Mob.orig_onTouch orig, Mob self, Entity atk)
        {
            var net = GameMenu.NetRef;
            if (IsHost(net) && ModEntry.IsLocalPlayerDowned() && IsPlayerCombatTargetEntity(atk))
                return;

            orig(self, atk);

            if (!IsHost(net) || !IsSyncMob(self) || !IsPlayerCombatTargetEntity(atk))
                return;

            EnsureMobTracked(self);
            if (TryGetTrackedIndex(self, out var mobIndex) && ShouldSendHostContactPacket(mobIndex))
                TrySendHostMobAttack(self, ContactAttackPacketSkillId, false, null, atk);
        }

        private void Hook_OldMobSkill_execute(Hook_OldMobSkill.orig_execute orig, OldMobSkill self, double? a)
        {
            orig(self, a);

            var net = GameMenu.NetRef;
            var ownerMob = self?.owner as Mob;
            var skillId = self?.id?.ToString() ?? string.Empty;

            if (ownerMob == null || string.IsNullOrWhiteSpace(skillId))
                return;

            if (IsClient(net))
            {
                RegisterClientQueuedOldSkillMarker(ownerMob, skillId);
                return;
            }

            if (!IsHost(net))
                return;

            TrySendHostMobAttack(ownerMob, OldSkillChargeCompletePacketPrefix + skillId, false, null);
        }

        private bool Hook_OldSkill_prepare(Hook_OldSkill.orig_prepare orig, OldSkill self, int? data)
        {
            var prepared = false;
            try
            {
                prepared = orig(self, data);
            }
            catch
            {
                return false;
            }

            if (!prepared || self is OldMobSkill)
                return prepared;

            var net = GameMenu.NetRef;
            if (!IsHost(net))
                return true;

            var ownerMob = self?.owner as Mob;
            var skillId = self?.id?.ToString() ?? string.Empty;
            if (ownerMob == null || string.IsNullOrWhiteSpace(skillId))
                return true;

            Entity? explicitTarget = null;
            try { explicitTarget = ownerMob.aTarget; } catch { }
            TrySendHostMobAttack(ownerMob, OldSkillPreparePacketPrefix + skillId, false, data, explicitTarget);
            return true;
        }

        private void Hook_OldSkill_execute(Hook_OldSkill.orig_execute orig, OldSkill self, double? ratio)
        {
            orig(self, ratio);

            if (self is OldMobSkill)
                return;

            var net = GameMenu.NetRef;
            var ownerMob = self?.owner as Mob;
            var skillId = self?.id?.ToString() ?? string.Empty;

            if (ownerMob == null || string.IsNullOrWhiteSpace(skillId))
                return;

            if (IsClient(net))
            {
                RegisterClientQueuedOldSkillMarker(ownerMob, skillId);
                return;
            }

            if (!IsHost(net))
                return;

            TrySendHostMobAttack(ownerMob, OldSkillExecutePacketPrefix + skillId, false, null);
        }

        private bool Hook_OldMobSkill_prepareOnOwnerTarget(Hook_OldMobSkill.orig_prepareOnOwnerTarget orig, OldMobSkill self, bool? data, int? e)
        {
            var prepared = false;
            try
            {
                prepared = orig(self, data, e);
            }
            catch
            {
                return false;
            }

            if (!prepared)
                return false;

            var net = GameMenu.NetRef;
            if (!IsHost(net))
                return true;

            var ownerMob = self?.owner as Mob;
            var skillId = self?.id?.ToString() ?? string.Empty;
            if (ownerMob == null || string.IsNullOrWhiteSpace(skillId))
                return true;

            Entity? explicitTarget = null;
            try { explicitTarget = ownerMob.aTarget; } catch { }
            TrySendHostMobAttack(ownerMob, OldSkillPreparePacketPrefix + skillId, false, e, explicitTarget);
            return true;
        }

        private void Hook_Mob_queueAttack(Hook_Mob.orig_queueAttack orig, Mob self, OldMobSkill a, bool requiresTargetInArea, int? data)
        {
            var net = GameMenu.NetRef;
            if (IsClient(net) && IsSyncMob(self) && !IsClientNetworkQueuedAttackAllowed(self))
                return;

            orig(self, a, requiresTargetInArea, data);

            if (self == null || a == null)
                return;

            if (!IsHost(net))
                return;

            var skillId = a.id?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(skillId))
                return;

            EnsureMobTracked(self);
            if (!TryGetTrackedIndex(self, out var mobIndex))
                return;

            lock (Sync)
            {
                hostQueuedOldSkillMarkers[mobIndex] = new QueuedOldSkillMarker(skillId, Stopwatch.GetTimestamp());
            }

            TrySendHostMobAttack(self, skillId, requiresTargetInArea, data);
        }

        private static bool IsClientNetworkQueuedAttackAllowed(Mob? mob)
        {
            if (mob == null)
                return false;

            return clientNetworkQueuedAttackDepth > 0 &&
                   clientNetworkQueuedAttackMob != null &&
                   ReferenceEquals(clientNetworkQueuedAttackMob, mob);
        }

        private static void WithClientNetworkQueuedAttackContext(Mob mob, Action action)
        {
            if (mob == null || action == null)
                return;

            var previousMob = clientNetworkQueuedAttackMob;
            clientNetworkQueuedAttackMob = mob;
            clientNetworkQueuedAttackDepth++;
            try
            {
                action();
            }
            finally
            {
                clientNetworkQueuedAttackDepth--;
                clientNetworkQueuedAttackMob = previousMob;
            }
        }

        private void Hook_MobSkill_execute(Hook_MobSkill.orig_execute orig, MobSkill self, double? ratio)
        {
            orig(self, ratio);

            var net = GameMenu.NetRef;
            if (!IsHost(net))
                return;

            var ownerMob = self?.owner as Mob;
            var skillId = self?.id?.ToString() ?? string.Empty;
            
            if (ownerMob != null && !string.IsNullOrWhiteSpace(skillId))
                TrySendHostMobAttack(ownerMob, NewSkillExecutePacketPrefix + skillId, false, null);
        }

    }
}
