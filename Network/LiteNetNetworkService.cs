using System.Buffers;
using LiteNetLib;
using Serilog;

namespace DeadCellsMultiplayerMod.Network;

internal sealed class LiteNetNetworkService : INetworkService
{
    private const string ConnectionKey = "DeadCellsMultiplayerMod/2";
    private readonly ILogger _log;
    private readonly NetworkDiagnostics _diagnostics;
    private readonly object _peerSync = new();
    private readonly List<NetPeer> _peers = new();

    private EventBasedNetListener? _listener;
    private NetManager? _manager;
    private CancellationTokenSource? _serviceCts;
    private Task? _pollTask;
    private bool _disposed;

    public LiteNetNetworkService(ILogger log, NetworkDiagnostics diagnostics)
    {
        _log = log;
        _diagnostics = diagnostics;
    }

    public bool IsRunning { get; private set; }

    public bool HasPeers
    {
        get
        {
            lock (_peerSync)
                return _peers.Count > 0;
        }
    }

    public int PeerCount
    {
        get
        {
            lock (_peerSync)
                return _peers.Count;
        }
    }

    public event Action<NetworkReceiveBuffer>? PacketReceived;
    public event Action<int>? PeerConnected;
    public event Action<int>? PeerDisconnected;

    public void StartHost(int port, int maxPeers, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        Initialize();

        _listener!.ConnectionRequestEvent += request =>
        {
            if (PeerCount < maxPeers)
                request.AcceptIfKey(ConnectionKey);
            else
                request.Reject();
        };

        if (!_manager!.Start(port))
            throw new InvalidOperationException("LiteNetLib host failed to bind UDP port " + port);

        IsRunning = true;
        StartPolling(cancellationToken);
        _log.Information("[NetNode] LiteNetLib host service started on UDP port {Port}", port);
    }

    public void StartClient(string host, int port, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        Initialize();

        if (!_manager!.Start())
            throw new InvalidOperationException("LiteNetLib client failed to start");

        _manager.Connect(host, port, ConnectionKey);
        IsRunning = true;
        StartPolling(cancellationToken);
        _log.Information("[NetNode] LiteNetLib client service connecting to {Host}:{Port}", host, port);
    }

    public bool TrySend(byte[] buffer, int length, NetworkDelivery delivery, int? excludePeerId, out int peerCount)
    {
        peerCount = 0;
        if (!IsRunning || _manager == null || length <= 0)
            return false;

        var method = delivery == NetworkDelivery.Unreliable
            ? DeliveryMethod.Unreliable
            : DeliveryMethod.ReliableOrdered;

        var startTicks = _diagnostics.IsEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        lock (_peerSync)
        {
            for (var i = 0; i < _peers.Count; i++)
            {
                var peer = _peers[i];
                if (excludePeerId.HasValue && peer.Id == excludePeerId.Value)
                    continue;

                peer.Send(buffer, 0, length, method);
                peerCount++;
            }
        }

        if (peerCount == 0)
            return false;

        if (startTicks != 0)
            _diagnostics.RecordSend(System.Diagnostics.Stopwatch.GetTimestamp() - startTicks, length * peerCount);

        return true;
    }

    public bool TrySendToPeer(int peerId, byte[] buffer, int length, NetworkDelivery delivery)
    {
        if (!IsRunning || _manager == null || length <= 0)
            return false;

        NetPeer? target = null;
        lock (_peerSync)
        {
            for (var i = 0; i < _peers.Count; i++)
            {
                if (_peers[i].Id == peerId)
                {
                    target = _peers[i];
                    break;
                }
            }
        }

        if (target == null)
            return false;

        var method = delivery == NetworkDelivery.Unreliable
            ? DeliveryMethod.Unreliable
            : DeliveryMethod.ReliableOrdered;

        var startTicks = _diagnostics.IsEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        target.Send(buffer, 0, length, method);
        if (startTicks != 0)
            _diagnostics.RecordSend(System.Diagnostics.Stopwatch.GetTimestamp() - startTicks, length);
        return true;
    }

    public void DisconnectPeer(int peerId)
    {
        NetPeer? target = null;
        lock (_peerSync)
        {
            for (var i = 0; i < _peers.Count; i++)
            {
                if (_peers[i].Id == peerId)
                {
                    target = _peers[i];
                    break;
                }
            }
        }

        try { target?.Disconnect(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        IsRunning = false;
        try { _serviceCts?.Cancel(); } catch { }
        try { _manager?.Stop(); } catch { }
        _serviceCts?.Dispose();
        _serviceCts = null;
        _manager = null;
        _listener = null;
        lock (_peerSync)
            _peers.Clear();
    }

    private void Initialize()
    {
        if (_manager != null)
            return;

        _listener = new EventBasedNetListener();
        _listener.PeerConnectedEvent += peer =>
        {
            lock (_peerSync)
                _peers.Add(peer);
            PeerConnected?.Invoke(peer.Id);
        };
        _listener.PeerDisconnectedEvent += (peer, _) =>
        {
            lock (_peerSync)
                _peers.Remove(peer);
            PeerDisconnected?.Invoke(peer.Id);
        };
        _listener.NetworkReceiveEvent += OnNetworkReceive;
        _manager = new NetManager(_listener)
        {
            IPv6Enabled = false
        };
    }

    private void StartPolling(CancellationToken parentToken)
    {
        _serviceCts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        var token = _serviceCts.Token;
        _pollTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && !_disposed)
            {
                try
                {
                    _manager?.PollEvents();
                }
                catch (Exception ex)
                {
                    _log.Warning("[NetNode] LiteNetLib poll failed: {Message}", ex.Message);
                }

                try
                {
                    await Task.Delay(4, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
            }
        }, token);
    }

    private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        var startTicks = _diagnostics.IsEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        try
        {
            var length = reader.AvailableBytes;
            if (length <= 0)
                return;

            var buffer = ArrayPool<byte>.Shared.Rent(length);
            reader.GetBytes(buffer, length);
            var receive = new NetworkReceiveBuffer(peer.Id, buffer, length, pooled: true);
            if (startTicks != 0)
                _diagnostics.RecordReceive(PacketOpcode.None, System.Diagnostics.Stopwatch.GetTimestamp() - startTicks, length);
            PacketReceived?.Invoke(receive);
        }
        catch (Exception ex)
        {
            _log.Warning("[NetNode] LiteNetLib receive failed: {Message}", ex.Message);
        }
        finally
        {
            reader.Recycle();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LiteNetNetworkService));
    }
}
