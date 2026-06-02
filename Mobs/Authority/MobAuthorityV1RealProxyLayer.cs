using System.Diagnostics;
using System.Globalization;
using dc;
using dc.en;
using dc.pr;
using dc.tool.atk;
using HaxeProxy.Runtime;
using ModCore.Events;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod.Mobs.Authority;

internal sealed class MobAuthorityV1RealProxyLayer : IOnHeroUpdate, IEventReceiver
{
    internal const bool Enabled = true;

    private sealed class ProxyMobState
    {
        public Mob? Mob;
        public string LevelId = string.Empty;
        public int HostUserId;
        public int NetMobId;
        public double X;
        public double Y;
        public int Dir;
        public int Life;
        public int MaxLife;
        public string Type = string.Empty;
        public string AnimGroup = string.Empty;
        public long CreatedTick;
        public long LastSeenTick;
        public long LastLocalDamageFeedbackTick;
        public long LastLocalHitRequestTick;
    }

    private const double StaleSeconds = 1.50;
    private const double SpawnSettleSeconds = 1.10;
    private const double PositionSnapDistancePx = 96.0;
    private const double PositionLerpPerSecond = 22.0;
    private const double ProxyAggroRangePx = 560.0;
    private const double LocalDamageFeedbackCooldownSeconds = 0.22;
    private const double LocalHitRequestCooldownSeconds = 0.08;
    private const double DefaultProxyHitRadiusPx = 150.0;
    private const int DefaultProxyHitDamage = 25;
    private const int MaxProxyHitDamage = 1000;
    private static long s_nextProxyAttackId;

    private static readonly object Sync = new();
    private static readonly HashSet<Mob> ProxyMobs = new(ReferenceComparer<Mob>.Instance);
    private static readonly Dictionary<Mob, ProxyMobState> ProxyStateByMob = new(ReferenceComparer<Mob>.Instance);
    private static bool s_hooksInstalled;

    private readonly Dictionary<string, ProxyMobState> _proxies = new(StringComparer.Ordinal);
    private Level? _lastLevel;

    public MobAuthorityV1RealProxyLayer()
    {
        EventSystem.AddReceiver(this);
        InstallHooks();
    }

    void IOnHeroUpdate.OnHeroUpdate(double dt)
    {
        var hero = ModEntry.me;
        var net = GameMenu.NetRef;
        if (!Enabled || !MobAuthorityV1Runtime.IsAuthorityModeEnabled() || hero == null || net == null || !net.IsAlive || net.IsHost)
        {
            ResetAll();
            return;
        }

        var level = hero._level ?? ModEntry.Instance?.game?.curLevel;
        if (!ReferenceEquals(level, _lastLevel))
        {
            ResetAll();
            _lastLevel = level;
        }

        ConsumeSpawns(net, level);
        ConsumeDespawns(net, level);
        ConsumeStates(net, level, dt);
        ConsumeHitResults(net, level);
        DriveAllProxies(dt);
        PruneStale();
    }

    internal static bool IsProxyMob(Mob? mob)
    {
        if (mob == null)
            return false;

        lock (Sync)
            return ProxyMobs.Contains(mob);
    }

    private static void InstallHooks()
    {
        if (s_hooksInstalled)
            return;

        s_hooksInstalled = true;
        Hook_Mob.preUpdate += Hook_Mob_preUpdate;
        Hook_Mob.fixedUpdate += Hook_Mob_fixedUpdate;
        Hook_Mob.postUpdate += Hook_Mob_postUpdate;
        Hook_Mob.onDamage += Hook_Mob_onDamage;
        Hook_Mob.contactAttack += Hook_Mob_contactAttack;
        Hook_Mob.onTouch += Hook_Mob_onTouch;
        Hook_Mob.queueAttack += Hook_Mob_queueAttack;
        Hook_Mob.onDie += Hook_Mob_onDie;
    }

    private static void Hook_Mob_preUpdate(Hook_Mob.orig_preUpdate orig, Mob self)
    {
        if (TryGetProxy(self, out var proxy))
        {
            ApplyPresentationProxyFrame(proxy, self);
            return;
        }

        if (IsProxyMob(self))
        {
            EnsureProxyTarget(self);
            KeepProxyRenderable(self);
        }

        orig(self);
    }

    private static void Hook_Mob_fixedUpdate(Hook_Mob.orig_fixedUpdate orig, Mob self)
    {
        if (TryGetProxy(self, out var proxy))
        {
            ApplyPresentationProxyFrame(proxy, self);
            return;
        }

        var isProxy = IsProxyMob(self);
        if (isProxy)
        {
            EnsureProxyTarget(self);
            KeepProxyRenderable(self);
        }

        orig(self);

        if (isProxy)
            SnapProxyToAuthoritative(self);
    }

    private static void Hook_Mob_postUpdate(Hook_Mob.orig_postUpdate orig, Mob self)
    {
        if (TryGetProxy(self, out var proxy))
        {
            ApplyPresentationProxyFrame(proxy, self);
            return;
        }

        orig(self);

        if (IsProxyMob(self))
            SnapProxyToAuthoritative(self);
    }

    private static void Hook_Mob_onDamage(Hook_Mob.orig_onDamage orig, Mob self, AttackData attackData)
    {
        if (!TryGetProxy(self, out var proxy))
        {
            orig(self, attackData);
            return;
        }

        var authoritativeLife = proxy.Life > 0 ? proxy.Life : SafeRead(() => self.life, 1);
        var authoritativeMaxLife = proxy.MaxLife > 0 ? proxy.MaxLife : SafeRead(() => self.maxLife, 0);
        var localHeroAttack = IsLocalHeroAttack(attackData);
        var damageHint = ReadDamageHint(attackData);

        if (localHeroAttack)
            TrySendLocalHeroHitRequest(proxy, self, attackData, damageHint);

        if (localHeroAttack && ShouldAllowLocalDamageFeedback(proxy))
        {
            try
            {
                orig(self, attackData);
            }
            catch
            {
            }

            var afterOrigLife = SafeRead(() => self.life, authoritativeLife);
            if (afterOrigLife > 0 && afterOrigLife < authoritativeLife)
                damageHint = System.Math.Max(damageHint, authoritativeLife - afterOrigLife);
        }

        var displayLife = authoritativeLife;
        if (localHeroAttack && damageHint > 0 && authoritativeLife > 1)
        {
            displayLife = System.Math.Max(1, authoritativeLife - damageHint);
            proxy.Life = displayLife;
        }

        RestoreProxyLife(self, displayLife, authoritativeMaxLife);
    }

    private static void Hook_Mob_contactAttack(Hook_Mob.orig_contactAttack orig, Mob self, Entity target)
    {
        orig(self, target);
    }

    private static void Hook_Mob_onTouch(Hook_Mob.orig_onTouch orig, Mob self, Entity target)
    {
        orig(self, target);
    }

    private static void Hook_Mob_queueAttack(Hook_Mob.orig_queueAttack orig, Mob self, dc.tool.skill.OldMobSkill skill, bool requiresTargetInArea, int? data)
    {
        orig(self, skill, requiresTargetInArea, data);
    }

    private static void Hook_Mob_onDie(Hook_Mob.orig_onDie orig, Mob self)
    {
        if (IsProxyMob(self))
        {
            try { if (self.life <= 0) self.life = 1; } catch { }
            return;
        }

        orig(self);
    }

    private void ConsumeSpawns(NetNode net, Level? level)
    {
        if (!net.TryConsumeMobV1Spawns(out var spawns))
            return;

        var localLevelId = GetLevelId(level);
        for (int i = 0; i < spawns.Count; i++)
        {
            var spawn = spawns[i];
            if (!ShouldAcceptRemote(spawn.HostUserId, spawn.NetMobId, spawn.LevelId, localLevelId))
                continue;

            var proxy = GetOrCreateProxy(spawn.HostUserId, spawn.NetMobId);
            ApplyState(proxy, spawn.LevelId, spawn.X, spawn.Y, spawn.Dir, spawn.Life, spawn.MaxLife, spawn.Type, spawn.AnimGroup);
            EnsureMob(proxy, level, snap: true);
        }
    }

    private void ConsumeDespawns(NetNode net, Level? level)
    {
        if (!net.TryConsumeMobV1Despawns(out var despawns))
            return;

        var localLevelId = GetLevelId(level);
        for (int i = 0; i < despawns.Count; i++)
        {
            var despawn = despawns[i];
            if (!ShouldAcceptRemote(despawn.HostUserId, despawn.NetMobId, despawn.LevelId, localLevelId))
                continue;

            RemoveProxy(despawn.HostUserId, despawn.NetMobId);
        }
    }

    private void ConsumeStates(NetNode net, Level? level, double dt)
    {
        if (!net.TryConsumeMobV1States(out var states))
            return;

        var localLevelId = GetLevelId(level);
        for (int i = 0; i < states.Count; i++)
        {
            var state = states[i];
            if (!ShouldAcceptRemote(state.HostUserId, state.NetMobId, state.LevelId, localLevelId))
                continue;

            var proxy = GetOrCreateProxy(state.HostUserId, state.NetMobId);
            ApplyState(proxy, state.LevelId, state.X, state.Y, state.Dir, state.Life, state.MaxLife, state.Type, state.AnimGroup);
            EnsureMob(proxy, level, snap: false);
            DriveProxy(proxy, dt);
        }
    }

    private void ConsumeHitResults(NetNode net, Level? level)
    {
        if (!net.TryConsumeMobV1HitResults(out var results))
            return;

        var localLevelId = GetLevelId(level);
        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            if (!result.Accepted || !ShouldAcceptRemote(result.HostUserId, result.NetMobId, result.LevelId, localLevelId))
                continue;
            if (!_proxies.TryGetValue(BuildKey(result.HostUserId, result.NetMobId), out var proxy))
                continue;

            proxy.Life = result.Life;
            proxy.MaxLife = result.MaxLife;
            if (result.Death || result.Life <= 0)
            {
                RemoveProxy(result.HostUserId, result.NetMobId);
                continue;
            }
        }
    }

    private ProxyMobState GetOrCreateProxy(int hostUserId, int netMobId)
    {
        var key = BuildKey(hostUserId, netMobId);
        if (_proxies.TryGetValue(key, out var proxy))
            return proxy;

        proxy = new ProxyMobState { HostUserId = hostUserId, NetMobId = netMobId };
        _proxies[key] = proxy;
        return proxy;
    }

    private static void ApplyState(ProxyMobState proxy, string levelId, double x, double y, int dir, int life, int maxLife, string type, string animGroup)
    {
        proxy.LevelId = levelId ?? string.Empty;
        proxy.X = x;
        proxy.Y = y;
        proxy.Dir = dir;
        proxy.Life = life;
        proxy.MaxLife = maxLife;
        proxy.Type = type ?? proxy.Type;
        proxy.AnimGroup = animGroup ?? string.Empty;
        proxy.LastSeenTick = Stopwatch.GetTimestamp();
        MobAuthorityV1ProxyRegistry.Upsert(proxy.HostUserId, proxy.LevelId, proxy.NetMobId, proxy.X, proxy.Y);
    }

    private void EnsureMob(ProxyMobState proxy, Level? level, bool snap)
    {
        if (level == null || string.IsNullOrWhiteSpace(proxy.Type))
            return;
        if (proxy.Mob != null && SafeRead(() => !proxy.Mob.destroyed && ReferenceEquals(proxy.Mob._level, level), false))
            return;

        RemoveMob(proxy.Mob);
        proxy.Mob = CreateProxyMob(level, proxy.Type, proxy.X, proxy.Y, proxy.Life, proxy.MaxLife);
        if (proxy.Mob == null)
            return;

        proxy.CreatedTick = Stopwatch.GetTimestamp();
        lock (Sync)
        {
            ProxyMobs.Add(proxy.Mob);
            ProxyStateByMob[proxy.Mob] = proxy;
        }

        if (snap)
            SetMobPixel(proxy.Mob, proxy.X, proxy.Y);
    }

    private static Mob? CreateProxyMob(Level level, string type, double x, double y, int life, int maxLife)
    {
        try
        {
            var cx = (int)System.Math.Floor(x / 24.0);
            var cy = (int)System.Math.Floor(y / 24.0);
            var template = new dc.level.Mob(type.AsHaxeString(), cx, cy, 0, 0, false);
            try { template.dir = 1; } catch { }
            var mob = level.attachMob(template);
            if (mob == null)
                return null;
            KeepProxyRenderable(mob);
            EnsureProxyTarget(mob);
            try { mob._targetable = true; } catch { }
            try { if (maxLife > 0) mob.maxLife = maxLife; } catch { }
            try { if (life > 0) mob.life = life; } catch { }
            SetMobPixel(mob, x, y);
            return mob;
        }
        catch
        {
            return null;
        }
    }

    private static void KeepProxyRenderable(Mob mob)
    {
        try { mob.visible = true; } catch { }
        try { if (mob.spr != null) mob.spr.visible = true; } catch { }
        try { mob.isOnScreen = true; } catch { }
        try { mob.isOutOfGame = false; } catch { }
        try { mob.lastOutOfGame = false; } catch { }
        try { if (mob.onScreenRecent < 120.0) mob.onScreenRecent = 120.0; } catch { }
    }

    private void DriveAllProxies(double dt)
    {
        if (_proxies.Count == 0)
            return;

        foreach (var proxy in _proxies.Values)
            DriveProxy(proxy, dt);
    }

    private static void DriveProxy(ProxyMobState proxy, double dt)
    {
        var mob = proxy.Mob;
        if (mob == null)
            return;

        KeepProxyRenderable(mob);
        EnsureProxyTarget(mob);
        var currentX = GetEntityX(mob);
        var currentY = GetEntityY(mob);
        var dx = proxy.X - currentX;
        var dy = proxy.Y - currentY;
        var distSq = dx * dx + dy * dy;
        var settling = IsInSpawnSettleWindow(proxy);
        var syncY = ShouldSyncProxyY(mob);
        var snapDistance = PositionSnapDistancePx;

        if (settling || distSq >= snapDistance * snapDistance)
        {
            SetProxyPosition(mob, proxy.X, syncY ? proxy.Y : currentY, syncY);
        }
        else
        {
            var alpha = 1.0 - System.Math.Exp(-PositionLerpPerSecond * System.Math.Max(0.0, dt));
            var nextY = syncY ? currentY + dy * alpha : currentY;
            SetProxyPosition(mob, currentX + dx * alpha, nextY, syncY);
        }

        StabilizeProxyMotion(mob, syncY ? proxy.Y : GetEntityY(mob), syncY);

        if (proxy.Dir != 0)
        {
            try { mob.dir = proxy.Dir; } catch { }
        }

        try { if (proxy.MaxLife > 0) mob.maxLife = proxy.MaxLife; } catch { }
        try { if (proxy.Life > 0) mob.life = proxy.Life; } catch { }

        RestoreProxyLife(mob, proxy.Life, proxy.MaxLife);

        TryPlayAnim(mob, ResolvePresentationAnimGroup(mob, proxy.AnimGroup));
    }

    private static void SnapProxyToAuthoritative(Mob mob)
    {
        ProxyMobState? proxy;
        lock (Sync)
        {
            if (!ProxyStateByMob.TryGetValue(mob, out proxy))
                return;
        }

        KeepProxyRenderable(mob);
        EnsureProxyTarget(mob);
        var syncY = ShouldSyncProxyY(mob);
        SetProxyPosition(mob, GetEntityX(mob), syncY ? proxy.Y : GetEntityY(mob), syncY);
        StabilizeProxyMotion(mob, syncY ? proxy.Y : GetEntityY(mob), syncY);

        if (proxy.Dir != 0)
        {
            try { mob.dir = proxy.Dir; } catch { }
        }

        if (IsFallingLikeAnim(SafeRead(() => mob.spr?.groupName?.ToString() ?? string.Empty, string.Empty)))
            TryPlayAnim(mob, ResolvePresentationAnimGroup(mob, proxy.AnimGroup));
    }

    private static void StabilizeProxyMotion(Mob mob, double y, bool syncY)
    {
        try { mob.dx = 0.0; } catch { }
        try { mob.bdx = 0.0; } catch { }
        try { mob.dy = 0.0; } catch { }
        try { mob.bdy = 0.0; } catch { }
        if (syncY)
        {
            try { mob.fallStartY = y; } catch { }
        }
    }

    private static void SnapProxyYToAuthoritative(Mob mob, double y)
    {
        var x = GetEntityX(mob);
        SetMobPixel(mob, x, y);
    }

    private static void EnsureProxyTarget(Mob mob)
    {
        var hero = ModEntry.me;
        if (mob == null || hero == null)
            return;

        if (!IsHeroNearMob(mob, hero, ProxyAggroRangePx))
            return;

        try { mob._targetable = true; } catch { }
        try { mob.setAttackTarget(hero); } catch { }
        try { mob.setNemesisTarget(hero); } catch { }
    }

    private static bool HasLocalCombatTarget(Mob mob)
    {
        var hero = ModEntry.me;
        if (mob == null || hero == null)
            return false;

        try
        {
            if (ReferenceEquals(mob.aTarget, hero))
                return true;
        }
        catch
        {
        }

        try
        {
            if (ReferenceEquals(mob.nemesisTarget, hero))
                return true;
        }
        catch
        {
        }

        return false;
    }

    private static void ApplyPresentationProxyFrame(ProxyMobState proxy, Mob mob)
    {
        KeepProxyRenderable(mob);
        var syncY = ShouldSyncProxyY(mob);
        SetProxyPosition(mob, proxy.X, syncY ? proxy.Y : GetEntityY(mob), syncY);
        StabilizeProxyMotion(mob, syncY ? proxy.Y : GetEntityY(mob), syncY);
        if (proxy.Dir != 0)
        {
            try { mob.dir = proxy.Dir; } catch { }
        }

        RestoreProxyLife(mob, proxy.Life, proxy.MaxLife);
        TryPlayAnim(mob, ResolvePresentationAnimGroup(mob, proxy.AnimGroup));
    }

    private static bool ShouldSyncProxyY(Mob mob)
    {
        return SafeRead(() => !mob.hasGravity, false);
    }

    private static bool TryGetProxy(Mob mob, out ProxyMobState proxy)
    {
        lock (Sync)
            return ProxyStateByMob.TryGetValue(mob, out proxy!);
    }

    private static bool IsLocalHeroAttack(AttackData? attackData)
    {
        if (attackData == null)
            return false;

        var hero = ModEntry.me;
        if (hero == null)
            return false;

        try
        {
            if (ReferenceEquals(attackData.source, hero))
                return true;
        }
        catch
        {
        }

        try
        {
            if (ReferenceEquals(attackData.carrier, hero))
                return true;
        }
        catch
        {
        }

        return false;
    }

    private static bool ShouldAllowLocalDamageFeedback(ProxyMobState proxy)
    {
        var now = Stopwatch.GetTimestamp();
        if (proxy.LastLocalDamageFeedbackTick != 0 &&
            Stopwatch.GetElapsedTime(proxy.LastLocalDamageFeedbackTick, now).TotalSeconds < LocalDamageFeedbackCooldownSeconds)
        {
            return false;
        }

        proxy.LastLocalDamageFeedbackTick = now;
        return true;
    }

    private static bool IsInSpawnSettleWindow(ProxyMobState proxy)
    {
        return proxy.CreatedTick != 0 &&
               Stopwatch.GetElapsedTime(proxy.CreatedTick, Stopwatch.GetTimestamp()).TotalSeconds < SpawnSettleSeconds;
    }

    private static void TrySendLocalHeroHitRequest(ProxyMobState proxy, Mob mob, AttackData? attackData, int damageHint)
    {
        var net = GameMenu.NetRef;
        var hero = ModEntry.me ?? ModCore.Modules.Game.Instance?.HeroInstance;
        if (net == null || !net.IsAlive || net.IsHost || hero == null)
            return;
        if (proxy.NetMobId <= 0)
            return;

        var now = Stopwatch.GetTimestamp();
        if (proxy.LastLocalHitRequestTick != 0 &&
            Stopwatch.GetElapsedTime(proxy.LastLocalHitRequestTick, now).TotalSeconds < LocalHitRequestCooldownSeconds)
        {
            return;
        }

        proxy.LastLocalHitRequestTick = now;
        damageHint = System.Math.Clamp(damageHint > 0 ? damageHint : DefaultProxyHitDamage, 1, MaxProxyHitDamage);
        var attackId = System.Threading.Interlocked.Increment(ref s_nextProxyAttackId);
        var sentAtSeconds = (double)now / Stopwatch.Frequency;

        net.SendMobV1HitRequest(
            proxy.LevelId,
            proxy.NetMobId,
            GetEntityX(mob),
            GetEntityY(mob),
            damageHint,
            "proxy-onDamage",
            GetEntityX(hero),
            GetEntityY(hero),
            SafeRead(() => hero.dir, 0),
            attackId,
            sentAtSeconds,
            DefaultProxyHitRadiusPx);
    }

    private static int ReadDamageHint(AttackData? attackData)
    {
        if (attackData == null)
            return 0;

        var damage = 0;
        damage = System.Math.Max(damage, SafeRead(() => attackData.inflictedDmg, 0));
        damage = System.Math.Max(damage, SafeRead(() => attackData.finalDmg, 0));
        var rawFinal = SafeRead(() => attackData.rawFinalDmg, 0.0);
        if (rawFinal > 0.0)
            damage = System.Math.Max(damage, (int)System.Math.Round(rawFinal));

        try
        {
            damage = System.Math.Max(damage, attackData.baseDmg switch
            {
                int value => value,
                double value => (int)System.Math.Round(value),
                float value => (int)System.Math.Round(value),
                long value => value > int.MaxValue ? int.MaxValue : (int)value,
                _ => 0
            });
        }
        catch
        {
        }

        return System.Math.Clamp(damage, 0, MaxProxyHitDamage);
    }

    private static void RestoreProxyLife(Mob mob, int life, int maxLife)
    {
        try { if (maxLife > 0) mob.maxLife = maxLife; } catch { }
        try { if (life > 0) mob.life = life; } catch { }
        try { if (mob.life <= 0) mob.life = 1; } catch { }
    }

    private static bool IsHeroNearMob(Mob mob, Hero hero, double range)
    {
        var dx = GetEntityX(mob) - GetEntityX(hero);
        var dy = GetEntityY(mob) - GetEntityY(hero);
        return dx * dx + dy * dy <= range * range;
    }

    private static void TryPlayAnim(Mob mob, string group)
    {
        if (string.IsNullOrWhiteSpace(group))
            return;

        try
        {
            var current = mob.spr?.groupName?.ToString() ?? string.Empty;
            if (string.Equals(current, group, StringComparison.Ordinal))
                return;

            mob.spr?.get_anim().play(group.AsHaxeString(), null, null).loop(null);
        }
        catch
        {
        }
    }

    private static string ResolvePresentationAnimGroup(Mob mob, string group)
    {
        var cleaned = (group ?? string.Empty).Trim();
        if (ShouldSyncProxyY(mob) || !IsFallingLikeAnim(cleaned))
            return cleaned;

        return ResolveExistingAnimGroup(mob, "idle", "Idle", "stand", "Stand", "run", "Run", cleaned);
    }

    private static bool IsFallingLikeAnim(string group)
    {
        if (string.IsNullOrWhiteSpace(group))
            return false;

        return group.Contains("fall", StringComparison.OrdinalIgnoreCase) ||
               group.Contains("jumpDown", StringComparison.OrdinalIgnoreCase) ||
               group.Contains("land", StringComparison.OrdinalIgnoreCase) ||
               group.Contains("drop", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveExistingAnimGroup(Mob mob, params string[] candidates)
    {
        for (int i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            try
            {
                var groups = mob.spr?.lib?.groups;
                if (groups == null || groups.exists(candidate.AsHaxeString()))
                    return candidate;
            }
            catch
            {
                return candidate;
            }
        }

        return "idle";
    }

    private void PruneStale()
    {
        if (_proxies.Count == 0)
            return;

        var now = Stopwatch.GetTimestamp();
        List<string>? stale = null;
        foreach (var pair in _proxies)
        {
            var proxy = pair.Value;
            var remove = proxy.LastSeenTick == 0 ||
                         Stopwatch.GetElapsedTime(proxy.LastSeenTick, now).TotalSeconds > StaleSeconds ||
                         SafeRead(() => proxy.Mob == null || proxy.Mob.destroyed || proxy.Mob._level == null, true);
            if (!remove)
                continue;

            stale ??= new List<string>();
            stale.Add(pair.Key);
        }

        if (stale == null)
            return;

        for (int i = 0; i < stale.Count; i++)
            RemoveProxy(stale[i]);
    }

    private void ResetAll()
    {
        if (_proxies.Count > 0)
        {
            foreach (var proxy in _proxies.Values)
            {
                MobAuthorityV1ProxyRegistry.Remove(proxy.HostUserId, proxy.NetMobId);
                RemoveMob(proxy.Mob);
            }
            _proxies.Clear();
        }

        _lastLevel = null;
    }

    private void RemoveProxy(int hostUserId, int netMobId)
    {
        RemoveProxy(BuildKey(hostUserId, netMobId));
    }

    private void RemoveProxy(string key)
    {
        if (!_proxies.TryGetValue(key, out var proxy))
            return;

        MobAuthorityV1ProxyRegistry.Remove(proxy.HostUserId, proxy.NetMobId);
        RemoveMob(proxy.Mob);
        _proxies.Remove(key);
    }

    private static void RemoveMob(Mob? mob)
    {
        if (mob == null)
            return;

        lock (Sync)
        {
            ProxyMobs.Remove(mob);
            ProxyStateByMob.Remove(mob);
        }

        try { mob.destroy(); } catch { }
        try { mob.dispose(); } catch { }
    }

    private static bool ShouldAcceptRemote(int hostUserId, int netMobId, string levelId, string localLevelId)
    {
        var net = GameMenu.NetRef;
        if (net == null || hostUserId <= 0 || hostUserId == net.id || netMobId < 0)
            return false;

        return string.IsNullOrWhiteSpace(levelId) ||
               string.IsNullOrWhiteSpace(localLevelId) ||
               string.Equals(levelId, localLevelId, StringComparison.Ordinal);
    }

    private static string BuildKey(int hostUserId, int netMobId)
    {
        return hostUserId.ToString(CultureInfo.InvariantCulture) + ":" + netMobId.ToString(CultureInfo.InvariantCulture);
    }

    private static string GetLevelId(Level? level)
    {
        return SafeRead(() => level?.map?.id?.ToString() ?? string.Empty, string.Empty).Trim();
    }

    private static double GetEntityX(Entity entity)
    {
        return SafeRead(() => entity.spr?.x ?? (entity.cx + entity.xr) * 24.0, 0.0);
    }

    private static double GetEntityY(Entity entity)
    {
        return SafeRead(() => entity.spr?.y ?? (entity.cy + entity.yr) * 24.0, 0.0);
    }

    private static void SetProxyPosition(Mob mob, double x, double y, bool syncY)
    {
        if (syncY)
        {
            SetMobPixel(mob, x, y);
            return;
        }

        SetMobPixel(mob, x, GetEntityY(mob));
    }

    private static void SetMobPixel(Mob mob, double x, double y)
    {
        try
        {
            mob.setPosPixel(x, y);
            SyncMobGridPosition(mob, x, y);
            return;
        }
        catch
        {
        }

        try { if (mob.spr != null) { mob.spr.x = x; mob.spr.y = y; } } catch { }
        SyncMobGridPosition(mob, x, y);
    }

    private static void SyncMobGridPosition(Mob mob, double x, double y)
    {
        try
        {
            var cx = (int)System.Math.Floor(x / 24.0);
            var cy = (int)System.Math.Floor(y / 24.0);
            mob.cx = cx;
            mob.cy = cy;
            mob.xr = x / 24.0 - cx;
            mob.yr = y / 24.0 - cy;
        }
        catch
        {
        }
    }

    private static bool TryInvoke(object target, string name, params object?[] args)
    {
        try
        {
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;
            var methods = target.GetType().GetMethods(flags);
            for (int i = 0; i < methods.Length; i++)
            {
                var method = methods[i];
                if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                    continue;
                if (method.GetParameters().Length != args.Length)
                    continue;

                method.Invoke(target, args);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static T SafeRead<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch { return fallback; }
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static readonly ReferenceComparer<T> Instance = new();
        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
