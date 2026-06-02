using System.Diagnostics;
using System.Globalization;
using dc;
using dc.en;
using dc.h2d;
using dc.libs.heaps.slib;
using dc.pr;
using DeadCellsMultiplayerMod.Mobs.Authority;
using HaxeProxy.Runtime;
using ModCore.Events;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod.Mobs;

internal sealed class MobProjectionLayer : IOnHeroUpdate, IEventReceiver
{
    private sealed class ProjectionVisual
    {
        public HSprite? Sprite;
        public Graphics? Marker;
        public dc.h2d.Object? Parent;
        public long LastSeenTick;
        public double X;
        public double Y;
        public double RenderX;
        public double RenderY;
        public int Dir;
        public string Type = string.Empty;
        public string AnimGroup = string.Empty;
        public string SpriteType = string.Empty;
        public string AppliedAnimGroup = string.Empty;
        public string PlayingGroup = string.Empty;
        public SpriteLib? SpriteLib;
        public long HitFlashUntilTick;
    }

    private const double SendIntervalSeconds = 1.0 / 12.0;
    private const double StaleProjectionSeconds = 1.50;
    private const double ProjectionSmoothingPerSecond = 18.0;
    private const int MaxMobsPerPacket = 64;
    private const double MarkerRadiusPx = 14.0;
    private const double MarkerInnerRadiusPx = 6.0;
    private const double MarkerYOffsetPx = 14.0;
    private const double ProjectionAlpha = 0.34;
    private const double ProjectionCoreAlpha = 0.58;
    private const double ProjectionOnlySpriteAlpha = 0.72;
    private const double AuthoritySpriteAlpha = 1.0;
    private const double HitFlashSeconds = 0.16;

    private readonly Serilog.ILogger _log;
    private readonly List<NetNode.MobV1StateSnapshot> _scratch = new(MaxMobsPerPacket);
    private readonly List<NetNode.MobV1SpawnSnapshot> _spawnScratch = new(MaxMobsPerPacket);
    private readonly List<NetNode.MobV1DespawnSnapshot> _despawnScratch = new(MaxMobsPerPacket);
    private readonly Dictionary<string, ProjectionVisual> _visuals = new(StringComparer.Ordinal);
    private readonly HashSet<int> _hostDeclaredMobIds = new();
    private long _lastSendTick;
    private int _lastSentProjectionCount = -1;
    private Level? _lastLevel;
    private dc.h2d.Object? _projectionRoot;

    public MobProjectionLayer(ModEntry entry)
    {
        _log = entry.Logger;
        EventSystem.AddReceiver(this);
    }

    void IOnHeroUpdate.OnHeroUpdate(double dt)
    {
        var hero = ModEntry.me;
        var net = GameMenu.NetRef;
        if (!IsProjectionModeEnabled() || hero == null || net == null || !net.IsAlive || net.id <= 0)
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

        if (ShouldSendLocalMobProjections(net))
            SendHostMobV1States(net, level);

        if (ShouldConsumeRemoteMobProjections(net))
        {
            ConsumeHostMobV1Events(net, level);
            ConsumeHostMobV1HitResults(net, level);
            ConsumeHostMobV1States(net, level);
            AnimateProjectionVisuals(dt, level);
        }
        PruneStaleProjections();
    }

    private static bool IsProjectionModeEnabled()
    {
        return MobAuthorityV1Runtime.IsProjectionTransportEnabled();
    }

    private static bool ShouldSendLocalMobProjections(NetNode net)
    {
        if (net.IsHost)
            return true;

        return MobAuthorityV1Runtime.IsProjectionOnlyModeEnabled();
    }

    private static bool ShouldConsumeRemoteMobProjections(NetNode net)
    {
        if (MobAuthorityV1RealProxyLayer.Enabled && MobAuthorityV1Runtime.IsAuthorityModeEnabled())
            return false;

        if (!net.IsHost)
            return true;

        return MobAuthorityV1Runtime.IsProjectionOnlyModeEnabled();
    }

    private void SendHostMobV1States(NetNode net, Level? level)
    {
        if (level?.entities == null)
            return;

        var now = Stopwatch.GetTimestamp();
        if (_lastSendTick != 0 && Stopwatch.GetElapsedTime(_lastSendTick, now).TotalSeconds < SendIntervalSeconds)
            return;

        _lastSendTick = now;
        _scratch.Clear();
        _spawnScratch.Clear();

        var levelId = GetLevelId(level);
        var entities = level.entities;
        MobAuthorityV1Ids.Prune(level);
        var activeIds = new HashSet<int>();
        for (int i = 0; i < entities.length && _scratch.Count < MaxMobsPerPacket; i++)
        {
            if (entities.getDyn(i) is not Mob mob)
                continue;
            if (!IsProjectionMob(mob, level))
                continue;

            var netMobId = MobAuthorityV1Ids.GetOrCreate(mob, level);
            if (netMobId <= 0)
                continue;

            activeIds.Add(netMobId);
            var state = new NetNode.MobV1StateSnapshot(
                net.id,
                levelId,
                netMobId,
                GetEntityX(mob),
                GetEntityY(mob),
                NormalizeDir(SafeRead(() => mob.dir, 0)),
                SafeRead(() => mob.life, 0),
                SafeRead(() => mob.maxLife, 0),
                BuildMobTypeLabel(mob),
                BuildMobAnimGroup(mob));
            _scratch.Add(state);

            if (_hostDeclaredMobIds.Add(netMobId))
            {
                _spawnScratch.Add(new NetNode.MobV1SpawnSnapshot(
                    state.HostUserId,
                    state.LevelId,
                    state.NetMobId,
                    state.X,
                    state.Y,
                    state.Dir,
                    state.Life,
                    state.MaxLife,
                    state.Type,
                    state.AnimGroup));
            }
        }

        BuildHostDespawns(net.id, levelId, activeIds);

        if (_scratch.Count == 0 && _lastSentProjectionCount == 0)
            return;

        try
        {
            if (_despawnScratch.Count > 0)
                net.SendMobV1Despawns(levelId, _despawnScratch);
            if (_spawnScratch.Count > 0)
                net.SendMobV1Spawns(levelId, _spawnScratch);
            net.SendMobV1States(levelId, _scratch);
            _lastSentProjectionCount = _scratch.Count;
        }
        catch (Exception ex)
        {
            _log.Warning("[MobsAuthorityV1] Failed to send state packet: {Message}", ex.Message);
        }
    }

    private void BuildHostDespawns(int hostUserId, string levelId, HashSet<int> activeIds)
    {
        _despawnScratch.Clear();
        if (_hostDeclaredMobIds.Count == 0)
            return;

        List<int>? stale = null;
        foreach (var id in _hostDeclaredMobIds)
        {
            if (activeIds.Contains(id))
                continue;
            stale ??= new List<int>();
            stale.Add(id);
        }

        if (stale == null)
            return;

        for (int i = 0; i < stale.Count; i++)
        {
            var id = stale[i];
            _hostDeclaredMobIds.Remove(id);
            _despawnScratch.Add(new NetNode.MobV1DespawnSnapshot(hostUserId, levelId, id, "missing"));
        }
    }

    private void ConsumeHostMobV1Events(NetNode net, Level? level)
    {
        var localId = net.id;
        var localLevelId = GetLevelId(level);
        var parent = ResolveProjectionParent(level);
        var now = Stopwatch.GetTimestamp();

        if (net.TryConsumeMobV1Despawns(out var despawns))
        {
            for (int i = 0; i < despawns.Count; i++)
            {
                var despawn = despawns[i];
                if (despawn.HostUserId <= 0 || despawn.HostUserId == localId || despawn.NetMobId < 0)
                    continue;
                if (!string.IsNullOrWhiteSpace(despawn.LevelId) &&
                    !string.IsNullOrWhiteSpace(localLevelId) &&
                    !string.Equals(despawn.LevelId, localLevelId, StringComparison.Ordinal))
                {
                    continue;
                }

                RemoveProjectionVisual(despawn.HostUserId, despawn.NetMobId);
                MobAuthorityV1ProxyRegistry.Remove(despawn.HostUserId, despawn.NetMobId);
            }
        }

        if (!net.TryConsumeMobV1Spawns(out var spawns))
            return;

        for (int i = 0; i < spawns.Count; i++)
        {
            var spawn = spawns[i];
            if (spawn.HostUserId <= 0 || spawn.HostUserId == localId || spawn.NetMobId < 0)
                continue;
            if (!string.IsNullOrWhiteSpace(spawn.LevelId) &&
                !string.IsNullOrWhiteSpace(localLevelId) &&
                !string.Equals(spawn.LevelId, localLevelId, StringComparison.Ordinal))
            {
                continue;
            }

            var key = BuildVisualKey(spawn.HostUserId, spawn.NetMobId);
            if (!_visuals.TryGetValue(key, out var visual))
            {
                visual = new ProjectionVisual();
                _visuals[key] = visual;
            }

            ApplyProjectionState(visual, spawn.X, spawn.Y, spawn.Dir, spawn.Type, spawn.AnimGroup, now, snapRender: true);
            MobAuthorityV1ProxyRegistry.Upsert(spawn.HostUserId, spawn.LevelId, spawn.NetMobId, spawn.X, spawn.Y);
            UpdateProjectionVisual(visual, spawn.HostUserId, parent);
        }
    }

    private void ConsumeHostMobV1States(NetNode net, Level? level)
    {
        if (!net.TryConsumeMobV1States(out var states))
            return;

        var localId = net.id;
        var localLevelId = GetLevelId(level);
        var parent = ResolveProjectionParent(level);
        var now = Stopwatch.GetTimestamp();
        var activeMobIndicesByUser = new Dictionary<int, HashSet<int>>();

        for (int i = 0; i < states.Count; i++)
        {
            var state = states[i];
            if (state.HostUserId <= 0 || state.HostUserId == localId)
                continue;
            if (!string.IsNullOrWhiteSpace(state.LevelId) &&
                !string.IsNullOrWhiteSpace(localLevelId) &&
                !string.Equals(state.LevelId, localLevelId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!activeMobIndicesByUser.TryGetValue(state.HostUserId, out var activeMobIndices))
            {
                activeMobIndices = new HashSet<int>();
                activeMobIndicesByUser[state.HostUserId] = activeMobIndices;
            }

            if (state.NetMobId >= 0)
                activeMobIndices.Add(state.NetMobId);
        }

        foreach (var pair in activeMobIndicesByUser)
            RemoveMissingProjectionVisuals(pair.Key, pair.Value);

        for (int i = 0; i < states.Count; i++)
        {
            var state = states[i];
            if (state.HostUserId <= 0 || state.HostUserId == localId || state.NetMobId < 0)
                continue;
            if (!string.IsNullOrWhiteSpace(state.LevelId) &&
                !string.IsNullOrWhiteSpace(localLevelId) &&
                !string.Equals(state.LevelId, localLevelId, StringComparison.Ordinal))
            {
                continue;
            }

            var key = BuildVisualKey(state.HostUserId, state.NetMobId);
            if (!_visuals.TryGetValue(key, out var visual))
            {
                visual = new ProjectionVisual();
                _visuals[key] = visual;
            }

            ApplyProjectionState(visual, state.X, state.Y, state.Dir, state.Type, state.AnimGroup, now, snapRender: false);
            MobAuthorityV1ProxyRegistry.Upsert(state.HostUserId, state.LevelId, state.NetMobId, state.X, state.Y);
            UpdateProjectionVisual(visual, state.HostUserId, parent);
        }
    }

    private void ConsumeHostMobV1HitResults(NetNode net, Level? level)
    {
        if (!net.TryConsumeMobV1HitResults(out var results))
            return;

        var localLevelId = GetLevelId(level);
        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            if (!result.Accepted || result.HostUserId <= 0 || result.NetMobId <= 0)
                continue;
            if (!string.IsNullOrWhiteSpace(result.LevelId) &&
                !string.IsNullOrWhiteSpace(localLevelId) &&
                !string.Equals(result.LevelId, localLevelId, StringComparison.Ordinal))
            {
                continue;
            }

            if (result.Life <= 0)
            {
                RemoveProjectionVisual(result.HostUserId, result.NetMobId);
                continue;
            }

            FlashProjectionVisual(result.HostUserId, result.NetMobId);
        }
    }

    private void FlashProjectionVisual(int userId, int netMobId)
    {
        var key = BuildVisualKey(userId, netMobId);
        if (!_visuals.TryGetValue(key, out var visual))
            return;

        var now = Stopwatch.GetTimestamp();
        visual.HitFlashUntilTick = now + (long)(Stopwatch.Frequency * HitFlashSeconds);
    }

    private static void ApplyProjectionState(
        ProjectionVisual visual,
        double x,
        double y,
        int dir,
        string type,
        string animGroup,
        long now,
        bool snapRender)
    {
        var wasUninitialized = visual.LastSeenTick == 0;
        visual.LastSeenTick = now;
        visual.X = x;
        visual.Y = y;
        if (snapRender || wasUninitialized)
        {
            visual.RenderX = x;
            visual.RenderY = y;
        }

        visual.Dir = dir;
        visual.Type = type;
        visual.AnimGroup = animGroup;
    }

    private void AnimateProjectionVisuals(double dt, Level? level)
    {
        if (_visuals.Count == 0)
            return;

        var parent = ResolveProjectionParent(level);
        var alpha = 1.0 - System.Math.Exp(-ProjectionSmoothingPerSecond * System.Math.Max(0.0, dt));
        foreach (var pair in _visuals)
        {
            var visual = pair.Value;
            visual.RenderX += (visual.X - visual.RenderX) * alpha;
            visual.RenderY += (visual.Y - visual.RenderY) * alpha;
            UpdateProjectionVisual(visual, ParseUserIdFromVisualKey(pair.Key), parent);
        }
    }

    private void RemoveMissingProjectionVisuals(int userId, HashSet<int> activeMobIndices)
    {
        if (_visuals.Count == 0)
            return;

        var prefix = userId.ToString(CultureInfo.InvariantCulture) + ":";
        List<string>? missing = null;
        foreach (var key in _visuals.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var indexText = key[prefix.Length..];
            if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mobIndex))
                continue;
            if (activeMobIndices.Contains(mobIndex))
                continue;

            missing ??= new List<string>();
            missing.Add(key);
        }

        if (missing == null)
            return;

        for (int i = 0; i < missing.Count; i++)
        {
            var key = missing[i];
            if (!_visuals.TryGetValue(key, out var visual))
                continue;

            RemoveVisual(visual);
            _visuals.Remove(key);
        }
    }

    private void RemoveProjectionVisual(int userId, int netMobId)
    {
        var key = BuildVisualKey(userId, netMobId);
        if (!_visuals.TryGetValue(key, out var visual))
            return;

        RemoveVisual(visual);
        _visuals.Remove(key);
        MobAuthorityV1ProxyRegistry.Remove(userId, netMobId);
    }

    private void UpdateProjectionVisual(ProjectionVisual visual, int userId, dc.h2d.Object? parent)
    {
        if (parent == null)
            return;

        HSprite? spriteTemplate = null;
        SpriteLib? spriteLib = visual.SpriteLib;
        var needsSpriteResolve = visual.Sprite == null ||
                                  visual.SpriteLib == null ||
                                  visual.Parent == null ||
                                  !ReferenceEquals(visual.Parent, parent) ||
                                  !string.Equals(visual.SpriteType, visual.Type, StringComparison.Ordinal);

        if (needsSpriteResolve)
        {
            spriteTemplate = ResolveSpriteTemplate(visual.Type, visual.X, visual.Y);
            spriteLib = SafeRead(() => spriteTemplate?.lib, null);
        }

        if (needsSpriteResolve && spriteLib != null)
        {
            RemoveVisual(visual);
            try
            {
                var initialGroup = ResolveSpriteGroup(spriteLib, visual.AnimGroup);
                visual.Sprite = new HSprite(spriteLib, initialGroup.AsHaxeString(), Ref<int>.Null, parent);
                visual.SpriteLib = spriteLib;
                visual.Parent = parent;
                visual.SpriteType = visual.Type;
                visual.AppliedAnimGroup = string.Empty;
                visual.PlayingGroup = string.Empty;
                visual.Sprite.alpha = GetProjectionSpriteAlpha(visual);
                visual.Sprite.visible = true;
                NormalizeProjectionSpriteVisuals(visual.Sprite);
                TryCopySpritePivot(spriteTemplate, visual.Sprite);
                TryPlaySpriteGroup(visual, initialGroup);
                visual.AppliedAnimGroup = visual.AnimGroup;
            }
            catch
            {
                visual.Sprite = null;
                visual.SpriteLib = null;
                visual.Parent = null;
                visual.SpriteType = string.Empty;
                visual.AppliedAnimGroup = string.Empty;
                visual.PlayingGroup = string.Empty;
            }
        }

        if (!ShouldShowProjectionMarker())
        {
            if (visual.Marker != null)
            {
                try { visual.Marker.remove(); } catch { }
                visual.Marker = null;
            }
        }
        else if (visual.Marker == null || visual.Parent == null || !ReferenceEquals(visual.Parent, parent))
        {
            try
            {
                visual.Marker = new Graphics(parent);
                visual.Parent = parent;
                DrawProjectionMarker(visual.Marker, ResolveProjectionColor(userId));
            }
            catch
            {
                visual.Marker = null;
                visual.Parent = null;
                return;
            }
        }

        try
        {
            if (visual.Sprite != null)
            {
                visual.Sprite.x = visual.RenderX;
                visual.Sprite.y = visual.RenderY;
                visual.Sprite.scaleX = visual.Dir < 0 ? -1.0 : 1.0;
                visual.Sprite.alpha = GetProjectionSpriteAlpha(visual);
                visual.Sprite.visible = true;
                NormalizeProjectionSpriteVisuals(visual.Sprite);
                if (!string.Equals(visual.AppliedAnimGroup, visual.AnimGroup, StringComparison.Ordinal))
                {
                    TryPlaySpriteGroup(visual, ResolveSpriteGroup(visual.SpriteLib, visual.AnimGroup));
                    visual.AppliedAnimGroup = visual.AnimGroup;
                }
            }

            if (visual.Marker != null)
            {
                visual.Marker.x = visual.RenderX;
                visual.Marker.y = visual.RenderY - MarkerYOffsetPx;
                visual.Marker.scaleX = visual.Dir < 0 ? -1.0 : 1.0;
                visual.Marker.alpha = IsHitFlashing(visual) ? 1.0 : 0.82;
                visual.Marker.visible = true;
            }
        }
        catch
        {
        }
    }

    private static double GetProjectionSpriteAlpha(ProjectionVisual visual)
    {
        if (IsHitFlashing(visual))
            return 1.0;

        return MobAuthorityV1Runtime.IsAuthorityModeEnabled()
            ? AuthoritySpriteAlpha
            : ProjectionOnlySpriteAlpha;
    }

    private static bool ShouldShowProjectionMarker()
    {
        return MobAuthorityV1Runtime.IsProjectionOnlyModeEnabled();
    }

    private static bool IsHitFlashing(ProjectionVisual visual)
    {
        return visual.HitFlashUntilTick > 0 &&
               Stopwatch.GetTimestamp() <= visual.HitFlashUntilTick;
    }

    private static void DrawProjectionMarker(Graphics marker, int color)
    {
        try
        {
            marker.clear();

            var outerColor = color;
            var outerAlpha = ProjectionAlpha;
            marker.beginFill(Ref<int>.From(ref outerColor), Ref<double>.From(ref outerAlpha));
            marker.drawCircle(0.0, 0.0, MarkerRadiusPx, Ref<int>.Null);
            marker.endFill();

            var coreColor = color;
            var coreAlpha = ProjectionCoreAlpha;
            marker.beginFill(Ref<int>.From(ref coreColor), Ref<double>.From(ref coreAlpha));
            marker.drawCircle(0.0, 0.0, MarkerInnerRadiusPx, Ref<int>.Null);
            marker.drawCircle(9.0, -3.0, 3.0, Ref<int>.Null);
            marker.endFill();

            marker.alpha = 1.0;
            marker.visible = true;
        }
        catch
        {
        }
    }

    private void PruneStaleProjections()
    {
        if (_visuals.Count == 0)
            return;

        var now = Stopwatch.GetTimestamp();
        List<string>? stale = null;
        foreach (var pair in _visuals)
        {
            var visual = pair.Value;
            var remove = visual.LastSeenTick == 0 ||
                         Stopwatch.GetElapsedTime(visual.LastSeenTick, now).TotalSeconds > StaleProjectionSeconds ||
                         (visual.Marker == null && visual.Sprite == null) ||
                         SafeRead(() => visual.Marker != null && visual.Marker.parent == null, false) ||
                         SafeRead(() => visual.Sprite != null && visual.Sprite.parent == null, false);
            if (!remove)
                continue;

            stale ??= new List<string>();
            stale.Add(pair.Key);
        }

        if (stale == null)
            return;

        for (int i = 0; i < stale.Count; i++)
        {
            if (!_visuals.TryGetValue(stale[i], out var visual))
                continue;

            RemoveVisual(visual);
            _visuals.Remove(stale[i]);
        }
    }

    private void ResetAll()
    {
        _lastSendTick = 0;
        _lastSentProjectionCount = -1;
        _lastLevel = null;
        MobAuthorityV1Ids.Clear();
        _hostDeclaredMobIds.Clear();
        MobAuthorityV1ProxyRegistry.Clear();
        try { _projectionRoot?.remove(); } catch { }
        _projectionRoot = null;
        _scratch.Clear();
        _spawnScratch.Clear();
        _despawnScratch.Clear();
        if (_visuals.Count == 0)
            return;

        foreach (var visual in _visuals.Values)
            RemoveVisual(visual);
        _visuals.Clear();
    }

    private static void RemoveVisual(ProjectionVisual visual)
    {
        try { visual.Marker?.remove(); } catch { }
        try { visual.Sprite?.remove(); } catch { }
        visual.Marker = null;
        visual.Sprite = null;
        visual.Parent = null;
        visual.SpriteLib = null;
        visual.SpriteType = string.Empty;
        visual.AppliedAnimGroup = string.Empty;
        visual.PlayingGroup = string.Empty;
    }

    private static HSprite? ResolveSpriteTemplate(string type, double x, double y)
    {
        var level = ModEntry.me?._level ?? ModEntry.Instance?.game?.curLevel;
        if (level?.entities == null)
            return null;

        Mob? best = null;
        var bestDistSq = double.MaxValue;
        var normalizedType = NormalizeTypeLabel(type);
        var entities = level.entities;

        for (int i = 0; i < entities.length; i++)
        {
            if (entities.getDyn(i) is not Mob candidate)
                continue;
            if (!IsProjectionMob(candidate, level))
                continue;

            var candidateType = BuildMobTypeLabel(candidate);
            var typeMatches = string.IsNullOrWhiteSpace(normalizedType) ||
                              string.Equals(candidateType, normalizedType, StringComparison.OrdinalIgnoreCase);
            if (!typeMatches)
                continue;

            var dx = GetEntityX(candidate) - x;
            var dy = GetEntityY(candidate) - y;
            var distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = candidate;
            }
        }

        return SafeRead(() => best?.spr, null);
    }

    private static string ResolveSpriteGroup(SpriteLib? lib, string preferredGroup)
    {
        var cleaned = string.IsNullOrWhiteSpace(preferredGroup) ? string.Empty : preferredGroup.Trim();
        if (lib != null && !string.IsNullOrWhiteSpace(cleaned))
        {
            try
            {
                if (lib.groups != null && lib.groups.exists(cleaned.AsHaxeString()))
                    return cleaned;
            }
            catch
            {
                return cleaned;
            }
        }

        if (lib != null)
        {
            foreach (var fallback in new[] { "idle", "Idle", "stand", "Stand", "run", "Run" })
            {
                try
                {
                    if (lib.groups != null && lib.groups.exists(fallback.AsHaxeString()))
                        return fallback;
                }
                catch
                {
                }
            }
        }

        return string.IsNullOrWhiteSpace(cleaned) ? "idle" : cleaned;
    }

    private static void TryCopySpritePivot(HSprite? source, HSprite target)
    {
        if (source == null)
            return;

        try
        {
            var sourcePivot = source.pivot;
            var targetPivot = target.pivot;
            targetPivot.centerFactorX = sourcePivot.centerFactorX;
            targetPivot.centerFactorY = sourcePivot.centerFactorY;
            targetPivot.usingFactor = sourcePivot.usingFactor;
            targetPivot.isUndefined = sourcePivot.isUndefined;
        }
        catch
        {
        }
    }

    private static void NormalizeProjectionSpriteVisuals(HSprite sprite)
    {
        if (sprite == null)
            return;

        try { sprite.filter = null; } catch { }
        try { sprite.removeShader(sprite.getShader(dc.shader.GlowKey.Class)); } catch { }
        try { sprite.scaleY = 1.0; } catch { }
    }

    private static void TryPlaySpriteGroup(ProjectionVisual visual, string group)
    {
        var sprite = visual.Sprite;
        if (sprite == null || string.IsNullOrWhiteSpace(group))
            return;

        try
        {
            if (sprite.groupName != null &&
                string.Equals(sprite.groupName.ToString(), group, StringComparison.Ordinal))
            {
                visual.PlayingGroup = group;
                return;
            }

            sprite.get_anim().play(group.AsHaxeString(), null, null).loop(null);
            visual.PlayingGroup = group;
        }
        catch
        {
            try
            {
                sprite.set(sprite.lib, group.AsHaxeString(), Ref<int>.Null, Ref<bool>.Null);
                visual.PlayingGroup = group;
            }
            catch
            {
            }
        }
    }

    private dc.h2d.Object? ResolveProjectionParent(Level? level)
    {
        if (_projectionRoot != null && SafeRead(() => _projectionRoot.parent != null, false))
            return _projectionRoot;

        level ??= ModEntry.me?._level ?? ModEntry.Instance?.game?.curLevel;
        if (level == null)
            return null;

        try
        {
            var root = new dc.h2d.Object(null);
            level.scroller.addChildAt(root, Const.Class.DP_ROOM_FRONT_HERO);
            _projectionRoot = root;
            return root;
        }
        catch
        {
        }

        try
        {
            var root = new dc.h2d.Object(null);
            level.scroller.addChild(root);
            _projectionRoot = root;
            return root;
        }
        catch
        {
            return ModEntry.me?.spr?.parent;
        }
    }

    private static bool IsProjectionMob(Mob mob, Level level)
    {
        if (mob == null || level == null)
            return false;

        try
        {
            if (mob.destroyed || mob._level == null || !ReferenceEquals(mob._level, level))
                return false;
            if (mob.life <= 0)
                return false;
            if (mob._team != null && level.teamMob != null && ReferenceEquals(mob._team, level.teamMob))
                return true;
        }
        catch
        {
            return false;
        }

        var typeName = SafeRead(() => mob.GetType().FullName ?? mob.GetType().Name, string.Empty);
        return typeName.Contains("dc.en.mob.", StringComparison.Ordinal) ||
               typeName.Contains(".mob.", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("dc.en.boss.", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains(".boss.", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildVisualKey(int userId, int mobIndex)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{userId}:{mobIndex}");
    }

    private static int ParseUserIdFromVisualKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return 0;

        var colon = key.IndexOf(':');
        var userIdText = colon >= 0 ? key[..colon] : key;
        return int.TryParse(userIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId) ? userId : 0;
    }

    private static string GetLevelId(Level? level)
    {
        if (level == null)
            return string.Empty;

        return SafeRead(() => level.map?.id?.ToString() ?? string.Empty, string.Empty).Trim();
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

    private static int NormalizeDir(int dir)
    {
        if (dir < 0) return -1;
        if (dir > 0) return 1;
        return 0;
    }

    private static int ResolveProjectionColor(int userId)
    {
        return userId switch
        {
            1 => 0x45D6FF,
            2 => 0xFFB347,
            3 => 0xC77DFF,
            4 => 0x7DFF9A,
            _ => 0xFFFFFF
        };
    }

    private static string BuildMobTypeLabel(Mob mob)
    {
        var typeId = SafeRead(() => mob.type?.ToString() ?? string.Empty, string.Empty);
        if (!string.IsNullOrWhiteSpace(typeId))
            return NormalizeTypeLabel(typeId);

        return NormalizeTypeLabel(SafeRead(() => mob.GetType().Name, string.Empty));
    }

    private static string BuildMobAnimGroup(Mob mob)
    {
        return SafeRead(() => mob.spr?.groupName?.ToString() ?? string.Empty, string.Empty);
    }

    private static string NormalizeTypeLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = value.Trim();
        var slash = value.LastIndexOf('/');
        var dot = value.LastIndexOf('.');
        var colon = value.LastIndexOf(':');
        var sep = System.Math.Max(System.Math.Max(slash, dot), colon);
        if (sep >= 0 && sep + 1 < value.Length)
            value = value[(sep + 1)..];

        return value.Trim();
    }

    private static T SafeRead<T>(Func<T> getter, T fallback)
    {
        try { return getter(); } catch { return fallback; }
    }
}
