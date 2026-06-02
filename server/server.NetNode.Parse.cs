using System.Globalization;
using DeadCellsMultiplayerMod.Interaction;

public sealed partial class NetNode
{
    private static void ParseAnimPayload(string payload, out int? parsedId, out string animName, out int? queue, out bool? gFlag)
    {
        parsedId = null;
        animName = string.Empty;
        queue = null;
        gFlag = null;

        var parts = payload.Split('|');
        var startIndex = 0;
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idValue))
        {
            parsedId = idValue;
            startIndex = 1;
        }

        if (parts.Length > startIndex)
            animName = parts[startIndex];

        if (parts.Length > startIndex + 1 &&
            int.TryParse(parts[startIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedQ))
            queue = parsedQ;

        if (parts.Length > startIndex + 2 && TryParseBool(parts[startIndex + 2], out var parsedBool))
            gFlag = parsedBool;
    }

    private static void ParseRoomPayload(string payload, out int? parsedId, out string levelId, out int roomId)
    {
        parsedId = null;
        levelId = string.Empty;
        roomId = -1;

        if (string.IsNullOrWhiteSpace(payload))
            return;

        var parts = payload.Split('|');
        if (parts.Length >= 3 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRemoteId))
        {
            parsedId = parsedRemoteId;
            levelId = parts[1];
            _ = int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out roomId);
            return;
        }

        if (parts.Length >= 2)
        {
            levelId = parts[0];
            _ = int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out roomId);
        }
    }


    private static void ParseHeadAnimPayload(string payload, out int? parsedId, out string animName)
    {
        parsedId = null;
        animName = string.Empty;

        var parts = payload.Split('|');
        var startIndex = 0;
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idValue))
        {
            parsedId = idValue;
            startIndex = 1;
        }

        if (parts.Length > startIndex)
            animName = parts[startIndex];
    }

    private static void ParseWeaponPayload(string payload, out int? parsedId, out string kind, out int slot, out int permanentId, out int? ammo)
    {
        parsedId = null;
        kind = string.Empty;
        slot = -1;
        permanentId = 0;
        ammo = null;

        var parts = payload.Split('|');
        var startIndex = 0;
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idValue))
        {
            parsedId = idValue;
            startIndex = 1;
        }

        if (parts.Length > startIndex)
            kind = parts[startIndex];

        if (parts.Length > startIndex + 1 &&
            int.TryParse(parts[startIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSlot))
            slot = parsedSlot;

        if (parts.Length > startIndex + 2 &&
            int.TryParse(parts[startIndex + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPermanent))
            permanentId = parsedPermanent;

        if (parts.Length > startIndex + 3 &&
            int.TryParse(parts[startIndex + 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAmmo))
            ammo = parsedAmmo;
    }

    private static void ParseAttackPayload(
        string payload,
        out int? parsedId,
        out string kind,
        out int slot,
        out int permanentId,
        out int? ammo,
        out RemoteAttackAction action)
    {
        ParseWeaponPayload(payload, out parsedId, out kind, out slot, out permanentId, out ammo);
        action = RemoteAttackAction.Attack;

        var parts = payload.Split('|');
        var startIndex = 0;
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            startIndex = 1;
        }

        var firstOptionalIndex = startIndex + 3;
        var actionIndex = -1;
        if (parts.Length > firstOptionalIndex)
        {
            if (int.TryParse(parts[firstOptionalIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                actionIndex = firstOptionalIndex + 1;
            else
                actionIndex = firstOptionalIndex;
        }

        if (actionIndex >= 0 && parts.Length > actionIndex)
            action = ParseAttackActionToken(parts[actionIndex]);
    }

    private static RemoteAttackAction ParseAttackActionToken(string? rawAction)
    {
        if (string.IsNullOrWhiteSpace(rawAction))
            return RemoteAttackAction.Attack;

        var action = rawAction.Trim();
        if (action.Equals("INT", StringComparison.OrdinalIgnoreCase) ||
            action.Equals("INTERRUPT", StringComparison.OrdinalIgnoreCase) ||
            action.Equals("I", StringComparison.OrdinalIgnoreCase) ||
            action.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            return RemoteAttackAction.Interrupt;
        }

        return RemoteAttackAction.Attack;
    }

    private static void ParseHpPayload(string payload, out int? parsedId, out int life, out int maxLife, out int lif, out int bonusLife, out int recover)
    {
        parsedId = null;
        life = 0;
        maxLife = 0;
        lif = 0;
        bonusLife = 0;
        recover = 0;

        var parts = payload.Split('|');
        var startIndex = 0;
        if (parts.Length >= 6 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idValue))
        {
            parsedId = idValue;
            startIndex = 1;
        }

        if (parts.Length > startIndex &&
            int.TryParse(parts[startIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLife))
            life = parsedLife;
        if (parts.Length > startIndex + 1 &&
            int.TryParse(parts[startIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMaxLife))
            maxLife = parsedMaxLife;
        if (parts.Length > startIndex + 2 &&
            int.TryParse(parts[startIndex + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLif))
            lif = parsedLif;
        if (parts.Length > startIndex + 3 &&
            int.TryParse(parts[startIndex + 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBonusLife))
            bonusLife = parsedBonusLife;
        if (parts.Length > startIndex + 4 &&
            int.TryParse(parts[startIndex + 4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRecover))
            recover = parsedRecover;
    }

    private static void ParseChatPayload(string payload, out int? parsedId, out string message)
    {
        parsedId = null;
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(payload))
            return;

        var parts = payload.Split(new[] { '|' }, 2);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idValue))
        {
            parsedId = idValue;
            message = parts[1];
            return;
        }

        message = payload;
    }

    private static List<MobStateSnapshot> ParseMobStatesPayload(string payload)
    {
        var states = new List<MobStateSnapshot>();
        if (string.IsNullOrWhiteSpace(payload))
            return states;

        var entries = payload.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in entries)
        {
            var parts = entry.Split(',');
            if (parts.Length < 7)
                continue;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
                continue;
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                continue;
            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dir))
                continue;
            if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var life))
                continue;
            if (!int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxLife))
                continue;
            var animPayload = parts[6];
            var type = parts.Length > 7 ? parts[7] : string.Empty;
            var statePayload = parts.Length > 8 ? parts[8] : string.Empty;

            states.Add(new MobStateSnapshot(index, x, y, dir, life, maxLife, animPayload, type, statePayload));
        }

        return states;
    }

    private static List<MobMoveSnapshot> ParseMobMovesPayload(string payload)
    {
        var moves = new List<MobMoveSnapshot>();
        if (string.IsNullOrWhiteSpace(payload))
            return moves;

        var entries = payload.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in entries)
        {
            var parts = entry.Split(',');
            if (parts.Length < 5)
                continue;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
                continue;
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                continue;
            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dir))
                continue;
            var animPayload = parts.Length > 4 ? parts[4] : string.Empty;

            moves.Add(new MobMoveSnapshot(index, x, y, dir, animPayload));
        }

        return moves;
    }

    private static List<MobChargeSnapshot> ParseMobChargesPayload(string payload)
    {
        var charges = new List<MobChargeSnapshot>();
        if (string.IsNullOrWhiteSpace(payload))
            return charges;

        var entries = payload.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in entries)
        {
            var parts = entry.Split(',');
            if (parts.Length < 3)
                continue;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                continue;
            var skillId = parts.Length > 1 ? parts[1] : string.Empty;
            if (!double.TryParse(parts.Length > 2 ? parts[2] : "0", NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio))
                ratio = 0;

            charges.Add(new MobChargeSnapshot(index, skillId, ratio));
        }

        return charges;
    }

    private static bool TryParseMobHitPayload(string payload, int? senderId, bool forceSenderId, out MobHit hit)
    {
        hit = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 5)
            return false;

        int parsedUserId = 0;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedUserId))
            parsedUserId = senderId ?? 0;

        if (forceSenderId && senderId.HasValue)
            parsedUserId = senderId.Value;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mobIndex))
            return false;
        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hp))
            return false;
        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return false;
        if (!double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;

        var type = parts.Length > 5 ? parts[5] : string.Empty;
        hit = new MobHit(parsedUserId, mobIndex, hp, x, y, type);
        return true;
    }

    private static bool TryParseMobDiePayload(string payload, int? senderId, bool forceSenderId, out MobDie die)
    {
        die = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 4)
            return false;

        int parsedUserId = 0;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedUserId))
            parsedUserId = senderId ?? 0;

        if (forceSenderId && senderId.HasValue)
            parsedUserId = senderId.Value;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mobIndex))
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return false;
        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;

        die = new MobDie(parsedUserId, mobIndex, x, y);
        return true;
    }

    private static bool TryParseMobAttackPayload(string payload, out MobAttack attack)
    {
        attack = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split(',');
        if (parts.Length < 7)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mobIndex))
            return false;

        string skillId;
        try
        {
            skillId = Uri.UnescapeDataString(parts[1]);
        }
        catch
        {
            skillId = parts[1];
        }

        if (string.IsNullOrWhiteSpace(skillId))
            return false;

        var requiresTargetInArea = parts[2] == "1";
        var hasData = parts[3] == "1";

        int? data = null;
        if (hasData)
        {
            if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedData))
                return false;
            data = parsedData;
        }

        if (!double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return false;
        if (!double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;

        var targetUserId = 0;
        if (parts.Length > 7)
            int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out targetUserId);

        var dir = 0;
        if (parts.Length > 8)
            int.TryParse(parts[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out dir);

        attack = new MobAttack(mobIndex, skillId, requiresTargetInArea, data, x, y, targetUserId, dir);
        return true;
    }

    /// <summary>Parse attack event: attack|skillId|blockSec|forcedDirSec|reqTarget|data|targetUid|dir (8 parts)</summary>
    private static bool TryParseMobAttackEvent(string ev, int index, double x, double y, int dir, string type, out MobAttack attack)
    {
        attack = default;
        if (string.IsNullOrEmpty(ev) || !ev.StartsWith("attack|", StringComparison.Ordinal))
            return false;

        var parts = ev.Split('|');
        if (parts.Length < 8)
            return false;

        string skillId;
        try
        {
            skillId = Uri.UnescapeDataString(parts[1]);
        }
        catch
        {
            skillId = parts[1];
        }

        if (string.IsNullOrWhiteSpace(skillId))
            return false;

        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var blockSec))
            blockSec = 0;
        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var forcedDirSec))
            forcedDirSec = 0;
        var requiresTargetInArea = parts[4] == "1";
        var dataVal = 0;
        int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out dataVal);
        int? data = dataVal != 0 ? dataVal : null;
        var targetUserId = 0;
        int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out targetUserId);
        var attackDir = 0;
        if (parts.Length > 7)
            int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out attackDir);

        attack = new MobAttack(index, skillId, requiresTargetInArea, data, x, y, targetUserId, attackDir != 0 ? attackDir : dir, blockSec, forcedDirSec, type ?? string.Empty);
        return true;
    }

    /// <summary>Parse hit event: hit|life or hit|life|maxLife</summary>
    private static bool TryParseMobHitEvent(string ev, int index, double x, double y, int userId, string? mobType, out MobHit hit)
    {
        hit = default;
        if (string.IsNullOrEmpty(ev) || !ev.StartsWith("hit|", StringComparison.Ordinal))
            return false;

        var parts = ev.Split('|');
        if (parts.Length < 2)
            return false;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var life))
            return false;

        hit = new MobHit(userId, index, life, x, y, mobType ?? string.Empty);
        return true;
    }

    /// <summary>Parse MOBEVENT payload. Format: idx,x,y,dir[,type]§event1§event2;idx2,x2,y2,dir2[,type2]§event1. Events use § separator (they contain |).</summary>
    private static List<MobEventUpdate> ParseMobEventsPayload(string payload)
    {
        const char EventSep = '\u00A7';
        var result = new List<MobEventUpdate>();
        if (string.IsNullOrWhiteSpace(payload))
            return result;

        var mobEntries = payload.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in mobEntries)
        {
            var sepIndex = entry.IndexOf(EventSep);
            var basePart = sepIndex >= 0 ? entry[..sepIndex] : entry;
            var eventsPart = sepIndex >= 0 && sepIndex + 1 < entry.Length ? entry[(sepIndex + 1)..] : string.Empty;

            var baseParts = basePart.Split(',');
            if (baseParts.Length < 4)
                continue;

            if (!int.TryParse(baseParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                continue;
            if (!double.TryParse(baseParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
                continue;
            if (!double.TryParse(baseParts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                continue;
            if (!int.TryParse(baseParts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dir))
                continue;

            var type = baseParts.Length >= 5 ? string.Join(",", baseParts.Skip(4)) : string.Empty;

            var events = new List<string>();
            foreach (var ev in eventsPart.Split(EventSep, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!string.IsNullOrEmpty(ev))
                    events.Add(ev);
            }

            result.Add(new MobEventUpdate(index, x, y, dir, events, type));
        }

        return result;
    }

    private static bool TryParseMobDrawPayload(string payload, int? senderId, bool forceSenderId, out List<MobDraw> draws)
    {
        draws = new List<MobDraw>();
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var entries = payload.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (entries.Length == 0)
            entries = new[] { payload };

        for (int i = 0; i < entries.Length; i++)
        {
            if (!TryParseSingleMobDrawPayload(entries[i], senderId, forceSenderId, out var draw))
                continue;

            draws.Add(draw);
        }

        return draws.Count > 0;
    }

    private static bool TryParseMobProjectionPayload(string payload, int? senderId, bool forceSenderId, out int userId, out string levelId, out List<MobProjectionSnapshot> projections)
    {
        userId = 0;
        levelId = string.Empty;
        projections = new List<MobProjectionSnapshot>();
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split(new[] { '|' }, 3);
        if (parts.Length < 3)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId))
            userId = senderId ?? 0;
        if (forceSenderId && senderId.HasValue)
            userId = senderId.Value;
        if (userId <= 0)
            return false;

        try
        {
            levelId = Uri.UnescapeDataString(parts[1] ?? string.Empty);
        }
        catch
        {
            levelId = parts[1] ?? string.Empty;
        }

        levelId = levelId.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();

        var entries = parts[2].Split(';', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < entries.Length; i++)
        {
            var entryParts = entries[i].Split(',');
            if (entryParts.Length < 7)
                continue;

            if (!int.TryParse(entryParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mobIndex))
                continue;
            if (!double.TryParse(entryParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
                continue;
            if (!double.TryParse(entryParts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                continue;
            if (!int.TryParse(entryParts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dir))
                dir = 0;
            if (!int.TryParse(entryParts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var life))
                life = 0;
            if (!int.TryParse(entryParts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxLife))
                maxLife = 0;

            var type = DecodeProjectionToken(entryParts[6] ?? string.Empty);
            var animGroup = entryParts.Length > 7 ? DecodeProjectionToken(entryParts[7] ?? string.Empty) : string.Empty;

            projections.Add(new MobProjectionSnapshot(userId, levelId, mobIndex, x, y, dir, life, maxLife, type, animGroup));
        }

        return true;
    }

    private static bool TryParseMobV1StatePayload(string payload, int? senderId, bool forceSenderId, out int hostUserId, out string levelId, out List<MobV1StateSnapshot> states)
    {
        hostUserId = 0;
        levelId = string.Empty;
        states = new List<MobV1StateSnapshot>();
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split(new[] { '|' }, 3);
        if (parts.Length < 3)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hostUserId))
            hostUserId = senderId ?? 0;
        if (forceSenderId && senderId.HasValue)
            hostUserId = senderId.Value;
        if (hostUserId <= 0)
            return false;

        try
        {
            levelId = Uri.UnescapeDataString(parts[1] ?? string.Empty);
        }
        catch
        {
            levelId = parts[1] ?? string.Empty;
        }

        levelId = levelId.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();

        var entries = parts[2].Split(';', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < entries.Length; i++)
        {
            var entryParts = entries[i].Split(',');
            if (entryParts.Length < 7)
                continue;

            if (!int.TryParse(entryParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var netMobId))
                continue;
            if (!double.TryParse(entryParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
                continue;
            if (!double.TryParse(entryParts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                continue;
            if (!int.TryParse(entryParts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dir))
                dir = 0;
            if (!int.TryParse(entryParts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var life))
                life = 0;
            if (!int.TryParse(entryParts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxLife))
                maxLife = 0;

            var type = DecodeProjectionToken(entryParts[6] ?? string.Empty);
            var animGroup = entryParts.Length > 7 ? DecodeProjectionToken(entryParts[7] ?? string.Empty) : string.Empty;

            states.Add(new MobV1StateSnapshot(hostUserId, levelId, netMobId, x, y, dir, life, maxLife, type, animGroup));
        }

        return true;
    }

    private static bool TryParseMobV1SpawnPayload(string payload, int? senderId, bool forceSenderId, out int hostUserId, out string levelId, out List<MobV1SpawnSnapshot> spawns)
    {
        hostUserId = 0;
        levelId = string.Empty;
        spawns = new List<MobV1SpawnSnapshot>();
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split(new[] { '|' }, 3);
        if (parts.Length < 3)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hostUserId))
            hostUserId = senderId ?? 0;
        if (forceSenderId && senderId.HasValue)
            hostUserId = senderId.Value;
        if (hostUserId <= 0)
            return false;

        levelId = DecodeProjectionToken(parts[1] ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();

        var entries = parts[2].Split(';', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < entries.Length; i++)
        {
            var entryParts = entries[i].Split(',');
            if (entryParts.Length < 7)
                continue;

            if (!int.TryParse(entryParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var netMobId))
                continue;
            if (!double.TryParse(entryParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
                continue;
            if (!double.TryParse(entryParts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                continue;
            if (!int.TryParse(entryParts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dir))
                dir = 0;
            if (!int.TryParse(entryParts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var life))
                life = 0;
            if (!int.TryParse(entryParts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxLife))
                maxLife = 0;

            var type = DecodeProjectionToken(entryParts[6] ?? string.Empty);
            var animGroup = entryParts.Length > 7 ? DecodeProjectionToken(entryParts[7] ?? string.Empty) : string.Empty;
            spawns.Add(new MobV1SpawnSnapshot(hostUserId, levelId, netMobId, x, y, dir, life, maxLife, type, animGroup));
        }

        return true;
    }

    private static bool TryParseMobV1DespawnPayload(string payload, int? senderId, bool forceSenderId, out int hostUserId, out string levelId, out List<MobV1DespawnSnapshot> despawns)
    {
        hostUserId = 0;
        levelId = string.Empty;
        despawns = new List<MobV1DespawnSnapshot>();
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split(new[] { '|' }, 3);
        if (parts.Length < 3)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hostUserId))
            hostUserId = senderId ?? 0;
        if (forceSenderId && senderId.HasValue)
            hostUserId = senderId.Value;
        if (hostUserId <= 0)
            return false;

        levelId = DecodeProjectionToken(parts[1] ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();

        var entries = parts[2].Split(';', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < entries.Length; i++)
        {
            var entryParts = entries[i].Split(',');
            if (entryParts.Length == 0)
                continue;
            if (!int.TryParse(entryParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var netMobId))
                continue;

            var reason = entryParts.Length > 1 ? DecodeProjectionToken(entryParts[1] ?? string.Empty) : string.Empty;
            despawns.Add(new MobV1DespawnSnapshot(hostUserId, levelId, netMobId, reason));
        }

        return true;
    }

    private static bool TryParseMobV1HitRequestPayload(string payload, int? senderId, bool forceSenderId, out MobV1HitRequest request)
    {
        request = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 3)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var attackerUserId))
            attackerUserId = senderId ?? 0;
        if (forceSenderId && senderId.HasValue)
            attackerUserId = senderId.Value;
        if (attackerUserId <= 0)
            return false;

        var levelId = DecodeProjectionToken(parts[1] ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        var entryParts = parts[2].Split(',');
        if (entryParts.Length < 4)
            return false;

        if (!int.TryParse(entryParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var netMobId))
            return false;
        if (!double.TryParse(entryParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return false;
        if (!double.TryParse(entryParts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;
        if (!int.TryParse(entryParts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var damageHint))
            damageHint = 0;

        var attackKind = entryParts.Length > 4 ? DecodeProjectionToken(entryParts[4] ?? string.Empty) : string.Empty;
        double heroX = 0.0;
        double heroY = 0.0;
        var heroDir = 0;
        long attackId = 0;
        double sentAtSeconds = 0.0;
        double hitRadius = 0.0;

        if (entryParts.Length > 5)
            double.TryParse(entryParts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out heroX);
        if (entryParts.Length > 6)
            double.TryParse(entryParts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out heroY);
        if (entryParts.Length > 7)
            int.TryParse(entryParts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out heroDir);
        if (entryParts.Length > 8)
            long.TryParse(entryParts[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out attackId);
        if (entryParts.Length > 9)
            double.TryParse(entryParts[9], NumberStyles.Float, CultureInfo.InvariantCulture, out sentAtSeconds);
        if (entryParts.Length > 10)
            double.TryParse(entryParts[10], NumberStyles.Float, CultureInfo.InvariantCulture, out hitRadius);

        request = new MobV1HitRequest(
            attackerUserId,
            levelId,
            netMobId,
            x,
            y,
            damageHint,
            attackKind,
            heroX,
            heroY,
            heroDir,
            attackId,
            sentAtSeconds,
            hitRadius);
        return true;
    }

    private static bool TryParseMobV1HitResultPayload(string payload, int? senderId, bool forceSenderId, out MobV1HitResult result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 3)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hostUserId))
            hostUserId = senderId ?? 0;
        if (forceSenderId && senderId.HasValue)
            hostUserId = senderId.Value;
        if (hostUserId <= 0)
            return false;

        var levelId = DecodeProjectionToken(parts[1] ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        var entryParts = parts[2].Split(',');
        if (entryParts.Length < 5)
            return false;

        if (!int.TryParse(entryParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var attackerUserId))
            return false;
        if (!int.TryParse(entryParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var netMobId))
            return false;
        var accepted = string.Equals(entryParts[2], "1", StringComparison.Ordinal) ||
                       string.Equals(entryParts[2], "true", StringComparison.OrdinalIgnoreCase);
        if (!int.TryParse(entryParts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var life))
            life = 0;
        if (!int.TryParse(entryParts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxLife))
            maxLife = 0;

        var reason = entryParts.Length > 5 ? DecodeProjectionToken(entryParts[5] ?? string.Empty) : string.Empty;
        var damage = 0;
        var death = false;
        if (entryParts.Length > 6)
            int.TryParse(entryParts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out damage);
        if (entryParts.Length > 7)
            death = string.Equals(entryParts[7], "1", StringComparison.Ordinal) ||
                    string.Equals(entryParts[7], "true", StringComparison.OrdinalIgnoreCase);

        result = new MobV1HitResult(hostUserId, attackerUserId, levelId, netMobId, accepted, life, maxLife, reason, damage, death);
        return true;
    }

    private static bool TryParseMobV1PlayerHitPayload(string payload, int? senderId, bool forceSenderId, out MobV1PlayerHit hit)
    {
        hit = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 3)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hostUserId))
            hostUserId = senderId ?? 0;
        if (forceSenderId && senderId.HasValue)
            hostUserId = senderId.Value;
        if (hostUserId <= 0)
            return false;

        var levelId = DecodeProjectionToken(parts[1] ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        var entryParts = parts[2].Split(',');
        if (entryParts.Length < 5)
            return false;

        if (!int.TryParse(entryParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var targetUserId))
            return false;
        if (!int.TryParse(entryParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var netMobId))
            return false;
        if (!int.TryParse(entryParts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var damage))
            damage = 0;
        if (!double.TryParse(entryParts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            x = 0.0;
        if (!double.TryParse(entryParts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            y = 0.0;

        hit = new MobV1PlayerHit(hostUserId, targetUserId, levelId, netMobId, damage, x, y);
        return true;
    }

    private static string DecodeProjectionToken(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value ?? string.Empty);
        }
        catch
        {
            return value ?? string.Empty;
        }
    }

    private static string BuildMobProjectionLine(int userId, string levelId, IReadOnlyList<MobProjectionSnapshot> projections)
    {
        var safeLevel = Uri.EscapeDataString((levelId ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim());
        var sb = new System.Text.StringBuilder();
        sb.Append("MOBPROJ|");
        sb.Append(userId.ToString(CultureInfo.InvariantCulture));
        sb.Append('|');
        sb.Append(safeLevel);
        sb.Append('|');

        for (int i = 0; i < projections.Count; i++)
        {
            var p = projections[i];
            if (i > 0)
                sb.Append(';');

            sb.Append(p.MobIndex.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(p.X.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(p.Y.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(p.Dir.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(p.Life.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(p.MaxLife.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(Uri.EscapeDataString((p.Type ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty)));
            sb.Append(',');
            sb.Append(Uri.EscapeDataString((p.AnimGroup ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty)));
        }

        sb.Append('\n');
        return sb.ToString();
    }

    private static string BuildMobV1StateLine(int hostUserId, string levelId, IReadOnlyList<MobV1StateSnapshot> states)
    {
        var safeLevel = Uri.EscapeDataString((levelId ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim());
        var sb = new System.Text.StringBuilder();
        sb.Append("MOBV1STATE|");
        sb.Append(hostUserId.ToString(CultureInfo.InvariantCulture));
        sb.Append('|');
        sb.Append(safeLevel);
        sb.Append('|');

        for (int i = 0; i < states.Count; i++)
        {
            var state = states[i];
            if (i > 0)
                sb.Append(';');

            sb.Append(state.NetMobId.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(state.X.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(state.Y.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(state.Dir.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(state.Life.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(state.MaxLife.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(Uri.EscapeDataString((state.Type ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty)));
            sb.Append(',');
            sb.Append(Uri.EscapeDataString((state.AnimGroup ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty)));
        }

        return sb.ToString();
    }

    private static string BuildMobV1SpawnLine(int hostUserId, string levelId, IReadOnlyList<MobV1SpawnSnapshot> spawns)
    {
        var safeLevel = Uri.EscapeDataString((levelId ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim());
        var sb = new System.Text.StringBuilder();
        sb.Append("MOBV1SPAWN|");
        sb.Append(hostUserId.ToString(CultureInfo.InvariantCulture));
        sb.Append('|');
        sb.Append(safeLevel);
        sb.Append('|');

        for (int i = 0; i < spawns.Count; i++)
        {
            var spawn = spawns[i];
            if (i > 0)
                sb.Append(';');

            sb.Append(spawn.NetMobId.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(spawn.X.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(spawn.Y.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(spawn.Dir.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(spawn.Life.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(spawn.MaxLife.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(Uri.EscapeDataString((spawn.Type ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty)));
            sb.Append(',');
            sb.Append(Uri.EscapeDataString((spawn.AnimGroup ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty)));
        }

        return sb.ToString();
    }

    private static string BuildMobV1DespawnLine(int hostUserId, string levelId, IReadOnlyList<MobV1DespawnSnapshot> despawns)
    {
        var safeLevel = Uri.EscapeDataString((levelId ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim());
        var sb = new System.Text.StringBuilder();
        sb.Append("MOBV1DESPAWN|");
        sb.Append(hostUserId.ToString(CultureInfo.InvariantCulture));
        sb.Append('|');
        sb.Append(safeLevel);
        sb.Append('|');

        for (int i = 0; i < despawns.Count; i++)
        {
            var despawn = despawns[i];
            if (i > 0)
                sb.Append(';');

            sb.Append(despawn.NetMobId.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(Uri.EscapeDataString((despawn.Reason ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty)));
        }

        return sb.ToString();
    }

    private static string BuildMobV1HitRequestLine(MobV1HitRequest request)
    {
        var safeLevel = Uri.EscapeDataString((request.LevelId ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim());
        return string.Create(
            CultureInfo.InvariantCulture,
            $"MOBV1HITREQ|{request.AttackerUserId}|{safeLevel}|{request.NetMobId},{request.X:R},{request.Y:R},{request.DamageHint},{Uri.EscapeDataString((request.AttackKind ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty))},{request.HeroX:R},{request.HeroY:R},{request.HeroDir},{request.AttackId},{request.SentAtSeconds:R},{request.HitRadius:R}");
    }

    private static string BuildMobV1HitResultLine(MobV1HitResult result)
    {
        var safeLevel = Uri.EscapeDataString((result.LevelId ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim());
        var accepted = result.Accepted ? "1" : "0";
        var death = result.Death ? "1" : "0";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"MOBV1HITRES|{result.HostUserId}|{safeLevel}|{result.AttackerUserId},{result.NetMobId},{accepted},{result.Life},{result.MaxLife},{Uri.EscapeDataString((result.Reason ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty))},{result.Damage},{death}");
    }

    private static string BuildMobV1PlayerHitLine(MobV1PlayerHit hit)
    {
        var safeLevel = Uri.EscapeDataString((hit.LevelId ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim());
        return string.Create(
            CultureInfo.InvariantCulture,
            $"MOBV1PLAYERHIT|{hit.HostUserId}|{safeLevel}|{hit.TargetUserId},{hit.NetMobId},{hit.Damage},{hit.X:R},{hit.Y:R}");
    }

    private static bool TryParseSingleMobDrawPayload(string payload, int? senderId, bool forceSenderId, out MobDraw draw)
    {
        draw = default;
        var parts = payload.Split('|');
        if (parts.Length < 4)
            return false;

        int parsedUserId = 0;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedUserId))
            parsedUserId = senderId ?? 0;

        if (forceSenderId && senderId.HasValue)
            parsedUserId = senderId.Value;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mobIndex))
            return false;
        if (!TryParseBool(parts[2], out var isOutOfGame))
            return false;
        if (!TryParseBool(parts[3], out var isOnScreen))
            return false;

        draw = new MobDraw(parsedUserId, mobIndex, isOutOfGame, isOnScreen);
        return true;
    }

    private static bool TryParseExitReadyPayload(string payload, int? senderId, bool forceSenderId, out ExitReadyState state)
    {
        state = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 7)
            return false;

        int parsedUserId = 0;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedUserId))
            parsedUserId = senderId ?? 0;

        if (forceSenderId && senderId.HasValue)
            parsedUserId = senderId.Value;
        if (parsedUserId <= 0)
            return false;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var doorCx))
            return false;
        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var doorCy))
            return false;
        if (!TryParseBool(parts[3], out var pressed))
            return false;
        if (!TryParseBool(parts[4], out var insideCircle))
            return false;
        if (!TryParseBool(parts[5], out var isOutOfGame))
            return false;
        if (!TryParseBool(parts[6], out var isOnScreen))
            return false;

        state = new ExitReadyState(parsedUserId, doorCx, doorCy, pressed, insideCircle, isOutOfGame, isOnScreen);
        return true;
    }

    private static bool TryParsePlayerDownPayload(string payload, int? senderId, bool forceSenderId, out PlayerDownState state)
    {
        state = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 5)
            return false;

        int parsedUserId;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedUserId))
            parsedUserId = senderId ?? 0;
        if (forceSenderId && senderId.HasValue)
            parsedUserId = senderId.Value;
        if (parsedUserId <= 0)
            return false;

        if (!TryParseBool(parts[1], out var isDowned))
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return false;
        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;

        var levelId = parts[4] ?? string.Empty;
        levelId = levelId.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        if (levelId.Length == 0)
            levelId = string.Empty;

        var hasHeadPosition = false;
        var headX = 0d;
        var headY = 0d;
        var hasHeadAnim = false;
        string? headAnim = null;
        if (parts.Length >= 7 &&
            double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedHeadX) &&
            double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedHeadY))
        {
            hasHeadPosition = true;
            headX = parsedHeadX;
            headY = parsedHeadY;

            if (parts.Length >= 8)
            {
                var parsedAnim = (parts[7] ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(parsedAnim))
                {
                    hasHeadAnim = true;
                    headAnim = parsedAnim;
                }
            }
        }

        state = new PlayerDownState(parsedUserId, isDowned, x, y, levelId, hasHeadPosition, headX, headY, hasHeadAnim, headAnim);
        return true;
    }

    private static bool TryParsePlayerRevivePayload(string payload, int? senderId, bool forceSenderId, out PlayerReviveRequest request)
    {
        request = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 2)
            return false;

        int reviverId;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out reviverId))
            reviverId = senderId ?? 0;
        if (forceSenderId && senderId.HasValue)
            reviverId = senderId.Value;
        if (reviverId <= 0)
            return false;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var targetId))
            return false;
        if (targetId <= 0)
            return false;

        request = new PlayerReviveRequest(reviverId, targetId);
        return true;
    }

    private static bool TryParseInterDoorPayload(string payload, int? senderId, bool forceSenderId, out InterDoorEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 5)
            return false;

        int userId = 0;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId))
            userId = senderId ?? 0;
        if (forceSenderId && senderId.HasValue)
            userId = senderId.Value;
        if (userId <= 0)
            return false;

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;

        var action = (parts[3] ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(action))
            return false;

        if (!TryParseBool(parts[4], out var broken))
            return false;

        ev = new InterDoorEvent(userId, x, y, action, broken);
        return true;
    }

    private static bool TryParseInterElevatorPayload(string payload, out InterElevatorEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 2)
            return false;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;

        ev = new InterElevatorEvent(x, y);
        return true;
    }

    private static bool TryParseInterPressurePlatePayload(string payload, out InterPressurePlateEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 2)
            return false;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;

        ev = new InterPressurePlateEvent(x, y);
        return true;
    }

    private static bool TryParseInterTreasureChestPayload(string payload, out InterTreasureChestEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 2)
            return false;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;

        ev = new InterTreasureChestEvent(x, y);
        return true;
    }

    private static bool TryParseInterVineLadderPayload(string payload, out InterVineLadderEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 2)
            return false;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;

        ev = new InterVineLadderEvent(x, y);
        return true;
    }

    private static bool TryParseInterTeleportPayload(string payload, out InterTeleportEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 2)
            return false;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;

        ev = new InterTeleportEvent(x, y);
        return true;
    }

    private static bool TryParseBossHeroTeleportPayload(string payload, int? senderId, bool forceSenderId, out BossHeroTeleportEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 4)
            return false;

        int userId;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId))
            userId = senderId ?? 0;
        if (forceSenderId && senderId.HasValue)
            userId = senderId.Value;
        if (userId <= 0)
            return false;

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;
        if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dir))
            return false;

        ev = new BossHeroTeleportEvent(userId, x, y, dir);
        return true;
    }

    private static bool TryParseInterBreakableGroundPayload(string payload, out InterBreakableGroundEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 2)
            return false;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;

        ev = new InterBreakableGroundEvent(x, y);
        return true;
    }

    private static bool TryParseInterPortalPayload(string payload, out InterPortalEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 3)
            return false;

        var action = parts[0]?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(action) || (action != "show" && action != "close"))
            return false;

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;

        ev = new InterPortalEvent(x, y, action);
        return true;
    }

    private static bool TryParseInterBridgePayload(string payload, out InterBridgeLeverEvent ev)
    {
        ev = default;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split('|');
        if (parts.Length < 3)
            return false;

        var action = parts[0]?.Trim() ?? string.Empty;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;

        var cooldownKey = parts.Length > 3 ? (parts[3] ?? string.Empty) : string.Empty;
        var cooldownIdx = 0;
        if (parts.Length > 4)
            int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out cooldownIdx);

        ev = new InterBridgeLeverEvent(x, y, action, cooldownKey, cooldownIdx);
        return true;
    }

    private static bool TryParsePositionLine(string line, int? senderId, out int remoteId, out double rx, out double ry, out int dir, out bool hasDir)
    {
        remoteId = 0;
        rx = 0;
        ry = 0;
        dir = 0;
        hasDir = false;

        var parts = line.Split('|');
        if (parts.Length >= 4 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRemoteIdWithDir) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var cxWithDir) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var cyWithDir) &&
            int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDir))
        {
            remoteId = parsedRemoteIdWithDir;
            rx = cxWithDir;
            ry = cyWithDir;
            dir = parsedDir < 0 ? -1 : parsedDir > 0 ? 1 : 0;
            hasDir = true;
            return true;
        }

        if (parts.Length >= 3 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRemoteId) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var cx) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var cy))
        {
            remoteId = parsedRemoteId;
            rx = cx;
            ry = cy;
            return true;
        }

        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var cxFallback) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var cyFallback) &&
            senderId.HasValue)
        {
            remoteId = senderId.Value;
            rx = cxFallback;
            ry = cyFallback;
            return true;
        }

        return false;
    }
    private static bool TryParseBool(string text, out bool value)
    {
        if (string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }
        if (string.Equals(text, "0", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }
        value = false;
        return false;
    }
}
