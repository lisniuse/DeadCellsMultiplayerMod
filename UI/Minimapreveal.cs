using dc;
using dc.en;
using dc.h2d;
using dc.haxe.io;
using dc.hl.types;
using dc.pr;
using dc.ui;
using dc.ui.hud;
using dc.ui.hud.map;
using HaxeProxy.Runtime;
using ModCore.Utitities;
using Debug = Serilog.Log;

namespace DeadCellsMultiplayerMod.Minimap
{
    public class Minimapreveal
    {
        public Graphics? kingcircle;
        public Minimapreveal()
        {
            init();
        }
        public void init()
        {
            Hook_MiniMap.postUpdate += Hook_MiniMap_postUpdate;
            Hook_MiniMap.initContainers += Hook_MiniMap_initContainers;
        }
        private Dictionary<int, Graphics> _kingMarkers = new Dictionary<int, Graphics>();
        private void Hook_MiniMap_initContainers(Hook_MiniMap.orig_initContainers orig, MiniMap self, Bytes max)
        {
            orig(self, max);
            int length = ModEntry.clients.Length;

            for (int i = 0; i < length; i++)
            {
                var displayName = ModEntry.GetClientLabel(i);
                dc.String remoteUsername = displayName.AsHaxeString();

                bool create = true;
                this.kingcircle = self.fow.createGraphics(Ref<bool>.From(ref create));
                var kmap = this.kingcircle;
                int color = 16711680;
                double alpha = 1;
                kmap.beginFill(Ref<int>.From(ref color), Ref<double>.From(ref alpha));
                kmap.drawCircle(0.0, 0.0, 17.0, Ref<int>.Null);
                kmap.endFill();
                kmap.set_visible(true);
                kmap.posChanged = true;

                _kingMarkers[i] = kmap;
            }


        }

        private void Hook_MiniMap_postUpdate(Hook_MiniMap.orig_postUpdate orig, MiniMap self)
        {
            orig(self);
            var kmap = this.kingcircle;
            MMTracker mmtracker;
            if (kmap != null)
            {
                var king = ModEntry.GetPrimaryClient();
                ArrayObj obj = self.trackers;
                int length = obj.length;
                for (int i = 0; i < length; i++)
                {
                    dynamic arrayElement = obj.array[i]!;
                    if (arrayElement == null) continue;
                    mmtracker = arrayElement;
                    if (king != null && mmtracker != null && Std.Class.@is(mmtracker.e, KingSkin.Class))
                    {
                        var clients = ModEntry.clients;
                        for (int k = 0; k < clients.Length; k++)
                        {
                            var client = clients[k];

                            if (client == null) continue;

                            if (!_kingMarkers.TryGetValue(k, out var marker))
                                continue;

                            marker.x = client.cx;
                            marker.y = client.cy;
                        }
                    }
                }


            }

        }

    }
}