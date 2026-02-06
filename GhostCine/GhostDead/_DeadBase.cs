using dc;
using dc.en;
using dc.h2d;
using dc.haxe.ds;
using dc.hl.types;
using dc.hxd;
using dc.hxd.res;
using dc.hxd.snd;
using dc.libs._Cooldown;
using dc.libs.data;
using dc.pr;
using dc.tool;
using dc.ui;
using DeadCellsMultiplayerMod.Ghost.GhostBase;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using Serilog;
using Cd = CooldownHelper.Cooldown;

namespace DeadCellsMultiplayerMod
{
    public static class _DeadBase
    {
        public static DeadBase deadBase = null!;
        public static Hero owen = null!;
        public static GhostKing k = null!;
        public static void EnterGhostDead(DeadBase @base, Hero heros, GhostKing king)
        {
            deadBase = @base;
            owen = heros;
            k = king;
            var cm = @base.cm;


            cm.__beginNewQueue();
            cm.__add(AHlAchtion001(), 0, null);
            cm.__add(Waiting(), 1000, null);
            cm.__addParallel(AHlAchtion002(), 0, null);
            cm.__add(Waiting(), 3000, null);
            cm.__add(AHlAchtion003(), 0, null);
            cm.__add(AHlAchtion004(), 0, null);

        }

        private static HlAction AHlAchtion001()
        {
            HlAction hl = new HlAction(() =>
            {
                var cm = deadBase;
                owen.hasGravity = true;
                deadBase.disableShowHUD = false;
            });
            return hl;
        }
        private static HlAction AHlAchtion002()
        {
            HlAction hl = new HlAction(() =>
            {
                var cd = Cd.Encode(Cd.Keys.KING_ANIM_stun);
                CdInst inst = new CdInst(cd, 0);
                owen.cd.fastCheck.set(cd, inst);
                owen.cd.cdList.push(inst);


            });
            return hl;
        }
        private static HlAction AHlAchtion003()
        {
            HlAction hl = new HlAction(() =>
            {

            });
            return hl;
        }
        private static HlAction AHlAchtion004()
        {
            HlAction hl = new HlAction(() =>
            {
                double time = 5 * owen.cd.baseFps * 1000.0;
                time = time / 1000.0;
                CdInst inst = new CdInst(Cd.Encode(Cd.Keys.DELET_YOLO), 0);
                deadBase.cd.fastCheck.set(Cd.Encode(Cd.Keys.DELET_YOLO), inst);
                deadBase.cd.cdList.push(inst);
                ArrayObj items = owen.inventory.items;
                var length = items.array.Count;

                for (int i = 0; i < length; i++)
                {
                    var t = items.array[i];

                    if (t != null && t.ToString()!.Contains("P_YoloDepleted"))
                    {
                        items.remove(t);
                    }

                    Log.Debug($"[ITEM]item:{t}");
                }


                virtual__n_<int> virtual__n_ = new virtual__n_<int>();
                virtual__n_._n = Game.Class.ME.user.pickDeathCells();
                StringMap colors = dc.ui.Text.Class.COLORS;
                int CE = colors.get("CE".AsHaxeString());
                GetText t10 = Lang.Class.t;


                dc.String str = t10.get("::n:: Cellules retrouvées".AsHaxeString(), virtual__n_);
                PopText popText = owen.popText(str, CE);
                owen.spr.get_anim().play("stun".AsHaxeString(), null, null).loop(99999);
                owen.say("text".AsHaxeString(), 16766720, null, null);

                Audio me77 = Audio.Class.ME;
                Loader loader77 = Res.Class.get_loader();
                str = "sfx/inter/pick_precious.wav".AsHaxeString();
                Channel channel = me77.playUIEvent((Sound)loader77.loadCache(str, Sound.Class), null);



                LogManager log = owen._level.game.log;
                str = Lang.Class.t.get("Mort évitée !".AsHaxeString(), null);
                Tile tile = Assets.Class.gameElements.getTile("affectCurse".AsHaxeString(), Ref<int>.Null, Ref<double>.Null, Ref<double>.Null, null);
                log.textWithTitle(null, str, null, tile);
                str = "sfx/gpfeedback/perc_yolo.wav".AsHaxeString();
                channel = me77.playUIEvent((Sound)loader77.loadCache(str, Sound.Class), null);
                deadBase.destroyed = true;
                owen.closeSay();

            });
            return hl;
        }

        private static HlAction Waiting()
        {
            HlAction hl = new HlAction(() =>
            {

            });
            return hl;
        }
    }
}