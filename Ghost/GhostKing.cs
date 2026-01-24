using System;
using System.Reflection;
using dc;
using dc.en;
using dc.h3d.mat;
using dc.hl.types;
using dc.libs.heaps.slib;
using dc.pow;
using dc.pr;
using dc.shader;
using dc.tool;
using ModCore.Storage;
using ModCore.Utitities;

namespace DeadCellsMultiplayerMod.Ghost.GhostBase
{
    public class GhostKing : KingSkin, IHxbitSerializable<object>
    {
        public KingActiveSkillsManager? activeSkillsManager;
        public InventItem? activeWeapon;
        public Weapon? activeWeaponImpl;

        public GhostKing(Level lvl, int x, int y) : base(lvl, x, y)
        {
        }
        object IHxbitSerializable<object>.GetData()
        {
            return new();
        }

        void IHxbitSerializable<object>.SetData(object data)
        {
        }


        public override void init()
        {
            base.init();
        }

        public override void initGfx()
        {
            base.initGfx();
            var remoteSkin = ModEntry.Instance!.remoteSkin;
            if (remoteSkin == null) remoteSkin = "PrisonerDefault";
            dc.String group = "idle".AsHaxeString();
            SpriteLib heroLib = Assets.Class.getHeroLib(Cdb.Class.getSkinInfo(remoteSkin.AsHaxeString()));
            Texture normalMapFromGroup = heroLib.getNormalMapFromGroup(group);
            int? dp_ROOM_MAIN_HERO = Const.Class.DP_ROOM_MAIN_HERO;
            this.initSprite(heroLib, group, 0.5, 0.5, dp_ROOM_MAIN_HERO, true, null, normalMapFromGroup);
            this.initColorMap(Cdb.Class.getSkinInfo(remoteSkin.AsHaxeString()));

            // glow
            // ArrayObj glowData = CdbTypeConverter.Class.getGlowData(Cdb.Class.getSkinInfo(remoteSkin.AsHaxeString()));
            // if (glowData != null)
            // {
            //     GlowKey s2 = new GlowKey(glowData);
            //     if (s2 != null)
            //     {
            //         this.spr.addShader(s2);
            //     }
            // }

            ArrayObj glowData = CdbTypeConverter.Class.getGlowData(Cdb.Class.getSkinInfo(remoteSkin.AsHaxeString()));
            if (glowData != null && glowData.length > 0)
            {
                GlowKey glowKey = (GlowKey)this.spr.getShader(GlowKey.Class);
                if (glowKey == null)
                {
                    glowKey = new GlowKey(null);
                    this.spr.addShader(glowKey);
                }
                glowKey.setGlowDatas(glowData);
            }


            // Ambient light
            var General = 1.0;
            var radiusCase = 1.2 * General;
            var Math = dc.Math.Class.random() * 0.20000000000000007;
            General = 0.9 + Math;
            var decayStart = 5.0 * General;
            this.createLight(1161471, radiusCase, decayStart, 0.35);
        }

        public override void onActivate(Hero by, bool longPress)
        {
            base.onActivate(by, longPress);
        }

        private static Level GetFallbackLevel()
        {
            var hero = ModEntry.me ?? dc.pr.Game.Class.ME?.hero;
            if (hero?._level != null)
                return hero._level;

            var game = ModEntry.Instance?.game ?? dc.pr.Game.Class.ME;
            if (game != null)
            {
                var type = game.GetType();
                var levelValue = type.GetField("level", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase)?.GetValue(game)
                    ?? type.GetField("_level", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase)?.GetValue(game)
                    ?? type.GetProperty("level", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase)?.GetValue(game)
                    ?? type.GetProperty("_level", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase)?.GetValue(game);
                if (levelValue is Level lvl)
                    return lvl;
            }

            throw new InvalidOperationException("GhostKing deserialization requires a Level.");
        }

    }
}
