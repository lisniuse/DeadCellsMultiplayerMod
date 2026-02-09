using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using dc;
using dc.en;
using dc.h2d;
using dc.pr;
using dc.tool.atk;
using dc.tool._Cooldown;
using DeadCellsMultiplayerMod.Interface.ModuleInitializing;
using ModCore.Events;
using ModCore.Modules;

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

        private static Level? currentLevel;
        private static long lastHostStateSendTick;

        private const double HostStateSendRateHz = 20.0;
        private const double ClientInterpolationAlpha = 0.25;
        private const double ClientAiLockSeconds = 0.3;
        private const bool ClientSyncVerticalPosition = false;
        private const double MaxCoordinateMatchDistance = 96.0;
        private const double MaxCoordinateMatchDistanceSq = MaxCoordinateMatchDistance * MaxCoordinateMatchDistance;

        private readonly struct ClientMobState
        {
            public readonly double X;
            public readonly double Y;
            public readonly int Dir;
            public readonly int Life;
            public readonly int MaxLife;
            public readonly int CdSignature;

            public ClientMobState(double x, double y, int dir, int life, int maxLife, int cdSignature)
            {
                X = x;
                Y = y;
                Dir = dir;
                Life = life;
                MaxLife = maxLife;
                CdSignature = cdSignature;
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
            Hook_Mob.fixedUpdate += Hook_Mob_fixedupdate;
            Hook_Mob.onDamage += Hook_Mob_onDamage;
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

            if (clid is not Mob mob || !IsSyncMob(mob))
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

        private void Hook_Mob_fixedupdate(Hook_Mob.orig_fixedUpdate orig, Mob self)
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

            if (isClient && IsSyncMob(self))
            {
                // Client does not run real mob AI, it only mirrors host states.
                TryLockMobAi(self, ClientAiLockSeconds);
            }

            orig(self);

            if (!IsSyncMob(self))
                return;

            if (isHost)
            {
                ConsumeIncomingMobHits(net!);
                TrySendHostMobStates(net!);
            }
            else if (isClient)
            {
                ConsumeIncomingHostMobStates(net!);
                ApplyInterpolatedState(self);
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

            if (!IsSyncMob(self) || IsOutOfGame(self))
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

        private void Hook_Mob_setNemesisTarget(Hook_Mob.orig_setNemesisTarget orig, Mob self, Entity e)
        {
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
            currentLevel = null;
            lastHostStateSendTick = 0;
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

        private static bool IsOutOfGame(Mob mob)
        {
            try
            {
                if (mob.isOutOfGame)
                    return true;
            }
            catch
            {
            }

            try
            {
                return mob._isOutOfGame();
            }
            catch
            {
                return false;
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

        private static int NormalizeDir(int dir)
        {
            if (dir < 0) return -1;
            if (dir > 0) return 1;
            return 0;
        }

        private static int BuildCdSignature(Mob mob)
        {
            var fast = mob.cd?.fastCheck;
            if (fast == null)
                return 0;

            var signature = 0;
            var accumulator = 0;
            try
            {
                var keys = fast.keys();
                while (keys != null && keys.hasNext.Invoke())
                {
                    var keyObj = keys.next.Invoke();
                    var valueObj = fast.get(keyObj);
                    var key = ReadCooldownKey(keyObj);
                    var frames = ReadCooldownFrames(valueObj);

                    unchecked
                    {
                        var entryHash = (key * 397) ^ frames;
                        signature ^= entryHash;
                        accumulator += (frames * 31) + key;
                    }
                }
            }
            catch
            {
                return 0;
            }

            unchecked
            {
                return (signature * 486187739) ^ accumulator;
            }
        }

        private static int ReadCooldownKey(object? keyObj)
        {
            try
            {
                return Convert.ToInt32(keyObj, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private static int ReadCooldownFrames(object? valueObj)
        {
            if (valueObj is CdInst inst)
            {
                try
                {
                    return (int)System.Math.Round(inst.frames);
                }
                catch
                {
                }
            }

            try
            {
                var raw = Convert.ToDouble(valueObj, CultureInfo.InvariantCulture);
                return (int)System.Math.Round(raw);
            }
            catch
            {
                return 0;
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
                    if (mob == null || IsOutOfGame(mob))
                        continue;

                    var x = mob.spr?.x ?? mob.cx;
                    var y = mob.spr?.y ?? mob.cy;
                    var dir = NormalizeDir(mob.dir);
                    var life = mob.life;
                    var maxLife = mob.maxLife;
                    var cd = BuildCdSignature(mob);

                    states.Add(new NetNode.MobStateSnapshot(i, x, y, dir, life, maxLife, cd));
                }
            }

            if (states.Count > 0)
                net.SendMobStates(states);
        }

        private static void ConsumeIncomingHostMobStates(NetNode net)
        {
            if (!net.TryConsumeMobStates(out var states))
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
                        state.CdSignature);
                }
            }
        }

        private static int ResolveLocalIndexByCoordinatesLocked(NetNode.MobStateSnapshot hostState)
        {
            if (hostToLocalIndices.TryGetValue(hostState.Index, out var mappedIndex))
            {
                if (IsValidLocalMobIndexLocked(mappedIndex))
                    return mappedIndex;

                hostToLocalIndices.Remove(hostState.Index);
                localToHostIndices.Remove(mappedIndex);
            }

            var bestIndex = -1;
            var bestDistance = double.MaxValue;

            for (int i = 0; i < trackedMobs.Count; i++)
            {
                if (!IsValidLocalMobIndexLocked(i))
                    continue;

                if (localToHostIndices.TryGetValue(i, out var boundHost) && boundHost != hostState.Index)
                    continue;

                var mob = trackedMobs[i];
                var x = mob.spr?.x ?? mob.cx;
                var y = mob.spr?.y ?? mob.cy;
                var dx = x - hostState.X;
                var dy = y - hostState.Y;
                var distSq = dx * dx + dy * dy;

                if (distSq < bestDistance)
                {
                    bestDistance = distSq;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0 && bestDistance <= MaxCoordinateMatchDistanceSq)
            {
                hostToLocalIndices[hostState.Index] = bestIndex;
                localToHostIndices[bestIndex] = hostState.Index;
                return bestIndex;
            }

            if (hostState.Index >= 0 && hostState.Index < trackedMobs.Count && IsValidLocalMobIndexLocked(hostState.Index))
            {
                hostToLocalIndices[hostState.Index] = hostState.Index;
                localToHostIndices[hostState.Index] = hostState.Index;
                return hostState.Index;
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

            return !IsOutOfGame(mob);
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

            var currentX = self.spr?.x ?? self.cx;
            var currentY = self.spr?.y ?? self.cy;
            var lerpedX = currentX + (target.X - currentX) * ClientInterpolationAlpha;
            var lerpedY = ClientSyncVerticalPosition
                ? currentY + (target.Y - currentY) * ClientInterpolationAlpha
                : currentY;

            try
            {
                self.setPosPixel(lerpedX, lerpedY);
            }
            catch
            {
                if (self.spr != null)
                {
                    self.spr.x = lerpedX;
                    self.spr.y = lerpedY;
                }
            }

            // Client-side mobs are host-driven; clear local velocity to avoid fall/impact side effects.
            try
            {
                self.dx = 0;
                self.bdx = 0;
                self.dy = 0;
                self.bdy = 0;
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

        private static void ConsumeIncomingMobHits(NetNode net)
        {
            if (!net.TryConsumeMobHits(out var hits))
                return;

            lock (Sync)
            {
                foreach (var hit in hits)
                {
                    var mob = ResolveMobFromHitLocked(hit);
                    if (mob == null || IsOutOfGame(mob))
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
                if (byIndex != null && !IsOutOfGame(byIndex))
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
                if (mob == null || IsOutOfGame(mob))
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
