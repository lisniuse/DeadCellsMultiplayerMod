using System.Diagnostics;
using dc;
using dc.en;
using dc.en.inter;
using dc.hl.types;
using dc.pr;
using DeadCellsMultiplayerMod.MultiplayerModUI.lifeUI;

namespace DeadCellsMultiplayerMod
{
    public partial class ModEntry
    {
        private const double BossTestTriggerBackstepPx = 72.0;
        private const double BossTestBossBackstepPx = 240.0;

        internal bool TryHostBossTestTeleport()
        {
            var net = _net;
            if (_netRole != NetRole.Host || net == null || !net.IsAlive)
            {
                MultiplayerUI.PushSystemMessage("BOSS TP: host only", 2.0, 0.5);
                return false;
            }

            var hero = me ?? ModCore.Modules.Game.Instance?.HeroInstance;
            var level = hero?._level ?? game?.curLevel;
            if (hero == null || level == null)
            {
                MultiplayerUI.PushSystemMessage("BOSS TP: no level", 2.0, 0.5);
                return false;
            }

            if (!TryFindBossTestTeleportTarget(level, out var x, out var y, out var dir, out var reason))
            {
                MultiplayerUI.PushSystemMessage("BOSS TP: no target", 2.0, 0.5);
                return false;
            }

            ApplyBossTestTeleport(hero, x, y, dir);
            try { net.SendBossTestTeleport(x, y, dir); } catch { }
            MultiplayerUI.PushSystemMessage($"BOSS TP: {reason}", 2.0, 0.5);
            return true;
        }

        private void ApplyReceivedBossTestTeleport()
        {
            var net = _net;
            if (net == null || !net.IsAlive)
                return;

            if (!net.TryConsumeBossTestTeleportEvents(out var teleports) || teleports.Count == 0)
                return;

            var localHero = me ?? ModCore.Modules.Game.Instance?.HeroInstance;
            if (localHero == null)
                return;

            var localId = net.id;
            for (int i = 0; i < teleports.Count; i++)
            {
                var teleport = teleports[i];
                if (teleport.UserId > 0 && teleport.UserId == localId)
                    continue;

                _suppressBossTriggerNetSendUntilTick =
                    Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * BossHeroTeleportEchoSuppressSeconds);
                ApplyBossTestTeleport(localHero, teleport.X, teleport.Y, teleport.Dir);
                MultiplayerUI.PushSystemMessage("BOSS TP", 2.0, 0.5);
            }
        }

        private static void ApplyBossTestTeleport(Hero hero, double x, double y, int dir)
        {
            try { hero.cancelVelocities(); } catch { }
            try { hero.setPosPixel(x, y); } catch { }
            try
            {
                if (dir != 0)
                    hero.dir = dir;
            }
            catch
            {
            }
        }

        private static bool TryFindBossTestTeleportTarget(Level level, out double x, out double y, out int dir, out string reason)
        {
            x = 0;
            y = 0;
            dir = 1;
            reason = string.Empty;

            if (TryFindBossRoomTrigger(level, out var trigger))
            {
                x = GetEntityWorldX(trigger) - BossTestTriggerBackstepPx;
                y = GetEntityWorldY(trigger);
                reason = "trigger";
                return true;
            }

            try
            {
                if (level.boss is Entity boss)
                {
                    x = GetEntityWorldX(boss) - BossTestBossBackstepPx;
                    y = GetEntityWorldY(boss);
                    reason = "boss";
                    return true;
                }
            }
            catch
            {
            }

            if (level.entities != null)
            {
                for (int i = 0; i < level.entities.length; i++)
                {
                    if (level.entities.getDyn(i) is not Entity entity)
                        continue;

                    var typeName = entity.GetType().FullName ?? entity.GetType().Name;
                    if (typeName.IndexOf("boss", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    x = GetEntityWorldX(entity) - BossTestBossBackstepPx;
                    y = GetEntityWorldY(entity);
                    reason = "entity";
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindBossRoomTrigger(Level level, out HiddenTrigger trigger)
        {
            trigger = null!;
            try
            {
                var entries = level.entitiesByClass?.get(HiddenTrigger.Class.__clid) as ArrayObj;
                if (entries == null)
                    return false;

                for (int i = 0; i < entries.length; i++)
                {
                    if (entries.getDyn(i) is not HiddenTrigger ht)
                        continue;

                    var eventId = ht.genericEventId?.ToString();
                    if (string.IsNullOrWhiteSpace(eventId))
                        continue;
                    if (!BossRoomGenericEventIds.Contains(eventId))
                        continue;

                    trigger = ht;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static double GetEntityWorldX(Entity entity)
        {
            try
            {
                if (entity.spr != null)
                    return entity.spr.x;
            }
            catch
            {
            }

            return (entity.cx + entity.xr) * 24.0;
        }

        private static double GetEntityWorldY(Entity entity)
        {
            try
            {
                if (entity.spr != null)
                    return entity.spr.y;
            }
            catch
            {
            }

            return (entity.cy + entity.yr) * 24.0;
        }
    }
}
