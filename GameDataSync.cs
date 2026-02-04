
using dc;
using dc.haxe.ds;
using dc.pr;
using dc.tool;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using System;
using System.Globalization;
using System.Text;
using dc.haxe;
using dc.hl.types;
using Rand = dc.libs.Rand;

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

        private static string? _remoteCountersPayload;
        public static string? HostCountersPayload;
        private static string? _remoteBlueprintsPayload;
        public static string? HostBlueprintsPayload;
        private static bool _hasRemoteCounters;
        private static bool _hasRemoteBlueprints;
        private static bool _origStoryCaptured;
        private static StoryManager? _origStory;
        private static StringMap? _origCounters;
        private static bool _origItemMetaCaptured;
        private static ItemMetaManager? _origItemMeta;
        private static ArrayObj? _origItemProgress;
        private static ArrayObj? _origPermanentItems;
        private static bool _origItemMetaWasNull;
        private static bool _origBossRuneCaptured;
        private static int _origBossRune;
        private static bool _hasRemoteBossRune;
        private static bool _suppressDeathBroadcast;
        private static readonly object _levelSeedLock = new();
        private static string? _remoteLevelId;
        private static double? _remoteLevelSeed;
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

            if (net == null || !net.IsAlive)
                RestoreOriginalUserState(self, true);

            if (net != null && net.IsHost)
            {
                Seed = GameMenu.ForceGenerateServerSeed("NewGame_hook");
                SendBossRune(self, net);
                net.SendSeed(Seed);
            }
            else if (net != null)
            {
                if (!string.IsNullOrEmpty(_remoteCountersPayload))
                    ReceiveCounters(_remoteCountersPayload, self);
                if (!string.IsNullOrEmpty(_remoteBlueprintsPayload))
                    ReceiveBlueprints(_remoteBlueprintsPayload, self);
                if (GameMenu.TryGetRemoteSeed(out var remoteSeed))
                {
                    Seed = remoteSeed;
                }
                if (TryGetRemoteBossRune(out var bossRune))
                {
                    _origBossRune = self.bossRuneActivated;
                    _origBossRuneCaptured = true;
                    self.bossRuneActivated = bossRune;
                    _hasRemoteBossRune = true;
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
            SendCounters(self, net);
            SendBlueprints(self, net);
            orig(self, lvl, isTwitch, isCustom, mode, gdata);
        }

        public static void ReceiveBlueprints(string payload, User? target = null)
        {
            _remoteBlueprintsPayload = payload;
            if (string.IsNullOrEmpty(payload))
                return;

            void apply(User user)
            {
                CaptureOriginalUserData(user);
                var meta = user.itemMeta ?? new ItemMetaManager(user);
                var list = meta.itemProgress;
                var arr = list ?? (ArrayObj)ArrayUtils.CreateDyn().array;
                var existing = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < arr.length; i++)
                {
                    var progress = arr.getDyn(i) as ItemProgress;
                    var id = progress?.itemId?.ToString();
                    if (!string.IsNullOrWhiteSpace(id))
                        existing.Add(id);
                }
                _hasRemoteBlueprints = true;

                var item = new StringBuilder();
                var escaped = false;
                for (var i = 0; i < payload.Length; i++)
                {
                    var c = payload[i];
                    if (escaped)
                    {
                        item.Append(c);
                        escaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (c == '|')
                    {
                        if (item.Length > 0)
                        {
                            var text = item.ToString();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                if (!existing.Contains(text))
                                {
                                    var progress = new ItemProgress(text.AsHaxeString());
                                    progress.unlocked = true;
                                    arr.pushDyn(progress);
                                    existing.Add(text);
                                }
                            }
                            item.Clear();
                        }
                        continue;
                    }

                    item.Append(c);
                }

                if (item.Length > 0)
                {
                    var text = item.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        if (!existing.Contains(text))
                        {
                            var progress = new ItemProgress(text.AsHaxeString());
                            progress.unlocked = true;
                            arr.pushDyn(progress);
                            existing.Add(text);
                        }
                    }
                }

                meta.itemProgress = arr;
                user.itemMeta = meta;
            }

            if (target != null)
            {
                apply(target);
                return;
            }

            GameMenu.EnqueueMainThread(() =>
            {
                try
                {
                    var main = dc.Main.Class.ME;
                    if (main?.user != null)
                        apply(main.user);
                }
                catch
                {
                }
            });
        }

        public static void SendBlueprints(User user, NetNode? net)
        {
            if (user == null)
                return;

            var meta = user.itemMeta;
            var list = meta?.itemProgress;
            var builder = new StringBuilder();
            if (list != null)
            {
                var first = true;
                for (int i = 0; i < list.length; i++)
                {
                    var progress = list.getDyn(i) as ItemProgress;
                    if (progress == null || !progress.unlocked)
                        continue;
                    var text = progress.itemId?.ToString();
                    if (string.IsNullOrWhiteSpace(text))
                        continue;
                    if (!first)
                        builder.Append('|');
                    builder.Append(text.Replace("\\", "\\\\").Replace("|", "\\|"));
                    first = false;
                }
            }

            var payload = builder.ToString();
            HostBlueprintsPayload = payload;
            if (net != null && net.IsAlive)
                net.SendBlueprints(payload);
        }

        public static bool SwapToOriginalUserData(User user)
        {
            var swapped = false;
            if (_hasRemoteCounters && _origStoryCaptured)
            {
                if (_origStory == null)
                {
                    user.story = null;
                }
                else
                {
                    _origStory.counters = _origCounters;
                    user.story = _origStory;
                }
                swapped = true;
            }

            if (_hasRemoteBlueprints && _origItemMetaCaptured)
            {
                if (_origItemMetaWasNull)
                {
                    user.itemMeta = null;
                }
                else
                {
                    var meta = _origItemMeta ?? user.itemMeta ?? new ItemMetaManager(user);
                    meta.itemProgress = _origItemProgress;
                    meta.permanentItems = _origPermanentItems;
                    user.itemMeta = meta;
                }
                swapped = true;
            }

            if (_hasRemoteBossRune && _origBossRuneCaptured)
            {
                user.bossRuneActivated = _origBossRune;
                swapped = true;
            }

            return swapped;
        }

        public static bool RestoreOriginalUserState(User user, bool clearRemote)
        {
            var restored = false;
            if (_origStoryCaptured)
            {
                if (_origStory == null)
                {
                    user.story = null;
                }
                else
                {
                    _origStory.counters = _origCounters;
                    user.story = _origStory;
                }
                restored = true;
            }

            if (_origItemMetaCaptured)
            {
                if (_origItemMetaWasNull)
                {
                    user.itemMeta = null;
                }
                else
                {
                    var meta = _origItemMeta ?? user.itemMeta ?? new ItemMetaManager(user);
                    meta.itemProgress = _origItemProgress;
                    meta.permanentItems = _origPermanentItems;
                    user.itemMeta = meta;
                }
                restored = true;
            }

            if (_origBossRuneCaptured)
            {
                user.bossRuneActivated = _origBossRune;
                restored = true;
            }

            if (clearRemote)
            {
                _remoteCountersPayload = null;
                _remoteBlueprintsPayload = null;
                _hasRemoteCounters = false;
                _hasRemoteBlueprints = false;
                _hasRemoteBossRune = false;
                _origStoryCaptured = false;
                _origStory = null;
                _origCounters = null;
                _origItemMetaCaptured = false;
                _origItemMeta = null;
                _origItemProgress = null;
                _origPermanentItems = null;
                _origItemMetaWasNull = false;
                _origBossRuneCaptured = false;
                _origBossRune = 0;
            }

            return restored;
        }

        public static void CaptureOriginalUserData(User user)
        {
            if (!_origStoryCaptured)
            {
                _origStoryCaptured = true;
                _origStory = user.story;
                _origCounters = user.story?.counters;
            }

            if (!_origItemMetaCaptured)
            {
                var meta = user.itemMeta;
                _origItemMetaCaptured = true;
                _origItemMeta = meta;
                _origItemMetaWasNull = meta == null;
                _origItemProgress = CloneItemProgress(meta?.itemProgress);
                _origPermanentItems = CloneItemList(meta?.permanentItems);
            }

            if (!_origBossRuneCaptured)
            {
                _origBossRuneCaptured = true;
                _origBossRune = user.bossRuneActivated;
            }
        }

        public static void RestoreRemoteUserData(User user)
        {
            if (!string.IsNullOrEmpty(_remoteCountersPayload))
                ReceiveCounters(_remoteCountersPayload, user);
            if (!string.IsNullOrEmpty(_remoteBlueprintsPayload))
                ReceiveBlueprints(_remoteBlueprintsPayload, user);
            if (_hasRemoteBossRune && TryGetRemoteBossRune(out var bossRune))
                user.bossRuneActivated = bossRune;
        }

        public static void TriggerRemoteDeath()
        {
            _suppressDeathBroadcast = true;
            GameMenu.EnqueueMainThread(() =>
            {
                try
                {
                    ModEntry.me?.kill();
                }
                catch
                {
                }
            });
        }

        public static bool ConsumeSuppressDeathBroadcast()
        {
            if (!_suppressDeathBroadcast)
                return false;
            _suppressDeathBroadcast = false;
            return true;
        }

        public static void ReceiveLevelSeed(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return;

            var sep = payload.IndexOf('|');
            if (sep <= 0 || sep >= payload.Length - 1)
                return;

            var levelId = payload[..sep];
            var seedText = payload[(sep + 1)..];
            if (string.IsNullOrWhiteSpace(levelId))
                return;

            if (!double.TryParse(seedText, NumberStyles.Float, CultureInfo.InvariantCulture, out var seed))
                return;

            lock (_levelSeedLock)
            {
                _remoteLevelId = levelId;
                _remoteLevelSeed = seed;
            }
        }

        public static bool TryApplyRemoteLevelSeed(string levelId, Rand rng)
        {
            if (rng == null || string.IsNullOrWhiteSpace(levelId))
                return false;

            lock (_levelSeedLock)
            {
                if (_remoteLevelSeed.HasValue && string.Equals(_remoteLevelId, levelId, StringComparison.Ordinal))
                {
                    rng.seed = _remoteLevelSeed.Value;
                    return true;
                }
            }

            return false;
        }

        public static void SendLevelSeed(string levelId, Rand rng, NetNode? net)
        {
            if (net == null || !net.IsAlive || rng == null || string.IsNullOrWhiteSpace(levelId))
                return;

            net.SendLevelSeed(levelId, rng.seed);
        }

        public static void ReceiveCounters(string payload, User? target = null)
        {
            _remoteCountersPayload = payload;
            if (string.IsNullOrEmpty(payload))
                return;

            void apply(User user)
            {
                CaptureOriginalUserData(user);
                var story = user.story ?? new StoryManager();
                var map = new StringMap();
                var key = new StringBuilder();
                var value = new StringBuilder();
                var inKey = true;
                var escaped = false;

                for (var i = 0; i < payload.Length; i++)
                {
                    var c = payload[i];
                    if (escaped)
                    {
                        if (inKey)
                            key.Append(c);
                        else
                            value.Append(c);
                        escaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (inKey && c == '=')
                    {
                        inKey = false;
                        continue;
                    }

                    if (!inKey && c == '|')
                    {
                        if (key.Length > 0)
                        {
                            var keyText = key.ToString();
                            var valueText = value.ToString();
                            if (!int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                                parsed = 0;
                            map.set(keyText.AsHaxeString(), parsed);
                        }
                        key.Clear();
                        value.Clear();
                        inKey = true;
                        continue;
                    }

                    if (inKey)
                        key.Append(c);
                    else
                        value.Append(c);
                }

                if (key.Length > 0)
                {
                    var keyText = key.ToString();
                    var valueText = value.ToString();
                    if (!int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                        parsed = 0;
                    map.set(keyText.AsHaxeString(), parsed);
                }

                story.counters = map;
                user.story = story;
                _hasRemoteCounters = true;
            }

            if (target != null)
            {
                apply(target);
                return;
            }

            GameMenu.EnqueueMainThread(() =>
            {
                try
                {
                    var main = dc.Main.Class.ME;
                    if (main?.user != null)
                        apply(main.user);
                }
                catch
                {
                }
            });
        }

        private static void SendCounters(User user, NetNode? net)
        {
            if (user == null)
                return;

            var map = user.story?.counters;
            var builder = new StringBuilder();
            if (map != null)
            {
                var keys = map.keys();
                var first = true;
                while (keys.hasNext.Invoke())
                {
                    var key = keys.next.Invoke();
                    if (key == null)
                        continue;
                    var keyText = key.ToString();
                    if (string.IsNullOrWhiteSpace(keyText))
                        continue;
                    var value = ToInt(map.get(key));
                    if (!first)
                        builder.Append('|');
                    builder.Append(keyText.Replace("\\", "\\\\").Replace("|", "\\|").Replace("=", "\\="));
                    builder.Append('=');
                    builder.Append(value.ToString(CultureInfo.InvariantCulture));
                    first = false;
                }
            }

            var payload = builder.ToString();
            HostCountersPayload = payload;
            if (net != null && net.IsAlive)
                net.SendCounters(payload);
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
                return;
            }

            lock (_bossRuneLock)
            {
                _remoteBossRune = bossRune;
            }

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
            
            var cleaned = CleanSkin(skin);
            if (string.IsNullOrWhiteSpace(cleaned))
                cleaned = "PrisonerDefault";

            ModEntry.SetRemoteSkin(cleaned);
            
        }


        public static void ReceiveHeroHeadSkin(string skin)
        {
            var cleaned = CleanSkin(skin);
            if (string.IsNullOrWhiteSpace(cleaned))
                cleaned = "BaseFlame";

            ModEntry.SetRemoteHeadSkin(cleaned);
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

        private static ArrayObj? CloneItemProgress(ArrayObj? source)
        {
            if (source == null)
                return null;

            var arr = ArrayUtils.CreateDyn();
            for (int i = 0; i < source.length; i++)
            {
                var item = source.getDyn(i) as ItemProgress;
                if (item == null)
                    continue;
                var copy = new ItemProgress(item.itemId);
                copy.investedCells = item.investedCells;
                copy.isNew = item.isNew;
                copy.unlocked = item.unlocked;
                copy.__uid = item.__uid;
                arr.array.pushDyn(copy);
            }
            return (ArrayObj)arr.array;
        }

        private static ArrayObj? CloneItemList(ArrayObj? source)
        {
            if (source == null)
                return null;

            var arr = ArrayUtils.CreateDyn();
            for (int i = 0; i < source.length; i++)
            {
                var item = source.getDyn(i);
                if (item == null)
                    continue;
                arr.array.pushDyn(item);
            }
            return (ArrayObj)arr.array;
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
