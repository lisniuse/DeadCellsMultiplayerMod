using System.Collections.Concurrent;

namespace DeadCellsMultiplayerMod.Network;

internal enum NetworkDelivery
{
    ReliableOrdered,
    Unreliable
}

internal readonly struct QueuedPacket
{
    public readonly PooledPacket Packet;
    public readonly NetworkDelivery Delivery;
    public readonly int? ExcludePeerId;

    public QueuedPacket(PooledPacket packet, NetworkDelivery delivery, int? excludePeerId)
    {
        Packet = packet;
        Delivery = delivery;
        ExcludePeerId = excludePeerId;
    }
}

internal sealed class OutboundQueue
{
    private readonly ConcurrentQueue<QueuedPacket> _queue = new();
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Enqueue(PooledPacket packet, NetworkDelivery delivery, int? excludePeerId = null)
    {
        _queue.Enqueue(new QueuedPacket(packet, delivery, excludePeerId));
        Interlocked.Increment(ref _count);
    }

    public bool TryDequeue(out QueuedPacket packet)
    {
        if (_queue.TryDequeue(out packet))
        {
            Interlocked.Decrement(ref _count);
            return true;
        }

        return false;
    }

    public void Clear()
    {
        while (TryDequeue(out var packet))
            packet.Packet.Dispose();
    }
}
