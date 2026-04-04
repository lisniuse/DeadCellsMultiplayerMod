using System.Diagnostics;
using dc.en;
using ModCore.Utilities;
using dc.hl.types;
using dc.tool;
using HaxeProxy.Runtime;

namespace DeadCellsMultiplayerMod
{
    public partial class ModEntry
    {
        private static bool IsDebugImmortalLocalHero(Hero? hero)
        {
            return hero != null &&
                   me != null &&
                   ReferenceEquals(hero, me) &&
                   MultiplayerSettingsStorage.IsDebugSectionEnabled &&
                   MultiplayerSettingsStorage.DebugPlayerImmortal;
        }

        private static void ApplyDebugImmortalState(Hero hero)
        {
            if (hero == null)
                return;

            try { hero.noDamageDuringBossBattle = true; } catch { }
            try
            {
                if (hero.maxLife > 0 && hero.life < hero.maxLife)
                    hero.life = hero.maxLife;
            }
            catch
            {
                try { hero.fullHeal(); } catch { }
            }
            try { hero._targetable = true; } catch { }
        }

        private void ApplyDebugHeroRuntimeOptions()
        {
            var hero = me;
            if (hero == null || !MultiplayerSettingsStorage.IsDebugSectionEnabled)
                return;

            if (IsDebugImmortalLocalHero(hero))
            {
                ApplyDebugImmortalState(hero);
            }
            else
            {
                try { hero.noDamageDuringBossBattle = false; } catch { }
            }

            TryApplyDebugStartPerk(hero);
            TryApplyDebugExplorerRune(hero);
        }

        private void TryApplyDebugStartPerk(Hero hero)
        {
            if (hero == null)
                return;

            var configuredPerkId = MultiplayerSettingsStorage.DebugStartPerkId;
            if (string.IsNullOrWhiteSpace(configuredPerkId) ||
                string.Equals(configuredPerkId, MultiplayerSettingsStorage.NoStartPerkValue, StringComparison.OrdinalIgnoreCase))
            {
                _debugPerkAppliedHero = null;
                _debugPerkAppliedId = string.Empty;
                _lastDebugPerkApplyErrorId = string.Empty;
                _nextDebugPerkApplyTick = 0;
                return;
            }

            var perkId = configuredPerkId.Trim();
            if (ReferenceEquals(_debugPerkAppliedHero, hero) &&
                string.Equals(_debugPerkAppliedId, perkId, StringComparison.Ordinal))
            {
                return;
            }

            var now = Stopwatch.GetTimestamp();
            if (_nextDebugPerkApplyTick != 0 && now < _nextDebugPerkApplyTick)
                return;

            try
            {
                var item = new InventItem(new InventItemKind.Perk(perkId.AsHaxeString()));
                hero.applyItemPickEffect(hero, item);

                if (string.Equals(perkId, "P_Yolo", StringComparison.OrdinalIgnoreCase))
                {
                    try { hero.tryToApplyYoloPerk(); } catch { }
                }

                _debugPerkAppliedHero = hero;
                _debugPerkAppliedId = perkId;
                _lastDebugPerkApplyErrorId = string.Empty;
                _nextDebugPerkApplyTick = 0;
            }
            catch (Exception ex)
            {
                _nextDebugPerkApplyTick = now + (long)(Stopwatch.Frequency * 1.5);
                if (string.Equals(_lastDebugPerkApplyErrorId, perkId, StringComparison.Ordinal))
                    return;

                _lastDebugPerkApplyErrorId = perkId;
                Logger.Warning(ex, "[NetMod] Failed to apply debug start perk {PerkId}", perkId);
            }
        }

        private void TryApplyDebugExplorerRune(Hero hero)
        {
            if (hero == null)
                return;

            ItemMetaManager? itemMeta = null;
            try
            {
                var user = hero._level?.game?.user ?? game?.user ?? dc.pr.Game.Class.ME?.user;
                if (user == null)
                    return;

                itemMeta = user.itemMeta ?? new ItemMetaManager(user);
                itemMeta.itemProgress ??= (ArrayObj)ArrayUtils.CreateDyn().array;
                itemMeta.permanentItems ??= (ArrayObj)ArrayUtils.CreateDyn().array;
                user.itemMeta = itemMeta;
            }
            catch
            {
                return;
            }

            if (itemMeta == null)
                return;

            if (MultiplayerSettingsStorage.DebugUseExplorersRune)
            {
                try
                {
                    var runeKey = ExplorerRunePermanentItemId.AsHaxeString();
                    if (!itemMeta.hasPermanentItem(runeKey))
                    {
                        if (itemMeta.addPermanentItem(runeKey))
                        {
                            _debugExplorerRuneInjectedByDebug = true;
                            _debugExplorerRuneInjectedMeta = itemMeta;
                        }
                    }
                }
                catch
                {
                }

                TryRevealAllMinimapForDebugExplorerRune(hero);

                return;
            }

            if (!_debugExplorerRuneInjectedByDebug)
                return;

            try
            {
                var runeKey = ExplorerRunePermanentItemId.AsHaxeString();
                var targetMeta = _debugExplorerRuneInjectedMeta ?? itemMeta;
                var permanentItems = targetMeta?.permanentItems;
                if (permanentItems != null)
                {
                    while (permanentItems.remove(runeKey))
                    {
                    }
                }
            }
            catch
            {
            }
            finally
            {
                _debugExplorerRuneInjectedByDebug = false;
                _debugExplorerRuneInjectedMeta = null;
                _debugExplorerRevealAppliedSignature = string.Empty;
                _nextDebugExplorerRevealRetryTick = 0;
            }
        }

        private void TryRevealAllMinimapForDebugExplorerRune(Hero hero)
        {
            if (_debugExplorerRevealAllCount >= MaxDebugExplorerRevealAllCalls)
                return;

            var now = Stopwatch.GetTimestamp();

            var sig = GetDebugExplorerRevealSignature(hero);
            if (!string.IsNullOrWhiteSpace(sig) &&
                string.Equals(_debugExplorerRevealAppliedSignature, sig, StringComparison.Ordinal))
                return;

            if (_nextDebugExplorerRevealRetryTick != 0 && now < _nextDebugExplorerRevealRetryTick)
                return;

            try
            {
                var feedback = false;
                try
                {
                    // Match the native game flow: reveal rooms + refresh minimap trackers.
                    hero.triggerExplorerInstinct(Ref<bool>.From(ref feedback));
                }
                catch
                {
                }

                var minimap = hero._level?.game?.hud?.minimap ?? dc.ui.HUD.Class.ME?.minimap;
                if (minimap == null)
                {
                    _nextDebugExplorerRevealRetryTick = now + (long)(Stopwatch.Frequency * 0.05);
                    return;
                }

                minimap.revealAll();
                _debugExplorerRevealAllCount++;
                try { minimap.forceRenderRooms(); } catch { }
                try { minimap.invalidateMinimap(); } catch { }

                if (string.IsNullOrWhiteSpace(sig))
                    sig = GetDebugExplorerRevealSignature(hero);

                if (!string.IsNullOrWhiteSpace(sig))
                    _debugExplorerRevealAppliedSignature = sig;

                _nextDebugExplorerRevealRetryTick = 0;
            }
            catch
            {
                _nextDebugExplorerRevealRetryTick = now + (long)(Stopwatch.Frequency * 0.25);
            }
        }

        /// <summary>Level id + branch token so we re-reveal after room/sub-level changes with the same map id.</summary>
        private string GetDebugExplorerRevealSignature(Hero hero)
        {
            if (TryGetCurrentVisibilityContext(out var levelId, out var branch) && branch >= 0 &&
                !string.IsNullOrWhiteSpace(levelId))
                return $"{levelId.Trim()}|{branch}";

            var fallback = GetDebugExplorerRevealLevelKey(hero);
            if (!string.IsNullOrWhiteSpace(fallback))
                return $"{fallback.Trim()}|0";

            return string.Empty;
        }

        private string GetDebugExplorerRevealLevelKey(Hero hero)
        {
            try
            {
                var levelFromHero = hero?._level?.map?.id?.ToString();
                if (!string.IsNullOrWhiteSpace(levelFromHero))
                    return levelFromHero.Trim();
            }
            catch
            {
            }

            var currentLevelId = GetCurrentLevelId();
            if (!string.IsNullOrWhiteSpace(currentLevelId))
                return currentLevelId.Trim();

            return string.Empty;
        }
    }
}
