using CineHookInitialize;
using dc.en;
using Cd = CooldownHelper.Cooldown;

namespace DeadCellsMultiplayerMod
{
    public class DeadBase : dc.GameCinematic
    {
        private Hero owen = null!;
        public DeadBase(Hero hero, KingSkin king)
        {
            _DeadBase.EnterGhostDead(this, hero, king);
            owen = hero;
        }
        public override void update()
        {
            base.update();
            var item = CineHooks.item;
            if (item != null && this.cd.fastCheck.exists(Cd.Encode(Cd.Keys.DELET_YOLO)))
            {
                owen.dropAndUpdateItem(CineHooks.item);
                this.cd.fastCheck.remove(Cd.Encode(Cd.Keys.DELET_YOLO));
                this.cd.cdList.remove(Cd.Encode(Cd.Keys.DELET_YOLO));
            }
            if (!this.CanGohostCreate()) return;
        }

        public bool CanGohostCreate()
        {
            int k = Cd.Encode(Cd.Keys.KING_Create);
            var king = ModEntry.GetPrimaryClient();
            if (king == null)
                return false;
            var cd = king.cd;
            if (cd == null || cd.fastCheck == null)
                return false;
            return cd.fastCheck.exists(k);
        }

    }
}
