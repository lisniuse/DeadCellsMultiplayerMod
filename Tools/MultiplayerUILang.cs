using ModCore.Events;
using ModCore.Events.Interfaces.Game;
using ModCore.Modules;
using ModCore.Utilities;
using Serilog;

namespace DeadCellsMultiplayerMod.Tools.ModLang
{
    public class MultiplayerModLang :
    IEventReceiver,
    IOnGameEndInit
    {
        private ModEntry? Entry;
        public MultiplayerModLang(ModEntry entry)
        {
            Entry = entry;
            EventSystem.AddReceiver(this);
            entry.Logger.Information("\x1b[34m[[MultiplayerModLang] Language Module Loading]\x1b[0m ");
            GetText.Instance.RegisterMod("DeadCellsMultiplayerModLang");

        }

        void IOnGameEndInit.OnGameEndInit()
        {
            var res = Entry!.Info.ModRoot!.GetFilePath("res.pak");
            FsPak.Instance.FileSystem.loadPak(res.AsHaxeString());
        }
    }
}