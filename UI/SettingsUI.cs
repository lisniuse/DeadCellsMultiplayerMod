using System;
using DeadCellsMultiplayerMod.Interface.ModuleInitializing;
using DeadCellsMultiplayerMod.Mobs.MobsSynchronization;
using dc.hl.types;
using dc.ui;
using HaxeProxy.Runtime;
using ModCore.Events;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod.UI;

public class SettingsUI :
    IEventReceiver,
    IOnAdvancedModuleInitializing
{
    private const string MultiplayerSettingsButtonLabel = "Multiplayer settings";
    private const string MultiplayerSettingsMenuTitle = "Multiplayer settings";
    private const string MultiplayerSettingsBackLabel = "Back";
    private const string MobsSettingsHeaderLabel = "Mobs settings";
    private const string MobsSyncToggleLabel = "Enable mobs sync";
    private const string MobsInterpolationSliderLabel = "Mobs interpolation quality";
    private const string MobsHpSliderLabel = "Mobs HP multiplier";
    private const string BossesHpSliderLabel = "Bosses HP multiplier";
    private const string VerticalSyncToggleLabel = "Sync vertical position";

    private static bool _hooksAttached;
    private static bool _isMultiplayerSettingsOpen;
    private static int _multiplayerSettingsOptionsId = -1;

    private ModEntry mod { get; set; }

    public SettingsUI(ModEntry entry)
    {
        mod = entry;
        EventSystem.AddReceiver(this);
    }

    void IOnAdvancedModuleInitializing.OnAdvancedModuleInitializing(ModEntry entry)
    {
        entry.Logger.Information("\x1b[32m[[ModEntry.SettingsUI] Initializing SettingsUI...]\x1b[0m ");

        if (_hooksAttached)
            return;

        Hook_Options.showMain += Hook_Options_showMain;
        Hook_Options.showCredits += Hook_Options_showCredits;
        Hook_Options.onDispose += Hook_Options_onDispose;
        _hooksAttached = true;
    }

    private void Hook_Options_showMain(Hook_Options.orig_showMain orig, Options self)
    {
        if (self == null)
        {
            orig(self);
            return;
        }

        if (IsMultiplayerSettingsContext(self))
        {
            MultiplayerSettingsStorage.Save();
            ResetMenuState();
        }

        try
        {
            int leftPadding = 5;
            HlAction onSelect = new HlAction(() =>
            {
                OpenMultiplayerSettingsMenu(self);
            });

            // Insert before vanilla entries so it appears at the top.
            self.addSimpleWidget(
                MultiplayerSettingsButtonLabel.AsHaxeString(),
                null,
                onSelect,
                Ref<int>.From(ref leftPadding),
                null);
        }
        catch (Exception ex)
        {
            mod.Logger.Warning(ex, "[NetMod] Failed to add Multiplayer settings button");
        }

        orig(self);
    }

    private void Hook_Options_showCredits(Hook_Options.orig_showCredits orig, Options self)
    {
        if (!IsMultiplayerSettingsContext(self))
        {
            orig(self);
            return;
        }

        BuildMultiplayerSettingsSection(self);
    }

    private void Hook_Options_onDispose(Hook_Options.orig_onDispose orig, Options self)
    {
        bool wasMultiplayerSettings = IsMultiplayerSettingsContext(self);
        orig(self);

        if (wasMultiplayerSettings)
        {
            MultiplayerSettingsStorage.Save();
            ResetMenuState();
        }
    }

    private void OpenMultiplayerSettingsMenu(Options self)
    {
        try
        {
            if (self == null || self.destroyed)
                return;

            _isMultiplayerSettingsOpen = true;
            _multiplayerSettingsOptionsId = self.uniqId;
            self.setSection(new OptionsSection.S_Credits());
        }
        catch (Exception ex)
        {
            ResetMenuState();
            mod.Logger.Warning(ex, "[NetMod] Failed to open multiplayer settings menu");
        }
    }

    private static bool IsMultiplayerSettingsContext(Options self)
    {
        return _isMultiplayerSettingsOpen
            && self != null
            && !self.destroyed
            && self.uniqId == _multiplayerSettingsOptionsId;
    }

    private static void ResetMenuState()
    {
        _isMultiplayerSettingsOpen = false;
        _multiplayerSettingsOptionsId = -1;
    }

    private void BuildMultiplayerSettingsSection(Options self)
    {
        try
        {
            if (!IsMultiplayerSettingsContext(self))
                return;

            self.title?.set_text(MultiplayerSettingsMenuTitle.AsHaxeString());
            AddMobsSettingsWidgets(self);

            int leftPadding = 5;
            HlAction onBack = new HlAction(() =>
            {
                if (self != null && !self.destroyed)
                    self.onQuit();
            });

            self.addSimpleWidget(
                MultiplayerSettingsBackLabel.AsHaxeString(),
                null,
                onBack,
                Ref<int>.From(ref leftPadding),
                null);
        }
        catch (Exception ex)
        {
            mod.Logger.Warning(ex, "[NetMod] Failed to build multiplayer settings menu");
        }
    }

    private void AddMobsSettingsWidgets(Options self)
    {
        if (self == null)
            return;

        dc.h2d.Flow? headerParent = self.mainFlow ?? self.scrollerFlow;
        if (headerParent == null)
            return;

        // Keep custom page widgets on main flow (non-scroller mode) to avoid null cbmpScroller.height paths.
        dc.h2d.Flow? widgetParent = null;

        self.addSeparator(MobsSettingsHeaderLabel.AsHaxeString(), headerParent);

        bool enabledNow = MultiplayerSettingsStorage.EnableMobsSync;
        self.addToggleWidget(
            MobsSyncToggleLabel.AsHaxeString(),
            null,
            new HlFunc<bool>(ToggleMobsSyncSetting),
            Ref<bool>.From(ref enabledNow),
            widgetParent);

        double interpolationValue = MultiplayerSettingsStorage.MobsInterpolationQuality;
        double interpolationStep = 0.02;
        bool interpolationPercentDisplay = true;
        bool interpolationRawDisplay = false;
        double interpolationMin = 0.20;
        double interpolationMax = 1.00;

        self.addSliderWidget(
            MobsInterpolationSliderLabel.AsHaxeString(),
            new HlAction<double>(OnMobsInterpolationSliderChanged),
            interpolationValue,
            Ref<double>.From(ref interpolationStep),
            widgetParent,
            Ref<bool>.From(ref interpolationPercentDisplay),
            Ref<bool>.From(ref interpolationRawDisplay),
            Ref<double>.From(ref interpolationMin),
            Ref<double>.From(ref interpolationMax),
            null,
            Ref<int>.Null);

        double mobsHpValue = MultiplayerSettingsStorage.MobsHpMultiplier;
        double mobsHpStep = 0.10;
        bool mobsHpPercentDisplay = false;
        bool mobsHpRawDisplay = true;
        double mobsHpMin = 0.25;
        double mobsHpMax = 8.00;

        self.addSliderWidget(
            MobsHpSliderLabel.AsHaxeString(),
            new HlAction<double>(OnMobsHpSliderChanged),
            mobsHpValue,
            Ref<double>.From(ref mobsHpStep),
            widgetParent,
            Ref<bool>.From(ref mobsHpPercentDisplay),
            Ref<bool>.From(ref mobsHpRawDisplay),
            Ref<double>.From(ref mobsHpMin),
            Ref<double>.From(ref mobsHpMax),
            null,
            Ref<int>.Null);

        double bossesHpValue = MultiplayerSettingsStorage.BossesHpMultiplier;
        double bossesHpStep = 0.10;
        bool bossesHpPercentDisplay = false;
        bool bossesHpRawDisplay = true;
        double bossesHpMin = 0.25;
        double bossesHpMax = 8.00;

        self.addSliderWidget(
            BossesHpSliderLabel.AsHaxeString(),
            new HlAction<double>(OnBossesHpSliderChanged),
            bossesHpValue,
            Ref<double>.From(ref bossesHpStep),
            widgetParent,
            Ref<bool>.From(ref bossesHpPercentDisplay),
            Ref<bool>.From(ref bossesHpRawDisplay),
            Ref<double>.From(ref bossesHpMin),
            Ref<double>.From(ref bossesHpMax),
            null,
            Ref<int>.Null);

        bool verticalSyncNow = MultiplayerSettingsStorage.SyncVerticalPosition;
        self.addToggleWidget(
            VerticalSyncToggleLabel.AsHaxeString(),
            null,
            new HlFunc<bool>(ToggleVerticalSyncSetting),
            Ref<bool>.From(ref verticalSyncNow),
            widgetParent);
    }

    private bool ToggleMobsSyncSetting()
    {
        bool enabled = !MultiplayerSettingsStorage.EnableMobsSync;
        MultiplayerSettingsStorage.EnableMobsSync = enabled;

        if (!enabled)
        {
            MobsSynchronization.ClearTrackingForLevelChange();
            try { GameMenu.NetRef?.ClearMobSyncQueues(); } catch { }
        }

        return enabled;
    }

    private static bool ToggleVerticalSyncSetting()
    {
        bool enabled = !MultiplayerSettingsStorage.SyncVerticalPosition;
        MultiplayerSettingsStorage.SyncVerticalPosition = enabled;
        return enabled;
    }

    private static void OnMobsInterpolationSliderChanged(double value)
    {
        MultiplayerSettingsStorage.MobsInterpolationQuality = value;
    }

    private static void OnMobsHpSliderChanged(double value)
    {
        MultiplayerSettingsStorage.MobsHpMultiplier = value;
    }

    private static void OnBossesHpSliderChanged(double value)
    {
        MultiplayerSettingsStorage.BossesHpMultiplier = value;
    }
}
