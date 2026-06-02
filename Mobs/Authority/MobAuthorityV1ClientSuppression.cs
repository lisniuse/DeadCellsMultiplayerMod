using dc;
using dc.en;
using dc.pr;
using dc.tool.atk;
using dc.tool.skill;
using ModCore.Events;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod.Mobs.Authority;

internal sealed class MobAuthorityV1ClientSuppression : IOnHeroUpdate, IEventReceiver
{
    private static bool s_hooksInstalled;

    public MobAuthorityV1ClientSuppression()
    {
        EventSystem.AddReceiver(this);
        InstallHooks();
    }

    void IOnHeroUpdate.OnHeroUpdate(double dt)
    {
        if (!ShouldSuppressClientMobs())
            return;

        var level = ModEntry.me?._level ?? ModEntry.Instance?.game?.curLevel;
        SuppressLevel(level);
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
        Hook_Mob.onDie += Hook_Mob_onDie;
        Hook_Mob.contactAttack += Hook_Mob_contactAttack;
        Hook_Mob.onTouch += Hook_Mob_onTouch;
        Hook_Mob.queueAttack += Hook_Mob_queueAttack;
    }

    private static void Hook_Mob_preUpdate(Hook_Mob.orig_preUpdate orig, Mob self)
    {
        if (ShouldSuppress(self))
        {
            SuppressMob(self);
            return;
        }

        orig(self);
    }

    private static void Hook_Mob_fixedUpdate(Hook_Mob.orig_fixedUpdate orig, Mob self)
    {
        if (ShouldSuppress(self))
        {
            SuppressMob(self);
            return;
        }

        orig(self);
    }

    private static void Hook_Mob_postUpdate(Hook_Mob.orig_postUpdate orig, Mob self)
    {
        if (ShouldSuppress(self))
        {
            SuppressMob(self);
            return;
        }

        orig(self);
    }

    private static void Hook_Mob_onDamage(Hook_Mob.orig_onDamage orig, Mob self, AttackData attackData)
    {
        if (ShouldSuppress(self))
        {
            SuppressMob(self);
            return;
        }

        orig(self, attackData);
    }

    private static void Hook_Mob_onDie(Hook_Mob.orig_onDie orig, Mob self)
    {
        if (ShouldSuppress(self))
        {
            try
            {
                if (self.life <= 0)
                    self.life = 1;
            }
            catch
            {
            }

            SuppressMob(self);
            return;
        }

        orig(self);
    }

    private static void Hook_Mob_contactAttack(Hook_Mob.orig_contactAttack orig, Mob self, Entity target)
    {
        if (ShouldSuppress(self))
        {
            SuppressMob(self);
            return;
        }

        orig(self, target);
    }

    private static void Hook_Mob_onTouch(Hook_Mob.orig_onTouch orig, Mob self, Entity target)
    {
        if (ShouldSuppress(self))
        {
            SuppressMob(self);
            return;
        }

        orig(self, target);
    }

    private static void Hook_Mob_queueAttack(Hook_Mob.orig_queueAttack orig, Mob self, OldMobSkill skill, bool requiresTargetInArea, int? data)
    {
        if (ShouldSuppress(self))
        {
            SuppressMob(self);
            return;
        }

        orig(self, skill, requiresTargetInArea, data);
    }

    private static bool ShouldSuppress(Mob? mob)
    {
        if (!ShouldSuppressClientMobs())
            return false;

        var level = ModEntry.me?._level ?? ModEntry.Instance?.game?.curLevel;
        return level != null && mob != null && !MobAuthorityV1RealProxyLayer.IsProxyMob(mob) && IsLocalClientMob(mob, level);
    }

    private static bool ShouldSuppressClientMobs()
    {
        var net = GameMenu.NetRef;
        return MobAuthorityV1Runtime.IsAuthorityModeEnabled() &&
               net != null &&
               net.IsAlive &&
               !net.IsHost;
    }

    internal static void SuppressLevel(Level? level)
    {
        if (!ShouldSuppressClientMobs())
            return;
        if (level?.entities == null)
            return;

        var entities = level.entities;
        for (int i = 0; i < entities.length; i++)
        {
            if (entities.getDyn(i) is Mob mob && !MobAuthorityV1RealProxyLayer.IsProxyMob(mob) && IsLocalClientMob(mob, level))
                SuppressMob(mob);
        }
    }

    private static bool IsLocalClientMob(Mob mob, Level level)
    {
        if (mob == null || level == null)
            return false;

        try
        {
            if (mob.destroyed || mob._level == null || !ReferenceEquals(mob._level, level))
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

    private static void SuppressMob(Mob mob)
    {
        if (mob == null)
            return;

        try { mob.visible = false; } catch { }
        try { mob.spr.visible = false; } catch { }
        try { mob._targetable = false; } catch { }
        try { if (mob.life <= 0) mob.life = 1; } catch { }
        try { mob.dx = 0; } catch { }
        try { mob.dy = 0; } catch { }
    }

    private static T SafeRead<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch { return fallback; }
    }
}
