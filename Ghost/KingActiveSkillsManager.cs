using System;
using System.Collections.Generic;
using DeadCellsMultiplayerMod;
using dc.en;
using dc.pr;
using dc.tool;
using dc.tool.hero;
using ModCore.Storage;
using ModCore.Utitities;

namespace DeadCellsMultiplayerMod.Ghost.GhostBase
{
    public class KingActiveSkillsManager : HeroActiveSkillsManager, IHxbitSerializable<object>
    {
        private static Hero? lastKnownHero;
        private static readonly Random rng = new();

        private Hero? me;
        private GhostKing? king;
        private Level? lvl;

        public InventItem? equippedWeapon;

        // Parameterless ctor for serializer fallback when older saves don't carry custom data.
        public KingActiveSkillsManager() : base(GetFallbackHero())
        {
            me = lastKnownHero;
        }

        public KingActiveSkillsManager(Hero hero, GhostKing kingSkin, Level level) : base(hero)
        {
            me = hero;
            king = kingSkin;
            lvl = level;
            lastKnownHero = hero;
        }

        object IHxbitSerializable<object>.GetData()
        {
            return new();
        }

        void IHxbitSerializable<object>.SetData(object data)
        {
        }

        public InventItem? GiveWeaponFromHero(int slot = 0)
        {
            var hero = me ?? lastKnownHero;
            var inventory = hero?.inventory;
            if (inventory == null)
                return null;

            var weapon = inventory.getEquippedWeaponOn(slot)
                         ?? inventory.getEquippedWeaponOn(0)
                         ?? inventory.getEquippedWeaponOn(1)
                         ?? inventory.getBackpackWeapon();
            if (weapon != null)
            {
                equippedWeapon = weapon;
            }
            return weapon;
        }

        public InventItem? GiveRandomWeaponFromHero()
        {
            var hero = me ?? lastKnownHero;
            var inventory = hero?.inventory;
            if (inventory == null)
                return null;

            var candidates = new List<InventItem>(3);
            var weapon0 = inventory.getEquippedWeaponOn(0);
            if (weapon0 != null)
                candidates.Add(weapon0);

            var weapon1 = inventory.getEquippedWeaponOn(1);
            if (weapon1 != null)
                candidates.Add(weapon1);

            var backpack = inventory.getBackpackWeapon();
            if (backpack != null)
                candidates.Add(backpack);

            if (candidates.Count == 0)
                return null;

            var weapon = candidates[rng.Next(candidates.Count)];
            equippedWeapon = weapon;
            return weapon;
        }

        private static Hero GetFallbackHero()
        {
            var hero = ModEntry.me ?? dc.pr.Game.Class.ME?.hero;
            if (hero != null)
                return hero;

            if (lastKnownHero != null)
                return lastKnownHero;

            throw new InvalidOperationException("KingActiveSkillsManager deserialization requires a Hero.");
        }
    }
}
