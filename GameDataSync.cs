
using dc;
using dc.haxe.ds;
using dc.level;
using dc.pr;
using dc.tool;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using dc.haxe;
using dc.hl.types;
using Rand = dc.libs.Rand;

namespace DeadCellsMultiplayerMod
{
    internal partial class GameDataSync
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
        private static readonly object _serializerSyncLock = new();
        private static int _remoteSerializerSeq;
        private static int _remoteSerializerUid;
        private static bool _hasRemoteSerializerSync;

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
                SendSerializerSync(net);
                net.SendSeed(Seed);
            }
            else if (net != null)
            {
                TryApplyRemoteSerializerSync();
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
                    ApplyRemoteBossRune(self, bossRune);
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
                var meta = EnsureItemMeta(user, user.itemMeta);
                var arr = CloneItemProgress(meta.itemProgress) ?? EnsureArray(null);
                var existing = new Dictionary<string, ItemProgress>(StringComparer.Ordinal);
                for (int i = 0; i < arr.length; i++)
                {
                    var progress = arr.getDyn(i) as ItemProgress;
                    var id = progress?.itemId?.ToString();
                    if (progress != null && !string.IsNullOrWhiteSpace(id))
                        existing[id] = progress;
                }
                var permanent = CloneItemList(meta.permanentItems) ?? EnsureArray(null);
                var existingPermanent = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < permanent.length; i++)
                {
                    var permanentId = permanent.getDyn(i)?.ToString();
                    if (!string.IsNullOrWhiteSpace(permanentId))
                        existingPermanent.Add(permanentId);
                }
                _hasRemoteBlueprints = true;

                var isV2 = payload.Equals("V2", StringComparison.Ordinal) || payload.StartsWith("V2|", StringComparison.Ordinal);
                ForEachEscapedToken(payload, token =>
                {
                    if (string.IsNullOrWhiteSpace(token))
                        return;

                    if (isV2)
                    {
                        if (token.Equals("V2", StringComparison.Ordinal))
                            return;

                        if (token.StartsWith("I:", StringComparison.Ordinal))
                        {
                            var parts = token.Split(':');
                            if (parts.Length < 5)
                                return;

                            var itemId = DecodeToken(parts[1]);
                            if (string.IsNullOrWhiteSpace(itemId))
                                return;

                            if (!existing.TryGetValue(itemId, out var progress) || progress == null)
                            {
                                progress = new ItemProgress(itemId.AsHaxeString());
                                arr.pushDyn(progress);
                                existing[itemId] = progress;
                            }

                            progress.investedCells = ParseInt(parts[2], ToInt(progress.investedCells));
                            progress.unlocked = ParseBool(parts[3], progress.unlocked);
                            progress.isNew = ParseBool(parts[4], progress.isNew);
                            return;
                        }

                        if (token.StartsWith("P:", StringComparison.Ordinal))
                        {
                            var permanentId = DecodeToken(token[2..]);
                            if (string.IsNullOrWhiteSpace(permanentId))
                                return;
                            if (existingPermanent.Add(permanentId))
                                permanent.pushDyn(permanentId.AsHaxeString());
                        }
                        return;
                    }

                    var text = token;
                    if (!string.IsNullOrWhiteSpace(text) && !existing.ContainsKey(text))
                    {
                        var progress = new ItemProgress(text.AsHaxeString());
                        progress.unlocked = true;
                        arr.pushDyn(progress);
                        existing[text] = progress;
                    }
                });

                meta.itemProgress = arr;
                meta.permanentItems = permanent;
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
            var builder = new StringBuilder();
            builder.Append("V2");

            var list = meta?.itemProgress;
            if (list != null)
            {
                for (int i = 0; i < list.length; i++)
                {
                    var progress = list.getDyn(i) as ItemProgress;
                    if (progress == null)
                        continue;

                    var text = progress.itemId?.ToString();
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    builder.Append("|I:");
                    builder.Append(EncodeToken(text));
                    builder.Append(':');
                    builder.Append(ToInt(progress.investedCells).ToString(CultureInfo.InvariantCulture));
                    builder.Append(':');
                    builder.Append(progress.unlocked ? "1" : "0");
                    builder.Append(':');
                    builder.Append(progress.isNew ? "1" : "0");
                }
            }

            var permanentItems = meta?.permanentItems;
            if (permanentItems != null)
            {
                for (int i = 0; i < permanentItems.length; i++)
                {
                    var item = permanentItems.getDyn(i);
                    var text = item?.ToString();
                    if (string.IsNullOrWhiteSpace(text))
                        continue;
                    builder.Append("|P:");
                    builder.Append(EncodeToken(text));
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
                    var meta = EnsureItemMeta(user, _origItemMeta ?? user.itemMeta);
                    meta.itemProgress = EnsureArray(_origItemProgress);
                    meta.permanentItems = EnsureArray(_origPermanentItems);
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
                    var meta = EnsureItemMeta(user, _origItemMeta ?? user.itemMeta);
                    meta.itemProgress = EnsureArray(_origItemProgress);
                    meta.permanentItems = EnsureArray(_origPermanentItems);
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
                lock (_bossRuneLock)
                {
                    _remoteBossRune = null;
                }
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
                if (meta != null)
                    meta = EnsureItemMeta(user, meta);
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
            if (TryGetRemoteBossRune(out var bossRune))
                ApplyRemoteBossRune(user, bossRune);
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

            SendSerializerSync(net);
            net.SendLevelSeed(levelId, rng.seed);
        }

        public static void ReceiveSerializerSync(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return;

            var parts = payload.Split('|');
            if (parts.Length < 2)
                return;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq))
                return;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid))
                return;

            lock (_serializerSyncLock)
            {
                _remoteSerializerSeq = seq;
                _remoteSerializerUid = uid;
                _hasRemoteSerializerSync = true;
            }
        }

        public static bool TryApplyRemoteSerializerSync()
        {
            int seq;
            int uid;
            lock (_serializerSyncLock)
            {
                if (!_hasRemoteSerializerSync)
                    return false;

                seq = _remoteSerializerSeq;
                uid = _remoteSerializerUid;
                _hasRemoteSerializerSync = false;
            }

            try
            {
                var serializerClass = dc.hxbit.Serializer.Class;
                if (serializerClass == null)
                    return false;

                serializerClass.SEQ = seq;
                serializerClass.UID = uid;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void SendSerializerSync(NetNode? net)
        {
            if (net == null || !net.IsAlive || !net.IsHost)
                return;

            try
            {
                var serializerClass = dc.hxbit.Serializer.Class;
                if (serializerClass == null)
                    return;

                net.SendSerializerSync(serializerClass.SEQ, serializerClass.UID);
            }
            catch
            {
            }
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
            _hasRemoteBossRune = true;

            var net = GameMenu.NetRef;
            if (net != null && net.IsHost)
                return;

            GameMenu.EnqueueMainThread(() =>
            {
                try
                {
                    var user = dc.Main.Class.ME?.user;
                    if (user != null)
                        ApplyRemoteBossRune(user, bossRune);
                }
                catch
                {
                }
            });

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

        private static void ApplyRemoteBossRune(User user, int bossRune)
        {
            CaptureOriginalUserData(user);
            user.bossRuneActivated = bossRune;
            _hasRemoteBossRune = true;
        }

        private static void ForEachEscapedToken(string payload, Action<string> onToken)
        {
            if (string.IsNullOrEmpty(payload))
                return;

            var token = new StringBuilder();
            var escaped = false;
            for (var i = 0; i < payload.Length; i++)
            {
                var c = payload[i];
                if (escaped)
                {
                    token.Append(c);
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
                    onToken(token.ToString());
                    token.Clear();
                    continue;
                }

                token.Append(c);
            }

            if (escaped)
                token.Append('\\');
            onToken(token.ToString());
        }

        private static string EncodeToken(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static string? DecodeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch
            {
                return null;
            }
        }

        private static int ParseInt(string value, int fallback)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return fallback;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            if (value == "1")
                return true;
            if (value == "0")
                return false;
            if (bool.TryParse(value, out var parsed))
                return parsed;
            return fallback;
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

        private static ArrayObj EnsureArray(ArrayObj? source)
        {
            if (source != null)
                return source;
            return (ArrayObj)ArrayUtils.CreateDyn().array;
        }

        private static ItemMetaManager EnsureItemMeta(User user, ItemMetaManager? meta)
        {
            var result = meta ?? user.itemMeta ?? new ItemMetaManager(user);
            result.itemProgress = EnsureArray(result.itemProgress);
            result.permanentItems = EnsureArray(result.permanentItems);
            return result;
        }

        private static LevelGraphSync? CaptureLevelGraph(string levelId, LevelStruct graph)
        {
            var sync = new LevelGraphSync
            {
                V = 1,
                LevelId = levelId,
                ZLinkId = graph.zLinkId
            };

            var seenUids = new HashSet<string>(StringComparer.Ordinal);
            var all = graph.all;
            if (all != null)
            {
                for (int i = 0; i < all.length; i++)
                {
                    TryCaptureLevelGraphNode(all.getDyn(i), sync, seenUids);
                }
            }

            if (sync.Nodes.Count == 0 && graph.nodes != null)
            {
                try
                {
                    var keys = graph.nodes.keys();
                    while (keys.hasNext.Invoke())
                    {
                        var key = keys.next.Invoke();
                        if (key == null)
                            continue;
                        TryCaptureLevelGraphNode(graph.nodes.get(key), sync, seenUids);
                    }
                }
                catch
                {
                }
            }

            return sync;
        }

        private static void TryCaptureLevelGraphNode(object? candidate, LevelGraphSync sync, HashSet<string> seenUids)
        {
            if (candidate is not RoomNode node)
                return;

            var nodeSync = CaptureLevelGraphNode(node);
            if (nodeSync == null || string.IsNullOrWhiteSpace(nodeSync.Uid))
                return;

            if (!seenUids.Add(nodeSync.Uid))
                return;

            sync.Nodes.Add(nodeSync);
        }

        private static LevelGraphNodeSync? CaptureLevelGraphNode(RoomNode node)
        {
            var uid = node.uid?.ToString();
            var rType = node.rType?.ToString();
            if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(rType))
                return null;

            int? parentLinkConstraint = null;
            try
            {
                if (node.parentLinkConstraint is HaxeEnum plc)
                    parentLinkConstraint = plc.RawIndex;
            }
            catch
            {
            }

            return new LevelGraphNodeSync
            {
                Uid = uid,
                ParentUid = node.parent?.uid?.ToString(),
                SubTeleportUid = node.subTeleportTo?.uid?.ToString(),
                IsZRoot = node.isZRoot,
                RType = rType,
                Group = node.group,
                Id = node.id,
                Flags = node.flags,
                ForcedTemplateId = node.forcedTemplate?.id?.ToString(),
                ExitLevel = node.exitLevel?.ToString(),
                ExitName = node.exitName?.ToString(),
                ExitColor = node.exitColor,
                ChildPriority = node.childPriority,
                X = node.x,
                Y = node.y,
                SpawnDistance = node.spawnDistance,
                FillerWeight = node.fillerWeight,
                ParentLinkConstraint = parentLinkConstraint,
                ChildrenUids = CaptureRoomNodeUids(node.children),
                ZChildrenUids = CaptureRoomNodeUids(node.zChildren),
                Npcs = CaptureNpcIds(node.npcs),
                ZLinks = CaptureZLinks(node.zLinks),
                GenData = CaptureLevelGraphGenData(node.genData)
            };
        }

        private static List<string> CaptureRoomNodeUids(ArrayObj? nodes)
        {
            var result = new List<string>();
            if (nodes == null)
                return result;

            for (int i = 0; i < nodes.length; i++)
            {
                try
                {
                    if (nodes.getDyn(i) is not RoomNode node)
                        continue;

                    var uid = node.uid?.ToString();
                    if (!string.IsNullOrEmpty(uid))
                        result.Add(uid);
                }
                catch
                {
                }
            }

            return result;
        }

        private static List<int> CaptureNpcIds(ArrayObj? npcs)
        {
            var result = new List<int>();
            if (npcs == null)
                return result;

            for (int i = 0; i < npcs.length; i++)
            {
                try
                {
                    if (npcs.getDyn(i) is NpcId npcId)
                        result.Add((int)npcId.Index);
                }
                catch
                {
                }
            }

            return result;
        }

        private static List<LevelGraphZLinkSync> CaptureZLinks(ArrayObj? zLinks)
        {
            var result = new List<LevelGraphZLinkSync>();
            if (zLinks == null)
                return result;

            for (int i = 0; i < zLinks.length; i++)
            {
                try
                {
                    var link = zLinks.getDyn(i) as virtual_contentClue_dest_doorId_id_;
                    if (link == null)
                        continue;

                    var destUid = link.dest?.uid?.ToString();
                    if (string.IsNullOrWhiteSpace(destUid))
                        continue;

                    int? clue = null;
                    try
                    {
                        var contentClue = link.contentClue;
                    if (contentClue is HaxeEnum haxeEnum)
                        clue = haxeEnum.RawIndex;
                    }
                    catch
                    {
                    }

                    result.Add(new LevelGraphZLinkSync
                    {
                        Id = link.id,
                        DestUid = destUid,
                        DoorId = link.doorId?.ToString(),
                        ContentClue = clue
                    });
                }
                catch
                {
                }
            }

            return result;
        }

        private static LevelGraphGenDataSync? CaptureLevelGraphGenData(virtual_altarItemGroup_brLegendaryMultiTreasure_broken_cells_doorCost_doorCurse_flaskRefill_forcedMerchantType_forcePauseTimer_isCliffPath_itemInWall_itemLevelBonus_killsMultiTreasure_locked_maxPerks_mins_noHealingShop_shouldBeFlipped_specificBiome_subTeleportTo_timedMultiTreasure_zDoorLock_zDoorType_? genData)
        {
            if (genData == null)
                return null;

            var result = new LevelGraphGenDataSync();
            var hasAny = false;

            try
            {
                var v = genData.specificBiome?.ToString();
                if (!string.IsNullOrWhiteSpace(v))
                {
                    result.SpecificBiome = v;
                    hasAny = true;
                }
            }
            catch { }

            try
            {
                var v = genData.zDoorLock;
                if (v.HasValue)
                {
                    result.ZDoorLock = v;
                    hasAny = true;
                }
            }
            catch { }

            try
            {
                var v = genData.forcePauseTimer;
                if (v.HasValue)
                {
                    result.ForcePauseTimer = v;
                    hasAny = true;
                }
            }
            catch { }

            try
            {
                var v = genData.shouldBeFlipped;
                if (v.HasValue)
                {
                    result.ShouldBeFlipped = v;
                    hasAny = true;
                }
            }
            catch { }

            try
            {
                var v = genData.subTeleportTo;
                if (v.HasValue)
                {
                    result.GenSubTeleportTo = v;
                    hasAny = true;
                }
            }
            catch { }

            try
            {
                var zDoorType = CaptureZDoorType(genData.zDoorType);
                if (zDoorType != null)
                {
                    result.ZDoorType = zDoorType;
                    hasAny = true;
                }
            }
            catch { }

            return hasAny ? result : null;
        }

        private static LevelGraphZDoorTypeSync? CaptureZDoorType(ZDoorType? zDoorType)
        {
            if (zDoorType is null)
                return null;

            if (zDoorType is not HaxeEnum haxeEnum)
                return null;

            var result = new LevelGraphZDoorTypeSync
            {
                RawIndex = haxeEnum.RawIndex
            };

            switch (zDoorType)
            {
                case ZDoorType.BossRune bossRune:
                    result.IntParam0 = bossRune.Param0;
                    break;
                case ZDoorType.PerfectKills perfectKills:
                    result.IntParam0 = perfectKills.Param0;
                    break;
                case ZDoorType.Timed timed:
                    result.DoubleParam0 = timed.Param0;
                    break;
            }

            return result;
        }

        private static void ApplyLevelGraphGenData(RoomNode node, LevelGraphGenDataSync? genData)
        {
            if (node == null || genData == null)
                return;

            try
            {
                var dst = node.genData;
                if (dst == null)
                    return;

                var reflect = dc._Reflect.Class as dc._Reflect;
                if (reflect == null)
                    return;

                void SetIfPresent(string fieldName, object? value)
                {
                    if (value == null)
                        return;

                    try
                    {
                        var hxField = fieldName.AsHaxeString();
                        if (!reflect.hasField.Invoke(dst, hxField))
                            return;
                        reflect.setField.Invoke(dst, hxField, value);
                    }
                    catch
                    {
                    }
                }

                if (genData.SpecificBiome != null)
                    SetIfPresent("specificBiome", genData.SpecificBiome.AsHaxeString());
                if (genData.ZDoorLock.HasValue)
                    SetIfPresent("zDoorLock", genData.ZDoorLock.Value);
                if (genData.ForcePauseTimer.HasValue)
                    SetIfPresent("forcePauseTimer", genData.ForcePauseTimer.Value);
                if (genData.ShouldBeFlipped.HasValue)
                    SetIfPresent("shouldBeFlipped", genData.ShouldBeFlipped.Value);
                if (genData.GenSubTeleportTo.HasValue)
                    SetIfPresent("subTeleportTo", genData.GenSubTeleportTo.Value);
                if (genData.ZDoorType != null)
                {
                    var zDoorType = CreateZDoorTypeFromSync(genData.ZDoorType);
                    if (zDoorType is not null)
                        SetIfPresent("zDoorType", zDoorType);
                }
            }
            catch
            {
            }
        }

        private static bool ApplyLevelGraph(LevelStruct target, LevelGraphSync sync, out RoomNode? rebuiltRoot, out string reason)
        {
            rebuiltRoot = null;
            reason = string.Empty;
            if (sync.Nodes == null || sync.Nodes.Count == 0)
            {
                reason = "no nodes";
                return false;
            }

            try
            {
                var localNodesByUid = CaptureExistingRoomNodesByUid(target);

                target.nodes = new StringMap();
                target.all = (ArrayObj)ArrayUtils.CreateDyn().array;
                target.zLinkId = sync.ZLinkId;

                var byUid = new Dictionary<string, RoomNode>(StringComparer.Ordinal);
                var syncByUid = new Dictionary<string, LevelGraphNodeSync>(StringComparer.Ordinal);
                var orderedNodes = new List<RoomNode>(sync.Nodes.Count);

                for (int i = 0; i < sync.Nodes.Count; i++)
                {
                    var src = sync.Nodes[i];
                    if (src == null || string.IsNullOrWhiteSpace(src.Uid) || string.IsNullOrWhiteSpace(src.RType))
                        continue;

                    if (byUid.ContainsKey(src.Uid))
                        continue;

                    var ctorGroup = src.Group;
                    var node = new RoomNode(src.RType.AsHaxeString(), Ref<int>.From(ref ctorGroup), target, null);
                    node.uid = src.Uid.AsHaxeString();
                    node.rType = src.RType.AsHaxeString();
                    node.group = src.Group;
                    node.id = src.Id;
                    node.flags = src.Flags;
                    node.childPriority = src.ChildPriority;
                    node.x = src.X;
                    node.y = src.Y;
                    node.spawnDistance = src.SpawnDistance;
                    node.fillerWeight = src.FillerWeight;
                    node.exitLevel = string.IsNullOrWhiteSpace(src.ExitLevel) ? null : src.ExitLevel.AsHaxeString();
                    node.exitName = string.IsNullOrWhiteSpace(src.ExitName) ? null : src.ExitName.AsHaxeString();
                    node.exitColor = src.ExitColor;

                    if (!string.IsNullOrWhiteSpace(src.ForcedTemplateId))
                    {
                        try
                        {
                            node.forceTemplate(src.ForcedTemplateId.AsHaxeString());
                        }
                        catch
                        {
                            try
                            {
                                node.forcedTemplate = (virtual_active_flags_group_id_type_)(object)dc.Data.Class.room.byId.get(src.ForcedTemplateId.AsHaxeString());
                            }
                            catch
                            {
                            }
                        }

                        // Keep payload fields authoritative even if native forceTemplate mutates them.
                        node.rType = src.RType.AsHaxeString();
                        node.group = src.Group;
                    }

                    if (src.ParentLinkConstraint.HasValue)
                    {
                        var constraint = CreateLinkConstraintFromIndex(src.ParentLinkConstraint.Value);
                        if (constraint is not null)
                            node.parentLinkConstraint = constraint;
                    }

                    if (src.Npcs != null)
                    {
                        for (int n = 0; n < src.Npcs.Count; n++)
                        {
                            var npc = CreateNpcIdFromIndex(src.Npcs[n]);
                            if (npc is not null)
                                node.npcs.pushDyn(npc);
                        }
                    }

                    if (localNodesByUid.TryGetValue(src.Uid, out var localNode))
                    {
                        try
                        {
                            if (localNode.genData != null)
                                node.genData = localNode.genData;
                        }
                        catch
                        {
                        }
                    }

                    ApplyLevelGraphGenData(node, src.GenData);

                    orderedNodes.Add(node);
                    byUid[src.Uid] = node;
                    syncByUid[src.Uid] = src;
                }

                // Populate struct lookup early so RoomNode.addZChild() can resolve @struct.getId(uid).
                var earlyAll = ArrayUtils.CreateDyn();
                var earlyNodes = new StringMap();
                for (int i = 0; i < sync.Nodes.Count; i++)
                {
                    var src = sync.Nodes[i];
                    if (src == null || string.IsNullOrWhiteSpace(src.Uid))
                        continue;
                    if (!byUid.TryGetValue(src.Uid, out var node))
                        continue;
                    earlyAll.array.pushDyn(node);
                    earlyNodes.set(src.Uid.AsHaxeString(), node);
                }
                target.all = (ArrayObj)earlyAll.array;
                target.nodes = earlyNodes;

                for (int i = 0; i < sync.Nodes.Count; i++)
                {
                    var src = sync.Nodes[i];
                    if (src == null || string.IsNullOrWhiteSpace(src.Uid))
                        continue;

                    if (!byUid.TryGetValue(src.Uid, out var node))
                        continue;

                    try { node.set_isZRoot(src.IsZRoot); } catch { }

                    if (!string.IsNullOrWhiteSpace(src.ParentUid) && byUid.TryGetValue(src.ParentUid, out var parent))
                        node.set_parent(parent);
                    else
                        node.set_parent(null);
                }

                for (int i = 0; i < sync.Nodes.Count; i++)
                {
                    var src = sync.Nodes[i];
                    if (src == null || string.IsNullOrWhiteSpace(src.Uid))
                        continue;

                    if (!byUid.TryGetValue(src.Uid, out var node))
                        continue;

                    // Rebuild Z-links through native RoomNode.addZChild to keep HL object layout valid.
                    if (!src.IsZRoot || string.IsNullOrWhiteSpace(src.ParentUid))
                        continue;
                    if (!byUid.TryGetValue(src.ParentUid, out var parent))
                        continue;

                    ZDoorContentClue? clue = null;
                    string? parentDoorId = null;
                    string? childDoorId = null;
                    if (syncByUid.TryGetValue(src.ParentUid, out var parentSrc))
                    {
                        if (TryFindZLinkSync(parentSrc.ZLinks, src.Uid, out var parentToChild))
                        {
                            parentDoorId = parentToChild.DoorId;
                            if (parentToChild.ContentClue.HasValue)
                                clue = CreateZDoorContentClueFromIndex(parentToChild.ContentClue.Value);
                        }
                    }
                    if (TryFindZLinkSync(src.ZLinks, src.ParentUid, out var childToParent))
                        childDoorId = childToParent.DoorId;

                    try
                    {
                        parent.addZChild(node, clue);
                    }
                    catch
                    {
                        // If native rebuild fails, leave local z-links and continue; reason will surface later in apply/debug logs.
                    }

                    if (parentDoorId != null)
                        TrySetZLinkDoorId(parent, node, parentDoorId);
                    if (childDoorId != null)
                        TrySetZLinkDoorId(node, parent, childDoorId);
                }

                target.zLinkId = sync.ZLinkId;

                // Rebuild child arrays in host order. Parent pointers are already set above.
                for (int i = 0; i < sync.Nodes.Count; i++)
                {
                    var src = sync.Nodes[i];
                    if (src == null || string.IsNullOrWhiteSpace(src.Uid))
                        continue;
                    if (!byUid.TryGetValue(src.Uid, out var node))
                        continue;

                    node.children = BuildRoomNodeArrayByUid(src.ChildrenUids, byUid);
                    node.zChildren = BuildRoomNodeArrayByUid(src.ZChildrenUids, byUid);
                }

                for (int i = 0; i < sync.Nodes.Count; i++)
                {
                    var src = sync.Nodes[i];
                    if (src == null || string.IsNullOrWhiteSpace(src.Uid))
                        continue;
                    if (!byUid.TryGetValue(src.Uid, out var node))
                        continue;

                    if (!string.IsNullOrWhiteSpace(src.SubTeleportUid) && byUid.TryGetValue(src.SubTeleportUid, out var subTp))
                        node.subTeleportTo = subTp;
                    else
                        node.subTeleportTo = null;
                }

                var rebuiltAll = ArrayUtils.CreateDyn();
                var rebuiltNodes = new StringMap();
                for (int i = 0; i < sync.Nodes.Count; i++)
                {
                    var src = sync.Nodes[i];
                    if (src == null || string.IsNullOrWhiteSpace(src.Uid))
                        continue;
                    if (!byUid.TryGetValue(src.Uid, out var node))
                        continue;

                    rebuiltAll.array.pushDyn(node);
                    rebuiltNodes.set(src.Uid.AsHaxeString(), node);
                }

                target.all = (ArrayObj)rebuiltAll.array;
                target.nodes = rebuiltNodes;

                if (!string.IsNullOrWhiteSpace(sync.RootUid) && byUid.TryGetValue(sync.RootUid, out var explicitRoot))
                {
                    rebuiltRoot = explicitRoot;
                }
                else
                {
                    for (int i = 0; i < sync.Nodes.Count; i++)
                    {
                        var src = sync.Nodes[i];
                        if (src == null || string.IsNullOrWhiteSpace(src.Uid))
                            continue;
                        if (src.IsZRoot)
                            continue;
                        if (!string.IsNullOrWhiteSpace(src.ParentUid))
                            continue;

                        if (byUid.TryGetValue(src.Uid, out var inferredRoot))
                        {
                            rebuiltRoot = inferredRoot;
                            break;
                        }
                    }
                }

                if (rebuiltRoot == null)
                {
                    reason = "rebuilt root not found";
                    return false;
                }

                try
                {
                    LogGenericZDoorDiagnostics(sync, byUid);
                }
                catch
                {
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private static NpcId? CreateNpcIdFromIndex(int index)
        {
            return CreateEnumByIndex<NpcId, NpcId.Indexes>(index);
        }

        private static Dictionary<string, RoomNode> CaptureExistingRoomNodesByUid(LevelStruct target)
        {
            var result = new Dictionary<string, RoomNode>(StringComparer.Ordinal);
            if (target == null)
                return result;

            try
            {
                var all = target.all;
                if (all == null)
                    return result;

                for (int i = 0; i < all.length; i++)
                {
                    if (all.getDyn(i) is not RoomNode node)
                        continue;

                    var uid = node.uid?.ToString();
                    if (string.IsNullOrWhiteSpace(uid))
                        continue;

                    if (!result.ContainsKey(uid))
                        result[uid] = node;
                }
            }
            catch
            {
            }

            return result;
        }

        private static LinkConstraint? CreateLinkConstraintFromIndex(int index)
        {
            return index switch
            {
                0 => new LinkConstraint.All(),
                1 => new LinkConstraint.NeverDown(),
                2 => new LinkConstraint.NeverUp(),
                3 => new LinkConstraint.NeverRight(),
                4 => new LinkConstraint.NeverLeft(),
                5 => new LinkConstraint.HorizontalOnly(),
                6 => new LinkConstraint.VerticalOnly(),
                7 => new LinkConstraint.HorizontalLevelDirOnly(),
                8 => new LinkConstraint.RightOnly(),
                9 => new LinkConstraint.LeftOnly(),
                10 => new LinkConstraint.UpOnly(),
                11 => new LinkConstraint.DownOnly(),
                _ => null
            };
        }

        private static ZDoorContentClue? CreateZDoorContentClueFromIndex(int index)
        {
            return CreateEnumByIndex<ZDoorContentClue, ZDoorContentClue.Indexes>(index);
        }

        private static ZDoorType? CreateZDoorTypeFromSync(LevelGraphZDoorTypeSync? sync)
        {
            if (sync == null)
                return null;

            try
            {
                return sync.RawIndex switch
                {
                    0 => new ZDoorType.BossRune(sync.IntParam0 ?? 0),
                    1 => new ZDoorType.PerfectKills(sync.IntParam0 ?? 0),
                    2 => new ZDoorType.Timed(sync.DoubleParam0 ?? 0d),
                    3 => new ZDoorType.Conditional(),
                    4 => new ZDoorType.TumulusAntichamber(),
                    5 => new ZDoorType.CliffEnigma(),
                    6 => new ZDoorType.TrainingArena(),
                    7 => new ZDoorType.PurpleTeleport(),
                    8 => new ZDoorType.BossRushTeleport(),
                    _ => null
                };
            }
            catch
            {
                return null;
            }
        }

        private static TEnum? CreateEnumByIndex<TEnum, TIndex>(int index)
            where TEnum : class
            where TIndex : struct, Enum
        {
            if (!Enum.IsDefined(typeof(TIndex), index))
                return null;

            var name = Enum.GetName(typeof(TIndex), index);
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var nested = typeof(TEnum).GetNestedType(name, System.Reflection.BindingFlags.Public);
            if (nested == null)
                return null;

            try
            {
                return Activator.CreateInstance(nested) as TEnum;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryFindZLinkSync(List<LevelGraphZLinkSync>? zLinks, string? destUid, out LevelGraphZLinkSync result)
        {
            result = null!;
            if (zLinks == null || string.IsNullOrWhiteSpace(destUid))
                return false;

            for (int i = 0; i < zLinks.Count; i++)
            {
                var item = zLinks[i];
                if (item == null || string.IsNullOrWhiteSpace(item.DestUid))
                    continue;
                if (!string.Equals(item.DestUid, destUid, StringComparison.Ordinal))
                    continue;
                result = item;
                return true;
            }

            return false;
        }

        private static void TrySetZLinkDoorId(RoomNode from, RoomNode dest, string doorId)
        {
            if (from == null || dest == null)
                return;

            try
            {
                var zLinks = from.zLinks;
                if (zLinks == null)
                    return;

                for (int i = 0; i < zLinks.length; i++)
                {
                    var link = zLinks.getDyn(i) as virtual_contentClue_dest_doorId_id_;
                    if (link == null)
                        continue;
                    if (!ReferenceEquals(link.dest, dest))
                        continue;
                    link.doorId = doorId.AsHaxeString();
                    return;
                }
            }
            catch
            {
            }
        }

        private static void LogGenericZDoorDiagnostics(LevelGraphSync sync, Dictionary<string, RoomNode> byUid)
        {
            if (_log == null || sync.Nodes == null)
                return;

            for (int i = 0; i < sync.Nodes.Count; i++)
            {
                var src = sync.Nodes[i];
                if (src == null || !string.Equals(src.RType, "GenericZDoor", StringComparison.Ordinal))
                    continue;
                if (!byUid.TryGetValue(src.Uid, out var node))
                    continue;

                var childInfo = new List<string>();
                try
                {
                    var children = node.children;
                    if (children != null)
                    {
                        for (int c = 0; c < children.length; c++)
                        {
                            if (children.getDyn(c) is not RoomNode child)
                                continue;
                            var plc = "null";
                            var payloadPlc = "null";
                            try
                            {
                                if (child.parentLinkConstraint is HaxeEnum he)
                                    plc = he.RawIndex.ToString(CultureInfo.InvariantCulture);
                            }
                            catch { }
                            try
                            {
                                var childUid = child.uid?.ToString();
                                if (!string.IsNullOrWhiteSpace(childUid) &&
                                    sync.Nodes != null)
                                {
                                    for (int s = 0; s < sync.Nodes.Count; s++)
                                    {
                                        var childSrc = sync.Nodes[s];
                                        if (childSrc == null || !string.Equals(childSrc.Uid, childUid, StringComparison.Ordinal))
                                            continue;
                                        if (childSrc.ParentLinkConstraint.HasValue)
                                            payloadPlc = childSrc.ParentLinkConstraint.Value.ToString(CultureInfo.InvariantCulture);
                                        break;
                                    }
                                }
                            }
                            catch { }
                            childInfo.Add($"{child.uid}:{plc}/p{payloadPlc}");
                        }
                    }
                }
                catch { }

                var zdoorInfo = new List<string>();
                try
                {
                    var zLinks = node.zLinks;
                    if (zLinks != null)
                    {
                        for (int z = 0; z < zLinks.length; z++)
                        {
                            var link = zLinks.getDyn(z) as virtual_contentClue_dest_doorId_id_;
                            if (link == null)
                                continue;
                            zdoorInfo.Add(link.doorId?.ToString() ?? "null");
                        }
                    }
                }
                catch { }

                _log.Information(
                    "[NetMod] GenericZDoor diag {LevelId} uid={Uid} rType={RType} g={Group} forced={Forced} runtimeForced={RuntimeForced} parent={Parent} isZ={IsZ} children={ChildCount}[{ChildInfo}] zLinks={ZCount}[{ZInfo}] payloadChildren={PChild} payloadZ={PZ}",
                    sync.LevelId,
                    src.Uid,
                    src.RType ?? "null",
                    src.Group,
                    src.ForcedTemplateId ?? "null",
                    node.forcedTemplate?.id?.ToString() ?? "null",
                    src.ParentUid ?? "null",
                    src.IsZRoot,
                    node.children?.length ?? -1,
                    string.Join(",", childInfo),
                    node.zLinks?.length ?? -1,
                    string.Join(",", zdoorInfo),
                    src.ChildrenUids?.Count ?? 0,
                    src.ZLinks?.Count ?? 0);
            }
        }

        private static ArrayObj BuildRoomNodeArrayByUid(List<string>? orderedUids, Dictionary<string, RoomNode> byUid)
        {
            var arr = ArrayUtils.CreateDyn();
            if (orderedUids == null)
                return (ArrayObj)arr.array;

            for (int i = 0; i < orderedUids.Count; i++)
            {
                var uid = orderedUids[i];
                if (string.IsNullOrWhiteSpace(uid))
                    continue;
                if (!byUid.TryGetValue(uid, out var node))
                    continue;
                arr.array.pushDyn(node);
            }

            return (ArrayObj)arr.array;
        }

    }
}
