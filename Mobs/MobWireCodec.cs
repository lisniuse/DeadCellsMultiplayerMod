using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Hashlink-free wire encoding for mob sync lines (extracted from NetNode for reuse in MobSyncWorker).
/// </summary>
internal static class MobWireCodec
{
    public static string BuildMobStatesLine(IReadOnlyList<NetNode.MobStateSnapshot> states)
    {
        var sb = new StringBuilder("MOBSTATE|");
        if (states != null)
        {
            for (int i = 0; i < states.Count; i++)
            {
                var state = states[i];
                if (i > 0)
                    sb.Append(';');
                sb.Append(state.Index.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(state.X.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(state.Y.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(state.Dir.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(state.Life.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(state.MaxLife.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(state.AnimPayload ?? string.Empty);
                sb.Append(',');
                sb.Append(state.Type ?? string.Empty);
                sb.Append(',');
                sb.Append(state.StatePayload ?? string.Empty);
            }
        }
        sb.Append('\n');
        return sb.ToString();
    }

    public static string BuildMobEventsLine(IReadOnlyList<NetNode.MobEventUpdate> updates)
    {
        const char EventSep = '\u00A7';
        var sb = new StringBuilder("MOBEVENT|");
        if (updates != null)
        {
            for (int i = 0; i < updates.Count; i++)
            {
                var u = updates[i];
                if (i > 0)
                    sb.Append(';');
                sb.Append(u.Index.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(u.X.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(u.Y.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(u.Dir.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(u.Type ?? string.Empty);
                if (u.Events != null)
                {
                    for (int j = 0; j < u.Events.Count; j++)
                    {
                        sb.Append(EventSep);
                        sb.Append(u.Events[j] ?? string.Empty);
                    }
                }
            }
        }
        sb.Append('\n');
        return sb.ToString();
    }

    public static string BuildMobMovesLine(IReadOnlyList<NetNode.MobMoveSnapshot> moves)
    {
        var sb = new StringBuilder("MOBMOVE|");
        if (moves != null)
        {
            for (int i = 0; i < moves.Count; i++)
            {
                var move = moves[i];
                if (i > 0)
                    sb.Append(';');
                sb.Append(move.Index.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(move.X.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(move.Y.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(move.Dir.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(move.AnimPayload ?? string.Empty);
            }
        }
        sb.Append('\n');
        return sb.ToString();
    }

    public static string BuildMobChargesLine(IReadOnlyList<NetNode.MobChargeSnapshot> charges)
    {
        var sb = new StringBuilder("MOBCHARGE|");
        if (charges != null)
        {
            for (int i = 0; i < charges.Count; i++)
            {
                var charge = charges[i];
                if (i > 0)
                    sb.Append(';');
                sb.Append(charge.Index.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(charge.SkillId ?? string.Empty);
                sb.Append(',');
                sb.Append(charge.Ratio.ToString(CultureInfo.InvariantCulture));
            }
        }
        sb.Append('\n');
        return sb.ToString();
    }

    public static string BuildMobAttackLine(NetNode.MobAttack attack)
    {
        string encodedSkill;
        try
        {
            encodedSkill = Uri.EscapeDataString(attack.SkillId ?? string.Empty);
        }
        catch
        {
            encodedSkill = attack.SkillId ?? string.Empty;
        }

        var hasData = attack.Data.HasValue;
        var dataPart = attack.Data.GetValueOrDefault().ToString(CultureInfo.InvariantCulture);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"MOBATK|{attack.Index},{encodedSkill},{(attack.RequiresTargetInArea ? 1 : 0)},{(hasData ? 1 : 0)},{dataPart},{attack.X},{attack.Y},{attack.TargetUserId},{attack.Dir}\n");
    }

    public static string BuildMobDieLine(NetNode.MobDie die)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"MOBDIE|{die.UserId}|{die.MobIndex}|{die.X}|{die.Y}\n");
    }

    public static string BuildMobDrawLine(int userId, int mobIndex, bool isOutOfGame, bool isOnScreen)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"MOBDRAW|{userId}|{mobIndex}|{(isOutOfGame ? 1 : 0)}|{(isOnScreen ? 1 : 0)}\n");
    }

    public static string BuildMobDrawLine(IReadOnlyList<NetNode.MobDraw> draws)
    {
        var sb = new StringBuilder("MOBDRAW|");
        if (draws != null)
        {
            for (int i = 0; i < draws.Count; i++)
            {
                var draw = draws[i];
                if (i > 0)
                    sb.Append(';');

                sb.Append(draw.UserId.ToString(CultureInfo.InvariantCulture));
                sb.Append('|');
                sb.Append(draw.MobIndex.ToString(CultureInfo.InvariantCulture));
                sb.Append('|');
                sb.Append(draw.IsOutOfGame ? '1' : '0');
                sb.Append('|');
                sb.Append(draw.IsOnScreen ? '1' : '0');
            }
        }

        sb.Append('\n');
        return sb.ToString();
    }
}
