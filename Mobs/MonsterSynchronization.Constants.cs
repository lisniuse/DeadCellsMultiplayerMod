namespace DeadCellsMultiplayerMod.Mobs.MobsSynchronization
{
    public partial class MobsSynchronization
    {
        private const int ParsedAnimPayloadCacheLimit = 1024;
        private const double ClientInterpolationAlpha = 0.70;
        // 持盾冲刺突刺：冲刺执行后持续检测与 GhostKing 碰撞的时间窗口，以及碰撞检测的范围框大小。
        private const double HostDashLungeWindowSeconds = 1.6;
        private const double HostDashLungeContactRangeX = 42.0;
        private const double HostDashLungeContactRangeY = 42.0;
        /// <summary>Coalesce rapid client hit MOBEVENT spam; keep small so multi-hit weapons still report.</summary>
        private const double ClientAnimSpeedEpsilon = 0.05;
        private static readonly bool ClientSyncVerticalPosition = false;
        private const double ClientTurnSnapDeltaPx = 2.0;
        private const double MobStatePositionEpsilon = 0.35;
        private const double HostMobStateMidPositionEpsilon = 1.20;
        private const double HostMobStateDormantPositionEpsilon = 6.00;
        private const double MobFallbackMinimumScoreGap = 4.0;
        private const double MobStateTypeOrphanRebindSearchRadius = 1200.0;
        private const double MobStateTypeOrphanRebindSearchRadiusSq = MobStateTypeOrphanRebindSearchRadius * MobStateTypeOrphanRebindSearchRadius;
        private const double ClientAiAuthorityLockDurationSeconds = 99999.0;
        private const double AuthoritativeAffectPresenceSeconds = 99999.0;
        private const double PixelsPerCase = 24.0;
        private const string ContactAttackPacketSkillId = "@contact";
        private const string OldSkillPreparePacketPrefix = "@oldprep:";
        private const string OldSkillChargeCompletePacketPrefix = "@oldcc:";
        private const string OldSkillExecutePacketPrefix = "@oldexec:";
        private const string NewSkillExecutePacketPrefix = "@newexec:";
        private const int MobWirePacketByteBudget = 4096;
        private static double GetClientInterpolationAlpha()
        {
            var configured = MultiplayerSettingsStorage.MobsInterpolationQuality;
            if (double.IsNaN(configured) || double.IsInfinity(configured))
                return ClientInterpolationAlpha;

            return System.Math.Clamp(configured, 0.20, 1.00);
        }

        /// <summary>User toggle (and dev flag); combined per-mob with <c>!hasGravity</c> for client mob Y sync.</summary>
        private static bool IsClientVerticalSyncEnabled()
        {
            return ClientSyncVerticalPosition || MultiplayerSettingsStorage.SyncVerticalPosition;
        }
    }
}
