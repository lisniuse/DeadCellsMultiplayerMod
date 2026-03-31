using System;
using dc;
using dc.h2d;
using dc.hxd;
using dc.hl.types;
using dc.pr;
using dc.tool;
using dc.ui;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using DeadCellsMultiplayerMod.MultiplayerModUI.Connection;
using IOFile = System.IO.File;
using IODirectory = System.IO.Directory;
using IOPath = System.IO.Path;

namespace DeadCellsMultiplayerMod
{
    internal static partial class GameMenu
    {
        private const string MultiplayerSaveFolderName = "MSave";
        private const string MultiplayerSaveButtonHelp = "Choose multiplayer save slot";
        private const string MultiplayerSaveImportLabel = "Copy your save in that slot";
        private const string OriginalSaveImportTitle = "Choose original save to copy";
        private const int CopyActionCode = 20;

        private enum MultiplayerSaveMenuKind
        {
            None,
            MultiplayerSlots,
            OriginalSourceSelection
        }

        private static bool _multiplayerSaveHooksAttached;
        private static bool _multiplayerSaveMenuOpening;
        private static MultiplayerSaveMenuKind _multiplayerSaveMenuKind = MultiplayerSaveMenuKind.None;
        private static NetRole _multiplayerSaveMenuReturnRole = NetRole.None;
        private static bool _pendingOpenOriginalImportChooser;
        private static int? _multiplayerSaveImportTargetSlot;
        private static int? _preferredMultiplayerSaveSlot;

        private static void InitializeMultiplayerSaveHooks()
        {
            if (_multiplayerSaveHooksAttached)
                return;

            Hook__Save.fileName += Hook__Save_fileName;
            Hook_TitleScreen.onLeavingSaveMenu += Hook_TitleScreen_onLeavingSaveMenu;
            Hook__SaveChoice.__constructor__ += Hook__SaveChoice___constructor__;
            Hook_SaveChoice.onCopy += Hook_SaveChoice_onCopy;
            Hook_SaveChoice.onValidate += Hook_SaveChoice_onValidate;
            Hook_SaveChoice.onDelete += Hook_SaveChoice_onDelete;
            Hook_SaveChoice.onDispose += Hook_SaveChoice_onDispose;

            _multiplayerSaveHooksAttached = true;
        }

        private static string GetMultiplayerSaveButtonLabel()
        {
            return $"Save: Slot {ResolveSaveSlotNumber(null) + 1}";
        }

        private static void OpenMultiplayerSlotMenu(TitleScreen screen)
        {
            _multiplayerSaveMenuReturnRole = _inHostStatusMenu
                ? NetRole.Host
                : _inClientWaitingMenu
                    ? NetRole.Client
                    : _role;

            OpenSaveMenu(screen, MultiplayerSaveMenuKind.MultiplayerSlots);
        }

        private static void OpenOriginalSaveImportMenu(TitleScreen screen)
        {
            OpenSaveMenu(screen, MultiplayerSaveMenuKind.OriginalSourceSelection);
        }

        private static void OpenSaveMenu(TitleScreen screen, MultiplayerSaveMenuKind kind)
        {
            _multiplayerSaveMenuKind = kind;
            _multiplayerSaveMenuOpening = true;

            try
            {
                screen.saveMenu();
                screen.ShouldAutoHideConnectionUI(false);
            }
            catch (Exception ex)
            {
                _multiplayerSaveMenuKind = MultiplayerSaveMenuKind.None;
                _log?.Warning("[NetMod] Failed to open save menu {Kind}: {Message}", kind, ex.Message);
            }
            finally
            {
                _multiplayerSaveMenuOpening = false;
            }
        }

        private static void Hook_TitleScreen_onLeavingSaveMenu(Hook_TitleScreen.orig_onLeavingSaveMenu orig, TitleScreen self)
        {
            var previousKind = _multiplayerSaveMenuKind;
            var returnRole = _multiplayerSaveMenuReturnRole;
            var openOriginalImportChooser = _pendingOpenOriginalImportChooser;

            _multiplayerSaveMenuKind = MultiplayerSaveMenuKind.None;
            _multiplayerSaveMenuOpening = false;
            _pendingOpenOriginalImportChooser = false;

            orig(self);

            if (openOriginalImportChooser)
            {
                OpenOriginalSaveImportMenu(self);
                return;
            }

            if (previousKind == MultiplayerSaveMenuKind.OriginalSourceSelection)
            {
                OpenMultiplayerSlotMenu(self);
                return;
            }

            _multiplayerSaveMenuReturnRole = NetRole.None;

            if (returnRole == NetRole.Host)
            {
                ShowHostStatusMenu(self);
                self.ShouldAutoHideConnectionUI(true);
            }
            else if (returnRole == NetRole.Client)
            {
                ShowClientWaitingMenu(self);
                self.ShouldAutoHideConnectionUI(true);
            }
        }

        private static void Hook__SaveChoice___constructor__(Hook__SaveChoice.orig___constructor__ orig, SaveChoice self, TitleScreen tween)
        {
            orig(self, tween);

            try
            {
                switch (_multiplayerSaveMenuKind)
                {
                    case MultiplayerSaveMenuKind.MultiplayerSlots:
                        ConfigureMultiplayerSaveChoice(self);
                        break;
                    case MultiplayerSaveMenuKind.OriginalSourceSelection:
                        ConfigureOriginalSourceSaveChoice(self);
                        break;
                }
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] Failed to customize save choice UI: {Message}", ex.Message);
            }
        }

        private static void Hook_SaveChoice_onCopy(Hook_SaveChoice.orig_onCopy orig, SaveChoice self)
        {
            if (_multiplayerSaveMenuKind == MultiplayerSaveMenuKind.OriginalSourceSelection)
                return;

            if (_multiplayerSaveMenuKind != MultiplayerSaveMenuKind.MultiplayerSlots)
            {
                orig(self);
                return;
            }

            if (!TryGetSelectedSaveSlot(self, out var targetSlot))
                return;

            _multiplayerSaveImportTargetSlot = targetSlot;
            _preferredMultiplayerSaveSlot = targetSlot;
            _pendingOpenOriginalImportChooser = true;
            self.onCancel();
        }

        private static void Hook_SaveChoice_onValidate(Hook_SaveChoice.orig_onValidate orig, SaveChoice self)
        {
            if (_multiplayerSaveMenuKind != MultiplayerSaveMenuKind.OriginalSourceSelection)
            {
                if (_multiplayerSaveMenuKind == MultiplayerSaveMenuKind.MultiplayerSlots &&
                    TryGetSelectedSaveSlot(self, out var selectedSlot))
                {
                    _preferredMultiplayerSaveSlot = selectedSlot;
                }

                orig(self);
                return;
            }

            if (!TryGetSelectedSourceSaveSlot(self, out var sourceSlot))
                return;
            if (!_multiplayerSaveImportTargetSlot.HasValue)
                return;
            if (!CopyOriginalSaveIntoMultiplayerSlot(sourceSlot, _multiplayerSaveImportTargetSlot.Value))
                return;

            _preferredMultiplayerSaveSlot = _multiplayerSaveImportTargetSlot.Value;
            SetCurrentSaveSlot(_multiplayerSaveImportTargetSlot.Value);
            self.onCancel();
        }

        private static void Hook_SaveChoice_onDelete(Hook_SaveChoice.orig_onDelete orig, SaveChoice self)
        {
            if (_multiplayerSaveMenuKind == MultiplayerSaveMenuKind.OriginalSourceSelection)
                return;

            orig(self);
        }

        private static void Hook_SaveChoice_onDispose(Hook_SaveChoice.orig_onDispose orig, SaveChoice self)
        {
            try
            {
                orig(self);
            }
            finally
            {
                _multiplayerSaveMenuOpening = false;
            }
        }

        private static dc.String Hook__Save_fileName(Hook__Save.orig_fileName orig, int? slot)
        {
            if (!ShouldUseMultiplayerSaveStore())
                return orig(slot);

            try
            {
                EnsureMultiplayerSaveFolderExists();
                return MakeHLString(GetMultiplayerSaveRelativeFilePath(slot));
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] Failed to resolve multiplayer save path: {Message}", ex.Message);
                return orig(slot);
            }
        }

        private static bool ShouldUseMultiplayerSaveStore()
        {
            if (_multiplayerSaveMenuKind == MultiplayerSaveMenuKind.OriginalSourceSelection)
                return false;

            return _role != NetRole.None || _multiplayerSaveMenuKind == MultiplayerSaveMenuKind.MultiplayerSlots || _multiplayerSaveMenuOpening;
        }

        private static int ResolveSaveSlotNumber(int? slot)
        {
            if (slot.HasValue && slot.Value >= 0)
                return slot.Value;

            try
            {
                var current = Main.Class.ME?.options?.curSlot;
                if (current.HasValue && current.Value >= 0)
                    return current.Value;
            }
            catch
            {
            }

            return 0;
        }

        private static string GetSaveRootPath()
        {
            try
            {
                return IOPath.GetFullPath("save");
            }
            catch
            {
                return IOPath.Combine(Environment.CurrentDirectory, "save");
            }
        }

        private static string GetOriginalSaveRelativeFilePath(int? slot)
        {
            return $"user_{ResolveSaveSlotNumber(slot)}.dat";
        }

        private static string GetMultiplayerSaveRelativeFilePath(int? slot)
        {
            return $"{MultiplayerSaveFolderName}/user_{ResolveSaveSlotNumber(slot)}.dat";
        }

        private static string GetAbsoluteSavePath(string relativePath)
        {
            var normalized = relativePath
                .Replace('/', IOPath.DirectorySeparatorChar)
                .Replace('\\', IOPath.DirectorySeparatorChar);

            return IOPath.GetFullPath(IOPath.Combine(GetSaveRootPath(), normalized));
        }

        private static string GetOriginalSaveFilePath(int? slot)
        {
            return GetAbsoluteSavePath(GetOriginalSaveRelativeFilePath(slot));
        }

        private static string GetMultiplayerSaveFilePath(int? slot)
        {
            EnsureMultiplayerSaveFolderExists();
            return GetAbsoluteSavePath(GetMultiplayerSaveRelativeFilePath(slot));
        }

        private static void EnsureMultiplayerSaveFolderExists()
        {
            IODirectory.CreateDirectory(GetAbsoluteSavePath(MultiplayerSaveFolderName));
        }

        private static void ConfigureMultiplayerSaveChoice(SaveChoice self)
        {
            SetControlLabelVisible(self, 1, false);
            EnsureImportControlLabel(self);
            TrySelectPreferredMultiplayerSlot(self);
            self.fControlLabel?.reflow();
        }

        private static void ConfigureOriginalSourceSaveChoice(SaveChoice self)
        {
            if (self?.title != null)
                self.title.set_text(MakeHLString(OriginalSaveImportTitle));

            SetControlLabelVisible(self, 0, false);
            SetControlLabelVisible(self, 1, false);
            self.fControlLabel?.reflow();
        }

        private static void EnsureImportControlLabel(SaveChoice self)
        {
            if (self?.fControlLabel == null)
                return;

            var existing = FindImportControlLabel(self);
            if (existing != null)
            {
                existing.set_visible(true);
                existing.tfLabel?.set_text(MakeHLString(MultiplayerSaveImportLabel));
                existing.reflow();
                return;
            }

            var importLabel = new ControlLabel(CreateActionArray(CopyActionCode), MakeHLString(MultiplayerSaveImportLabel), null, null, null, null);
            importLabel.set_visible(true);
            importLabel.reflow();
            self.fControlLabel.addChild(importLabel);
            self.fControlLabel.reflow();
        }

        private static ControlLabel? FindImportControlLabel(SaveChoice self)
        {
            var children = self?.fControlLabel?.children;
            if (children == null)
                return null;

            for (var i = 0; i < children.length; i++)
            {
                if (children.array[i] is not ControlLabel label)
                    continue;

                var rawText = label.tfLabel?.rawText?.ToString();
                if (string.Equals(rawText, MultiplayerSaveImportLabel, StringComparison.Ordinal))
                    return label;
            }

            return null;
        }

        private static void SetControlLabelVisible(SaveChoice self, int index, bool visible)
        {
            var controlLabel = GetControlLabel(self, index);
            if (controlLabel == null)
                return;

            controlLabel.set_visible(visible);
            controlLabel.reflow();
        }

        private static ControlLabel? GetControlLabel(SaveChoice self, int index)
        {
            var children = self?.fControlLabel?.children;
            if (children == null || index < 0 || index >= children.length)
                return null;

            return children.array[index] as ControlLabel;
        }

        private static ArrayBytes_Int CreateActionArray(int actionCode)
        {
            var values = new ArrayBytes_Int();
            try
            {
                values.push(actionCode);
            }
            catch
            {
                values.pushDyn(actionCode);
            }

            return values;
        }

        private static void TrySelectPreferredMultiplayerSlot(SaveChoice self)
        {
            if (!_preferredMultiplayerSaveSlot.HasValue)
                return;

            var targetSlot = _preferredMultiplayerSaveSlot.Value;
            var saves = self?.saves;
            if (saves == null)
                return;

            for (var i = 0; i < saves.length; i++)
            {
                if (saves.array[i] is not SaveWindow window || window.si == null || window.si.index != targetSlot)
                    continue;

                try
                {
                    var instant = true;
                    self.select(i, Ref<bool>.From(ref instant));
                }
                catch
                {
                    self.curSaveId = i;
                }

                return;
            }
        }

        private static bool TryGetSelectedSaveWindow(SaveChoice self, out SaveWindow? window)
        {
            window = null;
            var saves = self?.saves;
            if (saves == null)
                return false;

            var selectedIndex = self.curSaveId;
            if (selectedIndex < 0 || selectedIndex >= saves.length)
                return false;

            window = saves.array[selectedIndex] as SaveWindow;
            return window != null;
        }

        private static bool TryGetSelectedSaveSlot(SaveChoice self, out int slot)
        {
            slot = 0;
            if (!TryGetSelectedSaveWindow(self, out var window) || window?.si == null)
                return false;

            slot = window.si.index;
            return slot >= 0;
        }

        private static bool TryGetSelectedSourceSaveSlot(SaveChoice self, out int slot)
        {
            slot = 0;
            if (!TryGetSelectedSaveWindow(self, out var window) || window?.si == null)
                return false;
            if (!window.si.exists || !window.si.usable)
                return false;

            slot = window.si.index;
            return slot >= 0;
        }

        private static bool CopyOriginalSaveIntoMultiplayerSlot(int sourceSlot, int targetSlot)
        {
            var sourcePath = GetOriginalSaveFilePath(sourceSlot);
            if (!IOFile.Exists(sourcePath))
            {
                _log?.Warning("[NetMod] No original save found for multiplayer import: {Path}", sourcePath);
                return false;
            }

            var targetPath = GetMultiplayerSaveFilePath(targetSlot);
            try
            {
                var targetDirectory = IOPath.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                    IODirectory.CreateDirectory(targetDirectory);

                IOFile.Copy(sourcePath, targetPath, overwrite: true);
                _log?.Information(
                    "[NetMod] Copied original save slot {SourceSlot} into multiplayer slot {TargetSlot}",
                    sourceSlot + 1,
                    targetSlot + 1);
                return true;
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] Failed to import original save into multiplayer slot: {Message}", ex.Message);
                return false;
            }
        }

        private static void SetCurrentSaveSlot(int slot)
        {
            try
            {
                var options = Main.Class.ME?.options;
                if (options == null)
                    return;

                options.curSlot = slot;
                options.save();
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] Failed to set multiplayer save slot {Slot}: {Message}", slot + 1, ex.Message);
            }
        }
    }
}
