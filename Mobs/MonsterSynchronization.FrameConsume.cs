namespace DeadCellsMultiplayerMod.Mobs.MobsSynchronization
{
    /// <summary>
    /// Per-frame incoming network consume for mob sync. Keep this synchronous: async state machines and
    /// <c>Task.Yield()</c> in the frame path create avoidable scheduler churn and sync-over-async stalls.
    /// </summary>
    public partial class MobsSynchronization
    {
        private static void RunHostIncomingFrameConsume(NetNode net)
        {
            if (!IsIncomingMobIdentityReady())
                return;

            ConsumeIncomingClientMobStates(net);
            ConsumeIncomingMobDraws(net);
            ConsumeIncomingMobDies(net);
            ConsumeIncomingMobHits(net);
        }

        private static void RunClientIncomingFrameConsume(NetNode net)
        {
            if (!IsIncomingMobIdentityReady())
                return;

            ConsumeIncomingHostMobStates(net);
            ConsumeIncomingHostMobMoves(net);
            ConsumeIncomingHostMobAttacks(net);
            ConsumeIncomingMobDies(net);
            ConsumeIncomingMobHits(net);
        }
    }
}
