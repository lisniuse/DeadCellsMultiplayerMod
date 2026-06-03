using dc.en;

namespace DeadCellsMultiplayerMod.Mobs.Bosses;

/// <summary>
/// Boss-only authority layer. Host keeps real boss simulation; clients replay explicit boss events.
/// Start narrowly with Death so regular mobs and other bosses keep the existing v0.3.3 path.
/// </summary>
public static class BossAuthoritySync
{
    public const string SkillPrefix = "@boss:";
    public const string DeathContactSkillId = "@boss:death:contact:v1";

    public static bool IsManagedBoss(Mob? mob)
    {
        return IsManagedDeathBoss(mob);
    }

    public static bool IsManagedDeathBoss(Mob? mob)
    {
        if (mob == null)
            return false;

        try
        {
            var typeName = mob.GetType().FullName ?? mob.GetType().Name;
            return typeName.Equals("dc.en.mob.boss.death.Death", StringComparison.OrdinalIgnoreCase) ||
                   typeName.EndsWith(".boss.death.Death", StringComparison.OrdinalIgnoreCase) ||
                   typeName.EndsWith(".death.Death", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsBossAuthoritySkill(string? skillId)
    {
        return !string.IsNullOrWhiteSpace(skillId) &&
               skillId.StartsWith(SkillPrefix, StringComparison.Ordinal);
    }

    public static bool IsDeathContactSkill(string? skillId)
    {
        return string.Equals(skillId, DeathContactSkillId, StringComparison.Ordinal);
    }
}
