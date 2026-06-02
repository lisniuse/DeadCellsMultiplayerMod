using ModCore.Events;
using ModCore.Events.Interfaces.Game;
using ModCore.Utilities;
using dc;
using dc.en;
using dc.pr;
using dc.tool.atk;
using DeadCellsMultiplayerMod.Ghost.GhostBase;
using System.Diagnostics;
using System.Reflection;

namespace DeadCellsMultiplayerMod.Mobs.Authority;

internal sealed class MobAuthorityV1Runtime : IOnFrameUpdate, IEventReceiver
{
    private const int DefaultClientHitDamage = 25;
    private const double DefaultHitValidationRangePx = 150.0;
    private const double MaxAcceptedHitRadiusPx = 220.0;
    private const double BackAttackGracePx = 36.0;
    private const double VerticalAttackGracePx = 96.0;
    private const double ReplayRememberSeconds = 30.0;
    private const double HostTargetInjectionRangePx = 420.0;
    private const double HostMobPlayerHitCooldownSeconds = 0.55;
    private const double ClientHitBlinkSeconds = 0.16;
    private const int MinClientHitDamage = 1;
    private const int MaxClientHitDamage = 120;
    private const int MinMobPlayerHitDamage = 1;
    private const int MaxMobPlayerHitDamage = 240;

    private readonly struct ProcessedAttack
    {
        public readonly long AttackId;
        public readonly long SeenTick;

        public ProcessedAttack(long attackId, long seenTick)
        {
            AttackId = attackId;
            SeenTick = seenTick;
        }
    }

    private readonly Serilog.ILogger _log;
    private readonly Dictionary<int, ProcessedAttack> _lastProcessedAttackByAttacker = new();
    private readonly Dictionary<string, long> _lastMobPlayerHitTicks = new(StringComparer.Ordinal);
    private bool _loggedActive;
    private static bool s_hooksInstalled;
    private static MobAuthorityV1Runtime? s_instance;

    public MobAuthorityV1Runtime(ModEntry entry)
    {
        _log = entry.Logger;
        s_instance = this;
        EventSystem.AddReceiver(this);
        InstallHooks();
    }

    public static bool IsAuthorityModeEnabled()
    {
        return MultiplayerSettingsStorage.EnableMobsSync &&
               MultiplayerSettingsStorage.IsModuleEnabled(DebugModuleId.MobsSynchronization);
    }

    public static bool IsProjectionOnlyModeEnabled()
    {
        return !MultiplayerSettingsStorage.EnableMobsSync &&
               !MultiplayerSettingsStorage.IsModuleEnabled(DebugModuleId.MobsSynchronization);
    }

    public static bool IsProjectionTransportEnabled()
    {
        return IsAuthorityModeEnabled() || IsProjectionOnlyModeEnabled();
    }

    void IOnFrameUpdate.OnFrameUpdate(double dt)
    {
        if (!IsAuthorityModeEnabled())
        {
            _loggedActive = false;
            _lastProcessedAttackByAttacker.Clear();
            _lastMobPlayerHitTicks.Clear();
            return;
        }

        if (!_loggedActive)
        {
            _loggedActive = true;
            _log.Information("[MobsAuthorityV1] Host-authority mob sync active.");
        }

        ProcessHitMessages();
    }

    private void ProcessHitMessages()
    {
        var net = GameMenu.NetRef;
        if (net == null || !net.IsAlive)
            return;

        if (net.IsHost)
        {
            InjectRemotePlayerTargets();
            ProcessHostHitRequests(net);
            return;
        }

        ProcessClientMobPlayerHits(net);
    }

    private static void InstallHooks()
    {
        if (s_hooksInstalled)
            return;

        s_hooksInstalled = true;
        Hook_Entity.onDamage += Hook_Entity_onDamage;
    }

    private void ProcessHostHitRequests(NetNode net)
    {
        if (!net.TryConsumeMobV1HitRequests(out var requests))
            return;

        PruneProcessedAttacks();
        var localLevelId = GetCurrentLevelId();
        for (int i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            var accepted = false;
            var reason = "mob_missing";
            var life = 0;
            var maxLife = 0;
            var damageApplied = 0;
            var death = false;

            if (IsReplayRequest(request))
            {
                reason = "replay";
            }
            else
            {
                MarkProcessedRequest(request);

                if (request.NetMobId > 0 && MobAuthorityV1Ids.TryGetMob(request.NetMobId, out var mob) && mob != null)
                {
                    life = SafeRead(() => mob.life, 0);
                    maxLife = SafeRead(() => mob.maxLife, maxLife);
                    var mobLevelId = SafeRead(() => mob._level?.map?.id?.ToString() ?? string.Empty, string.Empty);
                    var levelMatches = string.IsNullOrWhiteSpace(request.LevelId) ||
                                       string.IsNullOrWhiteSpace(mobLevelId) ||
                                       string.Equals(request.LevelId, mobLevelId, StringComparison.Ordinal);

                    if (!levelMatches)
                    {
                        reason = "level_mismatch";
                    }
                    else if (life <= 0)
                    {
                        reason = "already_dead";
                    }
                    else if (!IsHitRequestNearMob(request, mob))
                    {
                        reason = "range_mismatch";
                    }
                    else if (!IsHitDirectionValid(request, mob))
                    {
                        reason = "direction_mismatch";
                    }
                    else
                    {
                        accepted = true;
                        damageApplied = NormalizeClientDamage(request);
                        life = ApplyHostDamage(mob, damageApplied);
                        maxLife = SafeRead(() => mob.maxLife, maxLife);
                        death = life <= 0;
                        reason = "hit";
                    }
                }
            }

            var result = new NetNode.MobV1HitResult(
                net.id,
                request.AttackerUserId,
                string.IsNullOrWhiteSpace(request.LevelId) ? localLevelId : request.LevelId,
                request.NetMobId,
                accepted,
                life,
                maxLife,
                reason,
                damageApplied,
                death);
            net.SendMobV1HitResult(result);
        }
    }

    private void ProcessClientMobPlayerHits(NetNode net)
    {
        if (!net.TryConsumeMobV1PlayerHits(out var hits))
            return;

        var hero = ModEntry.me ?? ModCore.Modules.Game.Instance?.HeroInstance;
        if (hero == null)
            return;

        var localLevelId = GetCurrentLevelId();
        for (int i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            if (hit.TargetUserId != net.id)
                continue;
            if (!string.IsNullOrWhiteSpace(hit.LevelId) &&
                !string.IsNullOrWhiteSpace(localLevelId) &&
                !string.Equals(hit.LevelId, localLevelId, StringComparison.Ordinal))
            {
                continue;
            }

            ApplyClientHeroDamage(hero, hit.Damage, hit.X, hit.Y);
        }
    }

    private static void ApplyClientHeroDamage(Hero hero, int damage, double sourceX, double sourceY)
    {
        damage = System.Math.Clamp(damage, MinMobPlayerHitDamage, MaxMobPlayerHitDamage);
        try
        {
            var current = hero.life;
            if (current <= 0)
                return;

            hero.life = System.Math.Max(0, current - damage);
        }
        catch
        {
        }

        TryInvoke(hero, "colorBlink", 0xFF3333, 1.0, ClientHitBlinkSeconds);
        TryInvoke(hero, "bumpAwayFrom", CreateBumpAnchor(sourceX, sourceY), 0.8, true);

        try
        {
            if (hero.life <= 0)
                hero.kill();
        }
        catch
        {
        }
    }

    private static Entity? CreateBumpAnchor(double x, double y)
    {
        var hero = ModEntry.me;
        if (hero == null)
            return null;

        try
        {
            return hero;
        }
        catch
        {
            return null;
        }
    }

    private static void Hook_Entity_onDamage(Hook_Entity.orig_onDamage orig, Entity self, AttackData a)
    {
        CaptureHostMobDamageToRemotePlayer(self, a);
        orig(self, a);
    }

    private static void CaptureHostMobDamageToRemotePlayer(Entity self, AttackData attack)
    {
        if (!IsAuthorityModeEnabled())
            return;

        var net = GameMenu.NetRef;
        if (net == null || !net.IsAlive || !net.IsHost)
            return;
        if (self is not GhostKing ghost)
            return;
        if (!TryResolveRemoteGhostUserId(ghost, out var targetUserId))
            return;
        if (!TryResolveAttackMob(attack, out var mob) || mob == null)
            return;

        var level = SafeRead(() => mob._level, null);
        var netMobId = level != null ? MobAuthorityV1Ids.GetOrCreate(mob, level) : 0;
        if (netMobId <= 0)
            return;

        var runtime = s_instance;
        if (runtime != null && runtime.IsMobPlayerHitOnCooldown(targetUserId, netMobId))
            return;

        var damage = NormalizeMobPlayerDamage(attack);
        var hit = new NetNode.MobV1PlayerHit(
            net.id,
            targetUserId,
            SafeRead(() => level?.map?.id?.ToString() ?? GetCurrentLevelId(), GetCurrentLevelId()),
            netMobId,
            damage,
            GetEntityX(mob),
            GetEntityY(mob));
        net.SendMobV1PlayerHit(hit);
    }

    private bool IsMobPlayerHitOnCooldown(int targetUserId, int netMobId)
    {
        var key = targetUserId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                  netMobId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var now = Stopwatch.GetTimestamp();
        if (_lastMobPlayerHitTicks.TryGetValue(key, out var last) &&
            Stopwatch.GetElapsedTime(last, now).TotalSeconds < HostMobPlayerHitCooldownSeconds)
        {
            return true;
        }

        _lastMobPlayerHitTicks[key] = now;
        return false;
    }

    private static void InjectRemotePlayerTargets()
    {
        var level = ModEntry.me?._level ?? ModEntry.Instance?.game?.curLevel;
        if (level?.entities == null)
            return;

        var ghosts = BuildActiveRemoteGhosts(level);
        if (ghosts.Count == 0)
            return;

        var entities = level.entities;
        for (int i = 0; i < entities.length; i++)
        {
            if (entities.getDyn(i) is not Mob mob)
                continue;
            if (!IsActiveHostMob(mob, level))
                continue;

            var nearest = FindNearestGhost(mob, ghosts, HostTargetInjectionRangePx);
            if (nearest == null)
                continue;

            TrySetMobTarget(mob, nearest);
        }
    }

    private static List<GhostKing> BuildActiveRemoteGhosts(Level level)
    {
        var result = new List<GhostKing>();
        for (int i = 0; i < ModEntry.clients.Length; i++)
        {
            var ghost = ModEntry.clients[i];
            if (ghost == null)
                continue;

            var active = SafeRead(() => !ghost.destroyed && ghost.life > 0 && ghost._targetable, false);
            if (!active)
                continue;
            if (!SafeRead(() => ReferenceEquals(ghost._level, level), false))
                continue;

            result.Add(ghost);
        }

        return result;
    }

    private static GhostKing? FindNearestGhost(Mob mob, List<GhostKing> ghosts, double maxRange)
    {
        GhostKing? best = null;
        var bestDistSq = maxRange * maxRange;
        var mobX = GetEntityX(mob);
        var mobY = GetEntityY(mob);
        for (int i = 0; i < ghosts.Count; i++)
        {
            var ghost = ghosts[i];
            var dx = GetEntityX(ghost) - mobX;
            var dy = GetEntityY(ghost) - mobY;
            var distSq = dx * dx + dy * dy;
            if (distSq >= bestDistSq)
                continue;

            bestDistSq = distSq;
            best = ghost;
        }

        return best;
    }

    private static void TrySetMobTarget(Mob mob, GhostKing target)
    {
        if (TryInvoke(mob, "setAttackTarget", target))
            return;
        if (TryInvoke(mob, "set_aTarget", target))
            return;
        TrySetMember(mob, "aTarget", target);
        TryInvoke(mob, "setNemesisTarget", target);
    }

    private static bool TryResolveRemoteGhostUserId(GhostKing ghost, out int userId)
    {
        for (int i = 0; i < ModEntry.clients.Length; i++)
        {
            if (!ReferenceEquals(ModEntry.clients[i], ghost))
                continue;

            userId = ModEntry.clientIds[i];
            return userId > 0;
        }

        userId = 0;
        return false;
    }

    private static bool TryResolveAttackMob(AttackData attack, out Mob? mob)
    {
        mob = SafeRead(() => attack.source as Mob, null);
        if (mob != null)
            return true;

        mob = SafeRead(() => attack.carrier as Mob, null);
        return mob != null;
    }

    private static int NormalizeMobPlayerDamage(AttackData attack)
    {
        var damage = 0;
        damage = System.Math.Max(damage, SafeRead(() => attack.inflictedDmg, 0));
        damage = System.Math.Max(damage, SafeRead(() => attack.finalDmg, 0));
        var rawFinal = SafeRead(() => attack.rawFinalDmg, 0.0);
        if (rawFinal > 0.0)
            damage = System.Math.Max(damage, (int)System.Math.Round(rawFinal));
        if (damage <= 0)
            damage = DefaultClientHitDamage;
        return System.Math.Clamp(damage, MinMobPlayerHitDamage, MaxMobPlayerHitDamage);
    }

    private static bool IsHitRequestNearMob(NetNode.MobV1HitRequest request, dc.en.Mob mob)
    {
        var mobX = SafeRead(() => mob.spr?.x ?? (mob.cx + mob.xr) * 24.0, 0.0);
        var mobY = SafeRead(() => mob.spr?.y ?? (mob.cy + mob.yr) * 24.0, 0.0);
        var dx = mobX - request.X;
        var dy = mobY - request.Y;
        var hitRadius = request.HitRadius > 0.0
            ? System.Math.Min(request.HitRadius, MaxAcceptedHitRadiusPx)
            : DefaultHitValidationRangePx;
        return dx * dx + dy * dy <= hitRadius * hitRadius;
    }

    private static bool IsHitDirectionValid(NetNode.MobV1HitRequest request, dc.en.Mob mob)
    {
        if (request.HeroX == 0.0 && request.HeroY == 0.0)
            return true;

        var mobX = SafeRead(() => mob.spr?.x ?? (mob.cx + mob.xr) * 24.0, 0.0);
        var mobY = SafeRead(() => mob.spr?.y ?? (mob.cy + mob.yr) * 24.0, 0.0);
        var facing = request.HeroDir < 0 ? -1 : 1;
        var forward = (mobX - request.HeroX) * facing;
        var allowedForward = System.Math.Min(
            request.HitRadius > 0.0 ? request.HitRadius + BackAttackGracePx : DefaultHitValidationRangePx,
            MaxAcceptedHitRadiusPx + BackAttackGracePx);

        if (forward < -BackAttackGracePx || forward > allowedForward)
            return false;

        var verticalGrace = System.Math.Max(VerticalAttackGracePx, (request.HitRadius > 0.0 ? request.HitRadius * 0.7 : VerticalAttackGracePx));
        return System.Math.Abs(mobY - request.HeroY) <= verticalGrace;
    }

    private bool IsReplayRequest(NetNode.MobV1HitRequest request)
    {
        if (request.AttackId <= 0)
            return false;

        return _lastProcessedAttackByAttacker.TryGetValue(request.AttackerUserId, out var last) &&
               request.AttackId <= last.AttackId;
    }

    private void MarkProcessedRequest(NetNode.MobV1HitRequest request)
    {
        if (request.AttackId <= 0)
            return;

        _lastProcessedAttackByAttacker[request.AttackerUserId] = new ProcessedAttack(request.AttackId, Stopwatch.GetTimestamp());
    }

    private void PruneProcessedAttacks()
    {
        if (_lastProcessedAttackByAttacker.Count == 0)
            return;

        var now = Stopwatch.GetTimestamp();
        var maxAgeTicks = (long)(Stopwatch.Frequency * ReplayRememberSeconds);
        List<int>? stale = null;
        foreach (var pair in _lastProcessedAttackByAttacker)
        {
            if (now - pair.Value.SeenTick <= maxAgeTicks)
                continue;

            stale ??= new List<int>();
            stale.Add(pair.Key);
        }

        if (stale == null)
            return;

        for (int i = 0; i < stale.Count; i++)
            _lastProcessedAttackByAttacker.Remove(stale[i]);
    }

    private static int NormalizeClientDamage(NetNode.MobV1HitRequest request)
    {
        var damage = request.DamageHint > 0 ? request.DamageHint : DefaultClientHitDamage;
        return System.Math.Clamp(damage, MinClientHitDamage, MaxClientHitDamage);
    }

    private static int ApplyHostDamage(dc.en.Mob mob, int damage)
    {
        var currentLife = SafeRead(() => mob.life, 0);
        if (currentLife <= 0)
            return currentLife;

        var nextLife = System.Math.Max(0, currentLife - System.Math.Max(1, damage));
        try
        {
            mob.life = nextLife;
        }
        catch
        {
            return currentLife;
        }

        if (nextLife <= 0)
        {
            try { mob.onDie(); } catch { }
        }

        return nextLife;
    }

    private static string GetCurrentLevelId()
    {
        var level = ModEntry.me?._level ?? ModEntry.Instance?.game?.curLevel;
        return SafeRead(() => level?.map?.id?.ToString() ?? string.Empty, string.Empty).Trim();
    }

    private static bool IsActiveHostMob(Mob mob, Level level)
    {
        if (mob == null || level == null)
            return false;

        try
        {
            if (mob.destroyed || mob._level == null || !ReferenceEquals(mob._level, level))
                return false;
            return mob.life > 0;
        }
        catch
        {
            return false;
        }
    }

    private static double GetEntityX(Entity entity)
    {
        if (entity == null)
            return 0.0;
        return SafeRead(() => entity.spr?.x ?? (entity.cx + entity.xr) * 24.0, 0.0);
    }

    private static double GetEntityY(Entity entity)
    {
        if (entity == null)
            return 0.0;
        return SafeRead(() => entity.spr?.y ?? (entity.cy + entity.yr) * 24.0, 0.0);
    }

    private static bool TryInvoke(object target, string name, params object?[] args)
    {
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var methods = target.GetType().GetMethods(flags);
            for (int i = 0; i < methods.Length; i++)
            {
                var method = methods[i];
                if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                    continue;
                var parameters = method.GetParameters();
                if (parameters.Length != args.Length)
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

    private static void TrySetMember(object target, string name, object? value)
    {
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = target.GetType();
            var prop = type.GetProperty(name, flags);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(target, value);
                return;
            }

            var field = type.GetField(name, flags);
            field?.SetValue(target, value);
        }
        catch
        {
        }
    }

    private static T SafeRead<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch { return fallback; }
    }
}
