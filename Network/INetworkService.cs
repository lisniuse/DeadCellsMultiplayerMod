using System.Buffers;

namespace DeadCellsMultiplayerMod.Network;

internal readonly struct NetworkReceiveBuffer : IDisposable
{
    private readonly bool _pooled;

    public readonly int PeerId;
    public readonly byte[] Buffer;
    public readonly int Length;

    public NetworkReceiveBuffer(int peerId, byte[] buffer, int length, bool pooled)
    {
        PeerId = peerId;
        Buffer = buffer;
        Length = length;
        _pooled = pooled;
    }

    public ReadOnlySpan<byte> Span => Buffer.AsSpan(0, Length);

    public void Dispose()
    {
        if (_pooled && Buffer.Length > 0)
            ArrayPool<byte>.Shared.Return(Buffer);
    }
}

internal interface INetworkService : IDisposable
{
    bool IsRunning { get; }
    bool HasPeers { get; }
    int PeerCount { get; }

    event Action<NetworkReceiveBuffer>? PacketReceived;
    event Action<int>? PeerConnected;
    event Action<int>? PeerDisconnected;

    void StartHost(int port, int maxPeers, CancellationToken cancellationToken);
    void StartClient(string host, int port, CancellationToken cancellationToken);
    bool TrySend(byte[] buffer, int length, NetworkDelivery delivery, int? excludePeerId, out int peerCount);
    bool TrySendToPeer(int peerId, byte[] buffer, int length, NetworkDelivery delivery);
    void DisconnectPeer(int peerId);
}
