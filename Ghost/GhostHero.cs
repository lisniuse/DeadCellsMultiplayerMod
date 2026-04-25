using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using dc.en;
using dc.pr;
using ModCore.Utilities;
using Serilog;
using dc;
using HaxeProxy.Runtime;
using DeadCellsMultiplayerMod.Ghost.GhostBase;
namespace DeadCellsMultiplayerMod
{
    public class GhostHero
    {
        private sealed class LabelState
        {
            public dc.h2d.Text Label { get; }
            public string TextValue { get; set; }
            public int TextLength { get; set; }

            public LabelState(dc.h2d.Text label, string textValue)
            {
                Label = label;
                TextValue = textValue;
                TextLength = textValue.Length;
            }
        }

        private const double NickScaleWindowed = 0.8;
        private const double NickScaleFullscreen = 0.5;
        private const int WindowedDisplayMode = 0;
        private const int FullscreenDisplayMode = 1;
        private const int BorderlessDisplayMode = 2;

        private readonly Hero _me;
        private static ILogger? _log;
        private readonly Dictionary<Entity, LabelState> _labels = new();
        private readonly List<Entity> _staleLabels = new();
        private static int _cachedDisplayMode = int.MinValue;
        private static int _cachedFullScreenMode = int.MinValue;
        private static double _cachedNicknameScale = NickScaleWindowed;
        private static readonly object KingTeardownMarker = new();
        private static readonly ConditionalWeakTable<GhostKing, object> DisposingKings = new();
        private static readonly ConditionalWeakTable<GhostKing, object> DisposedKings = new();

        private const double RestartFrameIndex = 0;

        public int PlayerId { get; }

        public GhostKing king = null!;
        public KingHead.Kinghead kinghead = null!;


        public GhostHero(
        int playerId,
        dc.pr.Game game,
        Hero me,
        ILogger logger,
        ModEntry entry)
        {
            PlayerId = playerId;
            _ = game;
            _ = entry;
            _me = me;
            _log = logger;
        }


        public GhostKing CreateGhostKing(Level level, string? label = null)
        {

            king = new GhostKing(level, (int)-1000, (int)-1000);
            king.init();
            king.set_level(level);
            king.set_team(level.teamHero);
            king._targetable = true;
            king.hasWineGlass = false;
            king.lifeBarAbove = true;
            king.initLife(100, 100);
            king.hasRepelling = true;
            king.collisionMode = new CollisionMode.Normal();
            king.hasEntityTouchChecks = true;
            king.onActivate(_me, true);
            king.canBeActivated(_me);
            king.needsLongPress = true;
            king.hasEntityTouchChecks = true;


            bool sics = false;
            king.enableAllPhysics(Ref<bool>.From(ref sics));
            king.visible = true;
            var miniMap = ModEntry.miniMap;
            if (miniMap != null && _me._level == king._level)
            {
                miniMap.track(king, 14888237, "minimapHero".AsHaxeString(), null, true, null, null, null);
            }
            if (king.spr == null || king.spr._animManager == null)
                king.ApplyRemoteSkin(king.RemoteSkinId);

            if (!string.IsNullOrWhiteSpace(label) && king.spr != null)
                SetLabel(king, label);

            var animManager = king.spr?._animManager;
            if (animManager != null)
                animManager.play("idle".AsHaxeString(), null, null).loop(null);

            return king;
        }

        public void disposeKing(GhostKing k)
        {
            if (k == null)
                return;

            if (ReferenceEquals(king, k))
                king = null!;
            RemoveLabel(k);
            DisposeKingRuntime(k);
        }

        public static void DisposeKingRuntime(GhostKing k)
        {
            if (k == null)
                return;
            if (DisposingKings.TryGetValue(k, out _))
                return;

            DisposingKings.Add(k, KingTeardownMarker);
            List<Exception>? failures = null;
            try
            {
                var level = TryGetKingLevel(k);
                if (level == null && IsKingDisposed(k))
                    return;

                RunKingTeardownStep(ref failures, "destroy", () =>
                {
                    if (!k.destroyed)
                        k.destroy();
                });

                if (level != null)
                {
                    RunKingTeardownStep(ref failures, "level entity GC", level.runEntitiesGC);
                    RunKingTeardownStep(ref failures, "level collection purge", () => RemoveKingFromLevelCollections(level, k));
                }
                else if (!IsKingDisposed(k))
                {
                    RunKingTeardownStep(ref failures, "entity dispose", k.dispose);
                }

                if (failures == null && !DisposedKings.TryGetValue(k, out _))
                    DisposedKings.Add(k, KingTeardownMarker);
            }
            finally
            {
                DisposingKings.Remove(k);
            }

            ThrowKingTeardownFailures(failures);
        }

        private static bool IsKingDisposed(GhostKing k)
        {
            return DisposedKings.TryGetValue(k, out _) ||
                   (k.destroyed && k.cd == null && k.spr == null);
        }

        private static Level? TryGetKingLevel(GhostKing k)
        {
            try
            {
                return k._level;
            }
            catch
            {
                return null;
            }
        }

        private static void RemoveKingFromLevelCollections(Level level, GhostKing k)
        {
            level.entities?.remove(k);
            level.qTreeEntities?.remove(k);
            level.savedEntities?.remove(k);
            level.entitiesGC?.remove(k);

            var clids = k.getEntityCLIDS();
            if (clids == null || level.entitiesByClass == null)
                return;

            for (var i = 0; i < clids.length; i++)
            {
                var entries = level.entitiesByClass.get(clids.getDyn(i)) as dc.hl.types.ArrayObj;
                entries?.remove(k);
            }
        }

        public static int PurgeGhostKingsFromLevel(Level? level)
        {
            if (level == null)
                return 0;

            var ghosts = new HashSet<GhostKing>();
            CollectGhostKings(level.entities, ghosts);
            CollectGhostKings(level.qTreeEntities, ghosts);
            CollectGhostKings(level.savedEntities, ghosts);
            CollectGhostKings(level.entitiesGC, ghosts);

            foreach (var ghost in ghosts)
                DisposeKingRuntime(ghost);

            return ghosts.Count;
        }

        private static void CollectGhostKings(dc.hl.types.ArrayObj? entries, HashSet<GhostKing> ghosts)
        {
            if (entries == null)
                return;

            for (var i = 0; i < entries.length; i++)
            {
                if (entries.getDyn(i) is GhostKing ghost)
                    ghosts.Add(ghost);
            }
        }

        private static void RunKingTeardownStep(ref List<Exception>? failures, string step, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failures ??= new List<Exception>();
                failures.Add(new InvalidOperationException($"GhostKing runtime teardown failed during {step}.", ex));
            }
        }

        private static void ThrowKingTeardownFailures(List<Exception>? failures)
        {
            if (failures == null || failures.Count == 0)
                return;
            if (failures.Count == 1)
                throw failures[0];
            throw new AggregateException("GhostKing runtime teardown failed.", failures);
        }

        public void SetLabel(Entity entity, string? text)
        {
            if (entity == null || entity.spr == null) return;
            var normalizedText = string.IsNullOrWhiteSpace(text) ? "Guest" : text;
            if (_labels.TryGetValue(entity, out var existing))
            {
                if (existing.Label.parent != null)
                {
                    if (!string.Equals(existing.TextValue, normalizedText, StringComparison.Ordinal))
                    {
                        try { existing.Label.set_text(normalizedText.AsHaxeString()); } catch { }
                        existing.TextValue = normalizedText;
                        existing.TextLength = normalizedText.Length;
                    }
                    return;
                }

                _labels.Remove(entity);
                RemoveLabelNode(existing.Label);
            }
            _Assets _Assets = Assets.Class;
            dc.h2d.Text text_h2d = _Assets.makeText(normalizedText.AsHaxeString(), dc.ui.Text.Class.COLORS.get("ST".AsHaxeString()), null, entity.spr);
            var targetScale = GetNicknameScale();
            text_h2d.y -= 80;
            text_h2d.x -= 2.5 * normalizedText.Length;
            text_h2d.font.size = 12;
            text_h2d.alpha = 0.8;
            text_h2d.scaleX = targetScale;
            text_h2d.scaleY = targetScale;
            text_h2d.textColor = 0;
            _labels[entity] = new LabelState(text_h2d, normalizedText);
        }

        private void RemoveLabel(Entity entity)
        {
            if (entity == null)
                return;

            if (_labels.TryGetValue(entity, out var state))
            {
                _labels.Remove(entity);
                RemoveLabelNode(state.Label);
            }

            _staleLabels.Remove(entity);
        }

        private static void RemoveLabelNode(dc.h2d.Text? label)
        {
            if (label?.parent != null)
                label.remove();
        }

        public void Dispose()
        {
            var head = kinghead;
            kinghead = null!;
            List<Exception>? failures = null;
            RunTeardownStep(ref failures, "GhostHero.kinghead", () => head?.dispose());

            var ownedKing = king;
            king = null!;
            RunTeardownStep(ref failures, "GhostHero.king", () => disposeKing(ownedKing));

            foreach (var state in _labels.Values)
            {
                RunTeardownStep(ref failures, "GhostHero.label", () => RemoveLabelNode(state.Label));
            }

            _labels.Clear();
            _staleLabels.Clear();
            ThrowTeardownFailures(failures);
        }

        private static void RunTeardownStep(ref List<Exception>? failures, string step, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failures ??= new List<Exception>();
                failures.Add(new InvalidOperationException($"GhostHero teardown failed during {step}.", ex));
            }
        }

        private static void ThrowTeardownFailures(List<Exception>? failures)
        {
            if (failures == null || failures.Count == 0)
                return;
            if (failures.Count == 1)
                throw failures[0];
            throw new AggregateException("GhostHero teardown failed.", failures);
        }

        public void UpdateLabels()
        {
            if (_labels.Count == 0) 
            {
                return;
            } 
            var targetScale = GetNicknameScale();
            _staleLabels.Clear();
            foreach (var pair in _labels)
            {
                var entity = pair.Key;
                var state = pair.Value;
                var label = state.Label;
                if (entity == null || label == null || entity.spr == null || label.parent == null)
                {
                    if (entity != null)
                        _staleLabels.Add(entity);
                    continue;
                }

                var targetX = -2.5 * state.TextLength;
                var targetY = -80;
                if (entity.dir < 0)
                {
                    label.scaleX = -targetScale;
                    label.x = -targetX;
                }
                else
                {
                    label.scaleX = targetScale;
                    label.x = targetX;
                }
                label.scaleY = targetScale;
                label.y = targetY;
            }

            if (_staleLabels.Count == 0) return;
            for (int i = 0; i < _staleLabels.Count; i++)
            {
                _labels.Remove(_staleLabels[i]);
            }
        }

        private static double GetNicknameScale()
        {
            try
            {
                var win = dc.hxd.Window.Class.getInstance();
                if (win != null)
                {
                    var displayMode = int.MinValue;
                    var sdlWin = win.window;
                    if (sdlWin != null)
                        displayMode = sdlWin.displayMode;

                    var mode = win.fullScreenMode;
                    if (_cachedDisplayMode == displayMode && _cachedFullScreenMode == mode)
                        return _cachedNicknameScale;

                    _cachedDisplayMode = displayMode;
                    _cachedFullScreenMode = mode;
                    _cachedNicknameScale = ResolveNicknameScale(displayMode, mode);
                    return _cachedNicknameScale;
                }
            }
            catch
            {
            }

            return _cachedNicknameScale;
        }

        private static double ResolveNicknameScale(int displayMode, int fullScreenMode)
        {
            if (displayMode == FullscreenDisplayMode || displayMode == BorderlessDisplayMode)
                return NickScaleFullscreen;
            if (displayMode == WindowedDisplayMode)
                return NickScaleWindowed;

            if (fullScreenMode == FullscreenDisplayMode || fullScreenMode == BorderlessDisplayMode)
                return NickScaleFullscreen;
            if (fullScreenMode == WindowedDisplayMode)
                return NickScaleWindowed;

            return NickScaleWindowed;
        }

    }
}
