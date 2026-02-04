using System.Diagnostics;
using dc.tool;

namespace DeadCellsMultiplayerMod;

public partial class ModEntry
{
    internal void NotifyLocalWeaponPrepareFromKingWeaponHooks(Weapon self)
    {
        if(_netRole == NetRole.None || self == null || me == null)
            return;

        if(!ReferenceEquals(self.owner, me))
            return;

        var item = self.item;
        if(item != null && TryGetWeaponKindId(item, out var kindId))
        {
            var slot = GetWeaponSlot(me.inventory, item);
            _net?.SendAttack(kindId!, slot, item.permanentId);
            _suppressHeroAnimUntilTicks = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * 0.25);
        }
    }
}

