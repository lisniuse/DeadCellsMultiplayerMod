namespace DeadCellsMultiplayerMod;

internal static class PointerFxHelper
{
    internal static void SuppressPointerFx(dc.ui.Pointer? pointer, int suppressionKey)
    {
        if (pointer == null)
            return;

        try
        {
            var fastCheck = pointer.cd?.fastCheck;
            if (fastCheck == null)
                return;

            fastCheck.set(suppressionKey, (object)1);
        }
        catch
        {
        }
    }
}
