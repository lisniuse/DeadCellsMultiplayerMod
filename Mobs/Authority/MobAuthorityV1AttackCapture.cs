using dc;
using dc.en;
using dc.tool.atk;
using System.Diagnostics;

namespace DeadCellsMultiplayerMod.Mobs.Authority;

internal static class MobAuthorityV1AttackCapture
{
    private const double RecentDamageWindowSeconds = 0.45;
    private const int MaxCapturedDamage = 1000;

    private static bool s_installed;
    private static readonly object s_sync = new();
    private static CapturedDamage s_lastDamage;

    private readonly struct CapturedDamage
    {
        public readonly int Damage;
        public readonly long Tick;
        public readonly string Source;

        public CapturedDamage(int damage, long tick, string source)
        {
            Damage = damage;
            Tick = tick;
            Source = source;
        }
    }

    internal static void Install()
    {
        if (s_installed)
            return;

        s_installed = true;
        Hook__AttackUtils.createFromHero += Hook__AttackUtils_createFromHero;
        Hook__AttackUtils.createFromHeroAndHit += Hook__AttackUtils_createFromHeroAndHit;
        Hook_Hero.onOwnAttackDealt += Hook_Hero_onOwnAttackDealt;
    }

    internal static bool TryGetRecentDamageHint(out int damage)
    {
        damage = 0;

        if (!MobAuthorityV1Runtime.IsAuthorityModeEnabled())
        {
            Clear();
            return false;
        }

        CapturedDamage snapshot;
        lock (s_sync)
            snapshot = s_lastDamage;

        if (snapshot.Damage <= 0)
            return false;

        var ageSeconds = (double)(Stopwatch.GetTimestamp() - snapshot.Tick) / Stopwatch.Frequency;
        if (ageSeconds > RecentDamageWindowSeconds)
            return false;

        damage = snapshot.Damage;
        return true;
    }

    private static AttackData Hook__AttackUtils_createFromHero(
        Hook__AttackUtils.orig_createFromHero orig,
        Entity source,
        object baseDmg,
        int? tier)
    {
        var attack = orig(source, baseDmg, tier);
        CaptureIfLocalHeroAttack(attack, "createFromHero");
        return attack;
    }

    private static AttackData Hook__AttackUtils_createFromHeroAndHit(
        Hook__AttackUtils.orig_createFromHeroAndHit orig,
        Entity source,
        object baseDmg,
        int? tier,
        Entity target)
    {
        var attack = orig(source, baseDmg, tier, target);
        CaptureIfLocalHeroAttack(attack, "createFromHeroAndHit");
        return attack;
    }

    private static void Hook_Hero_onOwnAttackDealt(
        Hook_Hero.orig_onOwnAttackDealt orig,
        Hero self,
        AttackData attack,
        Entity target)
    {
        CaptureIfLocalHero(self, attack, "onOwnAttackDealt");
        orig(self, attack, target);
    }

    private static void CaptureIfLocalHeroAttack(AttackData? attack, string source)
    {
        if (attack == null)
            return;

        if (!IsLocalHeroAttack(attack))
            return;

        Capture(attack, source);
    }

    private static void CaptureIfLocalHero(Hero? hero, AttackData? attack, string source)
    {
        var localHero = ModEntry.me;
        if (hero == null || localHero == null || !ReferenceEquals(hero, localHero))
            return;

        Capture(attack, source);
    }

    private static bool IsLocalHeroAttack(AttackData attack)
    {
        var localHero = ModEntry.me;
        if (localHero == null)
            return false;

        try
        {
            if (ReferenceEquals(attack.source, localHero))
                return true;
        }
        catch
        {
        }

        try
        {
            if (ReferenceEquals(attack.carrier, localHero))
                return true;
        }
        catch
        {
        }

        return false;
    }

    private static void Capture(AttackData? attack, string source)
    {
        if (attack == null || !MobAuthorityV1Runtime.IsAuthorityModeEnabled())
            return;

        if (!TryReadDamage(attack, out var damage))
            return;

        lock (s_sync)
            s_lastDamage = new CapturedDamage(damage, Stopwatch.GetTimestamp(), source);
    }

    private static bool TryReadDamage(AttackData attack, out int damage)
    {
        damage = 0;

        var inflicted = SafeRead(() => attack.inflictedDmg, 0);
        var final = SafeRead(() => attack.finalDmg, 0);
        var rawFinal = SafeRead(() => attack.rawFinalDmg, 0.0);
        var baseDamage = SafeReadBaseDamage(attack);

        damage = System.Math.Max(damage, inflicted);
        damage = System.Math.Max(damage, final);
        if (rawFinal > 0.0)
            damage = System.Math.Max(damage, (int)System.Math.Round(rawFinal));
        if (baseDamage > 0)
            damage = System.Math.Max(damage, baseDamage);

        if (damage <= 0)
            return false;

        damage = System.Math.Clamp(damage, 1, MaxCapturedDamage);
        return true;
    }

    private static int SafeReadBaseDamage(AttackData attack)
    {
        try
        {
            return attack.baseDmg switch
            {
                int value => value,
                double value => (int)System.Math.Round(value),
                float value => (int)System.Math.Round(value),
                long value => value > int.MaxValue ? int.MaxValue : (int)value,
                _ => 0
            };
        }
        catch
        {
            return 0;
        }
    }

    private static T SafeRead<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch { return fallback; }
    }

    private static void Clear()
    {
        lock (s_sync)
            s_lastDamage = default;
    }
}
