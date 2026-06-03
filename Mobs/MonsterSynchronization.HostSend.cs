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
        private static bool IsMobOnScreenForSync(Mob mob)
        {
            if (mob == null)
                return false;

            var hasVisibility = TryGetMobVisibilityState(mob, out var isOnScreen, out _, out _);
            if (hasVisibility && isOnScreen)
                return true;

            if (IsHost(GameMenu.NetRef) && TryGetMobSyncId(mob, out var mobSyncId) && mobSyncId >= 0 &&
                IsMobClientVisibleForSync(mobSyncId))
                return true;

            return false;
        }

        /// <summary>
        /// When a mob is off-screen we still must push state for HP changes and death (host → clients).
        /// </summary>
        private static bool IsMobClientVisibleForSync(int mobSyncId)
        {
            if (mobSyncId < 0)
                return false;

            lock (Sync)
            {
                return hostClientInterestUsersBySyncId.TryGetValue(mobSyncId, out var users) &&
                       users != null &&
                       users.Count > 0;
            }
        }

        /// <summary>
        /// Client → host affect payloads must still send when HP/death matters while off-screen.
        /// </summary>
        private static void SetHostClientInterestLocked(int mobSyncId, int userId, bool isInterested)
        {
            if (mobSyncId < 0 || userId <= 0)
                return;

            if (!isInterested)
            {
                if (!hostClientInterestUsersBySyncId.TryGetValue(mobSyncId, out var existing))
                    return;

                existing.Remove(userId);
                if (existing.Count <= 0)
                    hostClientInterestUsersBySyncId.Remove(mobSyncId);
                return;
            }

            if (!hostClientInterestUsersBySyncId.TryGetValue(mobSyncId, out var users) || users == null)
            {
                users = new HashSet<int>();
                hostClientInterestUsersBySyncId[mobSyncId] = users;
            }

            users.Add(userId);
        }

        private static void ClearHostClientInterestLocked()
        {
            foreach (var users in hostClientInterestUsersBySyncId.Values)
                users?.Clear();

            hostClientInterestUsersBySyncId.Clear();
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

        private static void TryApplyHostClientVisibilityInterest(Mob mob)
        {
            if (mob == null)
                return;
            if (!TryGetMobSyncId(mob, out var syncId) || syncId < 0)
                return;

            if (!IsMobClientVisibleForSync(syncId))
                return;

            PromoteMobToSyncVisibleState(mob);
        }

        private static void PromoteMobToSyncVisibleState(Mob mob)
        {
            if (mob == null)
                return;

            try
            {
                var wasOutOfGame = mob.isOutOfGame;
                mob.isOnScreen = true;
                if (mob.onScreenRecent < 1.0)
                    mob.onScreenRecent = 1.0;
                mob.isOutOfGame = false;
                mob.lastOutOfGame = false;
                if (wasOutOfGame)
                    mob.onOutOfGameChange();
            }
            catch
            {
            }
        }

        private static bool TryBuildHostMobDeltaSnapshot(
            Mob mob,
            int mobSyncId,
            bool forceFullState,
            out bool sendStateSnapshot,
            out NetNode.MobStateSnapshot stateSnapshot,
            out NetNode.MobMoveSnapshot moveSnapshot,
            HostMobSyncPriority? priorityHint = null,
            string? prebuiltAnimPayload = null)
        {
            sendStateSnapshot = true;
            stateSnapshot = default;
            moveSnapshot = default;
            if (mob == null)
                return false;

            if (!TryGetCurrentLevelIdentityToken(out var identityToken))
                return false;

            var x = GetWorldX(mob);
            var y = GetWorldY(mob);
            var dir = NormalizeDir(mob.dir);
            var life = mob.life;
            var maxLife = mob.maxLife;
            var animPayload = string.Empty;
            var mobType = string.Empty;
            var statePayload = string.Empty;
            HostMobObservedState observed = default;
            var hasObserved = false;
            HostMobSentState previous;
            var hadPrevious = false;

            lock (Sync)
            {
                hasObserved = hostObservedMobStatesBySyncId.TryGetValue(mobSyncId, out observed);
                hadPrevious = hostLastSentMobStatesBySyncId.TryGetValue(mobSyncId, out previous);
            }

            if (hasObserved)
            {
                animPayload = prebuiltAnimPayload ?? observed.AnimPayload;
                mobType = observed.MobType;
                statePayload = observed.StatePayload;
            }
            else
            {
                animPayload = prebuiltAnimPayload ?? BuildAnimPayload(mob);
                mobType = BuildMobStateTypeSignature(mob);
                statePayload = BuildHostMobStatePayload(mob);
            }

            var current = new HostMobSentState(x, y, dir, life, maxLife, animPayload, mobType, statePayload);
            var resolvedPriority = priorityHint ?? GetHostMobSyncPriority(mob);
            var positionEpsilon = GetHostStatePositionEpsilon(resolvedPriority);
            var lifeChanged = !hadPrevious || previous.Life != life || previous.MaxLife != maxLife;
            var payloadChanged = !hadPrevious ||
                                 !string.Equals(previous.Type, mobType, StringComparison.Ordinal) ||
                                 !string.Equals(previous.StatePayload, statePayload, StringComparison.Ordinal);
            var animChanged = !hadPrevious ||
                              !string.Equals(previous.AnimPayload, animPayload, StringComparison.Ordinal);
            var positionChanged = !hadPrevious ||
                                  !IsApproximatelyEqual(previous.X, x, positionEpsilon) ||
                                  !IsApproximatelyEqual(previous.Y, y, positionEpsilon) ||
                                  previous.Dir != dir;

            if (!forceFullState && hadPrevious && !lifeChanged && !payloadChanged && !animChanged && !positionChanged)
            {
                return false;
            }

            lock (Sync)
            {
                hostLastSentMobStatesBySyncId[mobSyncId] = current;
            }

            if (!forceFullState && hadPrevious && !lifeChanged && !payloadChanged && (positionChanged || animChanged))
            {
                sendStateSnapshot = false;
                moveSnapshot = new NetNode.MobMoveSnapshot(
                    mobSyncId,
                    x,
                    y,
                    dir,
                    animChanged ? animPayload : string.Empty,
                    identityToken);
                return true;
            }

            var snapshotAnimPayload = forceFullState
                ? animPayload
                : hadPrevious && !animChanged ? string.Empty : animPayload;
            var snapshotMobType = forceFullState
                ? mobType
                : hadPrevious &&
                                  string.Equals(previous.Type, mobType, StringComparison.Ordinal)
                ? string.Empty
                : mobType;
            var snapshotStatePayload = forceFullState
                ? EncodeStatePayloadForWire(statePayload)
                : hadPrevious &&
                                       string.Equals(previous.StatePayload, statePayload, StringComparison.Ordinal)
                ? string.Empty
                : EncodeStatePayloadForWire(statePayload);

            stateSnapshot = new NetNode.MobStateSnapshot(
                mobSyncId,
                x,
                y,
                dir,
                life,
                maxLife,
                snapshotAnimPayload,
                snapshotMobType,
                snapshotStatePayload,
                identityToken);
            return true;
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

        private static void TrySendHostMobSpawn(Mob mob, int syncId)
        {
            var net = GameMenu.NetRef;
            if (!IsHost(net) || mob == null || syncId < 0)
                return;
            if (!TryGetCurrentLevelIdentityToken(out var identityToken))
                return;

            try
            {
                var spawn = new NetNode.MobSpawnSnapshot(
                    syncId,
                    GetWorldX(mob),
                    GetWorldY(mob),
                    NormalizeDir(mob.dir),
                    mob.life,
                    mob.maxLife,
                    BuildMobStateTypeSignature(mob),
                    identityToken);
                net?.SendMobSpawns(new[] { spawn });

                if (MobSyncTrace.Enabled)
                {
                    Log.Information(
                        "[MobSync] -> SEND spawn host syncId={SyncId} type={MobType} x={X} y={Y} gen={Generation}",
                        spawn.Index,
                        spawn.Type,
                        spawn.X,
                        spawn.Y,
                        spawn.Generation);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[MobsSync] Host mob spawn send failed syncId={SyncId}", syncId);
            }
        }

        private static HostMobSyncPriority GetHostMobSyncPriority(Mob? mob)
        {
            if (mob == null)
                return HostMobSyncPriority.Dormant;
            if (BossSyncHelpers.IsBossMob(mob) || HasValidLivingPlayerCombatTarget(mob))
                return HostMobSyncPriority.Active;

            if (TryGetMobSyncId(mob, out var syncId) && syncId >= 0 && IsMobClientVisibleForSync(syncId))
                return HostMobSyncPriority.Active;

            TryGetMobVisibilityState(mob, out var isOnScreen, out var isOutOfGame, out var onScreenRecent);

            if (isOnScreen)
                return HostMobSyncPriority.Active;
            if (onScreenRecent > 0.0 || !isOutOfGame)
                return HostMobSyncPriority.MidRange;

            return HostMobSyncPriority.Dormant;
        }

        private static double GetHostStatePositionEpsilon(HostMobSyncPriority priority) =>
            priority switch
            {
                HostMobSyncPriority.Active => MobStatePositionEpsilon,
                HostMobSyncPriority.MidRange => HostMobStateMidPositionEpsilon,
                _ => HostMobStateDormantPositionEpsilon
            };

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

        private static string EncodeStatePayloadForWire(string? payload)
        {
            var safePayload = payload ?? string.Empty;
            return safePayload.Length == 0 ? ExplicitEmptyStatePayloadMarker : safePayload;
        }

        private static bool TryDecodeStatePayloadFromWire(string? wirePayload, out string payload)
        {
            var safePayload = wirePayload ?? string.Empty;
            if (safePayload.Length == 0)
            {
                payload = string.Empty;
                return false;
            }

            if (string.Equals(safePayload, ExplicitEmptyStatePayloadMarker, StringComparison.Ordinal))
            {
                payload = string.Empty;
                return true;
            }

            payload = safePayload;
            return true;
        }

        private static string ExtractAffectPresenceSignature(string? payload)
        {
            var parsed = ParseAffectStatePayload(payload);
            if (parsed.Count == 0)
                return string.Empty;

            var ids = new List<int>(parsed.Count);
            foreach (var affectId in parsed)
                ids.Add(affectId);
            ids.Sort();
            return string.Join(".", ids);
        }

        private void Hook_Mob_contactAttack(Hook_Mob.orig_contactAttack orig, Mob self, Entity pow)
        {
            var net = GameMenu.NetRef;
            LogHostBossAttackHook("host-hook-contactAttack-enter", self, ContactAttackPacketSkillId, $"target={DescribeCombatEntity(pow)}");
            // 仅当全员倒地时才抑制接触攻击；主机倒地但客机存活时，怪物仍应能接触攻击存活玩家。
            if (IsHost(net) && !IsAnyNonDownedPlayerPresent() && IsPlayerCombatTargetEntity(pow))
                return;

            orig(self, pow);
            LogHostBossAttackHook("host-hook-contactAttack-after", self, ContactAttackPacketSkillId, $"target={DescribeCombatEntity(pow)} isHost={IsHost(net)} isPlayer={IsPlayerCombatTargetEntity(pow)}");

            if (!IsHost(net) || !IsPlayerCombatTargetEntity(pow))
                return;

            if (ShouldSendHostContactPacket(self, pow))
            {
                var skillId = BossAuthoritySync.IsManagedBoss(self)
                    ? BossAuthoritySync.BossContactSkillId
                    : ContactAttackPacketSkillId;
                TrySendHostMobAttack(self, skillId, false, null, pow);
            }
        }

        private void Hook_Mob_onTouch(Hook_Mob.orig_onTouch orig, Mob self, Entity atk)
        {
            var net = GameMenu.NetRef;
            LogHostBossAttackHook("host-hook-onTouch-enter", self, ContactAttackPacketSkillId, $"target={DescribeCombatEntity(atk)}");
            // 仅当全员倒地时才抑制接触攻击；主机倒地但客机存活时，怪物仍应能接触攻击存活玩家。
            if (IsHost(net) && !IsAnyNonDownedPlayerPresent() && IsPlayerCombatTargetEntity(atk))
                return;

            orig(self, atk);
            LogHostBossAttackHook("host-hook-onTouch-after", self, ContactAttackPacketSkillId, $"target={DescribeCombatEntity(atk)} isHost={IsHost(net)} isSync={IsSyncMob(self)} isPlayer={IsPlayerCombatTargetEntity(atk)}");

            if (!IsHost(net) || !IsSyncMob(self))
                return;

            if (!IsPlayerCombatTargetEntity(atk))
            {
                if (TrySendHostBossProxyContactAttack(self, atk))
                    return;
                return;
            }

            EnsureMobTracked(self);
            if (ShouldSendHostContactPacket(self, atk))
            {
                var skillId = BossAuthoritySync.IsManagedBoss(self)
                    ? BossAuthoritySync.BossContactSkillId
                    : ContactAttackPacketSkillId;
                TrySendHostMobAttack(self, skillId, false, null, atk);
            }
        }

        private void Hook_OldMobSkill_execute(Hook_OldMobSkill.orig_execute orig, OldMobSkill self, double? a)
        {
            orig(self, a);

            var net = GameMenu.NetRef;
            var ownerMob = self?.owner as Mob;
            var skillId = self?.id?.ToString() ?? string.Empty;
            LogHostBossAttackHook("host-hook-oldMobSkill-execute", ownerMob, skillId, $"ratio={a}");

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

            // 持盾敌人的冲刺技能通过在移动过程中物理突刺目标来造成伤害。
            // 原生碰撞检测只能识别真实的 Hero 实体，无法识别 GhostKing（客户端玩家）。
            // 因此我们需要打开一个短暂的检测窗口，手动驱动突刺碰撞（见 postUpdate 中的 TryDriveHostDashLungeContact）。
            if (IsDashLungeSkill(ownerMob, skillId) && TryGetMobSyncId(ownerMob, out var dashSyncId))
            {
                lock (Sync)
                {
                    hostDashLungeWindowUntilTick[dashSyncId] =
                        OffsetTimestampBySeconds(Stopwatch.GetTimestamp(), HostDashLungeWindowSeconds);
                }
            }
        }

        private static long OffsetTimestampBySeconds(long timestamp, double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
                return timestamp;

            var ticks = (long)System.Math.Round(seconds * Stopwatch.Frequency, MidpointRounding.AwayFromZero);
            return timestamp + ticks;
        }

        private static bool IsDashLungeSkill(Mob mob, string skillId)
        {
            if (string.Equals(skillId, "dash", StringComparison.OrdinalIgnoreCase))
                return true;

            return string.Equals(GetMobRuntimeClassKeySafe(mob), "Shield", StringComparison.OrdinalIgnoreCase) &&
                   skillId.IndexOf("dash", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // 在主机端每帧（postUpdate）为同步怪物调用。当持盾冲刺窗口打开时，
        // 如果突刺中的怪物与 GhostKing 重叠，则发送一个接触攻击包，使客户端能在本地英雄身上重播伤害。
        // 闪避机制仍然有效：GhostKing 的位置从客户端同步，因此滚躲开的客户端不会被判定为重叠。
        private static void TryDriveHostDashLungeContact(Mob mob)
        {
            if (mob == null || ModEntry.clients == null)
                return;
            if (!TryGetMobSyncId(mob, out var syncId))
                return;

            long until;
            lock (Sync)
            {
                if (!hostDashLungeWindowUntilTick.TryGetValue(syncId, out until))
                    return;
            }

            if (Stopwatch.GetTimestamp() > until)
            {
                lock (Sync) { hostDashLungeWindowUntilTick.Remove(syncId); }
                return;
            }

            var mx = GetWorldX(mob);
            var my = GetWorldY(mob);

            for (int i = 0; i < ModEntry.clients.Length; i++)
            {
                var ghost = ModEntry.clients[i];
                if (ghost == null || ModEntry.clientIds[i] <= 0)
                    continue;
                if (ModEntry.IsEntityDownedForCombat(ghost))
                    continue;

                double dx, dy;
                try
                {
                    dx = System.Math.Abs(GetWorldX(ghost) - mx);
                    dy = System.Math.Abs(GetWorldY(ghost) - my);
                }
                catch
                {
                    continue;
                }

                if (dx > HostDashLungeContactRangeX || dy > HostDashLungeContactRangeY)
                {
                    MobSyncTrace.LogAttackDiag(
                        "host-dash-check",
                        syncId,
                        BuildMobStateTypeSignature(mob),
                        "dash",
                        ModEntry.clientIds[i],
                        NormalizeDir(mob.dir),
                        $"dx={dx:F1} dy={dy:F1} rangeX={HostDashLungeContactRangeX:F1} rangeY={HostDashLungeContactRangeY:F1}");
                    continue;
                }

                // 每次冲刺只触发一次伤害：突刺命中后立即关闭检测窗口。
                lock (Sync) { hostDashLungeWindowUntilTick.Remove(syncId); }
                MobSyncTrace.LogAttackDiag(
                    "host-dash-hit",
                    syncId,
                    BuildMobStateTypeSignature(mob),
                    "dash",
                    ModEntry.clientIds[i],
                    NormalizeDir(mob.dir),
                    $"dx={dx:F1} dy={dy:F1}");
                TrySendHostMobAttack(mob, ContactAttackPacketSkillId, false, null, ghost);
                return;
            }
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
            LogHostBossAttackHook("host-hook-oldSkill-prepare", ownerMob, skillId, $"prepared={prepared} data={data}");
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
            LogHostBossAttackHook("host-hook-oldSkill-execute", ownerMob, skillId, $"ratio={ratio}");

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
            LogHostBossAttackHook("host-hook-oldMobSkill-prepareOnOwnerTarget", ownerMob, skillId, $"prepared={prepared} data={data} e={e}");
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
            LogHostBossAttackHook("host-hook-queueAttack-enter", self, a?.id?.ToString() ?? string.Empty, $"requiresTarget={requiresTargetInArea} data={data}");
            if (IsClient(net) && IsSyncMob(self) && !IsClientNetworkQueuedAttackAllowed(self))
                return;

            orig(self, a, requiresTargetInArea, data);
            LogHostBossAttackHook("host-hook-queueAttack-after", self, a?.id?.ToString() ?? string.Empty, $"requiresTarget={requiresTargetInArea} data={data} isHost={IsHost(net)}");

            if (self == null || a == null)
                return;

            if (!IsHost(net))
                return;

            var skillId = a.id?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(skillId))
                return;

            EnsureMobTracked(self);
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
            LogHostBossAttackHook("host-hook-mobSkill-execute", ownerMob, skillId, $"ratio={ratio}");
             
            if (ownerMob != null && !string.IsNullOrWhiteSpace(skillId))
                TrySendHostMobAttack(ownerMob, NewSkillExecutePacketPrefix + skillId, false, null);
        }

        private void Hook__DeathSickle__constructor__(
            dc.en.mob.boss.death.Hook__DeathSickle.orig___constructor__ orig,
            dc.en.mob.boss.death.DeathSickle self,
            Level lvl,
            double x,
            double y,
            Mob parent,
            Entity target)
        {
            orig(self, lvl, x, y, parent, target);

            try
            {
                if (!IsHost(GameMenu.NetRef))
                    return;
                if (!BossAuthoritySync.IsManagedDeathBoss(parent))
                    return;

                var resolvedTarget = ResolveMobAttackTargetEntity(parent, target);
                TrySendHostBossAuthorityEvent(
                    parent,
                    BossAuthoritySync.DeathSickleSpawnSkillId,
                    x,
                    y,
                    NormalizeDir(parent.dir),
                    resolvedTarget);

                LogHostBossAttackHook(
                    "host-hook-death-sickle-spawn",
                    parent,
                    BossAuthoritySync.DeathSickleSpawnSkillId,
                    $"spawn=({x.ToString(CultureInfo.InvariantCulture)},{y.ToString(CultureInfo.InvariantCulture)}) target={DescribeCombatEntity(resolvedTarget)} rawTarget={DescribeCombatEntity(target)}");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[MobsSync] DeathSickle authority spawn send failed");
            }
        }

        private static void LogHostBossAttackHook(string stage, Mob? mob, string? skillId, string detail)
        {
            try
            {
                if (mob == null || !BossSyncHelpers.IsBossMob(mob))
                    return;

                _ = TryGetMobSyncId(mob, out var syncId);
                MobSyncTrace.LogBossSyncDiag(
                    stage,
                    syncId,
                    BuildMobStateTypeSignature(mob),
                    $"skill={skillId ?? string.Empty} role={MobSyncNetRoleForTrace(GameMenu.NetRef)} localDowned={ModEntry.IsLocalPlayerDowned()} aTarget={DescribeCombatEntity(SafeGetAttackTarget(mob))} nemesis={DescribeCombatEntity(SafeGetNemesisTarget(mob))} {detail}",
                    0.25);
            }
            catch
            {
            }
        }

        private static bool TrySendHostBossProxyContactAttack(Mob mob, Entity? touched)
        {
            if (mob == null || touched == null || !BossSyncHelpers.IsBossMob(mob))
                return false;
            if (!IsDeathSickleEntity(touched))
                return false;

            var target = ResolveCurrentHostPlayerCombatTarget(mob);
            if (target == null)
                return false;

            EnsureMobTracked(mob);
            if (!ShouldSendHostContactPacket(mob, target))
                return false;

            LogHostBossAttackHook(
                "host-hook-death-sickle-proxy-contact",
                mob,
                BossAuthoritySync.BossContactSkillId,
                $"proxyTarget={DescribeCombatEntity(target)} touched={DescribeCombatEntity(touched)}");
            TrySendHostMobAttack(mob, BossAuthoritySync.BossContactSkillId, false, null, target);
            return true;
        }

        private static void TrySendHostBossAuthorityEvent(Mob mob, string skillId, double x, double y, int dir, Entity? explicitTarget)
        {
            if (mob == null || string.IsNullOrWhiteSpace(skillId))
                return;

            var net = GameMenu.NetRef;
            if (!IsHost(net))
                return;
            if (!IsSyncMob(mob))
                return;
            if (!TryGetMobSyncId(mob, out var mobSyncId))
                return;
            if (!TryGetCurrentLevelIdentityToken(out var identityToken))
                return;

            var targetEntity = ResolveMobAttackTargetEntity(mob, explicitTarget);
            var targetUserId = ResolveHostTargetUserId(targetEntity, net!.id);
            var encodedSkill = Uri.EscapeDataString(skillId);
            var attackEvent = $"attack|{encodedSkill}|0|0|0|0|{targetUserId}|{dir}";
            var mobType = BuildMobStateTypeSignature(mob);
            var update = new NetNode.MobEventUpdate(mobSyncId, x, y, dir, SingleEvent(attackEvent), mobType, identityToken);
            MobSyncTrace.LogSendMobEvents(MobSyncNetRoleForTrace(net), SingleUpdate(update));
            net.SendMobEvents(SingleUpdate(update));
        }

        private static bool IsDeathSickleEntity(Entity? entity)
        {
            if (entity == null)
                return false;

            try
            {
                var name = entity.GetType().Name;
                if (name.IndexOf("DeathSickle", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch
            {
            }

            return false;
        }

    }
}
