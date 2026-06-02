using System.Globalization;
using DeadCellsMultiplayerMod;
using DeadCellsMultiplayerMod.Mobs.Authority;

public sealed partial class NetNode
{
    public void TickSend(double cx, double cy, int dir)
    {
        if (!HasAnyConnection()) return;
        if (ID <= 0) return;
        var line = BuildPosLine(ID, cx, cy, dir);
        _ = SendLineSafe(line);
    }

    public void LevelSend(int senderId, string lvl) => SendLevelId(senderId, lvl);

    public void SendSeed(int seed)
    {
        if (_role == NetRole.Host)
        {
            lock (_hostCacheSync)
            {
                _cachedHostSeed = seed;
            }
        }

        if (!HasAnyConnection())
        {
            _log.Information("[NetNode] Skip sending seed {Seed}: no connected client", seed);
            return;
        }
        var line = $"SEED|{seed}\n";
        _ = SendLineSafe(line);
        _log.Information("[NetNode] Sent seed {Seed}", seed);
    }

    public void SendSerializerSync(int seq, int uid)
    {
        if (_role != NetRole.Host)
            return;
        lock (_hostCacheSync)
        {
            _cachedHostSerializerSeq = seq;
            _cachedHostSerializerUid = uid;
        }
        if (!HasAnyConnection())
            return;

        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"HXSYNC|{seq}|{uid}\n");
        _ = SendLineSafe(line);
    }

    public void SendCounters(string countersPayload)
    {
        return;
    }

    public void SendProgress(string progressPayload)
    {
        return;
    }

    public void SendBlueprints(string blueprintsPayload)
    {
        return;
    }

    public void SendUsername(string username)
    {
        if (!HasAnyConnection())
        {
            _log.Information("[NetNode] Skip sending username: no connected client");
            return;
        }

        var safe = (username ?? "guest").Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        if (safe.Length == 0) safe = "guest";

        var idPart = ID > 0 ? $"{ID}|" : string.Empty;
        SendRaw("USER|" + idPart + safe);
        _log.Information("[NetNode] Sent username {Username}", safe);
    }

    public void SendBossRune(int bossRune)
    {
        if (_role == NetRole.Host)
        {
            lock (_hostCacheSync)
            {
                _cachedHostBossRune = bossRune;
            }
        }

        if (!HasAnyConnection())
        {
            _log.Information("[NetNode] Skip sending boss rune: no connected client");
            return;
        }

        var payload = bossRune.ToString(CultureInfo.InvariantCulture);
        SendRaw("BOSSRUNE|" + payload);
        // _log.Information("[NetNode] Sent boss rune {BossRune}", bossRune);
    }

    public void SendPermanentItems(string itemsList)
    {
        if (_role == NetRole.Host)
        {
            lock (_hostCacheSync)
            {
                _cachedHostPermanentItems = itemsList;
            }
        }

        if (!HasAnyConnection())
            return;

        SendRaw("PERMRUNES|" + itemsList);
    }

    public void SendLevelDesc(string json)
    {
        var safeJson = (json ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
        if (_role == NetRole.Host)
        {
            lock (_hostCacheSync)
            {
                _cachedHostLevelDescPayload = string.IsNullOrWhiteSpace(safeJson) ? null : safeJson;
            }
        }

        if (string.IsNullOrWhiteSpace(safeJson))
            return;

        if (!HasAnyConnection())
        {
            _log.Information("[NetNode] Skip sending level desc: no connected client");
            return;
        }

        SendRaw("LDESC|" + safeJson);
        _log.Information("[NetNode] Sent LevelDesc payload");
    }

    public void SendLevelSeed(string levelId, double seed)
    {
        var safeSeed = seed.ToString(CultureInfo.InvariantCulture);
        var safeId = (levelId ?? string.Empty).Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        var payload = $"{safeId}|{safeSeed}";
        if (_role == NetRole.Host)
        {
            lock (_hostCacheSync)
            {
                _cachedHostLevelSeedPayload = string.IsNullOrWhiteSpace(safeId) ? null : payload;
            }
        }

        if (string.IsNullOrWhiteSpace(safeId))
            return;

        if (!HasAnyConnection())
        {
            _log.Information("[NetNode] Skip sending level seed: no connected client");
            return;
        }

        SendRaw($"LSEED|{payload}");
        _log.Information("[NetNode] Sent level seed for {LevelId}", safeId);
    }

    public void SendLevelGraph(string levelId, string json)
    {
        if (!HasAnyConnection())
        {
            _log.Information("[NetNode] Skip sending level graph: no connected client");
            lock (_hostCacheSync)
            {
                _cachedHostLevelGraphPayload = string.IsNullOrWhiteSpace(json) ? null : json;
                if (!string.IsNullOrWhiteSpace(levelId) && !string.IsNullOrWhiteSpace(json))
                    _cachedHostLevelGraphsByLevelId[levelId] = json;
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(json))
            return;

        lock (_hostCacheSync)
        {
            _cachedHostLevelGraphPayload = json;
            if (!string.IsNullOrWhiteSpace(levelId))
                _cachedHostLevelGraphsByLevelId[levelId] = json;
        }

        SendRaw("LGRAPH|" + json);
        _log.Information("[NetNode] Sent level graph ({Length} bytes)", json.Length);
    }

    public void SendGeneratePayload(string json)
    {
        if (!HasAnyConnection())
        {
            _log.Information("[NetNode] Skip sending generate payload: no connected client");
            return;
        }

        SendRaw("GEN|" + json);
        _log.Information("[NetNode] Sent Generate payload ({Length} bytes)", json.Length);
    }


    public void SendHP(double life, double maxLife, double lif, double bonusLife, double recover)
    {
        lock (_sync)
        {
            _localHpLife = (int)System.Math.Round(life, System.MidpointRounding.AwayFromZero);
            _localHpMaxLife = (int)System.Math.Round(maxLife, System.MidpointRounding.AwayFromZero);
            _localHpLif = (int)System.Math.Round(lif, System.MidpointRounding.AwayFromZero);
            _localHpBonusLife = (int)System.Math.Round(bonusLife, System.MidpointRounding.AwayFromZero);
            _localHpRecover = (int)System.Math.Round(recover, System.MidpointRounding.AwayFromZero);
            _hasLocalHpSnapshot = true;
        }

        if (!HasAnyConnection())
        {
            return;
        }
        var idPart = ID > 0 ? $"{ID}|" : string.Empty;
        SendRaw($"HP|{idPart}{life}|{maxLife}|{lif}|{bonusLife}|{recover}");
    }

    public void SendChatMessage(string message)
    {
        if (!HasAnyConnection())
            return;

        var safe = SanitizeChatMessage(message);
        if (string.IsNullOrWhiteSpace(safe))
            return;

        var idPart = ID > 0 ? $"{ID}|" : string.Empty;
        SendRaw($"CHAT|{idPart}{safe}");
    }

    public void SendLevelId(int senderId, string levelId)
    {
        if (!HasAnyConnection())
        {
            return;
        }

        var safe = levelId.Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        SendRaw($"LEVEL|{senderId}|{safe}");
    }

    public void SendRoomTarget(string levelId, int roomId)
    {
        if (!HasAnyConnection())
            return;
        if (ID <= 0 || roomId < 0)
            return;

        var safe = (levelId ?? string.Empty)
            .Replace("|", "/", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(safe))
            return;

        SendRaw($"ZROOM|{ID}|{safe}|{roomId}");
    }

    public void SendKick()
    {
        if (!HasAnyConnection()) return;
        SendRaw("KICK");
    }

    public void SendControlAndFlush(string payload, int timeoutMs = 250)
    {
        if (!HasAnyConnection())
            return;

        if (string.IsNullOrWhiteSpace(payload))
            return;

        var line = payload.EndsWith('\n') ? payload : payload + "\n";
        try
        {
            var task = SendLineSafe(line);
            if (!task.Wait(timeoutMs))
                _log.Warning("[NetNode] Timed out sending control line \"{Payload}\"", payload);
        }
        catch (Exception ex)
        {
            _log.Warning("[NetNode] Failed to send control line \"{Payload}\": {Message}", payload, ex.Message);
        }
    }


    public void SendHeadAnim(string anim)
    {
        if (!HasAnyConnection())
        {
            return;
        }

        var safe = (anim ?? string.Empty).Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        var idPart = ID > 0 ? $"{ID}|" : string.Empty;
        SendRaw($"HEADANIM|{idPart}{safe}");
    }

    public void SendAnim(string anim, int? queueAnim = null, bool? g = null)
    {
        if (!HasAnyConnection())
        {
            return;
            
        }

        var safe = (anim ?? string.Empty).Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        if (safe.Length == 0) safe = "idle";
        var queuePart = queueAnim.HasValue ? queueAnim.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
        var gPart = g.HasValue ? (g.Value ? "1" : "0") : string.Empty;
        var idPart = ID > 0 ? $"{ID}|" : string.Empty;
        SendRaw($"ANIM|{idPart}{safe}|{queuePart}|{gPart}");
    }

    public void SendInventoryWeapon(string kind, int slot, int permanentId, int? ammo = null)
    {
        if (!HasAnyConnection())
        {
            return;
        }

        var safe = (kind ?? string.Empty).Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        if (safe.Length == 0) return;

        var idPart = ID > 0 ? $"{ID}|" : string.Empty;
        if (ammo.HasValue)
            SendRaw($"INV|{idPart}{safe}|{slot}|{permanentId}|{ammo.Value}");
        else
            SendRaw($"INV|{idPart}{safe}|{slot}|{permanentId}");
    }

    public void SendAttack(string kind, int slot, int permanentId, int? ammo = null, RemoteAttackAction action = RemoteAttackAction.Attack)
    {
        if (!HasAnyConnection())
        {
            return;
        }

        var safe = (kind ?? string.Empty).Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        if (safe.Length == 0) return;

        var idPart = ID > 0 ? $"{ID}|" : string.Empty;
        var actionToken = AttackActionToToken(action);
        if (ammo.HasValue)
            SendRaw($"ATK|{idPart}{safe}|{slot}|{permanentId}|{ammo.Value}|{actionToken}");
        else
            SendRaw($"ATK|{idPart}{safe}|{slot}|{permanentId}|{actionToken}");

        TrySendMobV1HitRequestForLocalAttack(safe, slot, permanentId, ammo, action, actionToken);
    }

    private void TrySendMobV1HitRequestForLocalAttack(
        string attackKind,
        int slot,
        int permanentId,
        int? ammo,
        RemoteAttackAction action,
        string actionToken)
    {
        if (_role != NetRole.Client)
            return;
        if (!MobAuthorityV1Runtime.IsAuthorityModeEnabled())
            return;

        var hero = ModEntry.me ?? ModCore.Modules.Game.Instance?.HeroInstance;
        if (hero == null)
            return;

        string levelId;
        double x;
        double y;
        int dir;
        try
        {
            levelId = hero._level?.map?.id?.ToString() ?? string.Empty;
            x = hero.spr?.x ?? (hero.cx + hero.xr) * 24.0;
            y = hero.spr?.y ?? (hero.cy + hero.yr) * 24.0;
            dir = hero.dir;
        }
        catch
        {
            return;
        }

        if (!MobAuthorityV1ProxyRegistry.TryFindAttackTarget(levelId, x, y, dir, out var netMobId, out var targetX, out var targetY))
            return;

        var attackKey = $"{slot}:{permanentId}:{attackKind}:{actionToken}";
        if (ShouldThrottleMobV1HitRequest(netMobId, attackKey))
            return;

        var damageHint = MobAuthorityV1AttackCapture.TryGetRecentDamageHint(out var capturedDamage)
            ? capturedDamage
            : EstimateMobV1DamageHint(attackKind, slot, permanentId, ammo, action);
        var hitRadius = EstimateMobV1HitRadius(attackKind, ammo, action);
        var attackId = System.Threading.Interlocked.Increment(ref _nextMobV1AttackId);
        var sentAtSeconds = (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
        SendMobV1HitRequest(levelId, netMobId, targetX, targetY, damageHint, attackKey, x, y, dir, attackId, sentAtSeconds, hitRadius);
    }

    private bool ShouldThrottleMobV1HitRequest(int netMobId, string attackKey)
    {
        const double minIntervalSeconds = 0.18;
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var elapsedSeconds = _lastMobV1HitRequestTicks > 0
            ? (double)(now - _lastMobV1HitRequestTicks) / System.Diagnostics.Stopwatch.Frequency
            : double.MaxValue;

        if (_lastMobV1HitRequestNetMobId == netMobId &&
            string.Equals(_lastMobV1HitRequestAttackKey, attackKey, StringComparison.Ordinal) &&
            elapsedSeconds < minIntervalSeconds)
        {
            return true;
        }

        _lastMobV1HitRequestTicks = now;
        _lastMobV1HitRequestNetMobId = netMobId;
        _lastMobV1HitRequestAttackKey = attackKey;
        return false;
    }

    private static int EstimateMobV1DamageHint(string attackKind, int slot, int permanentId, int? ammo, RemoteAttackAction action)
    {
        var kind = attackKind ?? string.Empty;

        if (kind.Equals("__DIVE_ATTACK__", StringComparison.Ordinal))
            return action == RemoteAttackAction.Interrupt ? 45 : 22;

        if (action == RemoteAttackAction.Interrupt)
            return 32;

        if (ContainsOrdinalIgnoreCase(kind, "shield") ||
            ContainsOrdinalIgnoreCase(kind, "parry") ||
            ContainsOrdinalIgnoreCase(kind, "block"))
        {
            return 18;
        }

        if (ContainsOrdinalIgnoreCase(kind, "bow") ||
            ContainsOrdinalIgnoreCase(kind, "crossbow") ||
            ContainsOrdinalIgnoreCase(kind, "xbow") ||
            ammo.HasValue)
        {
            return 30;
        }

        return slot >= 0 || permanentId > 0 ? 25 : 20;
    }

    private static double EstimateMobV1HitRadius(string attackKind, int? ammo, RemoteAttackAction action)
    {
        var kind = attackKind ?? string.Empty;
        if (kind.Equals("__DIVE_ATTACK__", StringComparison.Ordinal))
            return action == RemoteAttackAction.Interrupt ? 120.0 : 80.0;
        if (ContainsOrdinalIgnoreCase(kind, "bow") ||
            ContainsOrdinalIgnoreCase(kind, "crossbow") ||
            ContainsOrdinalIgnoreCase(kind, "xbow") ||
            ammo.HasValue)
        {
            return 180.0;
        }
        if (ContainsOrdinalIgnoreCase(kind, "shield") ||
            ContainsOrdinalIgnoreCase(kind, "parry") ||
            ContainsOrdinalIgnoreCase(kind, "block"))
        {
            return 90.0;
        }
        return 135.0;
    }

    private static bool ContainsOrdinalIgnoreCase(string value, string needle)
    {
        return value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public void SendHeroSkin(string skin)
    {
        var safe = (skin ?? "PrisonerDefault").Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        if (string.IsNullOrWhiteSpace(safe))
            safe = "PrisonerDefault";

        if (_role == NetRole.Host)
        {
            lock (_hostCacheSync)
            {
                _cachedHostHeroSkin = safe;
            }
        }

        if (!HasAnyConnection())
        {
            _log.Information("[NetNode] Skip sending hero skin: no connected client");
            return;
        }

        var idPart = ID > 0 ? $"{ID}|" : string.Empty;
        SendRaw("SKIN|" + idPart + safe);
        _log.Information("[NetNode] Sent hero skin {Skin}", safe);
    }

    public void SendHeroHeadSkin(string skin)
    {
        var safe = (skin ?? "PrisonerDefault").Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        if (string.IsNullOrWhiteSpace(safe))
            safe = "BaseFlame";

        if (_role == NetRole.Host)
        {
            lock (_hostCacheSync)
            {
                _cachedHostHeroHeadSkin = safe;
            }
        }

        if (!HasAnyConnection())
        {
            _log.Information("[NetNode] Skip sending hero skin: no connected client");
            return;
        }

        var idPart = ID > 0 ? $"{ID}|" : string.Empty;
        SendRaw("HEAD|" + idPart + safe);
        _log.Information("[NetNode] Sent hero skin {Skin}", safe);
    }

    public void SendHeroDeath()
    {
        if (!HasAnyConnection())
        {
            _log.Information("[NetNode] Skip sending death: no connected client");
            return;
        }

        SendRaw("DIED");
        _log.Information("[NetNode] Sent hero death");
    }

    public void SendPlayerDownState(bool isDowned, double x, double y, string? levelId, double? headX = null, double? headY = null, string? headAnim = null)
    {
        if (!HasAnyConnection())
            return;
        if (ID <= 0)
            return;

        var hasHead = isDowned && headX.HasValue && headY.HasValue;
        var hasAnim = hasHead && !string.IsNullOrWhiteSpace(headAnim);
        var state = new PlayerDownState(ID, isDowned, x, y, levelId ?? string.Empty, hasHead, headX ?? 0, headY ?? 0, hasAnim, headAnim);
        var line = BuildPlayerDownLine(state);
        _ = SendLineSafe(line);
    }

    public void SendPlayerReviveRequest(int targetId)
    {
        if (!HasAnyConnection())
            return;
        if (ID <= 0 || targetId <= 0)
            return;

        var request = new PlayerReviveRequest(ID, targetId);
        var line = BuildPlayerReviveLine(request);
        _ = SendLineSafe(line);
    }

    public void SendMobStates(IReadOnlyList<MobStateSnapshot> states)
    {
        if (_role != NetRole.Host && _role != NetRole.Client)
            return;
        if (!HasAnyConnection())
            return;
        if (states == null || states.Count == 0)
            return;

        if (MobWireBinary.UseBinaryWire && MobWireBinary.TryBuildMobStatesBinary(states, out var bin) && bin != null)
        {
            var line = "MOBSTATE2|" + Convert.ToBase64String(bin) + "\n";
            _ = SendLineSafe(line);
            return;
        }

        var textLine = MobWireCodec.BuildMobStatesLine(states);
        _ = SendLineSafe(textLine);
    }

    public void SendMobMoves(IReadOnlyList<MobMoveSnapshot> moves)
    {
        if (_role != NetRole.Host)
            return;
        if (!HasAnyConnection())
            return;
        if (moves == null || moves.Count == 0)
            return;

        var line = MobWireCodec.BuildMobMovesLine(moves);
        _ = SendLineSafe(line);
    }

    public void SendMobCharges(IReadOnlyList<MobChargeSnapshot> charges)
    {
        if (_role != NetRole.Host)
            return;
        if (!HasAnyConnection())
            return;
        if (charges == null || charges.Count == 0)
            return;

        var line = MobWireCodec.BuildMobChargesLine(charges);
        _ = SendLineSafe(line);
    }

    public void SendMobAttack(int mobIndex, string skillId, bool requiresTargetInArea, int? data, double x, double y, int targetUserId, int dir = 0)
    {
        if (_role != NetRole.Host)
            return;
        if (!HasAnyConnection())
            return;
        if (mobIndex < 0 || string.IsNullOrWhiteSpace(skillId))
            return;

        var attack = new MobAttack(mobIndex, skillId, requiresTargetInArea, data, x, y, targetUserId, dir);
        var line = MobWireCodec.BuildMobAttackLine(attack);
        _ = SendLineSafe(line);
    }

    /// <summary>Send event-based mob updates. Format: x, y, dir + events. Sent when something changes, not repeatedly.</summary>
    public void SendMobEvents(IReadOnlyList<MobEventUpdate> updates)
    {
        if (_role != NetRole.Host && _role != NetRole.Client)
            return;
        if (!HasAnyConnection())
            return;
        if (updates == null || updates.Count == 0)
            return;

        var line = MobWireCodec.BuildMobEventsLine(updates);
        _ = SendLineSafe(line);
    }

    public void SendMobHit(int mobIndex, int hp, double x, double y)
    {
        if (_role != NetRole.Client)
            return;
        if (!HasAnyConnection())
            return;
        if (ID <= 0)
            return;

        var payload = string.Create(
            CultureInfo.InvariantCulture,
            $"MOBHIT|{ID}|{mobIndex}|{hp}|{x}|{y}");
        SendRaw(payload);
    }

    public void SendMobDie(int mobIndex, double x, double y)
    {
        if (_role != NetRole.Client && _role != NetRole.Host)
            return;
        if (!HasAnyConnection())
            return;
        if (ID <= 0)
            return;

        var payload = string.Create(
            CultureInfo.InvariantCulture,
            $"MOBDIE|{ID}|{mobIndex}|{x}|{y}");
        SendRaw(payload);
    }

    public void SendMobDraw(int mobIndex, bool isOutOfGame, bool isOnScreen)
    {
        if (_role != NetRole.Client)
            return;
        if (!HasAnyConnection())
            return;
        if (ID <= 0)
            return;
        if (mobIndex < 0)
            return;

        var line = MobWireCodec.BuildMobDrawLine(ID, mobIndex, isOutOfGame, isOnScreen);
        _ = SendLineSafe(line);
    }

    public void SendMobDrawBatch(IReadOnlyList<MobDraw> draws)
    {
        if (_role != NetRole.Client)
            return;
        if (!HasAnyConnection())
            return;
        if (ID <= 0)
            return;
        if (draws == null || draws.Count == 0)
            return;

        var line = MobWireCodec.BuildMobDrawLine(draws);
        _ = SendLineSafe(line);
    }

    public void SendMobProjections(string levelId, IReadOnlyList<MobProjectionSnapshot> projections)
    {
        if (_role != NetRole.Host && _role != NetRole.Client)
            return;
        if (!HasAnyConnection())
            return;
        if (ID <= 0)
            return;
        if (projections == null)
            return;

        var line = BuildMobProjectionLine(ID, levelId, projections);
        _ = SendLineSafe(line);
    }

    public void SendMobV1States(string levelId, IReadOnlyList<MobV1StateSnapshot> states)
    {
        if (_role != NetRole.Host && _role != NetRole.Client)
            return;
        if (!HasAnyConnection())
            return;
        if (ID <= 0 || states == null)
            return;

        var line = BuildMobV1StateLine(ID, levelId, states);
        _ = SendLineSafe(line);
    }

    public void SendMobV1Spawns(string levelId, IReadOnlyList<MobV1SpawnSnapshot> spawns)
    {
        if (_role != NetRole.Host && _role != NetRole.Client)
            return;
        if (!HasAnyConnection())
            return;
        if (ID <= 0 || spawns == null || spawns.Count == 0)
            return;

        var line = BuildMobV1SpawnLine(ID, levelId, spawns);
        _ = SendLineSafe(line);
    }

    public void SendMobV1Despawns(string levelId, IReadOnlyList<MobV1DespawnSnapshot> despawns)
    {
        if (_role != NetRole.Host && _role != NetRole.Client)
            return;
        if (!HasAnyConnection())
            return;
        if (ID <= 0 || despawns == null || despawns.Count == 0)
            return;

        var line = BuildMobV1DespawnLine(ID, levelId, despawns);
        _ = SendLineSafe(line);
    }

    public void SendMobV1HitRequest(
        string levelId,
        int netMobId,
        double x,
        double y,
        int damageHint,
        string attackKind = "",
        double heroX = 0.0,
        double heroY = 0.0,
        int heroDir = 0,
        long attackId = 0,
        double sentAtSeconds = 0.0,
        double hitRadius = 0.0)
    {
        if (_role != NetRole.Client)
            return;
        if (!HasAnyConnection())
            return;
        if (ID <= 0 || netMobId <= 0)
            return;

        var request = new MobV1HitRequest(ID, levelId, netMobId, x, y, damageHint, attackKind, heroX, heroY, heroDir, attackId, sentAtSeconds, hitRadius);
        _ = SendLineSafe(BuildMobV1HitRequestLine(request));
    }

    public void SendMobV1HitResult(MobV1HitResult result)
    {
        if (_role != NetRole.Host)
            return;
        if (!HasAnyConnection())
            return;
        if (ID <= 0)
            return;

        _ = SendLineSafe(BuildMobV1HitResultLine(result));
    }

    public void SendMobV1PlayerHit(MobV1PlayerHit hit)
    {
        if (_role != NetRole.Host)
            return;
        if (!HasAnyConnection())
            return;
        if (ID <= 0 || hit.TargetUserId <= 0)
            return;

        _ = SendLineSafe(BuildMobV1PlayerHitLine(hit));
    }

    public void SendExitReady(int doorCx, int doorCy, bool pressed, bool insideCircle, bool isOutOfGame, bool isOnScreen)
    {
        if (!HasAnyConnection())
            return;
        if (ID <= 0)
            return;

        var state = new ExitReadyState(ID, doorCx, doorCy, pressed, insideCircle, isOutOfGame, isOnScreen);
        var line = BuildExitReadyLine(state);
        _ = SendLineSafe(line);
    }

    public void SendBossCine(string levelId)
    {
        if (!HasAnyConnection())
            return;
        if (string.IsNullOrWhiteSpace(levelId))
            return;

        var safe = levelId.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        if (string.IsNullOrEmpty(safe))
            return;

        SendRaw($"BOSSCINE|{safe}");
    }

    public void SendBossHeroTeleport(double x, double y, int dir)
    {
        if (!HasAnyConnection())
            return;
        if (ID <= 0)
            return;

        SendRaw(
            $"BOSSHEROTELE|{ID.ToString(CultureInfo.InvariantCulture)}|{x.ToString(CultureInfo.InvariantCulture)}|{y.ToString(CultureInfo.InvariantCulture)}|{dir.ToString(CultureInfo.InvariantCulture)}");
    }

    public void SendInterDoor(int userId, double x, double y, string action, bool broken)
    {
        if (!HasAnyConnection())
            return;
        if (userId <= 0)
            return;
        if (string.IsNullOrWhiteSpace(action))
            return;

        SendRaw($"INTERDOOR|{userId}|{x.ToString(CultureInfo.InvariantCulture)}|{y.ToString(CultureInfo.InvariantCulture)}|{action}|{(broken ? 1 : 0)}");
    }

    public void SendInterElevator(double x, double y)
    {
        if (!HasAnyConnection())
            return;
        if (ID <= 0)
            return;

        SendRaw($"INTERELEV|{x.ToString(CultureInfo.InvariantCulture)}|{y.ToString(CultureInfo.InvariantCulture)}");
    }

    public void SendInterPressurePlate(double x, double y)
    {
        if (!HasAnyConnection())
            return;
        if (ID <= 0)
            return;

        SendRaw($"INTERPLATE|{x.ToString(CultureInfo.InvariantCulture)}|{y.ToString(CultureInfo.InvariantCulture)}");
    }

    public void SendInterTreasureChest(double x, double y)
    {
        if (!HasAnyConnection())
            return;
        if (ID <= 0)
            return;

        SendRaw($"INTERCHEST|{x.ToString(CultureInfo.InvariantCulture)}|{y.ToString(CultureInfo.InvariantCulture)}");
    }

    public void SendInterVineLadder(double x, double y)
    {
        if (!HasAnyConnection())
            return;
        if (ID <= 0)
            return;

        SendRaw($"INTERVINELADDER|{x.ToString(CultureInfo.InvariantCulture)}|{y.ToString(CultureInfo.InvariantCulture)}");
    }

    public void SendInterTeleport(double x, double y)
    {
        if (!HasAnyConnection())
            return;
        if (ID <= 0)
            return;

        SendRaw($"INTERTELEPORT|{x.ToString(CultureInfo.InvariantCulture)}|{y.ToString(CultureInfo.InvariantCulture)}");
    }

    public void SendInterBreakableGround(double x, double y)
    {
        if (!HasAnyConnection())
            return;
        if (ID <= 0)
            return;

        SendRaw($"INTERBREAK|{x.ToString(CultureInfo.InvariantCulture)}|{y.ToString(CultureInfo.InvariantCulture)}");
    }

    public void SendInterBossRuneUpdateCells(double x, double y, bool add)
    {
        if (!HasAnyConnection())
            return;
        if (ID <= 0)
            return;

        SendRaw($"BOSSRUNE_UPDATE_CELLS|{x.ToString(CultureInfo.InvariantCulture)}|{y.ToString(CultureInfo.InvariantCulture)}|{(add ? 1 : 0)}");
    }

    public void SendInterPortal(double x, double y, string action)
    {
        if (!HasAnyConnection())
            return;
        if (ID <= 0)
            return;
        if (string.IsNullOrWhiteSpace(action))
            return;

        SendRaw($"INTERPORTAL|{action}|{x.ToString(CultureInfo.InvariantCulture)}|{y.ToString(CultureInfo.InvariantCulture)}");
    }

    public void SendInterBridgeLever(double x, double y, string action = "extend", string cooldownKey = "", int cooldownIdx = 0)
    {
        if (!HasAnyConnection())
            return;
        if (ID <= 0)
            return;

        SendRaw($"INTERBRIDGE|{action}|{x.ToString(CultureInfo.InvariantCulture)}|{y.ToString(CultureInfo.InvariantCulture)}|{cooldownKey}|{cooldownIdx}");
    }

    private void SendRaw(string payload)
    {
        var line = payload.EndsWith('\n') ? payload : payload + "\n";
        _ = SendLineSafe(line);
    }
}
