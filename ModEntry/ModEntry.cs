
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using dc.en;
using dc.en.inter;
using dc.pr;
using dc.level;
using dc.hl.types;
using dc;
using dc.libs.heaps.slib;
using Rand = dc.libs.Rand;
using dc.ui.hud;
using dc.h2d;
using Hashlink.Virtuals;
using dc.tool;
using dc.tool.mainSkills;
using HaxeProxy.Runtime;
using dc.cine;
using dc.cine.coll;
using dc.cine.dlcp;
using dc.cine.kf;
using dc.cine.queen;
using CineHookInitialize;
using DeadCellsMultiplayerMod.Ghost.GhostBase;
using ModCore.Events;
using DeadCellsMultiplayerMod.Mobs.MobsSynchronization;
using DeadCellsMultiplayerMod.Interface.ModuleInitializing;
using DeadCellsMultiplayerMod.MultiplayerModUI.Minimap;
using DeadCellsMultiplayerMod.MultiplayerModUI.lifeUI;
using DeadCellsMultiplayerMod.MultiplayerModUI.LevelExit;
using DeadCellsMultiplayerMod.MultiplayerModUI.Connection;
using DeadCellsMultiplayerMod.Tools.ModLang;
using DeadCellsMultiplayerMod.KingHead;
using DeadCellsMultiplayerMod.Mobs.Levelinit;
using dc.en.inter.door;
using DeadCellsMultiplayerMod.Interaction;
using DeadCellsMultiplayerMod.UI;


namespace DeadCellsMultiplayerMod
{
    public partial class ModEntry(ModInfo info) : ModBase(info),
        IOnGameEndInit,
        IOnHeroInit,
        IOnHeroUpdate,
        IOnFrameUpdate,
        IOnAfterLoadingCDB,
        IOnAdvancedModuleInitializing
    {
        public static ModEntry? Instance { get; private set; }
        private bool _ready;
        private static bool s_hooksInstalled;
        private static IDisposable? s_steamOverlayJoinCallback;
        private static IDisposable? s_steamRichPresenceJoinCallback;
        private static bool s_steamOverlayCallbackPending;
        private static Timer? s_steamCallbackPumpTimer;
        private static int s_steamOverlayCallbackRetryCount;
        private static bool s_steamApiReady;
        private static string s_lastSteamLaunchCommand = string.Empty;
        private static string s_lastSteamLaunchConnectLobbyParam = string.Empty;
        private static ulong s_lastOverlayJoinLobbyId;
        private static long s_lastOverlayJoinTicks;
        private static long s_nextSteamLaunchPollTicks;
        private const int SteamOverlayCallbackMaxRetries = 600;
        private const int SteamOverlayLaunchPollIntervalMs = 500;
        private const int SteamOverlayJoinDedupMs = 3000;
        private NetRole _netRole = NetRole.None;
        public static NetNode? _net;

        public dc.pr.Game? game;

        public static GhostKing[] clients = new GhostKing[NetNode.MaxClientSlots];
        public static Kinghead?[] clientHeads = new Kinghead?[NetNode.MaxClientSlots];
        public static string?[] clientLabels = new string?[NetNode.MaxClientSlots];
        public static int[] clientIds = new int[NetNode.MaxClientSlots];
        public static string?[] clientSkins = new string?[NetNode.MaxClientSlots];
        public static string?[] clientHeadSkins = new string?[NetNode.MaxClientSlots];
        private static bool[] pendingClientHeadRecreate = new bool[NetNode.MaxClientSlots];
        public static Hero me = null!;
        public static GhostHero _ghost = null!;

        private GameDataSync? gds;
        private Hero? _debugPerkAppliedHero;
        private string _debugPerkAppliedId = string.Empty;
        private string _lastDebugPerkApplyErrorId = string.Empty;
        private long _nextDebugPerkApplyTick;
        private ItemMetaManager? _debugExplorerRuneInjectedMeta;
        private bool _debugExplorerRuneInjectedByDebug;
        private const string ExplorerRunePermanentItemId = "ExploKey";
        /// <summary>Last successful minimap reveal signature (level + branch); cleared on level/wakeup.</summary>
        private string _debugExplorerRevealAppliedSignature = string.Empty;
        private long _nextDebugExplorerRevealRetryTick;
        private int _debugExplorerRevealAllCount;
        private const int MaxDebugExplorerRevealAllCalls = 3;

        private string? _lastAnimSent;
        private int? _lastAnimQueueSent;
        private bool? _lastAnimGSent;
        private long _suppressHeroAnimUntilTicks;
        private string? _lastSentHeroSkin;
        private string? _lastSentHeroHeadSkin;

        public static MiniMap miniMap = null!;

        public static bool kingInitialized = false;

        public string levelId = string.Empty;

        public static int remotePlayerId = -1;

        public string remoteSkin = string.Empty;
        public string remoteHeadSkin = string.Empty;

        public string lastHeadAnim = string.Empty;
        public static ArrayDyn customHeads = null!;

        public InventItem inventItem = null!;
        private bool _inventorySyncGuard;
        private bool _localFakeDead;
        // 全员倒地重开时置位：下一次英雄重生（hook_hero_wakeup）时把英雄补满血，
        // 修复客机重开后残留假死的 1 血。一次性消费。
        internal bool _pendingDeathRestartFullHeal;
        private bool _localExitPenaltyApplied;
        private long _localFakeDeadStartedTicks;
        private DeadBase? _localDeadCine;
        private double _localDownedX;
        private double _localDownedY;
        private double _localHeldX;
        private double _localHeldY;
        private string _localDownedLevelId = string.Empty;
        private long _nextReviveAttemptTicks;
        private long _nextDownedStateSendTicks;
        private long _postReviveLockUntilTicks;
        private double _postReviveLockX;
        private double _postReviveLockY;
        private int _reviveHoldTargetId;
        private long _reviveHoldStartedTicks;
        private const double ReviveUseDistancePx = 48.0;
        private const int ReviveInteractKey = 82; // R
        private const double ReviveAttemptCooldownSeconds = 0.2;
        private const double ReviveHoldSeconds = 0.7;
        private const double ReviveHomunculusBodyMaxDistancePx = 64.0;
        private const double DownedStateResendSeconds = 0.4;
        private const double DownedHeadStateResendSeconds = 1.0 / 30.0;
        private const double DownedGhostBodyYOffsetPx = 40.0;
        private const double LocalReviveBodyYOffsetPx = 0.5;
        private const double PostRevivePositionLockSeconds = 0.0;
        private const string ReviveHintText = "Hold to revive.";
        private string _lastDoorMarkerLevelId = string.Empty;
        private int _lastDoorMarkerToken = int.MinValue;
        private string _localLastDoorMarkerLevelId = string.Empty;
        private int _localLastDoorMarkerToken = int.MinValue;

        private sealed class RemoteDownedState
        {
            public int UserId;
            public double X;
            public double Y;
            public bool HasHeadPosition;
            public double HeadX;
            public double HeadY;
            public bool HasHeadAnim;
            public string HeadAnim = string.Empty;
            public string LevelId = string.Empty;
            public long UpdatedAtTicks;
        }

        private sealed class RemoteDoorMarkerState
        {
            public int MarkerToken;
            public string LevelId = string.Empty;
            public long UpdatedAtTicks;
        }

        private readonly Dictionary<int, RemoteDownedState> _remoteDowned = new();
        private readonly Dictionary<int, RemoteDownedCorpse> _remoteDownedCines = new();
        private readonly HashSet<int> _downedAnnouncements = new();
        private readonly Dictionary<int, RemoteDoorMarkerState> _remoteLastDoorMarkers = new();
        private readonly Dictionary<int, RemoteDoorMarkerState> _remotePendingDoorMarkers = new();
        private readonly Dictionary<int, long> _pendingClientDisposeTicks = new();
        private const double ClientDisposeTransitionSeconds = 0.28;
        private const double PendingDoorMarkerHideMaxSeconds = 1.5;

        private static readonly HashSet<string> BossLevelIds = new(StringComparer.OrdinalIgnoreCase)
        {
            // Canonical boss level ids are derived from CDB bossRush.bosses[level],
            // boss-related achievements, and GameProxy level display constructors.
            "BeholderPit", "Throne", "DeathArena", "DookuArena", "QueenArena", "Observatory",
            "SwampHeart", "GardenerStage", "Lighthouse", "Giant",
            "DookuCastle", "RichterCastle", "BossRushZone", "Bridge", "TopClockTower",
            // Compatibility aliases seen in earlier multiplayer boss handling.
            "CastleAlchemy", "LighthouseTop", "LighthouseBottom"
        };

        internal static bool IsBossLevel(string? levelId)
        {
            return !string.IsNullOrWhiteSpace(levelId) && BossLevelIds.Contains(levelId);
        }

        /// <summary>Known boss-room genericEventIds from game's HiddenTrigger (set via marker.customId in level data).</summary>
        private static readonly HashSet<string> BossRoomGenericEventIds = new(StringComparer.Ordinal)
        {
            "roomDeath", "roomBeholder", "roomBerserk", "roomCollectorBoss", "roomDooku",
            "roomGardenerBoss", "roomGiant", "roomKingsHand", "roomQueen", "roomBehemoth",
            "roomMamaTick", "roomKingsHandAsKing"
        };
        private static readonly HashSet<string> BossDeathCineTypeNames = new(StringComparer.Ordinal)
        {
            "BeholderDeath", "GiantDeath", "GiantDeath4", "KillKingCinem", "KillQueenCinem",
            "QueenDefeated", "KillDookuBeastCinem", "FakeKillDooku", "RichterDeath",
            "EndCollectorPreSmash", "SmashCinem", "EndCollectorPostSmash", "EndCollectorPostSmashKS"
        };
        private static readonly HashSet<string> BossIntroCineTypeNames = new(StringComparer.Ordinal)
        {
            "EnterRoomBoss", "EnterRoomDeathBoss", "EnterRoomGardenerBoss", "EnterRoomQueenBoss",
            "EnterThroneRoom", "EnterThroneBossRush", "EnterThroneRoomAsKing", "EnterGiantRoom",
            "EnterModifiedGiantRoom", "EnterDualBehemoth", "StartCollectorFight", "StartCollectorFightAlt",
            "MeetCollectorEnd"
        };
        private string? _lastBossCineSentLevelId;
        private long _lastBossCineSentTick;
        private const double BossCineSendCooldownSeconds = 2.0;
        private readonly Dictionary<string, long> _pendingBossCineApplyByLevel = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _suppressBossCineEchoByLevel = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _completedBossCineLevels = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _appliedBossHeroTeleportLevels = new(StringComparer.OrdinalIgnoreCase);
        private const double BossCineApplyPendingTtlSeconds = 20.0;
        private const double BossCineEchoSuppressSeconds = 12.0;
        private const double BossHeroTeleportYOffsetPx = 20.0;
        private const double BossHeroTeleportEchoSuppressSeconds = 1.5;
        private int _suppressBossCineSendDepth;
        private long _suppressBossTriggerNetSendUntilTick;


        void IOnAfterLoadingCDB.OnAfterLoadingCDB(dc._Data_ cdb)
        {
            customHeads = cdb.customHead.all;
        }


        internal static void SetRemoteSkin(string? skin)
        {
            var instance = Instance;
            if (instance == null)
                return;

            instance.remoteSkin = string.IsNullOrWhiteSpace(skin)
                ? "PrisonerDefault"
                : skin.Replace("|", "/").Trim();
        }

        internal static void SetRemoteHeadSkin(string? skin)
        {
            var instance = Instance;
            if (instance == null)
                return;

            instance.remoteHeadSkin = NormalizeSkin(skin, "BaseFlame");
        }

        public static string GetClientLabel(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= clientLabels.Length)
                return string.Empty;

            return clientLabels[slotIndex] ?? string.Empty;
        }


        internal static GhostKing? GetPrimaryClient()
        {
            for (int i = 0; i < clients.Length; i++)
            {
                var client = clients[i];
                if (client != null && clientIds[i] > 0)
                    return client;
            }

            return clients.Length > 0 ? clients[0] : null;
        }

        internal static void ResetClientSlots()
        {
            for (int i = 0; i < clients.Length; i++)
            {
                var head = clientHeads[i];
                if (head != null)
                {
                    head.dispose();
                    clientHeads[i] = null;
                }
                clients[i] = null!;
                clientLabels[i] = null;
                clientIds[i] = 0;
                clientSkins[i] = null;
                clientHeadSkins[i] = null;
                pendingClientHeadRecreate[i] = false;
                rLastX[i] = 0;
                rLastY[i] = 0;
            }
        }

        private static string BuildRemoteLabel(int remoteId, string? username)
        {
            var clean = string.IsNullOrWhiteSpace(username) ? "Guest" : username.Trim();
            if (remoteId > 0)
                return $"{clean}";
            return clean;
        }


        public void OnGameEndInit()
        {
            _ready = true;
            GameMenu.SetRole(NetRole.None);
            s_steamOverlayCallbackPending = true;
            s_steamOverlayCallbackRetryCount = 0;
            _debugPerkAppliedHero = null;
            _debugPerkAppliedId = string.Empty;
            _lastDebugPerkApplyErrorId = string.Empty;
            _nextDebugPerkApplyTick = 0;
            _debugExplorerRuneInjectedMeta = null;
            _debugExplorerRuneInjectedByDebug = false;
            _debugExplorerRevealAppliedSignature = string.Empty;
            _nextDebugExplorerRevealRetryTick = 0;
            _debugExplorerRevealAllCount = 0;
            TryEnsureSteamApiInitialized("OnGameEndInit", logFailure: true);
            TryParseConnectLobbyFromCommandLine();
        }
        public override void Initialize()
        {
            Instance = this;

            InstallGlobalCrashLogging();
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Init(Logger);

            this.gds = new GameDataSync(Logger);
            InitializeOptionalModule(
                DebugModuleId.MultiplayerModLang,
                "MultiplayerModLang",
                () => _ = new MultiplayerModLang(this));
            InitializeOptionalModule(
                DebugModuleId.CineHooks,
                "CineHooks",
                () => _ = new CineHooks());
            InitializeOptionalModule(
                DebugModuleId.MultiplayerUI,
                "MultiplayerUI",
                () => _ = new MultiplayerUI(this, 0));

            _ = new SettingsUI(this);

            InitializeOptionalModule(
                DebugModuleId.LevelInit,
                "Levelinit",
                () => _ = new Levelinit(info));
            InitializeOptionalModule(
                DebugModuleId.MobsSynchronization,
                "MobsSynchronization",
                () => _ = new MobsSynchronization(this));
            InitializeOptionalModule(
                DebugModuleId.MinimapReveal,
                "Minimapreveal",
                () => _ = new Minimapreveal());
            InitializeOptionalModule(
                DebugModuleId.LevelExitSync,
                "LevelExitSync",
                () => _ = new LevelExitSync(this));
            InitializeOptionalModule(
                DebugModuleId.InteractionSync,
                "InteractionSync",
                () => _ = new InteractionSync(this));
            InitializeOptionalModule(
                DebugModuleId.ConnectionUI,
                "ConnectionUI",
                () => ConnectionUI.Initialize(this));
            GameMenu.Initialize(Logger);
            s_steamOverlayCallbackPending = true;
            s_steamOverlayCallbackRetryCount = 0;
            EventSystem.BroadcastEvent<IOnAdvancedModuleInitializing, ModEntry>(this);

            void InitializeOptionalModule(DebugModuleId moduleId, string moduleName, Action init)
            {
                var isEnabled = !MultiplayerSettingsStorage.IsDebugSectionEnabled ||
                                MultiplayerSettingsStorage.IsModuleEnabled(moduleId);
                if (isEnabled)
                {
                    init();
                    return;
                }

                Logger.Information("[NetMod][Debug] Module disabled by settings: {Module}", moduleName);
            }
        }

        private static bool s_crashLoggingInstalled;

        /// <summary>
        /// 全局崩溃捕获：任何冒泡到顶、即将让进程退出的未处理托管异常（含未被 catch 的 Hashlink 异常），
        /// 在进程死亡前同步写入独立的崩溃文件（File.AppendAllText 立即落盘，不依赖 Serilog 缓冲），
        /// 同时也尝试经 Serilog 记一份。用于定位那些不一定来自桥、原因未知的闪退。
        /// </summary>
        private void InstallGlobalCrashLogging()
        {
            if (s_crashLoggingInstalled)
                return;
            s_crashLoggingInstalled = true;

            try
            {
                AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                    WriteCrashRecord("UnhandledException", e.ExceptionObject as Exception, e.IsTerminating);
                System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
                {
                    WriteCrashRecord("UnobservedTaskException", e.Exception, false);
                    e.SetObserved();
                };
            }
            catch (Exception ex)
            {
                try { Logger.Warning(ex, "[CRASH-DIAG] Failed to install global crash logging"); } catch { }
            }
        }

        private void WriteCrashRecord(string source, Exception? ex, bool terminating)
        {
            var text =
                $"==== [CRASH-DIAG] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} source={source} terminating={terminating} ===={Environment.NewLine}"
                + (ex?.ToString() ?? "(no exception object)") + Environment.NewLine + Environment.NewLine;

            // 1) 同步直写独立文件，保证即使硬退出也能落盘。
            try
            {
                string dir;
                try { dir = System.IO.Path.Combine(ModCore.Storage.FolderInfo.CoreRoot.FullPath, "logs"); }
                catch { dir = AppContext.BaseDirectory; }
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "mp_crash.log"), text);
            }
            catch { }

            // 2) 也走 Serilog 记一份（可能因缓冲丢失，但作为补充）。
            try { Logger.Fatal("[CRASH-DIAG] {Source} terminating={Terminating}{NL}{Detail}", source, terminating, Environment.NewLine, ex?.ToString()); } catch { }
        }

        /// <summary>同步直写一行到 mp_crash.log（硬崩也能留住），用于诊断。</summary>
        internal void AppendMpCrashLine(string line)
        {
            try
            {
                string dir;
                try { dir = System.IO.Path.Combine(ModCore.Storage.FolderInfo.CoreRoot.FullPath, "logs"); }
                catch { dir = AppContext.BaseDirectory; }
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "mp_crash.log"),
                    $"==== [NULLSPR-DIAG] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line} ===={Environment.NewLine}");
            }
            catch { }
            try { Logger.Warning("[NULLSPR-DIAG] {Line}", line); } catch { }
        }

        private readonly Dictionary<string, long> _interactGuardNextLogTick = new(StringComparer.Ordinal);

        /// <summary>
        /// [INTERACT-GUARD] 捕获交互路径里的 HL 异常并写出完整 .NET 栈（节流：同一来源每秒最多一次），
        /// 用于定位 "Null access .groupName" 究竟发生在哪个方法/实体。返回是否捕获到异常。
        /// </summary>
        private void LogInteractGuardException(string where, Exception ex, string? extra = null)
        {
            try
            {
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (_interactGuardNextLogTick.TryGetValue(where, out var next) && now < next)
                    return;
                _interactGuardNextLogTick[where] = now + System.Diagnostics.Stopwatch.Frequency; // 1s 节流

                string dir;
                try { dir = System.IO.Path.Combine(ModCore.Storage.FolderInfo.CoreRoot.FullPath, "logs"); }
                catch { dir = AppContext.BaseDirectory; }
                System.IO.Directory.CreateDirectory(dir);
                var text =
                    $"==== [INTERACT-GUARD] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} where={where}"
                    + (extra != null ? $" {extra}" : "") + $" ===={Environment.NewLine}"
                    + ex.ToString() + Environment.NewLine + Environment.NewLine;
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "mp_crash.log"), text);
            }
            catch { }
            try { Logger.Warning(ex, "[INTERACT-GUARD] {Where} {Extra}", where, extra); } catch { }
        }

        private static string DescribeInteractive(dc.en.Interactive? inter)
        {
            if (inter == null)
                return "inter=null";
            try
            {
                var tn = inter.GetType().Name;
                bool sprNull;
                try { sprNull = inter.spr == null; } catch { sprNull = true; }
                return $"inter={tn} sprNull={sprNull}";
            }
            catch { return "inter=?"; }
        }

        private void Hook_Hero_updateInteraction(Hook_Hero.orig_updateInteraction orig, Hero self)
        {
            try { orig(self); }
            catch (Exception ex) { LogInteractGuardException("Hero.updateInteraction", ex); }
        }

        private bool Hook_Hero_canUseInteractive(Hook_Hero.orig_canUseInteractive orig, Hero self, dc.en.Interactive inter)
        {
            try { return orig(self, inter); }
            catch (Exception ex) { LogInteractGuardException("Hero.canUseInteractive", ex, DescribeInteractive(inter)); return false; }
        }

        private void Hook_Hero_onUseInteractive(Hook_Hero.orig_onUseInteractive orig, Hero self, dc.en.Interactive inter)
        {
            try { orig(self, inter); }
            catch (Exception ex) { LogInteractGuardException("Hero.onUseInteractive", ex, DescribeInteractive(inter)); }
        }

        void IOnAdvancedModuleInitializing.OnAdvancedModuleInitializing(ModEntry entry)
        {
            if (s_hooksInstalled)
                return;

            s_hooksInstalled = true;
            entry.Logger.Information("\x1b[32m[[ModEntry] Initializing ModEntry...]\x1b[0m ");
            Hook_Game.init += Hook_gameinit;
            Hook_Hero.wakeup += hook_hero_wakeup;
            Hook_Hero.onLevelChanged += hook_level_changed;
            // [INTERACT-GUARD] 客机与 NPC 交互"即将触发"时偶发 "Null access .groupName" 硬崩。
            // 用 try/catch 包住交互路径的 vanilla 方法：既能把 HL 异常以 .NET 异常形式捕获、写出
            // 完整调用栈定位真凶，又能吞掉异常避免整进程闪退。定位到根因后再移除这层兜底。
            Hook_Hero.updateInteraction += Hook_Hero_updateInteraction;
            Hook_Hero.canUseInteractive += Hook_Hero_canUseInteractive;
            Hook_Hero.onUseInteractive += Hook_Hero_onUseInteractive;
            Hook_User.newGame += GameDataSync.user_hook_new_game;
            Hook_User.unserialize += Hook_User_unserialize;
            Hook_Game.onDispose += Hook_Game_onDispose;
            Hook__Save.save += Hook__Save_save;
            Hook_AnimManager.play += Hook_AnimManager_play;
            Hook_MiniMap.track += Hook_MiniMap_track;
            Hook__LevelStruct.get += Hook__LevelStruct_get;
            Hook_LevelGen.generateGraph += Hook_LevelGen_generateGraph;
            Hook_Boot.update += hook_boot_update;
            Hook_Game.pause += Hook_Game_pause;
            Hook_Hero.kill += Hook_Hero_kill;
            Hook_Hero.onDie += Hook_Hero_onDie;
            Hook_Hero.onDamage += Hook_Hero_onDamage;
            Hook_Hero.checkCursedWeaponHit += Hook_Hero_checkCursedWeaponHit;
            Hook_Hero.startDeathCine += Hook_Hero_startDeathCine;
            Hook_Hero.onHeroDie += Hook_Hero_onHeroDie;
            Hook_ZDoor.onActivate += Hook_ZDoor_onActivate;
            Hook_BossRushDoor.initGfx += Hook_BossRushDoor_initGfx;
            Hook_Hero.applySkin += Hook_Hero_applySkin;
            Hook_HeroHead.initCustomHead += Hook_HeroHead_initCustomHead;
            Hook_DiveAttack.onStart += Hook_DiveAttack_onStart;
            Hook_DiveAttack.onOwnerLand += Hook_DiveAttack_onOwnerLand;
            Hook_HiddenTrigger.trigger += Hook_HiddenTrigger_trigger;
            Hook__HeroDeath.__constructor__ += Hook__HeroDeath__constructor__;
            Hook__HeroDeathBase.__constructor__ += Hook__HeroDeathBase__constructor__;
            Hook__HeroDeathContinue.__constructor__ += Hook__HeroDeathContinue__constructor__;
            Hook__HeroDeathRespawn.__constructor__ += Hook__HeroDeathRespawn__constructor__;
            Hook__HeroDeathDLCP.__constructor__ += Hook__HeroDeathDLCP__constructor__;
            Hook__BeholderDeath.__constructor__ += Hook__BeholderDeath__constructor__;
            Hook__GiantDeath.__constructor__ += Hook__GiantDeath__constructor__;
            Hook__GiantDeath4.__constructor__ += Hook__GiantDeath4__constructor__;
            Hook__KillKingCinem.__constructor__ += Hook__KillKingCinem__constructor__;
            Hook__KillQueenCinem.__constructor__ += Hook__KillQueenCinem__constructor__;
            Hook__QueenDefeated.__constructor__ += Hook__QueenDefeated__constructor__;
            Hook__KillDookuBeastCinem.__constructor__ += Hook__KillDookuBeastCinem__constructor__;
            Hook__FakeKillDooku.__constructor__ += Hook__FakeKillDooku__constructor__;
            Hook__RichterDeath.__constructor__ += Hook__RichterDeath__constructor__;
            Hook__EndCollectorPreSmash.__constructor__ += Hook__EndCollectorPreSmash__constructor__;
            Hook__SmashCinem.__constructor__ += Hook__SmashCinem__constructor__;
            Hook__EndCollectorPostSmash.__constructor__ += Hook__EndCollectorPostSmash__constructor__;
            Hook__EndCollectorPostSmashKS.__constructor__ += Hook__EndCollectorPostSmashKS__constructor__;
            // Hook_Hero.tryToApplyYoloPerk += Hook_Hero_tryToApplyYoloPerk;
            // Hook_Hero.onEnterRoom += 
            Ghost.KingWeaponHooks.Install();
        }


        private void Hook_Hero_applySkin(Hook_Hero.orig_applySkin orig, Hero self, dc.String skinId)
        {
            orig(self, skinId);
            if (_netRole == NetRole.None)
                return;

            var net = _net;
            if (net == null || !net.IsAlive || me == null || self == null || !ReferenceEquals(self, me))
                return;

            var rawSkin = dc.Main.Class.ME?.user?.heroSkin?.ToString();
            if (string.IsNullOrWhiteSpace(rawSkin))
                rawSkin = self.getSkinInfo()?.consoleCmdId?.ToString();

            var skin = NormalizeSkin(rawSkin, "PrisonerDefault");

            if (string.Equals(_lastSentHeroSkin, skin, StringComparison.Ordinal))
                return;

            net.SendHeroSkin(skin);
            _lastSentHeroSkin = skin;
        }

        private void Hook_HeroHead_initCustomHead(Hook_HeroHead.orig_initCustomHead orig, HeroHead self)
        {
            orig(self);
            if (_netRole == NetRole.None)
                return;

            var net = _net;
            if (net == null || !net.IsAlive || me == null || self == null)
                return;

            var localHead = me.heroHead;
            if (localHead == null || !ReferenceEquals(self, localHead))
                return;

            var skin = NormalizeSkin(dc.Main.Class.ME?.user?.heroHeadSkin?.ToString(), "BaseFlame");
            if (string.Equals(_lastSentHeroHeadSkin, skin, StringComparison.Ordinal))
                return;

            net.SendHeroHeadSkin(skin);
            _lastSentHeroHeadSkin = skin;
        }

        private void Hook_ZDoor_onActivate(Hook_ZDoor.orig_onActivate orig, ZDoor self, Hero lp, bool mob)
        {
            orig(self, lp, mob);

            if (_netRole != NetRole.None &&
                _net != null &&
                me != null &&
                lp != null &&
                ReferenceEquals(lp, me))
            {
                SendCurrentRoomTarget(force: true);
                GameMenu.EnqueueMainThread(() => ReceiveGhostCoords());
            }
        }

        /// <summary>
        /// Client often lacks Boss Rush door animation frames until assets settle; HL throws "Unknown frame: bossRushDoor*".
        /// Only swallow those — rethrow everything else so unrelated bugs are not masked (and to avoid odd door state).
        /// </summary>
        private static bool IsBossRushDoorMissingFrameException(Exception ex)
        {
            for (var cur = ex; cur != null; cur = cur.InnerException)
            {
                var msg = cur.Message;
                if (string.IsNullOrWhiteSpace(msg))
                    continue;
                if (msg.IndexOf("Unknown frame", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (msg.IndexOf("bossRushDoor", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                return true;
            }

            return false;
        }

        /// <summary>
        /// At most one deferred <see cref="GameMenu.EnqueueMainThread"/> <c>orig(self)</c> per door instance.
        /// </summary>
        private static readonly ConditionalWeakTable<BossRushDoor, object> s_bossRushDoorGfxDeferredPending = new();

        /// <summary>
        /// Client-only: missing boss-rush door anim frames. Do not call <see cref="Assets.Class.lib.getLevel"/> for
        /// BossRushZone from this hook — it can run during level/entity init and corrupt unrelated HL state (casts such as
        /// <c>tool.CPoint</c> vs <c>level.LevelMap</c>).
        /// </summary>
        private void Hook_BossRushDoor_initGfx(Hook_BossRushDoor.orig_initGfx orig, BossRushDoor self)
        {
            try
            {
                orig(self);
            }
            catch (Exception ex)
            {
                if (_netRole != NetRole.Client || self == null || !IsBossRushDoorMissingFrameException(ex))
                    throw;

                string? bossRushType = null;
                try { bossRushType = self.bossRushType?.ToString(); } catch { }

                Logger.Warning("[NetMod] BossRushDoor.initGfx failed on client level={LevelId}: type={Type} ({Msg})",
                    levelId,
                    bossRushType ?? "null",
                    ex.Message);

                if (s_bossRushDoorGfxDeferredPending.TryGetValue(self, out _))
                {
                    Logger.Warning("[NetMod] BossRushDoor.initGfx second sync failure before deferred retry; clearing spr level={LevelId}", levelId);
                    try { self.spr = null; } catch (Exception ex2) { Logger.Warning(ex2, "[NetMod] BossRushDoor spr=null failed"); }
                    return;
                }

                s_bossRushDoorGfxDeferredPending.Add(self, new object());
                var localOrig = orig;
                var localSelf = self;
                GameMenu.EnqueueMainThread(() =>
                {
                    try
                    {
                        localOrig(localSelf);
                    }
                    catch (Exception ex2)
                    {
                        if (_netRole != NetRole.Client || !IsBossRushDoorMissingFrameException(ex2))
                        {
                            Logger.Warning(ex2, "[NetMod] BossRushDoor.initGfx deferred retry unexpected error");
                            return;
                        }

                        string? t = null;
                        try { t = localSelf.bossRushType?.ToString(); } catch { }
                        Logger.Warning("[NetMod] BossRushDoor.initGfx deferred retry still missing frames level={LevelId}: type={Type} ({Msg})",
                            levelId,
                            t ?? "null",
                            ex2.Message);
                        try { localSelf.spr = null; } catch (Exception ex3) { Logger.Warning(ex3, "[NetMod] BossRushDoor spr=null failed"); }
                    }
                });
            }
        }

        private void Hook_HiddenTrigger_trigger(Hook_HiddenTrigger.orig_trigger orig, HiddenTrigger self, Entity dh)
        {
            var senderLevelId = string.IsNullOrWhiteSpace(levelId) ? string.Empty : levelId.Trim();
            string? senderGenericEventId = null;
            double? senderPreX = null;
            double? senderPreY = null;
            int? senderPreDir = null;
            double? senderPostX = null;
            double? senderPostY = null;
            int? senderPostDir = null;

            if (self != null)
                senderGenericEventId = self.genericEventId?.ToString()?.Trim();

            if (dh != null && me != null && ReferenceEquals(dh, me))
                TryCaptureBossCineHeroPosition(me, out senderPreX, out senderPreY, out senderPreDir);

            orig(self, dh);

            if (_suppressBossCineSendDepth > 0)
                return;
            if (_suppressBossTriggerNetSendUntilTick != 0 &&
                Stopwatch.GetTimestamp() < _suppressBossTriggerNetSendUntilTick)
                return;
            if (_netRole == NetRole.None || _net == null || !_net.IsAlive)
                return;
            if (self == null || dh == null || me == null || !ReferenceEquals(dh, me))
                return;

            if (!BossLevelIds.Contains(senderLevelId))
                return;
            if (IsBossCineCompleted(senderLevelId) ||
                IsBossCineSendSuppressed(senderLevelId) ||
                HasAppliedBossHeroTeleport(senderLevelId))
                return;
            if (string.IsNullOrWhiteSpace(senderGenericEventId))
                return;
            if (!BossRoomGenericEventIds.Contains(senderGenericEventId))
                return;
            if (!DidBossTriggerStart(dc.pr.Game.Class.ME, self))
                return;

            TryCaptureBossCineHeroPosition(me, out senderPostX, out senderPostY, out senderPostDir);
            if (senderPreX.HasValue && senderPreY.HasValue)
                _net.SendBossHeroTeleport(senderPreX.Value, senderPreY.Value, senderPreDir ?? 0);

            if (TrySendBossCinePayload(BuildBossCinePayload(
                senderLevelId,
                senderGenericEventId,
                senderPreX,
                senderPreY,
                senderPreDir,
                senderPostX,
                senderPostY,
                senderPostDir)))
                MarkBossCineCompleted(senderLevelId);
        }

        private void Hook_Hero_lockControlFromSkill(Hook_Hero.orig_lockControlFromSkill orig, Hero self, double sec)
        {
            if(Ghost.KingWeaponSupport.IsInKingContext && me != null && ReferenceEquals(self, me))
                return;
            orig(self, sec);
        }

        private void Hook_Hero_unlockControls(Hook_Hero.orig_unlockControls orig, Hero self)
        {
            if(Ghost.KingWeaponSupport.IsInKingContext && me != null && ReferenceEquals(self, me))
                return;
            orig(self);
        }

        private void Hook_User_unserialize(Hook_User.orig_unserialize orig, User self, dc.hxbit.Serializer v)
        {
            orig(self, v);
            if (_netRole == NetRole.Client)
                GameDataSync.CaptureOriginalUserData(self, allowReplaceWhenBetter: true);
        }

        private void Hook_Game_onDispose(Hook_Game.orig_onDispose orig, dc.pr.Game self)
        {
            if (_netRole == NetRole.Client)
            {
                var user = self?.user;
                if (user != null)
                    GameDataSync.SwapToOriginalUserData(user);
                GameDataSync.SwapToLocalSerializerSync();
            }

            orig(self);
        }

        private void Hook__Save_save(Hook__Save.orig_save orig, User u, bool onlyGameData)
        {
            if (_netRole == NetRole.Host)
            {
                orig(u, onlyGameData);
                return;
            }

            if (_netRole == NetRole.Client)
            {
                var serializerSwapped = GameDataSync.SwapToLocalSerializerSync();
                try
                {
                    orig(u, onlyGameData);
                }
                finally
                {
                    if (serializerSwapped)
                        GameDataSync.RestoreRemoteSerializerSync();
                }
                return;
            }

            orig(u, onlyGameData);
        }

        private void Hook_Viewport_bumpDir(Hook_Viewport.orig_bumpDir orig, Viewport self, int dir, double? pow)
        {
            if(Ghost.KingWeaponSupport.IsInKingContext)
                return;
            orig(self, dir, pow);
        }

        private void Hook_Entity_recoil(Hook_Entity.orig_recoil orig, Entity self, double dx)
        {
            if(Ghost.KingWeaponSupport.IsInKingContext && me != null && ReferenceEquals(self, me))
                return;
            orig(self, dx);
        }

        private void Hook_Entity_bump(Hook_Entity.orig_bump orig, Entity self, double dy, double ignoreResist, bool? dx)
        {
            if(Ghost.KingWeaponSupport.IsInKingContext && me != null && ReferenceEquals(self, me))
                return;
            orig(self, dy, ignoreResist, dx);
        }

        private void Hook_Entity_bumpAwayFrom(Hook_Entity.orig_bumpAwayFrom orig, Entity self, Entity e, double? pow, bool? ignoreResist)
        {
            if(Ghost.KingWeaponSupport.IsInKingContext && me != null && ReferenceEquals(self, me))
                return;
            orig(self, e, pow, ignoreResist);
        }

        private void Hook_Entity_cancelVelocities(Hook_Entity.orig_cancelVelocities orig, Entity self)
        {
            if(Ghost.KingWeaponSupport.IsInKingContext && me != null && ReferenceEquals(self, me))
                return;
            orig(self);
        }

        private void Hook_Entity_setAffectS(Hook_Entity.orig_setAffectS orig, Entity self, int id, double sec, Ref<double> ignoreResist, bool? allowResist)
        {
            if(Ghost.KingWeaponSupport.IsInKingContext && me != null && ReferenceEquals(self, me))
                return;
            orig(self, id, sec, ignoreResist, allowResist);
        }

        private void Hook_Entity_removeAllAffects(Hook_Entity.orig_removeAllAffects orig, Entity self, int list)
        {
            if(Ghost.KingWeaponSupport.IsInKingContext && me != null && ReferenceEquals(self, me))
                return;
            orig(self, list);
        }

        private void Hook_Hero_onLevelChanged()
        {

        }



        private void Hook_Game_pause(Hook_Game.orig_pause orig, dc.pr.Game self)
        {
            // don't change that
            return; 
        }


        private void hook_boot_update(Hook_Boot.orig_update orig, Boot self, double dt)
        {
            orig(self, dt);
            GameMenu.ProcessMainThreadQueue();
            GameMenu.HandleTextInputClipboardShortcuts();
            _ghost?.UpdateLabels();
        }



        private LevelStruct Hook__LevelStruct_get(Hook__LevelStruct.orig_get orig,
        User user,
        virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ l,
        Rand rng)
        {
            levelId = l.id.ToString();
            var net = _net;
            Logger.Information("[NetMod] _LevelStruct.get hook role={Role} level={LevelId}", _netRole, levelId);
            SendLevel(levelId);

            if (_netRole == NetRole.Host)
                GameDataSync.SendLevelSeed(levelId, rng, net);
            else if (_netRole == NetRole.Client)
            {
                GameDataSync.TryApplyRemoteSerializerSync();
                GameDataSync.TryApplyRemoteLevelSeed(levelId, rng);
            }

            var result = orig(user, l, rng);

            return result;
        }

        private RoomNode Hook_LevelGen_generateGraph(Hook_LevelGen.orig_generateGraph orig,
        LevelGen self,
        User user,
        virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ l,
        Rand rng)
        {
            var graphLevelId = l?.id?.ToString() ?? levelId ?? string.Empty;
            Logger.Information("[NetMod] LevelGen.generateGraph hook role={Role} level={LevelId}", _netRole, graphLevelId);

            var root = orig(self, user, l, rng);
            var graph = root?.@struct;

            if (_netRole == NetRole.Host)
            {
                GameDataSync.SendLevelGraph(graphLevelId, root, graph, rng, _net);
                var activeUser = user ?? game?.user ?? dc.Main.Class.ME?.user;
                if (activeUser != null)
                {
                    var currentRune = GameDataSync.GetBossRuneInt(activeUser);
                    if (!GameDataSync.TryGetHostBossRune(out var lastSent) || lastSent != currentRune)
                        GameDataSync.SendBossRune(activeUser, _net);
                }
            }
            else if (_netRole == NetRole.Client)
            {
                const int graphSyncWaitMs = 10000;
                if (GameDataSync.TryApplyRemoteLevelGraph(graphLevelId, graph, rng, graphSyncWaitMs, out var remoteRoot, out var reason))
                {
                    Logger.Information("[NetMod] Applied remote level graph+rand for {LevelId}", graphLevelId);
                    if (remoteRoot != null)
                        root = remoteRoot;
                }
                else
                {
                    Logger.Warning("[NetMod] Remote level graph not applied for {LevelId}: {Reason}", graphLevelId, reason);
                }
            }

            return root!;
        }


        private void Hook_MiniMap_track(Hook_MiniMap.orig_track orig, MiniMap self, Entity col, int? iconId, dc.String forcedIconColor, int? blink, bool? customTile, Tile text, dc.String itemKind, dc.String isInfectedFood)
        {

            miniMap = self;
            orig(self, col, iconId, forcedIconColor, blink, customTile, text, itemKind, isInfectedFood);
        }

        private AnimManager Hook_AnimManager_play(Hook_AnimManager.orig_play orig, AnimManager self, dc.String plays, int? queueAnim, bool? g)
        {
            if(plays == null)
                return orig(self, plays, queueAnim, g);

            var play = plays.ToString();
            if(string.IsNullOrWhiteSpace(play))
                return orig(self, plays, queueAnim, g);

            if (me != null && me?.spr?._animManager != null && ReferenceEquals(self, me.spr._animManager))
            {
                if (!DeadCellsMultiplayerMod.Ghost.KingWeaponSupport.IsInKingContext &&
                    !IsAttackAnim(play))
                    SendHeroAnim(play, queueAnim, g);
            }
            if(me != null && me.heroHead.customHeadSpr != null && ReferenceEquals(self, me.heroHead.customHeadSpr._animManager))
            {
                SendHeadAnim(play);
            }

            return orig(self, plays, queueAnim, g);
        }


        private static bool IsAttackAnim(string anim)
        {
            if (string.IsNullOrWhiteSpace(anim)) return false;
            var a = anim.Trim();
            if (a.StartsWith("w_", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.StartsWith("attack", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.StartsWith("atk", StringComparison.OrdinalIgnoreCase)) return true;
            if (a.IndexOf("shoot", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (a.IndexOf("charge", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (a.IndexOf("hold", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (a.IndexOf("bow", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (a.IndexOf("crossbow", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (a.IndexOf("xbow", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (a.IndexOf("shield", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (a.IndexOf("block", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (a.IndexOf("parry", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (a.IndexOf("whip", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public void hook_level_changed(Hook_Hero.orig_onLevelChanged orig, Hero self, Level oldLevel)
        {
            kingInitialized = false;
            _net?.ClearMobSyncQueues();
            _pendingBossCineApplyByLevel.Clear();
            _suppressBossCineEchoByLevel.Clear();
            _completedBossCineLevels.Clear();
            _appliedBossHeroTeleportLevels.Clear();
            _lastBossCineSentLevelId = null;
            _lastBossCineSentTick = 0;
            ResetFakeDeathState(unlockLocalHero: true, sendNetworkUpState: false, clearRemoteDownedTracking: false, clearDownedAnnouncements: false);
            me = self;
            me._targetable = true;
            orig(self, oldLevel);
            var currentLevelId = GetCurrentLevelId();
            if (!string.IsNullOrWhiteSpace(currentLevelId))
                SendLevel(currentLevelId);
            SendCurrentRoomTarget(force: true);
            _net?.ClearMobSyncQueues();
            EnsureHeroVisibilityAfterRoomChange(me);
            if (_netRole == NetRole.None) return;
            var net = _net;
            var localId = net?.id ?? 0;
            _ghost = new GhostHero(localId, game!, me, Logger, this);
            _ghost.SetLabel(me, GameMenu.Username);

            for (int i = 0; i < clients.Length; i++)
            {
                DisposeClientSlot(i, clearIdentity: false);
                rLastX[i] = 0;
                rLastY[i] = 0;
            }

            DrainRemoteCombatQueuesAfterLevelChange();
            GameMenu.EnqueueMainThread(() => ReceiveGhostCoords());
            MarkDiveNetGuardAfterSpawnOrRoomChange();

            _debugExplorerRevealAppliedSignature = string.Empty;
            _nextDebugExplorerRevealRetryTick = 0;
            _debugExplorerRevealAllCount = 0;
        }


        public void hook_hero_wakeup(Hook_Hero.orig_wakeup orig, Hero self, Level lvl, int cx, int cy)
        {
            me = self;
            me._targetable = true;
            _debugExplorerRevealAppliedSignature = string.Empty;
            orig(self, lvl, cx, cy);

            // 死亡重开后的首次重生：补满血，清除残留的假死 1 血（主机由原生新游戏天然满血，
            // 客机此前会残留 1 血）。一次性消费，正常关卡过渡不受影响（过关保留血量）。
            if (_pendingDeathRestartFullHeal)
            {
                _pendingDeathRestartFullHeal = false;
                try { self.fullHeal(); } catch { }

                // 显式广播"存活"状态，清除对端对本机的倒地追踪。否则（尤其客机先死时）对端会因
                // 重开前迟到的倒地包把本机永久当作倒地，导致主机看不到客机、怪物只攻击主机。
                if (_netRole != NetRole.None && _net != null)
                {
                    try { SendLocalDownedState(_net, isDowned: false, force: true); } catch { }
                }
            }

            EnsureHeroVisibilityAfterRoomChange(me);
            SendCurrentRoomTarget(force: true);
            SendEquippedWeapons(self.inventory);
            MarkDiveNetGuardAfterSpawnOrRoomChange();
        }


        public void Hook_gameinit(Hook_Game.orig_init orig, dc.pr.Game self)
        {
            game = self;
            orig(self);
        }

        public void OnHeroInit()
        {
            GameMenu.MarkInRun();
            ApplyDebugHeroRuntimeOptions();
        }

        public void OnFrameUpdate(double dt)
        {
            if (!_ready) return;
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Heartbeat();
            GameMenu.ProcessMainThreadQueue();
            GameMenu.TickMenu(dt);
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("DetectAndSendBossCine");
            DetectAndSendBossCine();
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("ApplyReceivedBossHeroTeleport");
            ApplyReceivedBossHeroTeleport();
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("ApplyReceivedBossTestLevelTeleport");
            ApplyReceivedBossTestLevelTeleport();
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("ApplyReceivedBossTestTeleport");
            ApplyReceivedBossTestTeleport();
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("ApplyReceivedBossDebugWeaponSpawns");
            ApplyReceivedBossDebugWeaponSpawns();
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("ApplyReceivedBossDebugStatAdjustments");
            ApplyReceivedBossDebugStatAdjustments();
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("ApplyReceivedBossCine");
            ApplyReceivedBossCine();
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("SuppressRemoteBossDeathCineIfNeeded");
            SuppressRemoteBossDeathCineIfNeeded();
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("OnFrameUpdate-done");
        }
        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            if (me == null) return;
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("OnHeroUpdate-start");
            ApplyDebugHeroRuntimeOptions();
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("UpdateBossDebugNpc");
            UpdateBossDebugNpc(dt);
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("TryRecoverMissedFakeDeathFromLife");
            TryRecoverMissedFakeDeathFromLife();
            if (_netRole == NetRole.None || _net == null)
                return;
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("TrySendCurrentDiveSkillInfoSnapshot");
            TrySendCurrentDiveSkillInfoSnapshot();
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("SendCurrentRoomTarget");
            SendCurrentRoomTarget(force: false);
            if (!_localFakeDead)
                SendHeroCoords();
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("ReceiveGhostCoords");
            ReceiveGhostCoords();
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("UpdateFakeDeathFlow");
            UpdateFakeDeathFlow(dt);
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("MaintainPostRevivePositionLock");
            MaintainPostRevivePositionLock();
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("ReceiveGhostWeapons");
            ReceiveGhostWeapons();
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("ReceiveGhostAttacks");
            ReceiveGhostAttacks();
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("UpdateGhostWeapons");
            UpdateGhostWeapons();
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("UpdateGhostHeads");
            UpdateGhostHeads();
            DeadCellsMultiplayerMod.Mobs.Bosses.BossDiag.Phase("OnHeroUpdate-done");
        }



    }
}
