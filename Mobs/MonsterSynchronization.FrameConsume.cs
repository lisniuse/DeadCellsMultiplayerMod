using System.Threading.Tasks;
using ModCore.Events.Interfaces.Game;

namespace DeadCellsMultiplayerMod.Mobs.MobsSynchronization
{
    /// <summary>
    /// Per-frame incoming network consume for mob sync. Uses <c>await Task.Yield()</c> between stages so DCCM can schedule
    /// continuations without any environment configuration. <see cref="IOnFrameUpdate.OnFrameUpdate"/> runs the task to
    /// completion via <c>GetAwaiter().GetResult()</c>. Hashlink <c>Mob</c>/<c>Level</c> access stays inside <c>Consume*</c> only.
    /// </summary>
    public partial class MobsSynchronization
    {
        private static async Task RunHostIncomingFrameConsumeAsync(NetNode net)
        {
            Bosses.BossDiag.Phase("Host.Consume.ClientMobStates");
            ConsumeIncomingClientMobStates(net);
            await Task.Yield();
            Bosses.BossDiag.Phase("Host.Consume.MobDraws");
            ConsumeIncomingMobDraws(net);
            await Task.Yield();
            Bosses.BossDiag.Phase("Host.Consume.MobDies");
            ConsumeIncomingMobDies(net);
            await Task.Yield();
            Bosses.BossDiag.Phase("Host.Consume.MobHits");
            ConsumeIncomingMobHits(net);
            Bosses.BossDiag.Phase("Host.Consume.done");
        }

        private static async Task RunClientIncomingFrameConsumeAsync(NetNode net)
        {
            Bosses.BossDiag.Phase("Client.Consume.HostMobStates");
            ConsumeIncomingHostMobStates(net);
            await Task.Yield();
            Bosses.BossDiag.Phase("Client.Consume.HostMobAttacks");
            ConsumeIncomingHostMobAttacks(net);
            await Task.Yield();
            Bosses.BossDiag.Phase("Client.Consume.MobDies");
            ConsumeIncomingMobDies(net);
            await Task.Yield();
            Bosses.BossDiag.Phase("Client.Consume.MobHits");
            ConsumeIncomingMobHits(net);
            Bosses.BossDiag.Phase("Client.Consume.done");
        }
    }
}
