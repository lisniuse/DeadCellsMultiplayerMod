using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using dc;
using dc.en;
using dc.h2d;
using dc.libs.heaps.slib;
using dc.libs.heaps.slib._AnimManager;
using dc.pr;
using dc.tool.atk;
using dc.tool.skill;
using DeadCellsMultiplayerMod.Interface.ModuleInitializing;
using ModCore.Events;
using ModCore.Modules;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod.Mobs.MobsSynchronization
{
    public class MobsSynchronization :
    IOnAdvancedModuleInitializing,
    IEventReceiver
    {
        private readonly ModEntry modEntry;

        private static readonly object Sync = new();
        private static readonly List<Mob> trackedMobs = new();
        private static readonly Dictionary<Mob, int> trackedMobIndices = new();

        private static readonly Dictionary<int, ClientMobState> clientMobTargets = new();
        private static readonly Dictionary<int, int> hostToLocalIndices = new();
        private static readonly Dictionary<int, int> localToHostIndices = new();
        private static readonly Dictionary<int, long> clientAttackUnlockUntilTick = new();
        private static readonly List<Entity> hostDetectedTargets = new();
        private static readonly Random hostTargetRandom = new();
        private static readonly object AsyncPumpSync = new();

        private static Level? currentLevel;
        private static Level? lastClientNetPumpLevel;
        private static Level? lastHostNetPumpLevel;
        private static double lastClientNetPumpFrame = double.NaN;
        private static double lastHostNetPumpFrame = double.NaN;
        private static long lastClientMobDrawSendTick;
        private static long lastHostStateSendTick;
        private static Task? clientNetPumpTask;
        private static Task? hostNetPumpTask;
        private static int asyncSessionToken;
        private static int forceExactNemesisTargetDepth;

        private const double ClientMobDrawSendRateHz = 20.0;
        private const double HostStateSendRateHz = 20.0;
        private const double ClientInterpolationAlpha = 0.25;
        private const double ClientAiLockSeconds = 0.3;
        private const double ClientAttackUnlockSeconds = 0.2;
        private const double ClientAnimSpeedEpsilon = 0.05;
        private static readonly bool ClientSyncVerticalPosition = false;
        private const double PixelsPerCase = 24.0;
        private const double MaxCoordinateMatchDistance = 96.0;
        private const double MaxCoordinateMatchDistanceSq = MaxCoordinateMatchDistance * MaxCoordinateMatchDistance;
        private const string ContactAttackPacketSkillId = "@contact";
        private const string OldSkillExecutePacketPrefix = "@oldexec:";
        private const string NewSkillExecutePacketPrefix = "@newexec:";

        private readonly struct ClientMobState
        {
            public readonly double X;
            public readonly double Y;
            public readonly int Dir;
            public readonly int Life;
            public readonly int MaxLife;
            public readonly string AnimPayload;

            public ClientMobState(double x, double y, int dir, int life, int maxLife, string animPayload)
            {
                X = x;
                Y = y;
                Dir = dir;
                Life = life;
                MaxLife = maxLife;
                AnimPayload = animPayload ?? string.Empty;
            }
        }

        public MobsSynchronization(ModEntry entry)
        {
            EventSystem.AddReceiver(this);
            modEntry = entry;
        }

        public void OnAdvancedModuleInitializing(ModEntry entry)
        {
            entry.Logger.Information("\x1b[32m[[ModEntry.MobsSynchronization] Initializing MobsSynchronization hooks...]\x1b[0m ");

            Hook_Level.entitiesPostCreate += Hook_Level_entitiesPostCreate;
            Hook_Level.registerEntity += Hook_Level_registerEntity;
            Hook_Level.onDispose += Hook_Level_onDispose;

            Hook_Mob.setNemesisTarget += Hook_Mob_setNemesisTarget;
            Hook_Mob.preUpdate += Hook_Mob_preUpdate;
            Hook_Mob.fixedUpdate += Hook_Mob_fixedupdate;
            Hook_Mob.postUpdate += Hook_Mob_postUpdate;
            Hook_Mob.onDamage += Hook_Mob_onDamage;
            Hook_Mob.contactAttack += Hook_Mob_contactAttack;
            Hook_OldMobSkill.execute += Hook_OldMobSkill_execute;
            Hook_MobSkill.execute += Hook_MobSkill_execute;
        }

        private static bool IsHost(NetNode? net) => net != null && net.IsAlive && net.IsHost;
        private static bool IsClient(NetNode? net) => net != null && net.IsAlive && !net.IsHost;

        private static void Hook_Level_entitiesPostCreate(Hook_Level.orig_entitiesPostCreate orig, Level self)
        {
            orig(self);
            RebuildMobArray(self);
        }

        private static void Hook_Level_registerEntity(Hook_Level.orig_registerEntity orig, Level self, Entity clid)
        {
            orig(self, clid);

            // if (clid is not Mob mob || !IsSyncMob(mob))
            if (clid is not Mob mob)
                return;

            lock (Sync)
            {
                if (currentLevel != null && !ReferenceEquals(currentLevel, self))
                    return;

                if (trackedMobIndices.ContainsKey(mob))
                    return;

                var index = trackedMobs.Count;
                trackedMobs.Add(mob);
                trackedMobIndices[mob] = index;
            }
        }

        private static void Hook_Level_onDispose(Hook_Level.orig_onDispose orig, Level self)
        {
            orig(self);

            lock (Sync)
            {
                if (currentLevel != null && ReferenceEquals(currentLevel, self))
                    ResetMobTrackingLocked();
            }
        }

        private void Hook_Mob_preUpdate(Hook_Mob.orig_preUpdate orig, Mob self)
        {
            if (self == null)
            {
                orig(self);
                return;
            }

            EnsureMobTracked(self);

            var net = GameMenu.NetRef;
            var isHost = IsHost(net);
            var isClient = IsClient(net);

            if (isClient && ShouldRunClientNetPumpForFrame(self))
            {
                ScheduleClientNetPumpAsync(net!);
                TrySendClientMobDraws(net!);
            }

            if (isClient && IsSyncMob(self))
            {
                ApplyClientAnimationStateBeforeUpdate(self);
                if (!IsClientAttackUnlockActive(self))
                    TryLockMobAi(self, ClientAiLockSeconds);
            }

            if (isHost && IsSyncMob(self))
                TryAssignHostAttackTarget(self);

            orig(self);
        }

        private void Hook_Mob_fixedupdate(Hook_Mob.orig_fixedUpdate orig, Mob self)
        {
            if (self == null)
            {
                orig(self);
                return;
            }

            EnsureMobTracked(self);

            var net = GameMenu.NetRef;
            var isClient = IsClient(net);

            orig(self);

            if (!IsSyncMob(self))
                return;

            if (isClient)
                ApplyInterpolatedState(self);
        }

        private void Hook_Mob_postUpdate(Hook_Mob.orig_postUpdate orig, Mob self)
        {
            if (self == null)
            {
                orig(self);
                return;
            }

            EnsureMobTracked(self);

            var net = GameMenu.NetRef;
            var isHost = IsHost(net);

            orig(self);

            if (!IsSyncMob(self))
                return;

            if (isHost && ShouldRunHostNetPumpForFrame(self))
            {
                ScheduleHostNetPumpAsync(net!);
                TrySendHostMobStates(net!);
            }
        }

        private void Hook_Mob_onDamage(Hook_Mob.orig_onDamage orig, Mob self, AttackData i)
        {
            orig(self, i);

            if (self == null)
                return;

            var net = GameMenu.NetRef;
            if (!IsClient(net))
                return;

            if (!IsSyncMob(self))
                return;

            if (i?.source != null && ModEntry.me != null && !ReferenceEquals(i.source, ModEntry.me))
                return;

            if (!TryGetTrackedIndex(self, out var mobIndex))
                return;

            var hp = self.life;
            var x = self.spr?.x ?? self.cx;
            var y = self.spr?.y ?? self.cy;

            net!.SendMobHit(mobIndex, hp, x, y);
        }

        private void Hook_Mob_contactAttack(Hook_Mob.orig_contactAttack orig, Mob self, Entity pow)
        {
            orig(self, pow);

            if (self == null)
                return;

            var net = GameMenu.NetRef;
            if (!IsHost(net))
                return;

            TrySendHostMobAttack(self, ContactAttackPacketSkillId, false, null, pow);
        }

        private void Hook_OldMobSkill_execute(Hook_OldMobSkill.orig_execute orig, OldMobSkill self, double? a)
        {
            orig(self, a);

            if (self == null)
                return;

            var net = GameMenu.NetRef;
            if (!IsHost(net))
                return;

            var ownerMob = self.owner as Mob;
            if (ownerMob == null)
                return;

            var skillId = self.id?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(skillId))
                return;

            TrySendHostMobAttack(ownerMob, OldSkillExecutePacketPrefix + skillId, false, null);
        }

        private void Hook_MobSkill_execute(Hook_MobSkill.orig_execute orig, MobSkill self, double? ratio)
        {
            orig(self, ratio);

            if (self == null)
                return;

            var net = GameMenu.NetRef;
            if (!IsHost(net))
                return;

            var ownerMob = self.owner as Mob;
            if (ownerMob == null)
                return;

            var skillId = self.id?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(skillId))
                return;

            TrySendHostMobAttack(ownerMob, NewSkillExecutePacketPrefix + skillId, false, null);
        }

        private static void TrySendHostMobAttack(Mob mob, string skillId, bool requiresTargetInArea, int? data, Entity? explicitTarget = null)
        {
            if (mob == null || string.IsNullOrWhiteSpace(skillId))
                return;

            var net = GameMenu.NetRef;
            if (!IsHost(net))
                return;

            if (!IsSyncMob(mob))
                return;

            EnsureMobTracked(mob);
            if (!TryGetTrackedIndex(mob, out var mobIndex))
                return;

            var targetEntity = explicitTarget;
            if (targetEntity == null)
            {
                try
                {
                    targetEntity = mob.nemesisTarget;
                }
                catch
                {
                    targetEntity = null;
                }
            }

            var targetUserId = ResolveHostTargetUserId(targetEntity, net!.id);
            var x = mob.spr?.x ?? mob.cx;
            var y = mob.spr?.y ?? mob.cy;
            net.SendMobAttack(mobIndex, skillId, requiresTargetInArea, data, x, y, targetUserId);
        }

        private void Hook_Mob_setNemesisTarget(Hook_Mob.orig_setNemesisTarget orig, Mob self, Entity e)
        {
            if (System.Threading.Volatile.Read(ref forceExactNemesisTargetDepth) > 0)
            {
                orig(self, e);
                return;
            }

            if (e == ModCore.Modules.Game.Instance.HeroInstance)
            {
                var team = self._team;
                var helper = team.get_targetHelper();
                helper.filterUntargetables();
                e = helper.getBest();

                orig(self, helper.getBest());
                return;
            }

            orig(self, e);
        }

        private static void RebuildMobArray(Level? level)
        {
            lock (Sync)
            {
                ResetMobTrackingLocked();
                currentLevel = level;
                if (level == null || level.entities == null)
                    return;

                var buffer = new List<Mob>();
                var entities = level.entities;
                for (int i = 0; i < entities.length; i++)
                {
                    var mob = entities.getDyn(i) as Mob;
                    if (!IsSyncMob(mob))
                        continue;
                    buffer.Add(mob!);
                }

                buffer.Sort(CompareMobsForStableOrder);
                for (int i = 0; i < buffer.Count; i++)
                {
                    trackedMobs.Add(buffer[i]);
                    trackedMobIndices[buffer[i]] = i;
                }
            }
        }

        private static void ResetMobTrackingLocked()
        {
            trackedMobs.Clear();
            trackedMobIndices.Clear();
            clientMobTargets.Clear();
            hostToLocalIndices.Clear();
            localToHostIndices.Clear();
            clientAttackUnlockUntilTick.Clear();
            currentLevel = null;
            lastClientNetPumpLevel = null;
            lastHostNetPumpLevel = null;
            lastClientNetPumpFrame = double.NaN;
            lastHostNetPumpFrame = double.NaN;
            lastClientMobDrawSendTick = 0;
            lastHostStateSendTick = 0;
            unchecked
            {
                asyncSessionToken++;
            }
            lock (AsyncPumpSync)
            {
                clientNetPumpTask = null;
                hostNetPumpTask = null;
            }
        }

        private static int CompareMobsForStableOrder(Mob a, Mob b)
        {
            var byCx = a.cx.CompareTo(b.cx);
            if (byCx != 0) return byCx;

            var byCy = a.cy.CompareTo(b.cy);
            if (byCy != 0) return byCy;

            var ax = a.spr?.x ?? 0.0;
            var bx = b.spr?.x ?? 0.0;
            var byX = ax.CompareTo(bx);
            if (byX != 0) return byX;

            var ay = a.spr?.y ?? 0.0;
            var by = b.spr?.y ?? 0.0;
            var byY = ay.CompareTo(by);
            if (byY != 0) return byY;

            var at = a.type?.ToString() ?? string.Empty;
            var bt = b.type?.ToString() ?? string.Empty;
            return string.Compare(at, bt, StringComparison.Ordinal);
        }

        private static bool IsSyncMob(Mob? mob)
        {
            if (mob == null)
                return false;

            var typeName = mob.GetType().ToString();
            return typeName.Contains("dc.en.mob.", StringComparison.Ordinal);
        }

        private static void EnsureMobTracked(Mob mob)
        {
            if (!IsSyncMob(mob))
                return;

            lock (Sync)
            {
                var mobLevel = mob._level;
                if (mobLevel != null && !ReferenceEquals(currentLevel, mobLevel))
                {
                    RebuildMobArray(mobLevel);
                    return;
                }

                if (trackedMobIndices.ContainsKey(mob))
                    return;

                var index = trackedMobs.Count;
                trackedMobs.Add(mob);
                trackedMobIndices[mob] = index;
            }
        }

        private static bool TryGetTrackedIndex(Mob mob, out int index)
        {
            lock (Sync)
            {
                return trackedMobIndices.TryGetValue(mob, out index);
            }
        }

        private static void TryLockMobAi(Mob mob, double seconds)
        {
            try
            {
                mob.lockAiS(seconds);
            }
            catch
            {
            }
        }

        private static bool ShouldRunClientNetPumpForFrame(Mob mob)
        {
            return ShouldRunNetPumpForFrame(mob, isClientPump: true);
        }

        private static bool ShouldRunHostNetPumpForFrame(Mob mob)
        {
            return ShouldRunNetPumpForFrame(mob, isClientPump: false);
        }

        private static bool ShouldRunNetPumpForFrame(Mob mob, bool isClientPump)
        {
            var level = mob._level ?? currentLevel;
            if (level == null)
                return true;

            var frame = level.ftime;

            lock (Sync)
            {
                if (isClientPump)
                {
                    if (!ReferenceEquals(lastClientNetPumpLevel, level) || lastClientNetPumpFrame != frame)
                    {
                        lastClientNetPumpLevel = level;
                        lastClientNetPumpFrame = frame;
                        return true;
                    }

                    return false;
                }

                if (!ReferenceEquals(lastHostNetPumpLevel, level) || lastHostNetPumpFrame != frame)
                {
                    lastHostNetPumpLevel = level;
                    lastHostNetPumpFrame = frame;
                    return true;
                }

                return false;
            }
        }

        private static void ScheduleClientNetPumpAsync(NetNode net)
        {
            int session;
            lock (Sync)
            {
                session = asyncSessionToken;
            }

            lock (AsyncPumpSync)
            {
                if (clientNetPumpTask != null && !clientNetPumpTask.IsCompleted)
                    return;

                clientNetPumpTask = Task.Run(() =>
                {
                    try
                    {
                        if (net.TryConsumeMobStates(out var states) && states.Count > 0)
                        {
                            GameMenu.EnqueueMainThread(() =>
                            {
                                if (!IsCurrentAsyncSession(session))
                                    return;
                                ApplyIncomingHostMobStates(states);
                            });
                        }

                        if (net.TryConsumeMobAttacks(out var attacks) && attacks.Count > 0)
                        {
                            GameMenu.EnqueueMainThread(() =>
                            {
                                if (!IsCurrentAsyncSession(session))
                                    return;
                                ApplyIncomingHostMobAttacks(attacks);
                            });
                        }
                    }
                    catch
                    {
                    }
                });
            }
        }

        private static void ScheduleHostNetPumpAsync(NetNode net)
        {
            int session;
            lock (Sync)
            {
                session = asyncSessionToken;
            }

            lock (AsyncPumpSync)
            {
                if (hostNetPumpTask != null && !hostNetPumpTask.IsCompleted)
                    return;

                hostNetPumpTask = Task.Run(() =>
                {
                    try
                    {
                        if (net.TryConsumeMobDraws(out var draws) && draws.Count > 0)
                        {
                            GameMenu.EnqueueMainThread(() =>
                            {
                                if (!IsCurrentAsyncSession(session))
                                    return;
                                ApplyIncomingMobDraws(draws);
                            });
                        }

                        if (net.TryConsumeMobHits(out var hits) && hits.Count > 0)
                        {
                            GameMenu.EnqueueMainThread(() =>
                            {
                                if (!IsCurrentAsyncSession(session))
                                    return;
                                ApplyIncomingMobHits(hits);
                            });
                        }
                    }
                    catch
                    {
                    }
                });
            }
        }

        private static bool IsCurrentAsyncSession(int token)
        {
            lock (Sync)
            {
                return asyncSessionToken == token;
            }
        }

        private static void TryAssignHostAttackTarget(Mob mob)
        {
            if (mob == null)
                return;

            Entity? selected = null;

            lock (Sync)
            {
                hostDetectedTargets.Clear();

                TryCollectDetectedTarget(mob, ModEntry.me ?? ModCore.Modules.Game.Instance?.HeroInstance);

                for (int i = 0; i < ModEntry.clients.Length; i++)
                {
                    if (ModEntry.clientIds[i] <= 0)
                        continue;

                    TryCollectDetectedTarget(mob, ModEntry.clients[i]);
                }

                if (hostDetectedTargets.Count == 0)
                    return;

                var currentTarget = mob.nemesisTarget;
                if (currentTarget != null && hostDetectedTargets.Contains(currentTarget))
                {
                    selected = currentTarget;
                }
                else if (hostDetectedTargets.Count == 1)
                {
                    selected = hostDetectedTargets[0];
                }
                else
                {
                    selected = hostDetectedTargets[hostTargetRandom.Next(hostDetectedTargets.Count)];
                }
            }

            if (selected == null)
                return;

            try
            {
                mob.setAttackTarget(selected);
            }
            catch
            {
            }

            TrySetNemesisTargetExact(mob, selected);
        }

        private static void TryCollectDetectedTarget(Mob mob, Entity? candidate)
        {
            if (candidate == null)
                return;
            if (ReferenceEquals(candidate, mob))
                return;

            try
            {
                if (candidate.destroyed || candidate.life <= 0)
                    return;
            }
            catch
            {
                return;
            }

            var mobLevel = mob._level;
            var candidateLevel = candidate._level;
            if (mobLevel != null && candidateLevel != null && !ReferenceEquals(mobLevel, candidateLevel))
                return;

            bool inDetectArea;
            try
            {
                inDetectArea = mob.inDetectArea(candidate);
            }
            catch
            {
                return;
            }

            if (!inDetectArea)
                return;

            if (!hostDetectedTargets.Contains(candidate))
                hostDetectedTargets.Add(candidate);
        }

        private static int ResolveHostTargetUserId(Entity? target, int localUserId)
        {
            if (target == null || localUserId <= 0)
                return 0;

            var localHero = ModEntry.me ?? ModCore.Modules.Game.Instance?.HeroInstance;
            if (localHero != null && ReferenceEquals(target, localHero))
                return localUserId;

            var gameHero = ModCore.Modules.Game.Instance?.HeroInstance;
            if (gameHero != null && ReferenceEquals(target, gameHero))
                return localUserId;

            for (int i = 0; i < ModEntry.clients.Length; i++)
            {
                var clientId = ModEntry.clientIds[i];
                var client = ModEntry.clients[i];
                if (clientId <= 0 || client == null)
                    continue;

                if (ReferenceEquals(target, client))
                    return clientId;
            }

            return 0;
        }

        private static void TrySetNemesisTargetExact(Mob mob, Entity target)
        {
            if (mob == null || target == null)
                return;

            System.Threading.Interlocked.Increment(ref forceExactNemesisTargetDepth);
            try
            {
                mob.setNemesisTarget(target);
            }
            catch
            {
            }
            finally
            {
                System.Threading.Interlocked.Decrement(ref forceExactNemesisTargetDepth);
            }
        }

        private static void TrySetMobAttackTargetsExact(Mob mob, Entity target)
        {
            if (mob == null || target == null)
                return;

            try
            {
                mob.setAttackTarget(target);
            }
            catch
            {
            }

            TrySetNemesisTargetExact(mob, target);
        }

        private static bool IsClientAttackUnlockActive(Mob mob)
        {
            if (!TryGetTrackedIndex(mob, out var localIndex))
                return false;

            lock (Sync)
            {
                if (!clientAttackUnlockUntilTick.TryGetValue(localIndex, out var until))
                    return false;

                var now = Stopwatch.GetTimestamp();
                if (now <= until)
                    return true;

                clientAttackUnlockUntilTick.Remove(localIndex);
                return false;
            }
        }

        private static int NormalizeDir(int dir)
        {
            if (dir < 0) return -1;
            if (dir > 0) return 1;
            return 0;
        }

        private static double GetWorldX(Entity entity)
        {
            return (entity.cx + entity.xr) * PixelsPerCase;
        }

        private static double GetWorldY(Entity entity)
        {
            return (entity.cy + entity.yr) * PixelsPerCase;
        }

        private static void SetWorldXKeepingY(Mob mob, double worldX)
        {
            var xCase = (int)(worldX / PixelsPerCase);
            var xFrac = (worldX - xCase * PixelsPerCase) / PixelsPerCase;
            mob.setPosCase(xCase, mob.cy, xFrac, mob.yr);
        }

        private readonly struct ParsedAnimPayload
        {
            public readonly string Group;
            public readonly bool Reverse;
            public readonly double Speed;

            public ParsedAnimPayload(string group, bool reverse, double speed)
            {
                Group = group ?? string.Empty;
                Reverse = reverse;
                Speed = speed;
            }
        }

        private static AnimManager? GetMobAnimManager(Mob mob)
        {
            var spr = mob.spr;
            if (spr == null)
                return null;

            try
            {
                return spr._animManager ?? spr.get_anim();
            }
            catch
            {
                return spr._animManager;
            }
        }

        private static AnimInstance? GetTopAnimInstance(AnimManager? animManager)
        {
            var stack = animManager?.stack;
            if (stack == null || stack.length <= 0)
                return null;

            try
            {
                return stack.getDyn(0) as AnimInstance;
            }
            catch
            {
                return null;
            }
        }

        private static string BuildAnimPayload(Mob mob)
        {
            var spr = mob.spr;
            if (spr == null)
                return string.Empty;

            var group = spr.groupName?.ToString() ?? string.Empty;
            var reverse = false;
            var speed = 1.0;

            try
            {
                var animManager = GetMobAnimManager(mob);
                var top = GetTopAnimInstance(animManager);
                if (top != null)
                {
                    if (!string.IsNullOrWhiteSpace(top.group?.ToString()))
                        group = top.group.ToString();
                    reverse = top.reverse;
                    if (top.speed > 0.0)
                        speed = top.speed;
                }
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(group))
                return string.Empty;

            string encodedGroup;
            try
            {
                encodedGroup = Uri.EscapeDataString(group);
            }
            catch
            {
                encodedGroup = group;
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{encodedGroup}~{(reverse ? 1 : 0)}~{speed:R}");
        }

        private static bool TryParseAnimPayload(string? payload, out ParsedAnimPayload parsed)
        {
            parsed = default;
            if (string.IsNullOrWhiteSpace(payload))
                return false;

            var parts = payload.Split('~', StringSplitOptions.None);
            if (parts.Length < 3)
                return false;

            var encodedGroup = parts[0];
            string group;
            try
            {
                group = Uri.UnescapeDataString(encodedGroup);
            }
            catch
            {
                group = encodedGroup;
            }

            if (string.IsNullOrWhiteSpace(group))
                return false;

            var hasLegacyFrame = parts.Length >= 4 &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
            var reversePart = hasLegacyFrame ? parts[2] : parts[1];
            var speedPart = hasLegacyFrame ? parts[3] : parts[2];

            var reverse = reversePart == "1";
            if (!double.TryParse(speedPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
                speed = 1.0;

            parsed = new ParsedAnimPayload(group, reverse, System.Math.Max(0.01, speed));
            return true;
        }

        private static void ApplyAnimPayload(Mob mob, string? payload)
        {
            if (!TryParseAnimPayload(payload, out var parsed))
                return;

            var spr = mob.spr;
            if (spr == null)
                return;

            var animManager = GetMobAnimManager(mob);
            if (animManager == null)
                return;

            try
            {
                var currentGroup = spr.groupName?.ToString() ?? string.Empty;
                if (!string.Equals(currentGroup, parsed.Group, StringComparison.Ordinal))
                {
                    animManager.play(parsed.Group.AsHaxeString(), null, null).loop(null);
                }
            }
            catch
            {
            }

            try
            {
                var top = GetTopAnimInstance(animManager);
                if (top != null)
                {
                    if (top.reverse != parsed.Reverse)
                        top.reverse = parsed.Reverse;
                    if (System.Math.Abs(top.speed - parsed.Speed) > ClientAnimSpeedEpsilon)
                        top.speed = parsed.Speed;
                }
            }
            catch
            {
            }

        }

        private static void TrySendHostMobStates(NetNode net)
        {
            var now = Stopwatch.GetTimestamp();
            var minDelta = (long)(Stopwatch.Frequency / HostStateSendRateHz);
            if (lastHostStateSendTick != 0 && now - lastHostStateSendTick < minDelta)
                return;
            lastHostStateSendTick = now;

            List<NetNode.MobStateSnapshot> states = new();
            lock (Sync)
            {
                for (int i = 0; i < trackedMobs.Count; i++)
                {
                    var mob = trackedMobs[i];
                    if (mob == null)
                        continue;

                    var x = mob.spr?.x ?? mob.cx;
                    var y = mob.spr?.y ?? mob.cy;
                    var dir = NormalizeDir(mob.dir);
                    var life = mob.life;
                    var maxLife = mob.maxLife;
                    var animPayload = BuildAnimPayload(mob);

                    states.Add(new NetNode.MobStateSnapshot(i, x, y, dir, life, maxLife, animPayload));
                }
            }

            if (states.Count > 0)
                net.SendMobStates(states);
        }

        private static void ConsumeIncomingHostMobStates(NetNode net)
        {
            if (!net.TryConsumeMobStates(out var states))
                return;

            ApplyIncomingHostMobStates(states);
        }

        private static void ApplyIncomingHostMobStates(IReadOnlyList<NetNode.MobStateSnapshot> states)
        {
            if (states == null || states.Count == 0)
                return;

            lock (Sync)
            {
                foreach (var state in states)
                {
                    var localIndex = ResolveLocalIndexByCoordinatesLocked(state);
                    if (localIndex < 0)
                        continue;

                    clientMobTargets[localIndex] = new ClientMobState(
                        state.X,
                        state.Y,
                        NormalizeDir(state.Dir),
                        state.Life,
                        state.MaxLife,
                        state.AnimPayload);
                }
            }
        }

        private static void ConsumeIncomingHostMobAttacks(NetNode net)
        {
            if (!net.TryConsumeMobAttacks(out var attacks))
                return;

            ApplyIncomingHostMobAttacks(attacks);
        }

        private static void ApplyIncomingHostMobAttacks(IReadOnlyList<NetNode.MobAttack> attacks)
        {
            if (attacks == null || attacks.Count == 0)
                return;

            for (int i = 0; i < attacks.Count; i++)
            {
                var attack = attacks[i];
                Mob? mob = null;

                lock (Sync)
                {
                    var localIndex = ResolveLocalIndexByCoordinatesLocked(attack.Index, attack.X, attack.Y);
                    if (localIndex >= 0 && localIndex < trackedMobs.Count)
                    {
                        mob = trackedMobs[localIndex];
                        var unlockTicks = (long)(Stopwatch.Frequency * ClientAttackUnlockSeconds);
                        clientAttackUnlockUntilTick[localIndex] = Stopwatch.GetTimestamp() + unlockTicks;
                    }
                }

                if (mob == null)
                    continue;

                TryQueueClientMobAttack(mob, attack.SkillId, attack.RequiresTargetInArea, attack.Data, attack.TargetUserId);
            }
        }

        private static void TrySendClientMobDraws(NetNode net)
        {
            var now = Stopwatch.GetTimestamp();
            var minDelta = (long)(Stopwatch.Frequency / ClientMobDrawSendRateHz);
            if (lastClientMobDrawSendTick != 0 && now - lastClientMobDrawSendTick < minDelta)
                return;
            lastClientMobDrawSendTick = now;

            List<NetNode.MobDraw> draws = new();
            lock (Sync)
            {
                for (int i = 0; i < trackedMobs.Count; i++)
                {
                    var mob = trackedMobs[i];
                    if (!IsSyncMob(mob))
                        continue;

                    bool isOutOfGame;
                    bool isOnScreen;
                    try
                    {
                        isOutOfGame = mob!.isOutOfGame;
                        isOnScreen = mob.isOnScreen;
                    }
                    catch
                    {
                        continue;
                    }

                    if (!isOnScreen && isOutOfGame)
                        continue;

                    draws.Add(new NetNode.MobDraw(net.id, i, isOutOfGame, isOnScreen));
                }
            }

            if (draws.Count > 0)
                net.SendMobDrawBatch(draws);
        }

        private static void ConsumeIncomingMobDraws(NetNode net)
        {
            if (!net.TryConsumeMobDraws(out var draws))
                return;

            ApplyIncomingMobDraws(draws);
        }

        private static void ApplyIncomingMobDraws(IReadOnlyList<NetNode.MobDraw> draws)
        {
            if (draws == null || draws.Count == 0)
                return;

            lock (Sync)
            {
                for (int i = 0; i < draws.Count; i++)
                {
                    var draw = draws[i];
                    if (draw.MobIndex < 0 || draw.MobIndex >= trackedMobs.Count)
                        continue;

                    var mob = trackedMobs[draw.MobIndex];
                    if (!IsSyncMob(mob))
                        continue;

                    TryApplyHostDrawRequestLocked(mob!, draw);
                }
            }
        }

        private static void TryApplyHostDrawRequestLocked(Mob mob, NetNode.MobDraw draw)
        {
            if (mob == null)
                return;

            if (!draw.IsOnScreen && draw.IsOutOfGame)
                return;

            var refreshFrames = 120.0;
            try
            {
                var threshold = mob.frameCountThresholdForOutOfGame;
                if (threshold > 0)
                    refreshFrames = threshold;
            }
            catch
            {
            }

            try
            {
                if (draw.IsOnScreen)
                    mob.isOnScreen = true;
                if (mob.onScreenRecent < refreshFrames)
                    mob.onScreenRecent = refreshFrames;
            }
            catch
            {
            }

            var wasOutOfGame = false;
            try
            {
                wasOutOfGame = mob.isOutOfGame;
            }
            catch
            {
            }

            if (!wasOutOfGame)
                return;

            try
            {
                mob.isOutOfGame = false;
            }
            catch
            {
            }

            try
            {
                mob.lastOutOfGame = false;
            }
            catch
            {
            }

            try
            {
                mob.onOutOfGameChange();
            }
            catch
            {
            }
        }

        private static void TryQueueClientMobAttack(Mob mob, string skillId, bool requiresTargetInArea, int? data, int targetUserId)
        {
            if (mob == null || string.IsNullOrWhiteSpace(skillId))
                return;

            if (string.Equals(skillId, ContactAttackPacketSkillId, StringComparison.Ordinal))
            {
                TryApplyClientContactAttack(mob, targetUserId);
                return;
            }

            if (skillId.StartsWith(OldSkillExecutePacketPrefix, StringComparison.Ordinal))
            {
                TryExecuteClientOldSkill(mob, skillId[OldSkillExecutePacketPrefix.Length..], data, targetUserId);
                return;
            }

            if (skillId.StartsWith(NewSkillExecutePacketPrefix, StringComparison.Ordinal))
            {
                TryExecuteClientNewSkill(mob, skillId[NewSkillExecutePacketPrefix.Length..], data, targetUserId);
                return;
            }

            try
            {
                TrySetClientMobAttackTarget(mob, targetUserId);

                var haxeSkillId = skillId.AsHaxeString();
                if (!mob.hasOldSkill(haxeSkillId))
                    return;

                var oldSkill = mob.getOldSkill(haxeSkillId) as OldMobSkill;
                if (oldSkill == null)
                    return;

                mob.queueAttack(oldSkill, requiresTargetInArea, data);
            }
            catch
            {
            }
        }

        private static void TryExecuteClientOldSkill(Mob mob, string rawSkillId, int? data, int targetUserId)
        {
            if (mob == null || string.IsNullOrWhiteSpace(rawSkillId))
                return;

            try
            {
                TrySetClientMobAttackTarget(mob, targetUserId);

                var skillId = rawSkillId.AsHaxeString();
                if (!mob.hasOldSkill(skillId))
                    return;

                var oldSkill = mob.getOldSkill(skillId) as OldMobSkill;
                if (oldSkill == null)
                    return;

                try { oldSkill.prepare(data); } catch { }
                oldSkill.execute(null);
            }
            catch
            {
            }
        }

        private static void TryExecuteClientNewSkill(Mob mob, string rawSkillId, int? data, int targetUserId)
        {
            if (mob == null || string.IsNullOrWhiteSpace(rawSkillId))
                return;

            try
            {
                TrySetClientMobAttackTarget(mob, targetUserId);

                var skillId = rawSkillId.AsHaxeString();
                var skill = mob.getSkill(skillId) as MobSkill;
                if (skill == null)
                    return;

                try { skill.prepare(data); } catch { }
                skill.execute(null);
            }
            catch
            {
            }
        }

        private static void TryApplyClientContactAttack(Mob mob, int targetUserId)
        {
            try
            {
                var target = ResolveClientAttackTargetEntity(mob, targetUserId);
                if (target == null)
                    return;

                TrySetMobAttackTargetsExact(mob, target);
                mob.contactAttack(target);
            }
            catch
            {
            }
        }

        private static void TrySetClientMobAttackTarget(Mob mob, int targetUserId)
        {
            var target = ResolveClientAttackTargetEntity(mob, targetUserId);
            if (target == null)
                return;

            TrySetMobAttackTargetsExact(mob, target);
        }

        private static Entity? ResolveClientAttackTargetEntity(Mob mob, int targetUserId)
        {
            if (targetUserId > 0)
            {
                var net = GameMenu.NetRef;
                var localId = net?.id ?? 0;
                if (localId > 0)
                {
                    if (targetUserId == localId)
                        return ModEntry.me ?? ModCore.Modules.Game.Instance?.HeroInstance;

                    if (ModEntry.TryGetClientIndex(localId, targetUserId, out var index))
                    {
                        var client = ModEntry.clients[index];
                        if (client != null)
                            return client;
                    }
                }
            }

            try
            {
                return mob.nemesisTarget;
            }
            catch
            {
                return null;
            }
        }

        private static int ResolveLocalIndexByCoordinatesLocked(NetNode.MobStateSnapshot hostState)
        {
            return ResolveLocalIndexByCoordinatesLocked(hostState.Index, hostState.X, hostState.Y);
        }

        private static int ResolveLocalIndexByCoordinatesLocked(int hostIndex, double hostX, double hostY)
        {
            if (hostToLocalIndices.TryGetValue(hostIndex, out var mappedIndex))
            {
                if (IsValidLocalMobIndexLocked(mappedIndex))
                    return mappedIndex;

                hostToLocalIndices.Remove(hostIndex);
                localToHostIndices.Remove(mappedIndex);
            }

            var bestIndex = -1;
            var bestDistance = double.MaxValue;

            for (int i = 0; i < trackedMobs.Count; i++)
            {
                if (!IsValidLocalMobIndexLocked(i))
                    continue;

                if (localToHostIndices.TryGetValue(i, out var boundHost) && boundHost != hostIndex)
                    continue;

                var mob = trackedMobs[i];
                var x = mob.spr?.x ?? mob.cx;
                var y = mob.spr?.y ?? mob.cy;
                var dx = x - hostX;
                var dy = y - hostY;
                var distSq = dx * dx + dy * dy;

                if (distSq < bestDistance)
                {
                    bestDistance = distSq;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0 && bestDistance <= MaxCoordinateMatchDistanceSq)
            {
                hostToLocalIndices[hostIndex] = bestIndex;
                localToHostIndices[bestIndex] = hostIndex;
                return bestIndex;
            }

            if (hostIndex >= 0 && hostIndex < trackedMobs.Count && IsValidLocalMobIndexLocked(hostIndex))
            {
                hostToLocalIndices[hostIndex] = hostIndex;
                localToHostIndices[hostIndex] = hostIndex;
                return hostIndex;
            }

            return -1;
        }

        private static bool IsValidLocalMobIndexLocked(int index)
        {
            if (index < 0 || index >= trackedMobs.Count)
                return false;

            var mob = trackedMobs[index];
            if (mob == null || !IsSyncMob(mob))
                return false;

            return true;
        }

        private static void ApplyInterpolatedState(Mob self)
        {
            if (!TryGetTrackedIndex(self, out var localIndex))
                return;

            ClientMobState target;
            lock (Sync)
            {
                if (!clientMobTargets.TryGetValue(localIndex, out target))
                    return;
            }

            var currentX = GetWorldX(self);
            var currentY = GetWorldY(self);
            var lerpedX = currentX + (target.X - currentX) * ClientInterpolationAlpha;
            var lerpedY = ClientSyncVerticalPosition
                ? currentY + (target.Y - currentY) * ClientInterpolationAlpha
                : currentY;

            try
            {
                if (ClientSyncVerticalPosition)
                    self.setPosPixel(lerpedX, lerpedY);
                else
                    SetWorldXKeepingY(self, lerpedX);
            }
            catch
            {
                if (self.spr != null)
                {
                    self.spr.x = lerpedX;
                    if (ClientSyncVerticalPosition)
                        self.spr.y = lerpedY;
                }
            }

            try
            {
                self.dx = 0;
                self.bdx = 0;
                if (ClientSyncVerticalPosition)
                {
                    self.dy = 0;
                    self.bdy = 0;
                    self.fallStartY = lerpedY;
                }
                self.hasGravity = true;
            }
            catch
            {
            }

            if (target.Dir != 0)
                self.dir = target.Dir;

            if (target.MaxLife > 0 && self.maxLife != target.MaxLife)
                self.maxLife = target.MaxLife;
            if (target.Life >= 0 && self.life != target.Life)
                self.life = target.Life;
        }

        private static void ApplyClientAnimationStateBeforeUpdate(Mob self)
        {
            if (!TryGetTrackedIndex(self, out var localIndex))
                return;

            ClientMobState target;
            lock (Sync)
            {
                if (!clientMobTargets.TryGetValue(localIndex, out target))
                    return;
            }

            if (target.Dir != 0)
                self.dir = target.Dir;

            ApplyAnimPayload(self, target.AnimPayload);
        }

        private static void ConsumeIncomingMobHits(NetNode net)
        {
            if (!net.TryConsumeMobHits(out var hits))
                return;

            ApplyIncomingMobHits(hits);
        }

        private static void ApplyIncomingMobHits(IReadOnlyList<NetNode.MobHit> hits)
        {
            if (hits == null || hits.Count == 0)
                return;

            lock (Sync)
            {
                foreach (var hit in hits)
                {
                    var mob = ResolveMobFromHitLocked(hit);
                    if (mob == null)
                        continue;

                    var prevLife = mob.life;
                    var maxLife = System.Math.Max(1, mob.maxLife);
                    var targetLife = System.Math.Clamp(hit.Hp, 0, maxLife);

                    if (targetLife >= prevLife)
                        continue;

                    mob.life = targetLife;
                    if (targetLife <= 0 && prevLife > 0)
                    {
                        try
                        {
                            mob.onDie();
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        private static Mob? ResolveMobFromHitLocked(NetNode.MobHit hit)
        {
            if (hit.MobIndex >= 0 && hit.MobIndex < trackedMobs.Count)
            {
                var byIndex = trackedMobs[hit.MobIndex];
                if (byIndex != null)
                {
                    var idxX = byIndex.spr?.x ?? byIndex.cx;
                    var idxY = byIndex.spr?.y ?? byIndex.cy;
                    var idxDx = idxX - hit.X;
                    var idxDy = idxY - hit.Y;
                    if (idxDx * idxDx + idxDy * idxDy <= MaxCoordinateMatchDistanceSq)
                        return byIndex;
                }
            }

            Mob? best = null;
            var bestDist = double.MaxValue;

            for (int i = 0; i < trackedMobs.Count; i++)
            {
                var mob = trackedMobs[i];
                if (mob == null)
                    continue;

                var x = mob.spr?.x ?? mob.cx;
                var y = mob.spr?.y ?? mob.cy;
                var dx = x - hit.X;
                var dy = y - hit.Y;
                var distSq = dx * dx + dy * dy;
                if (distSq < bestDist)
                {
                    bestDist = distSq;
                    best = mob;
                }
            }

            if (bestDist <= MaxCoordinateMatchDistanceSq)
                return best;

            return null;
        }
    }
}
