using System.Runtime.InteropServices;
using dc.en;

namespace DeadCellsMultiplayerMod;

internal static partial class GameMenu
{
    private const int ReviveInteractKeyCode = 82; // R (keyboard)

    /// <summary>Hold-to-revive: keyboard R plus gamepad face buttons / primary-secondary (same binding resolution as menus).</summary>
    internal static bool IsReviveHoldInputDown(Hero? hero)
    {
        if (hero == null)
            return false;

        try
        {
            if (dc.hxd.Key.Class.isDown(ReviveInteractKeyCode))
                return true;
        }
        catch
        {
        }

#pragma warning disable CS8602
        try
        {
            var accessObj = hero.controller;
            if (accessObj == null)
                return false;

            dynamic access = accessObj;
            if (access.manualLock)
                return false;

            dynamic controller = access.parent;
            if (controller == null || controller.isLocked)
                return false;

            dynamic b = controller.get_bindings();
            if (RevivePadBindingHeld(controller, b.padA))
                return true;
            if (RevivePadBindingHeld(controller, b.padB))
                return true;
            if (RevivePadBindingHeld(controller, b.padC))
                return true;
            if (ReviveKeyboardBindingHeld(controller, b.primary))
                return true;
            if (ReviveKeyboardBindingHeld(controller, b.secondary))
                return true;
            if (ReviveKeyboardBindingHeld(controller, b.third))
                return true;
        }
        catch
        {
        }
#pragma warning restore CS8602

        return false;
    }

    private static bool RevivePadBindingHeld(dynamic controller, dynamic bindings)
    {
        if (bindings == null)
            return false;
        try
        {
            var len = (int)bindings.length;
            var bytes = bindings.bytes;
            for (var i = 0; i < len; i++)
            {
                var code = Marshal.ReadInt32(bytes, i << 2);
                if (code < 0)
                    continue;
                if (controller.padIsPressed(code))
                    return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool ReviveKeyboardBindingHeld(dynamic controller, dynamic bindings)
    {
        if (bindings == null)
            return false;
        try
        {
            var len = (int)bindings.length;
            var bytes = bindings.bytes;
            for (var i = 0; i < len; i++)
            {
                var code = Marshal.ReadInt32(bytes, i << 2);
                if (code < 0)
                    continue;
                if (dc.hxd.Key.Class.isDown(code))
                    return true;
            }
        }
        catch
        {
        }

        return false;
    }
}
