


using dc.pr;
using dc.ui;
using DeadCellsMultiplayerMod.Interface.ModuleInitializing;
using ModCore.Events;
using ModCore.Utitities;

namespace DeadCellsMultiplayerMod.MultiplayerModUI.Connection
{
    public static class _ConnectionUI

    {
        public static ConnectionUI GetUI { get; set; } = null!;


        public static void updateVisible(ConnectionUI connectionUI)
        {
            GetUI =connectionUI;
            
        }
    }
}