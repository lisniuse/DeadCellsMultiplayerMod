
using dc;
using dc.hl.types;
using Hashlink.Virtuals;
using dc.level;
using HaxeProxy.Runtime;
using dc.tool;
using System.Globalization;
using Serilog.Core;
// using Newtonsoft.Json;

namespace DeadCellsMultiplayerMod
{
    internal class GameDataSync
    {
        static Serilog.ILogger _log;
        static public int Seed;

        static public virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ _isTwitch;
        static public bool _isCustom;
        static public bool _mode;

        static public LaunchMode _launch;
        private static readonly object _bossRuneLock = new();
        private static int? _remoteBossRune;
        private static int? _hostBossRune;
        public GameDataSync(Serilog.ILogger log)
        {
            _log = log;
        }


        

        public static void user_hook_new_game(Hook_User.orig_newGame orig,
        User self,
        int lvl,
        virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ isTwitch,
        bool isCustom,
        bool mode,
        LaunchMode gdata)
        {
            isCustom = false;
            mode = false;

            Seed = lvl;
            ModEntry.me = null;
            ModEntry.ResetClientSlots();
            ModEntry.kingInitialized = false;
            ModEntry._ghost = null;
            var net = GameMenu.NetRef;
            
            if (net != null && net.IsHost)
            {
                Seed = GameMenu.ForceGenerateServerSeed("NewGame_hook");
                SendBossRune(self, net);
                net.SendSeed(Seed);
            }
            else if (net != null)
            {
                if (GameMenu.TryGetRemoteSeed(out var remoteSeed))
                {
                    Seed = remoteSeed;
                }
                if (TryGetRemoteBossRune(out var bossRune))
                {
                    self.bossRuneActivated = bossRune;
                }
                else
                {
                    _log?.Warning("[NetMod] Remote boss rune not received yet");
                }
            }
            lvl = Seed;
            _isTwitch = isTwitch;
            _isCustom = isCustom;
            _mode = mode;
            _launch = gdata;
            self.pickDeathItem();
            SendHeroSkin(self, net);
            SendHeroHeadSkin(self, net);
            orig(self, lvl, isTwitch, isCustom, mode, gdata);
        }

        public static ArrayObj hook_generate(Hook_LevelGen.orig_generate orig,
        LevelGen self,
        User seed,
        int ldat,
        virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ resetCount,
        Ref<bool> resetCount2)
        {
            // ldat = Seed;
            ModEntry.ResetClientSlots();
            // var net = GameMenu.NetRef;
            // var baseLootLevel = resetCount.baseLootLevel;
            // var biome = resetCount.biome.ToString();
            // var cellBonus = resetCount.cellBonus;
            // var doubleUps = resetCount.doubleUps;
            // var eliteRoomChance = resetCount.eliteRoomChance;
            // var eliteWanderChance = resetCount.eliteWanderChance;
            // var flagsProps = resetCount.flagsProps;
            // var group = resetCount.group;
            // var index = resetCount.index;
            // var loreDescriptions = resetCount.loreDescriptions;
            // var mapDepth = resetCount.mapDepth;
            // var minGold = resetCount.minGold;
            // var mobDensity = resetCount.mobDensity;
            // var mobs = resetCount.mobs;
            // var name = resetCount.name.ToString();
            // var nextLevels = resetCount.nextLevels;
            // var parallax = resetCount.parallax;
            // var props = resetCount.props;
            // var quarterUpsBC3 = resetCount.quarterUpsBC3;
            // var quarterUpsBC4 = resetCount.quarterUpsBC4;
            // var specificLoots = resetCount.specificLoots;
            // var specificSubBiome = resetCount.specificSubBiome;
            // var tripleUps = resetCount.tripleUps;
            // var worldDepth = resetCount.worldDepth;
            // var json = JsonConvert.SerializeObject(
            //     new
            //     {
            //         baseLootLevel,
            //         biome,
            //         cellBonus,
            //         doubleUps,
            //         eliteRoomChance,
            //         eliteWanderChance,
            //         flagsProps,
            //         group,
            //         index,
            //         loreDescriptions,
            //         mapDepth,
            //         minGold,
            //         mobDensity,
            //         mobs,
            //         name,
            //         nextLevels,
            //         parallax,
            //         props,
            //         quarterUpsBC3,
            //         quarterUpsBC4,
            //         specificLoots,
            //         specificSubBiome,
            //         tripleUps,
            //         worldDepth,
            //     });

            // _log.Debug(json);
            

            // SendHeroSkin(seed, net);
            return orig(self, seed, ldat, resetCount, resetCount2);
        }

        public static void SendBossRune(User self, NetNode? net)
        {
            if (self == null)
                return;

            var bossRune = ToInt(self.bossRuneActivated);
            lock (_bossRuneLock)
            {
                _hostBossRune = bossRune;
            }

            if (net == null || !net.IsAlive)
                return;

            net.SendBossRune(bossRune);
        }

        public static void ReceiveBossRune(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return;

            if (!int.TryParse(payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bossRune))
            {
                _log?.Warning("[NetMod] Failed to parse boss rune payload: {Payload}", payload);
                return;
            }

            lock (_bossRuneLock)
            {
                _remoteBossRune = bossRune;
            }

            _log?.Information("[NetMod] Received boss rune {BossRune}", bossRune);
        }

        public static bool TryGetHostBossRune(out int bossRune)
        {
            lock (_bossRuneLock)
            {
                if (_hostBossRune.HasValue)
                {
                    bossRune = _hostBossRune.Value;
                    return true;
                }
            }

            bossRune = 0;
            return false;
        }

        public static bool TryGetRemoteBossRune(out int bossRune)
        {
            lock (_bossRuneLock)
            {
                if (_remoteBossRune.HasValue)
                {
                    bossRune = _remoteBossRune.Value;
                    return true;
                }
            }

            bossRune = 0;
            return false;
        }

        public static void ReceiveHeroSkin(string skin)
        {
            try
            {
                var cleaned = CleanSkin(skin);
                if (string.IsNullOrWhiteSpace(cleaned))
                    cleaned = "PrisonerDefault";

                ModEntry.SetRemoteSkin(cleaned);
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] Failed to receive hero skin: {Message}", ex.Message);
            }
        }


        public static void ReceiveHeroHeadSkin(string skin)
        {
            try
            {
                var cleaned = CleanSkin(skin);
                if (string.IsNullOrWhiteSpace(cleaned))
                    cleaned = "BaseFlame";

                ModEntry.SetRemoteHeadSkin(cleaned);
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] Failed to receive hero skin: {Message}", ex.Message);
            }
        }

        private static void SendHeroSkin(User user, NetNode? net)
        {
            if (net == null || !net.IsAlive)
                return;

            try
            {
                var skin = CleanSkin(user?.heroSkin?.ToString());
                if (string.IsNullOrWhiteSpace(skin))
                    skin = "PrisonerDefault";

                net.SendHeroSkin(skin);
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] Failed to send hero skin: {Message}", ex.Message);
            }
        }


        private static void SendHeroHeadSkin(User user, NetNode? net)
        {
            if (net == null || !net.IsAlive)
                return;

            try
            {
                var skin = CleanSkin(user?.heroHeadSkin?.ToString());
                if (string.IsNullOrWhiteSpace(skin))
                    skin = "BaseFlame";

                net.SendHeroHeadSkin(skin);
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] Failed to send hero skin: {Message}", ex.Message);
            }
        }

        private static string CleanSkin(string? skin)
        {
            if (string.IsNullOrEmpty(skin))
                return string.Empty;

            return skin.Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        }

        private static int ToInt(object? value)
        {
            if (value == null)
                return 0;

            if (value is int i)
                return i;

            if (value is bool b)
                return b ? 1 : 0;

            if (value is IConvertible conv)
            {
                try
                {
                    return conv.ToInt32(CultureInfo.InvariantCulture);
                }
                catch { }
            }

            return 0;
        }

    }
}
