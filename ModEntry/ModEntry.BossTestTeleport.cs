using System.Diagnostics;
using dc;
using dc.en;
using dc.en.inter;
using dc.hl.types;
using dc.hxd;
using dc.level;
using dc.libs.heaps.slib;
using dc.pr;
using dc.ui;
using DeadCellsMultiplayerMod.MultiplayerModUI.lifeUI;
using HaxeProxy.Runtime;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod
{
    public partial class ModEntry
    {
        private const double BossTestTriggerBackstepPx = 72.0;
        private const double BossTestBossBackstepPx = 240.0;
        private const double BossDebugNpcSpawnOffsetX = 112.0;
        private const double BossDebugNpcUseDistancePx = 78.0;
        private const int KeyEnter = 13;
        private const int KeyEsc = 27;
        private const int KeySpace = 32;
        private const int KeyUp = 38;
        private const int KeyDown = 40;
        private const int KeyR = 82;
        private const int KeyW = 87;
        private const int KeyS = 83;

        private readonly struct BossDebugDestination
        {
            public readonly string Label;
            public readonly string LevelId;

            public BossDebugDestination(string label, string levelId)
            {
                Label = label;
                LevelId = levelId;
            }
        }

        private static readonly BossDebugDestination[] BossDebugDestinations =
        {
            new("Concierge - Bridge", "Bridge"),
            new("Conjunctivius - BeholderPit", "BeholderPit"),
            new("Mama Tick - SwampHeart", "SwampHeart"),
            new("Time Keeper - TopClockTower", "TopClockTower"),
            new("Giant - Giant", "Giant"),
            new("Hand of the King - Throne", "Throne"),
            new("Collector - Observatory", "Observatory"),
            new("Scarecrow - CastleAlchemy", "CastleAlchemy"),
            new("Servants - LighthouseBottom", "LighthouseBottom"),
            new("Queen - QueenArena", "QueenArena"),
            new("Death - DeathArena", "DeathArena"),
            new("Dooku - DookuArena", "DookuArena"),
            new("Richter - RichterCastle", "RichterCastle"),
            new("Boss Rush - BossRushZone", "BossRushZone")
        };

        private HSprite? _bossDebugNpcSprite;
        private dc.h2d.Text? _bossDebugNpcLabel;
        private Level? _bossDebugNpcLevel;
        private double _bossDebugNpcX;
        private double _bossDebugNpcY;
        private bool _bossDebugMenuOpen;
        private int _bossDebugSelectedIndex;

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

        internal bool TryHostBossTestLevelTeleport(string levelId, string? label = null)
        {
            var net = _net;
            if (_netRole != NetRole.Host || net == null || !net.IsAlive)
            {
                MultiplayerUI.PushSystemMessage("BOSS DEBUG: host only", 2.0, 0.5);
                return false;
            }

            var safeLevelId = (levelId ?? string.Empty).Trim();
            if (safeLevelId.Length == 0)
                return false;

            var currentLevelId = GetCurrentLevelId();
            if (string.Equals(currentLevelId, safeLevelId, StringComparison.OrdinalIgnoreCase))
                return TryHostBossTestTeleport();

            try { net.SendBossTestLevelTeleport(safeLevelId); } catch { }
            TryGotoBossDebugLevel(safeLevelId);
            MultiplayerUI.PushSystemMessage($"BOSS DEBUG: {label ?? safeLevelId}", 2.0, 0.5);
            return true;
        }

        private void ApplyReceivedBossTestLevelTeleport()
        {
            var net = _net;
            if (net == null || !net.IsAlive)
                return;

            if (!net.TryConsumeBossTestLevelTeleportEvents(out var teleports) || teleports.Count == 0)
                return;

            var localId = net.id;
            for (int i = 0; i < teleports.Count; i++)
            {
                var teleport = teleports[i];
                if (teleport.UserId > 0 && teleport.UserId == localId)
                    continue;
                if (string.IsNullOrWhiteSpace(teleport.LevelId))
                    continue;

                _suppressBossTriggerNetSendUntilTick =
                    Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * BossHeroTeleportEchoSuppressSeconds);
                TryGotoBossDebugLevel(teleport.LevelId);
                MultiplayerUI.PushSystemMessage($"BOSS DEBUG: {teleport.LevelId}", 2.0, 0.5);
            }
        }

        private void UpdateBossDebugNpc(double dt)
        {
            var net = _net;
            var hero = me ?? ModCore.Modules.Game.Instance?.HeroInstance;
            if (_netRole != NetRole.Host || net == null || !net.IsAlive || hero == null || hero._level == null)
            {
                ClearBossDebugNpc();
                _bossDebugMenuOpen = false;
                return;
            }

            var level = hero._level;
            if (!ReferenceEquals(_bossDebugNpcLevel, level) || _bossDebugNpcSprite == null)
                RebuildBossDebugNpc(level, hero);

            var near = IsBossDebugNpcNearby(hero);
            if (!near && !_bossDebugMenuOpen)
            {
                UpdateBossDebugNpcLabel("DEBUG\nCome closer");
                return;
            }

            if (_bossDebugMenuOpen)
            {
                HandleBossDebugMenuInput();
                UpdateBossDebugNpcLabel(BuildBossDebugMenuText());
                return;
            }

            UpdateBossDebugNpcLabel("DEBUG\nPress R");
            if (IsBossDebugConfirmPressed())
                _bossDebugMenuOpen = true;
        }

        private void RebuildBossDebugNpc(Level level, Hero hero)
        {
            ClearBossDebugNpc();
            _bossDebugNpcLevel = level;
            _bossDebugNpcX = GetEntityWorldX(hero) + BossDebugNpcSpawnOffsetX;
            _bossDebugNpcY = GetEntityWorldY(hero);

            try
            {
                var lib = Assets.Class.getHeroLib(Cdb.Class.getSkinInfo("PrisonerGold".AsHaxeString()));
                _bossDebugNpcSprite = new HSprite(lib, "idle".AsHaxeString(), Ref<int>.Null, null);
                _bossDebugNpcSprite.x = _bossDebugNpcX;
                _bossDebugNpcSprite.y = _bossDebugNpcY;
                _bossDebugNpcSprite.scaleX = 0.82;
                _bossDebugNpcSprite.scaleY = 0.82;
                _bossDebugNpcSprite.get_anim().play("idle".AsHaxeString(), null, null).loop(null);
                level.scroller?.addChildAt(_bossDebugNpcSprite, Const.Class.DP_ROOM_FRONT_HERO);

                _bossDebugNpcLabel = Assets.Class.makeText("DEBUG".AsHaxeString(), Text.Class.COLORS.get("WO".AsHaxeString()), false, _bossDebugNpcSprite);
                _bossDebugNpcLabel.textColor = 0x6CFF7A;
                _bossDebugNpcLabel.scaleX = 0.55;
                _bossDebugNpcLabel.scaleY = 0.55;
                _bossDebugNpcLabel.x = -42;
                _bossDebugNpcLabel.y = -82;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "[BossDebugNpc] Failed to create debug NPC");
                ClearBossDebugNpc();
            }
        }

        private void ClearBossDebugNpc()
        {
            try { _bossDebugNpcLabel?.remove(); } catch { }
            try { _bossDebugNpcSprite?.remove(); } catch { }
            _bossDebugNpcLabel = null;
            _bossDebugNpcSprite = null;
            _bossDebugNpcLevel = null;
        }

        private bool IsBossDebugNpcNearby(Hero hero)
        {
            var dx = GetEntityWorldX(hero) - _bossDebugNpcX;
            var dy = GetEntityWorldY(hero) - _bossDebugNpcY;
            return dx * dx + dy * dy <= BossDebugNpcUseDistancePx * BossDebugNpcUseDistancePx;
        }

        private void HandleBossDebugMenuInput()
        {
            if (Key.Class.isPressed(KeyEsc))
            {
                _bossDebugMenuOpen = false;
                return;
            }

            if (Key.Class.isPressed(KeyUp) || Key.Class.isPressed(KeyW))
                _bossDebugSelectedIndex = (_bossDebugSelectedIndex + BossDebugDestinations.Length - 1) % BossDebugDestinations.Length;
            if (Key.Class.isPressed(KeyDown) || Key.Class.isPressed(KeyS))
                _bossDebugSelectedIndex = (_bossDebugSelectedIndex + 1) % BossDebugDestinations.Length;

            if (IsBossDebugConfirmPressed())
            {
                var dest = BossDebugDestinations[_bossDebugSelectedIndex];
                _bossDebugMenuOpen = false;
                TryHostBossTestLevelTeleport(dest.LevelId, dest.Label);
            }
        }

        private static bool IsBossDebugConfirmPressed() =>
            Key.Class.isPressed(KeyR) ||
            Key.Class.isPressed(KeyEnter) ||
            Key.Class.isPressed(KeySpace);

        private string BuildBossDebugMenuText()
        {
            var dest = BossDebugDestinations[_bossDebugSelectedIndex];
            return $"DEBUG BOSS\n{_bossDebugSelectedIndex + 1}/{BossDebugDestinations.Length}\n{dest.Label}\nW/S + R";
        }

        private void UpdateBossDebugNpcLabel(string text)
        {
            if (_bossDebugNpcLabel == null)
                return;

            try { _bossDebugNpcLabel.text = text.AsHaxeString(); } catch { }
        }

        private void TryGotoBossDebugLevel(string levelId)
        {
            if (string.IsNullOrWhiteSpace(levelId))
                return;

            try
            {
                _bossDebugMenuOpen = false;
                ClearBossDebugNpc();
                GameDataSync.TryDebugGotoLevel(levelId);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "[BossDebugNpc] Failed to goto boss level {LevelId}", levelId);
                MultiplayerUI.PushSystemMessage($"BOSS DEBUG failed: {levelId}", 2.0, 0.5);
            }
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
