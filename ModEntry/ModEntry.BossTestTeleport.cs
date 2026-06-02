using System.Diagnostics;
using dc;
using dc.en;
using dc.en.inter;
using dc.hl.types;
using dc.hxd;
using dc.level;
using dc.libs.heaps.slib;
using dc.pr;
using dc.tool;
using dc.ui;
using DeadCellsMultiplayerMod.MultiplayerModUI.lifeUI;
using Hashlink.Virtuals;
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
        private const string BossDebugNpcAtlas = "atlas/merchantSmall.atlas";
        private const string BossDebugNpcAnim = "MerchantSmallIdle";
        private const int KeyEsc = 27;
        private const int KeyUp = 38;
        private const int KeyDown = 40;
        private const int KeyR = 82;
        private const int KeyW = 87;
        private const int KeyS = 83;
        private const int BossDebugMenuNormalColor = 0xFFFFFF;
        private const int BossDebugMenuSelectedColor = 0xF7FC65;
        private const double BossDebugConfirmDebounceSeconds = 0.25;
        private const int BossDebugVisibleRows = 12;

        private enum BossDebugMenuPage
        {
            Root,
            BossTeleport,
            WeaponSpawn
        }

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

        private readonly struct BossDebugWeaponChoice
        {
            public readonly string Label;
            public readonly string[] CandidateIds;

            public BossDebugWeaponChoice(string label, string[] candidateIds)
            {
                Label = label;
                CandidateIds = candidateIds;
            }
        }

        private static readonly BossDebugDestination[] BossDebugDestinations =
        {
            // Stable boss test destinations, derived from CDB bossRush.bosses[level],
            // achievement boss ids, and the level display constructors in GameProxy.
            new("Concierge - Bridge", "Bridge"),
            new("Conjunctivius - BeholderPit", "BeholderPit"),
            new("Mama Tick - SwampHeart", "SwampHeart"),
            new("Death - DeathArena", "DeathArena"),
            new("Time Keeper - TopClockTower", "TopClockTower"),
            new("Giant - Giant", "Giant"),
            new("Scarecrow - GardenerStage", "GardenerStage"),
            new("Dracula - DookuArena", "DookuArena"),
            new("Hand of the King - Throne", "Throne"),
            new("Servants - Lighthouse", "Lighthouse"),
            new("Queen - QueenArena", "QueenArena"),
            new("Collector - Observatory", "Observatory"),
            new("Richter Mode - RichterCastle", "RichterCastle"),
            new("Boss Rush - BossRushZone", "BossRushZone")
        };

        private static readonly string[] BossDebugRootOptions =
        {
            "BOSS传送",
            "获取武器"
        };

        private static readonly BossDebugWeaponChoice[] BossDebugWeaponChoices =
        {
            new("随机生成一把剑", new[]
            {
                "BloodSword", "Rapier", "TwinDaggers", "BalancedBlade", "SpiteSword",
                "FranticSword", "Broadsword", "AssassinDagger", "Shovel", "SadismSword"
            }),
            new("随机生成一个盾", new[]
            {
                "Shield", "BloodShield", "GreedShield", "ParryShield", "Punishment",
                "AssaultShield", "ForceShield", "Cudgel", "FrontLineShield", "Rampart"
            }),
            new("随机生成一个弓", new[]
            {
                "BeginnerBow", "MultipleNocksBow", "QuickBow", "InfantryBow", "MarksmanBow",
                "IceBow", "NervesOfSteel", "Bow", "LongBow", "RepeaterCrossbow"
            })
        };

        private HSprite? _bossDebugNpcSprite;
        private dc.h2d.Text? _bossDebugNpcLabel;
        private DebugPopUp? _bossDebugPopup;
        private readonly List<dc.ui.we.Text> _bossDebugPopupLines = new();
        private Level? _bossDebugNpcLevel;
        private double _bossDebugNpcX;
        private double _bossDebugNpcY;
        private bool _bossDebugMenuOpen;
        private BossDebugMenuPage _bossDebugMenuPage = BossDebugMenuPage.Root;
        private int _bossDebugSelectedIndex;
        private int _bossDebugRootSelectedIndex;
        private int _bossDebugBossSelectedIndex;
        private int _bossDebugWeaponSelectedIndex;
        private BossDebugMenuPage _bossDebugPopupPage = BossDebugMenuPage.Root;
        private int _bossDebugPopupSelectedIndex = -1;
        private int _bossDebugPopupFirstIndex = -1;
        private int _bossDebugPopupLineCount = -1;
        private long _bossDebugIgnoreConfirmUntilTick;

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
                CloseBossDebugPopup();
                _bossDebugMenuOpen = false;
                return;
            }

            var level = hero._level;
            if (!ReferenceEquals(_bossDebugNpcLevel, level) || _bossDebugNpcSprite == null)
                RebuildBossDebugNpc(level, hero);

            var near = IsBossDebugNpcNearby(hero);
            if (!near && !_bossDebugMenuOpen)
            {
                CloseBossDebugPopup();
                UpdateBossDebugNpcLabel("DEBUG\nCome closer");
                return;
            }

            if (_bossDebugMenuOpen)
            {
                UpdateBossDebugNpcLabel("DEBUG\nSelecting...");
                EnsureBossDebugPopup();
                HandleBossDebugMenuInput();
                UpdateBossDebugPopupSelection();
                return;
            }

            CloseBossDebugPopup();
            UpdateBossDebugNpcLabel("DEBUG\nPress R");
            if (IsBossDebugConfirmPressed())
            {
                _bossDebugMenuOpen = true;
                _bossDebugMenuPage = BossDebugMenuPage.Root;
                _bossDebugSelectedIndex = _bossDebugRootSelectedIndex;
                _bossDebugIgnoreConfirmUntilTick =
                    Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * BossDebugConfirmDebounceSeconds);
                EnsureBossDebugPopup();
            }
        }

        private void RebuildBossDebugNpc(Level level, Hero hero)
        {
            ClearBossDebugNpc();
            _bossDebugNpcLevel = level;
            _bossDebugNpcX = GetEntityWorldX(hero) + BossDebugNpcSpawnOffsetX;
            _bossDebugNpcY = GetEntityWorldY(hero);

            try
            {
                var lib = Assets.Class.lib.get(BossDebugNpcAtlas.AsHaxeString());
                _bossDebugNpcSprite = new HSprite(lib, BossDebugNpcAnim.AsHaxeString(), Ref<int>.Null, null);
                _bossDebugNpcSprite.x = _bossDebugNpcX;
                _bossDebugNpcSprite.y = _bossDebugNpcY;
                _bossDebugNpcSprite.scaleX = 1.0;
                _bossDebugNpcSprite.scaleY = 1.0;
                _bossDebugNpcSprite.alpha = 1;
                var pivot = _bossDebugNpcSprite.pivot;
                pivot.centerFactorX = 0.5;
                pivot.centerFactorY = 1.0;
                pivot.usingFactor = true;
                pivot.isUndefined = false;
                level.scroller?.addChildAt(_bossDebugNpcSprite, Const.Class.DP_ROOM_FRONT_HERO);

                _bossDebugNpcLabel = Assets.Class.makeText("DEBUG".AsHaxeString(), Text.Class.COLORS.get("WO".AsHaxeString()), false, level.scroller);
                _bossDebugNpcLabel.textColor = 0x6CFF7A;
                _bossDebugNpcLabel.scaleX = 0.55;
                _bossDebugNpcLabel.scaleY = 0.55;
                PositionBossDebugNpcLabel();
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
            CloseBossDebugPopup();
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

        private static bool IsBossDebugConfirmPressed() => Key.Class.isPressed(KeyR);

        private void HandleBossDebugMenuInput()
        {
            if (Key.Class.isPressed(KeyEsc))
            {
                if (_bossDebugMenuPage == BossDebugMenuPage.Root)
                {
                    _bossDebugMenuOpen = false;
                    CloseBossDebugPopup();
                }
                else
                {
                    _bossDebugMenuPage = BossDebugMenuPage.Root;
                    _bossDebugSelectedIndex = _bossDebugRootSelectedIndex;
                    CloseBossDebugPopup();
                }
                return;
            }

            var itemCount = GetBossDebugCurrentItemCount();
            if (itemCount <= 0)
                return;

            if (Key.Class.isPressed(KeyUp) || Key.Class.isPressed(KeyW))
                _bossDebugSelectedIndex = (_bossDebugSelectedIndex + itemCount - 1) % itemCount;

            if (Key.Class.isPressed(KeyDown) || Key.Class.isPressed(KeyS))
                _bossDebugSelectedIndex = (_bossDebugSelectedIndex + 1) % itemCount;

            if (Stopwatch.GetTimestamp() >= _bossDebugIgnoreConfirmUntilTick && IsBossDebugConfirmPressed())
            {
                HandleBossDebugMenuConfirm();
            }
        }

        private int GetBossDebugCurrentItemCount()
        {
            return _bossDebugMenuPage switch
            {
                BossDebugMenuPage.Root => BossDebugRootOptions.Length,
                BossDebugMenuPage.BossTeleport => BossDebugDestinations.Length,
                BossDebugMenuPage.WeaponSpawn => BossDebugWeaponChoices.Length,
                _ => 0
            };
        }

        private void HandleBossDebugMenuConfirm()
        {
            switch (_bossDebugMenuPage)
            {
                case BossDebugMenuPage.Root:
                    _bossDebugRootSelectedIndex = _bossDebugSelectedIndex;
                    if (_bossDebugSelectedIndex == 0)
                    {
                        _bossDebugMenuPage = BossDebugMenuPage.BossTeleport;
                        _bossDebugSelectedIndex = _bossDebugBossSelectedIndex;
                    }
                    else
                    {
                        _bossDebugMenuPage = BossDebugMenuPage.WeaponSpawn;
                        _bossDebugSelectedIndex = _bossDebugWeaponSelectedIndex;
                    }
                    CloseBossDebugPopup();
                    _bossDebugIgnoreConfirmUntilTick =
                        Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * BossDebugConfirmDebounceSeconds);
                    break;

                case BossDebugMenuPage.BossTeleport:
                    _bossDebugBossSelectedIndex = _bossDebugSelectedIndex;
                    var dest = BossDebugDestinations[_bossDebugSelectedIndex];
                    _bossDebugMenuOpen = false;
                    CloseBossDebugPopup();
                    TryHostBossTestLevelTeleport(dest.LevelId, dest.Label);
                    break;

                case BossDebugMenuPage.WeaponSpawn:
                    _bossDebugWeaponSelectedIndex = _bossDebugSelectedIndex;
                    if (_bossDebugSelectedIndex >= 0 && _bossDebugSelectedIndex < BossDebugWeaponChoices.Length)
                        TrySpawnRandomBossDebugWeapon(BossDebugWeaponChoices[_bossDebugSelectedIndex]);
                    _bossDebugIgnoreConfirmUntilTick =
                        Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * BossDebugConfirmDebounceSeconds);
                    break;
            }
        }

        private void UpdateBossDebugNpcLabel(string text)
        {
            if (_bossDebugNpcLabel == null)
                return;

            try
            {
                PositionBossDebugNpcLabel();
                _bossDebugNpcLabel.text = text.AsHaxeString();
            }
            catch { }
        }

        private void PositionBossDebugNpcLabel()
        {
            if (_bossDebugNpcLabel == null)
                return;

            _bossDebugNpcLabel.x = _bossDebugNpcX - 34;
            _bossDebugNpcLabel.y = _bossDebugNpcY - 118;
        }

        private void EnsureBossDebugPopup()
        {
            if (_bossDebugPopup != null)
                return;

            var hud = HUD.Class.ME;
            if (hud == null)
                return;

            try
            {
                var popup = new DebugPopUp(hud);
                _bossDebugPopup = popup;
                popup.cancelable = true;
                popup.uMaxWid = 430;
                popup.title(GetBossDebugPopupTitle().AsHaxeString());
                popup.text(GetBossDebugHelpText().AsHaxeString(), BossDebugMenuNormalColor, false);
                popup.spacerLine(null);
                popup.onClose = new HlAction(ClearBossDebugPopupRegistrations);
                popup.onCancel = new HlAction(() =>
                {
                    if (_bossDebugMenuPage == BossDebugMenuPage.Root)
                        _bossDebugMenuOpen = false;
                    else
                    {
                        _bossDebugMenuPage = BossDebugMenuPage.Root;
                        _bossDebugSelectedIndex = _bossDebugRootSelectedIndex;
                    }
                    CloseBossDebugPopup();
                });

                _bossDebugPopupPage = _bossDebugMenuPage;
                _bossDebugPopupSelectedIndex = _bossDebugSelectedIndex;
                _bossDebugPopupFirstIndex = GetBossDebugFirstVisibleIndex();
                _bossDebugPopupLineCount = GetBossDebugVisibleLineCount(_bossDebugPopupFirstIndex);
                for (int i = 0; i < _bossDebugPopupLineCount; i++)
                {
                    var itemIndex = _bossDebugPopupFirstIndex + i;
                    var selected = itemIndex == _bossDebugSelectedIndex;
                    var label = (selected ? "> " : "  ") + GetBossDebugLineLabel(itemIndex);
                    var line = popup.text(label.AsHaxeString(), selected ? BossDebugMenuSelectedColor : BossDebugMenuNormalColor, false);
                    if (line != null)
                        _bossDebugPopupLines.Add(line);
                }

                UpdateBossDebugPopupSelection();
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "[BossDebugNpc] Failed to open native boss debug popup");
                _bossDebugMenuOpen = false;
                CloseBossDebugPopup();
                MultiplayerUI.PushSystemMessage("BOSS DEBUG menu failed", 2.0, 0.5);
            }
        }

        private void CloseBossDebugPopup()
        {
            var popup = _bossDebugPopup;
            if (popup != null)
            {
                try { popup.close(); } catch { }
            }

            ClearBossDebugPopupRegistrations();
        }

        private void ClearBossDebugPopupRegistrations()
        {
            _bossDebugPopupLines.Clear();
            _bossDebugPopup = null;
            _bossDebugPopupSelectedIndex = -1;
            _bossDebugPopupFirstIndex = -1;
            _bossDebugPopupLineCount = -1;
        }

        private void UpdateBossDebugPopupSelection()
        {
            var firstIndex = GetBossDebugFirstVisibleIndex();
            var lineCount = GetBossDebugVisibleLineCount(firstIndex);
            if (_bossDebugPopup != null &&
                (_bossDebugPopupPage != _bossDebugMenuPage ||
                 _bossDebugPopupSelectedIndex != _bossDebugSelectedIndex ||
                 _bossDebugPopupFirstIndex != firstIndex ||
                 _bossDebugPopupLineCount != lineCount))
            {
                CloseBossDebugPopup();
                EnsureBossDebugPopup();
                return;
            }

            _bossDebugPopupSelectedIndex = _bossDebugSelectedIndex;
            _bossDebugPopupFirstIndex = firstIndex;
            for (int i = 0; i < _bossDebugPopupLines.Count; i++)
            {
                var line = _bossDebugPopupLines[i];
                var itemIndex = _bossDebugPopupFirstIndex + i;
                var selected = itemIndex == _bossDebugSelectedIndex;
                var label = (selected ? "> " : "  ") + GetBossDebugLineLabel(itemIndex);
                try
                {
                    line.tf.text = label.AsHaxeString();
                    line.tf.textColor = selected ? BossDebugMenuSelectedColor : BossDebugMenuNormalColor;
                }
                catch
                {
                }
            }
        }

        private string GetBossDebugPopupTitle()
        {
            return _bossDebugMenuPage switch
            {
                BossDebugMenuPage.Root => "DEBUG TOOLS",
                BossDebugMenuPage.BossTeleport => $"BOSS传送 ({BossDebugDestinations.Length})",
                BossDebugMenuPage.WeaponSpawn => $"获取武器 ({BossDebugWeaponChoices.Length})",
                _ => "DEBUG"
            };
        }

        private string GetBossDebugHelpText()
        {
            return _bossDebugMenuPage == BossDebugMenuPage.Root
                ? "W/S select, R enter, Esc close"
                : "W/S select, R confirm, Esc back";
        }

        private int GetBossDebugFirstVisibleIndex()
        {
            var count = GetBossDebugCurrentItemCount();
            if (count <= BossDebugVisibleRows)
                return 0;

            var half = BossDebugVisibleRows / 2;
            var first = _bossDebugSelectedIndex - half;
            if (first < 0)
                first = 0;
            var maxFirst = count - BossDebugVisibleRows;
            if (first > maxFirst)
                first = maxFirst;
            return first;
        }

        private int GetBossDebugVisibleLineCount(int firstIndex)
        {
            var count = GetBossDebugCurrentItemCount();
            if (count <= 0 || firstIndex < 0)
                return 0;
            return System.Math.Min(BossDebugVisibleRows, count - firstIndex);
        }

        private string GetBossDebugLineLabel(int itemIndex)
        {
            return _bossDebugMenuPage switch
            {
                BossDebugMenuPage.Root => BossDebugRootOptions[itemIndex],
                BossDebugMenuPage.BossTeleport => BossDebugDestinations[itemIndex].Label,
                BossDebugMenuPage.WeaponSpawn => FormatBossDebugWeaponLine(itemIndex),
                _ => string.Empty
            };
        }

        private string FormatBossDebugWeaponLine(int itemIndex)
        {
            if (itemIndex < 0 || itemIndex >= BossDebugWeaponChoices.Length)
                return string.Empty;

            var choice = BossDebugWeaponChoices[itemIndex];
            return $"{itemIndex + 1}/{BossDebugWeaponChoices.Length} {choice.Label}";
        }

        private void TrySpawnRandomBossDebugWeapon(BossDebugWeaponChoice choice)
        {
            var hero = me ?? ModCore.Modules.Game.Instance?.HeroInstance;
            var level = hero?._level ?? game?.curLevel;
            if (hero == null || level == null || choice.CandidateIds.Length == 0)
            {
                MultiplayerUI.PushSystemMessage("DEBUG weapon: no level", 2.0, 0.5);
                return;
            }

            var candidates = BuildShuffledBossDebugWeaponCandidates(choice.CandidateIds);
            for (int i = 0; i < candidates.Count; i++)
            {
                var itemId = candidates[i];
                try
                {
                    SpawnBossDebugWeaponDrop(hero, level, itemId);
                    MultiplayerUI.PushSystemMessage($"Weapon: {itemId}", 2.0, 0.5);
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "[BossDebugNpc] Failed candidate weapon {WeaponId}", itemId);
                }
            }

            MultiplayerUI.PushSystemMessage($"Weapon failed: {choice.Label}", 2.0, 0.5);
        }

        private static List<string> BuildShuffledBossDebugWeaponCandidates(string[] candidateIds)
        {
            var result = new List<string>();
            for (int i = 0; i < candidateIds.Length; i++)
            {
                var id = candidateIds[i]?.Trim();
                if (!string.IsNullOrWhiteSpace(id))
                    result.Add(id);
            }

            for (int i = result.Count - 1; i > 0; i--)
            {
                var j = Random.Shared.Next(i + 1);
                (result[i], result[j]) = (result[j], result[i]);
            }

            return result;
        }

        private static void SpawnBossDebugWeaponDrop(Hero hero, Level level, string itemId)
        {
            var item = new InventItem(new InventItemKind.Weapon(itemId.AsHaxeString()));
            try { item.refillAmmo(); } catch { }

            var inChest = false;
            var drop = new ItemDrop(level, hero.cx, hero.cy, item, true, new Ref<bool>(ref inChest));
            drop.init();
            drop.onDropAsLoot();
            drop.dx = hero.dx;
        }

        private void TryGotoBossDebugLevel(string levelId)
        {
            if (string.IsNullOrWhiteSpace(levelId))
                return;

            try
            {
                _bossDebugMenuOpen = false;
                ClearBossDebugNpc();
                CloseBossDebugPopup();
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
