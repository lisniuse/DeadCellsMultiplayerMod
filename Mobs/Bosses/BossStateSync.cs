using System.Globalization;
using dc.en;
using dc.en.mob;
using dc.en.mob.boss;
using dc.en.mob.boss.death;

namespace DeadCellsMultiplayerMod.Mobs.Bosses;

public static class BossStateSync
{
    private const string PhasePrefix = "bp:";
    private const string ActionPrefix = "ba:";
    private const string DeathCurrentActionPrefix = "bdca:";
    private const string DeathNextActionPrefix = "bdna:";
    private const string DeathForcedActionPrefix = "bdfa:";
    private const string DeathSicklesEnabledPrefix = "bdse:";
    private const string DeathChokingHeroPrefix = "bdch:";

    public static string AppendBossState(string basePayload, Mob mob)
    {
        if (mob == null)
            return basePayload ?? string.Empty;

        if (!BossSyncHelpers.IsBossMob(mob))
            return basePayload ?? string.Empty;

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(basePayload))
            parts.Add(basePayload);

        if (mob is GardenerBoss gardener)
        {
            try
            {
                var phase = gardener.phase;
                parts.Add(PhasePrefix + phase.ToString(CultureInfo.InvariantCulture));

                try
                {
                    var idx = (int)gardener.action.Index;
                    parts.Add(ActionPrefix + idx.ToString(CultureInfo.InvariantCulture));
                }
                catch
                {
                    // action may be unset
                }
            }
            catch
            {
                // ignore
            }
        }
        else if (mob is Collector collector)
        {
            try
            {
                var phase = collector.phase;
                parts.Add(PhasePrefix + phase.ToString(CultureInfo.InvariantCulture));
            }
            catch
            {
                // ignore
            }
        }
        else if (mob is Death death)
        {
            try
            {
                AppendDeathAction(parts, DeathCurrentActionPrefix, death.currentAction);
                AppendDeathAction(parts, DeathNextActionPrefix, death.nextAction);
                AppendDeathAction(parts, DeathForcedActionPrefix, death.forcedAction);
                parts.Add(DeathSicklesEnabledPrefix + (death.sicklesEnabled ? "1" : "0"));
                parts.Add(DeathChokingHeroPrefix + (death.isChokingHero ? "1" : "0"));
            }
            catch
            {
                // ignore
            }
        }

        return parts.Count == 0 ? (basePayload ?? string.Empty) : string.Join(".", parts);
    }

    public static void ApplyBossStateFromPayload(Mob mob, string? payload)
    {
        if (mob == null || mob.destroyed || string.IsNullOrWhiteSpace(payload))
            return;

        BossDiag.Phase($"ApplyBossStateFromPayload type={mob.GetType().Name} payload={payload}");

        int? phaseVal = null;
        int? actionVal = null;
        int? deathCurrentActionVal = null;
        int? deathNextActionVal = null;
        int? deathForcedActionVal = null;
        bool? deathSicklesEnabledVal = null;
        bool? deathChokingHeroVal = null;

        var parts = payload.Split('.', StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in parts)
        {
            var t = token?.Trim();
            if (string.IsNullOrEmpty(t))
                continue;

            if (t.StartsWith(PhasePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var s = t[PhasePrefix.Length..].Trim();
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
                    phaseVal = p;
            }
            else if (t.StartsWith(ActionPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var s = t[ActionPrefix.Length..].Trim();
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a))
                    actionVal = a;
            }
            else if (t.StartsWith(DeathCurrentActionPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var s = t[DeathCurrentActionPrefix.Length..].Trim();
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a))
                    deathCurrentActionVal = a;
            }
            else if (t.StartsWith(DeathNextActionPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var s = t[DeathNextActionPrefix.Length..].Trim();
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a))
                    deathNextActionVal = a;
            }
            else if (t.StartsWith(DeathForcedActionPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var s = t[DeathForcedActionPrefix.Length..].Trim();
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a))
                    deathForcedActionVal = a;
            }
            else if (t.StartsWith(DeathSicklesEnabledPrefix, StringComparison.OrdinalIgnoreCase))
            {
                deathSicklesEnabledVal = ParseWireBool(t[DeathSicklesEnabledPrefix.Length..]);
            }
            else if (t.StartsWith(DeathChokingHeroPrefix, StringComparison.OrdinalIgnoreCase))
            {
                deathChokingHeroVal = ParseWireBool(t[DeathChokingHeroPrefix.Length..]);
            }
        }

        if (mob is GardenerBoss gardener)
        {
            try
            {
                if (phaseVal.HasValue)
                {
#pragma warning disable CS8604, CS8625 // Gardener phase/action are Haxe-bound; compare via runtime equality
                    var currentPhase = gardener.phase;
                    if (!Equals(currentPhase, phaseVal.Value))
                    {
                        BossDiag.Phase($"gardener.phase set -> {phaseVal.Value}");
                        gardener.phase = phaseVal.Value;
                    }
#pragma warning restore CS8604, CS8625
                }

                if (actionVal.HasValue)
                {
                    var currentAction = gardener.action;
                    var currentActionIndex = TryGetBossActionIndex(currentAction);
                    if (!currentActionIndex.HasValue || currentActionIndex.Value != actionVal.GetValueOrDefault())
                    {
                        BossAction? newAction = CreateBossActionByIndex(actionVal.Value);
                        if (newAction is not null)
                        {
                            BossDiag.Phase($"gardener.action set -> {actionVal.Value}");
                            gardener.action = newAction;
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }
        }
        else if (mob is Collector collector && phaseVal.HasValue)
        {
            try
            {
                var currentPhase = collector.phase;
                if (currentPhase != phaseVal.Value)
                {
                    BossDiag.Phase($"collector.phase set -> {phaseVal.Value}");
                    collector.phase = phaseVal.Value;
                }
            }
            catch
            {
                // ignore
            }
        }
        else if (mob is Death death)
        {
            ApplyDeathState(death, deathCurrentActionVal, deathNextActionVal, deathForcedActionVal, deathSicklesEnabledVal, deathChokingHeroVal);
        }

        BossDiag.Phase("ApplyBossStateFromPayload-done");
    }

    private static void AppendDeathAction(List<string> parts, string prefix, DeathAction? action)
    {
        if (action == null)
            return;

        try
        {
            parts.Add(prefix + ((int)action.Index).ToString(CultureInfo.InvariantCulture));
        }
        catch
        {
            // action may be unset
        }
    }

    private static bool? ParseWireBool(string? value)
    {
        var v = value?.Trim();
        if (string.Equals(v, "1", StringComparison.Ordinal) ||
            string.Equals(v, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(v, "0", StringComparison.Ordinal) ||
            string.Equals(v, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private static void ApplyDeathState(
        Death death,
        int? currentAction,
        int? nextAction,
        int? forcedAction,
        bool? sicklesEnabled,
        bool? isChokingHero)
    {
        try
        {
            if (currentAction.HasValue)
            {
                var newAction = TryCreateChangedDeathAction(death.currentAction, currentAction.Value, "death.currentAction");
                if (newAction is not null)
                    death.currentAction = newAction;
            }
            if (nextAction.HasValue)
            {
                var newAction = TryCreateChangedDeathAction(death.nextAction, nextAction.Value, "death.nextAction");
                if (newAction is not null)
                    death.nextAction = newAction;
            }
            if (forcedAction.HasValue)
            {
                var newAction = TryCreateChangedDeathAction(death.forcedAction, forcedAction.Value, "death.forcedAction");
                if (newAction is not null)
                    death.forcedAction = newAction;
            }
            if (sicklesEnabled.HasValue && death.sicklesEnabled != sicklesEnabled.Value)
            {
                BossDiag.Phase($"death.sicklesEnabled set -> {sicklesEnabled.Value}");
                death.sicklesEnabled = sicklesEnabled.Value;
            }
            if (isChokingHero.HasValue && death.isChokingHero != isChokingHero.Value)
            {
                BossDiag.Phase($"death.isChokingHero set -> {isChokingHero.Value}");
                death.isChokingHero = isChokingHero.Value;
            }
        }
        catch
        {
            // ignore
        }
    }

    private static DeathAction? TryCreateChangedDeathAction(DeathAction? currentAction, int index, string label)
    {
        var currentIndex = TryGetDeathActionIndex(currentAction);
        if (currentIndex.HasValue && currentIndex.Value == index)
            return null;

        var newAction = CreateDeathActionByIndex(index);
        if (newAction == null)
            return null;

        BossDiag.Phase($"{label} set -> {index}");
        return newAction;
    }

    private static int? TryGetDeathActionIndex(DeathAction? action)
    {
        if (action == null)
            return null;

        try
        {
            return (int)action.Index;
        }
        catch
        {
            return null;
        }
    }

    private static DeathAction? CreateDeathActionByIndex(int index)
    {
        return index switch
        {
            (int)DeathAction.Indexes.None => new DeathAction.None(),
            (int)DeathAction.Indexes.ScytheCombo => new DeathAction.ScytheCombo(),
            (int)DeathAction.Indexes.BigScytheAttack => new DeathAction.BigScytheAttack(),
            (int)DeathAction.Indexes.ScytheThrow => new DeathAction.ScytheThrow(),
            (int)DeathAction.Indexes.SoulShot => new DeathAction.SoulShot(),
            (int)DeathAction.Indexes.SoulBlast => new DeathAction.SoulBlast(),
            (int)DeathAction.Indexes.SoulUltimate => new DeathAction.SoulUltimate(),
            _ => null
        };
    }

    private static int? TryGetBossActionIndex(BossAction? action)
    {
        if (action == null)
            return null;

        try
        {
            return (int)action.Index;
        }
        catch
        {
            return null;
        }
    }

    private static BossAction? CreateBossActionByIndex(int index)
    {
        return index switch
        {
            (int)BossAction.Indexes.Idle => new BossAction.Idle(),
            (int)BossAction.Indexes.Run => new BossAction.Run(),
            (int)BossAction.Indexes.Walk => new BossAction.Walk(),
            (int)BossAction.Indexes.Fall => new BossAction.Fall(),
            (int)BossAction.Indexes.Attack => new BossAction.Attack(),
            (int)BossAction.Indexes.Hoe => new BossAction.Hoe(),
            (int)BossAction.Indexes.PitchFork => new BossAction.PitchFork(),
            (int)BossAction.Indexes.Sickles => new BossAction.Sickles(),
            (int)BossAction.Indexes.SicklesStun => new BossAction.SicklesStun(),
            (int)BossAction.Indexes.Shovel => new BossAction.Shovel(),
            (int)BossAction.Indexes.ShovelAtk => new BossAction.ShovelAtk(),
            (int)BossAction.Indexes.ShovelUp => new BossAction.ShovelUp(),
            (int)BossAction.Indexes.ShovelAppear => new BossAction.ShovelAppear(),
            (int)BossAction.Indexes.ShovelDisappear => new BossAction.ShovelDisappear(),
            (int)BossAction.Indexes.Vine => new BossAction.Vine(),
            (int)BossAction.Indexes.Spore => new BossAction.Spore(),
            (int)BossAction.Indexes.JumpLoad => new BossAction.JumpLoad(),
            (int)BossAction.Indexes.Jump => new BossAction.Jump(),
            (int)BossAction.Indexes.Land => new BossAction.Land(),
            (int)BossAction.Indexes.Dashing => new BossAction.Dashing(),
            (int)BossAction.Indexes.DigUp => new BossAction.DigUp(),
            (int)BossAction.Indexes.DigDown => new BossAction.DigDown(),
            (int)BossAction.Indexes.Stun => new BossAction.Stun(),
            _ => null
        };
    }
}
