using dc;
using dc.en;
using dc.hl.types;
using dc.libs._Cooldown;
using dc.tool;
using dc.ui;
using HaxeProxy.Runtime;
using ModCore.Utitities;
using Serilog;
using Cd = CooldownHelper.Cooldown;

namespace DeadCellsMultiplayerMod
{
    public static class _DeadBase
    {
        public static DeadBase deadBase = null!;
        public static Hero owen = null!;
        public static KingSkin k = null!;
        public static void EnterGhostDead(DeadBase @base, Hero heros, KingSkin king)
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
                owen.spr.get_anim().play("stun".AsHaxeString(), null, null).loop(99999);
                owen.say("text".AsHaxeString(), 16766720, null, null);

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
                owen.cd.fastCheck.set(Cd.Encode(Cd.Keys.DELET_YOLO), inst);
                owen.cd.cdList.push(inst);
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