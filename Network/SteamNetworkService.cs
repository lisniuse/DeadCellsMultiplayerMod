using System.Buffers;
using DeadCellsMultiplayerMod;
using Serilog;
using Steamworks;

namespace DeadCellsMultiplayerMod.Network;

internal sealed class SteamNetworkService : INetworkService
{
    private const int ClientToHostChannel = 0;
    private const int HostToClientChannel = 1;
    private const double KeepAliveSeconds = 6.0;

    private static readonly byte[] KeepAliveBytes = { (byte)'D', (byte)'C', 1, (byte)PacketOpcode.None };

    private readonly ILogger _log;
    private readonly NetworkDiagnostics _diagnostics;
    private readonly object _peerSync = new();
    private readonly Dictionary<ulong, int> _peerIdsBySteamId = new();
    private readonly Dictionary<int, ulong> _steamIdsByPeerId = new();
    private readonly global::NetRole _role;
    private readonly CSteamID _hostSteamId;
    private readonly int _hostPort;
    private readonly string? _hostIp;

    private SteamP2PWorkerBridge? _bridge;
    private CancellationTokenSource? _serviceCts;
    private Task? _pollTask;
    private int _nextPeerId;
    private bool _disposed;
    private long _lastKeepAliveTicks;

    public SteamNetworkService(
        ILogger log,
        NetworkDiagnostics diagnostics,
        global::NetRole role,
        CSteamID hostSteamId,
        int hostPort,
        string? hostIp)
    {
        _log = log;
        _diagnostics = diagnostics;
        _role = role;
        _hostSteamId = hostSteamId;
        _hostPort = hostPort;
        _hostIp = hostIp;
    }

    public bool IsRunning => !_disposed && _bridge?.IsRunning == true;

    public bool HasPeers
    {
        get
        {
            if (_role == global::NetRole.Client && _hostSteamId.m_SteamID != 0UL)
                return IsRunning;

            lock (_peerSync)
                return _steamIdsByPeerId.Count > 0;
        }
    }

    public int PeerCount
    {
        get
        {
            lock (_peerSync)
                return _steamIdsByPeerId.Count;
        }
    }

    public SteamConnect.HostLobbyResult? HostLobbyResult => _bridge?.HostLobbyResult;
    public ulong LocalSteamId => _bridge?.LocalSteamId ?? 0UL;

    public event Action<NetworkReceiveBuffer>? PacketReceived;
    public event Action<int>? PeerConnected;
    public event Action<int>? PeerDisconnected;

    public void StartHost(int port, int maxPeers, CancellationToken cancellationToken)
    {
        if (_role != global::NetRole.Host)
            throw new InvalidOperationException("SteamNetworkService was not created for host role");

        StartBridge(cancellationToken);
        _log.Information("[NetNode] Steam network service started as host");
    }

    public void StartClient(string host, int port, CancellationToken cancellationToken)
    {
        if (_role != global::NetRole.Client)
            throw new InvalidOperationException("SteamNetworkService was not created for client role");

        StartBridge(cancellationToken);
        _log.Information("[NetNode] Steam network service started as client hostSteamId={HostSteamId}", _hostSteamId.m_SteamID);
    }

    public bool TrySend(byte[] buffer, int length, NetworkDelivery delivery, int? excludePeerId, out int peerCount)
    {
        peerCount = 0;
        if (!TryGetBridge(out var bridge) || buffer.Length < length || length <= 0)
            return false;

        var sendType = ResolveSendType(delivery);
        var channel = GetOutgoingChannel();
        var startTicks = _diagnostics.IsEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

        if (_role == global::NetRole.Client)
        {
            var hostSteamId = _hostSteamId.m_SteamID;
            if (hostSteamId == 0UL)
                return false;

            if (!bridge.TrySend(hostSteamId, sendType, channel, buffer, length, out var error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                    _log.Warning("[NetNode] Steam send failed to host {SteamId}: {Error}", hostSteamId, error);
                return false;
            }

            peerCount = 1;
            if (startTicks != 0)
                _diagnostics.RecordSend(System.Diagnostics.Stopwatch.GetTimestamp() - startTicks, length);
            return true;
        }

        List<ulong> steamIds;
        lock (_peerSync)
        {
            steamIds = new List<ulong>(_steamIdsByPeerId.Count);
            foreach (var kv in _steamIdsByPeerId)
            {
                if (!excludePeerId.HasValue || kv.Key != excludePeerId.Value)
                    steamIds.Add(kv.Value);
            }
        }

        if (steamIds.Count == 0)
            return false;

        for (var i = 0; i < steamIds.Count; i++)
        {
            if (bridge.TrySend(steamIds[i], sendType, channel, buffer, length, out var error))
            {
                peerCount++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(error))
                _log.Warning("[NetNode] Steam send failed to {SteamId}: {Error}", steamIds[i], error);
        }

        if (peerCount == 0)
            return false;

        if (startTicks != 0)
            _diagnostics.RecordSend(System.Diagnostics.Stopwatch.GetTimestamp() - startTicks, length * peerCount);

        return true;
    }

    public bool TrySendToPeer(int peerId, byte[] buffer, int length, NetworkDelivery delivery)
    {
        if (!TryGetBridge(out var bridge) || buffer.Length < length || length <= 0)
            return false;

        if (!TryGetSteamId(peerId, out var steamId))
            return false;

        var startTicks = _diagnostics.IsEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        if (!bridge.TrySend(steamId, ResolveSendType(delivery), GetOutgoingChannel(), buffer, length, out var error))
        {
            if (!string.IsNullOrWhiteSpace(error))
                _log.Warning("[NetNode] Steam send failed to {SteamId}: {Error}", steamId, error);
            return false;
        }

        if (startTicks != 0)
            _diagnostics.RecordSend(System.Diagnostics.Stopwatch.GetTimestamp() - startTicks, length);
        return true;
    }

    public void DisconnectPeer(int peerId)
    {
        if (!TryGetSteamId(peerId, out var steamId))
            return;

        try { _bridge?.TryClosePeer(steamId); } catch { }
        RemovePeer(steamId, notify: true);
    }

    public bool TrySetRichPresence(string key, string value, out string error)
    {
        error = string.Empty;
        return _bridge != null && _bridge.TrySetRichPresence(key, value, out error);
    }

    public bool TryClearRichPresence(out string error)
    {
        error = string.Empty;
        return _bridge != null && _bridge.TryClearRichPresence(out error);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try { _serviceCts?.Cancel(); } catch { }
        try { _bridge?.Dispose(); } catch { }
        _serviceCts?.Dispose();
        _serviceCts = null;
        _bridge = null;
        lock (_peerSync)
        {
            _peerIdsBySteamId.Clear();
            _steamIdsByPeerId.Clear();
        }
    }

    private void StartBridge(CancellationToken cancellationToken)
    {
        if (!SteamP2PWorkerBridge.TryStart(_role, _hostSteamId, _hostPort, _hostIp, out var bridge, out var error) || bridge == null)
            throw new InvalidOperationException(error);

        _bridge = bridge;
        _serviceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pollTask = Task.Run(() => PollLoop(_serviceCts.Token), _serviceCts.Token);
    }

    private async Task PollLoop(CancellationToken ct)
    {
        var expectedChannel = _role == global::NetRole.Host ? ClientToHostChannel : HostToClientChannel;

        while (!ct.IsCancellationRequested && !_disposed)
        {
            var hadWork = false;
            var bridge = _bridge;
            if (bridge == null)
                return;

            while (bridge.TryReadPacket(out var packet))
            {
                hadWork = true;
                if (packet.Channel != expectedChannel || packet.Payload.Length == 0)
                    continue;

                if (_role == global::NetRole.Client &&
                    _hostSteamId.m_SteamID != 0UL &&
                    packet.RemoteSteamId != _hostSteamId.m_SteamID)
                {
                    continue;
                }

                var peerId = EnsurePeer(packet.RemoteSteamId);
                var buffer = ArrayPool<byte>.Shared.Rent(packet.Payload.Length);
                Buffer.BlockCopy(packet.Payload, 0, buffer, 0, packet.Payload.Length);
                _diagnostics.RecordReceive(PacketOpcode.None, 0, packet.Payload.Length);
                PacketReceived?.Invoke(new NetworkReceiveBuffer(peerId, buffer, packet.Payload.Length, pooled: true));
            }

            while (bridge.TryReadWarning(out var warning))
            {
                hadWork = true;
                _log.Warning("[NetNode] Steam P2P worker: {Warning}", warning);
            }

            while (bridge.TryReadSessionFail(out var steamId))
            {
                hadWork = true;
                _log.Warning("[NetNode] P2P session failed: remote={RemoteId}", steamId);
                RemovePeer(steamId, notify: true);
            }

            if (!hadWork)
                TrySendKeepAlive();

            if (!hadWork)
            {
                try
                {
                    await Task.Delay(8, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private int EnsurePeer(ulong steamId)
    {
        var created = false;
        int peerId;
        lock (_peerSync)
        {
            if (_peerIdsBySteamId.TryGetValue(steamId, out var existingPeerId))
                return existingPeerId;

            peerId = _role == global::NetRole.Client ? 1 : ++_nextPeerId;
            _peerIdsBySteamId[steamId] = peerId;
            _steamIdsByPeerId[peerId] = steamId;
            created = true;
        }

        if (created)
            PeerConnected?.Invoke(peerId);
        return peerId;
    }

    private void RemovePeer(ulong steamId, bool notify)
    {
        int peerId;
        lock (_peerSync)
        {
            if (!_peerIdsBySteamId.TryGetValue(steamId, out peerId))
                return;

            _peerIdsBySteamId.Remove(steamId);
            _steamIdsByPeerId.Remove(peerId);
        }

        if (notify)
            PeerDisconnected?.Invoke(peerId);
    }

    private bool TryGetSteamId(int peerId, out ulong steamId)
    {
        lock (_peerSync)
            return _steamIdsByPeerId.TryGetValue(peerId, out steamId);
    }

    private bool TryGetBridge(out SteamP2PWorkerBridge bridge)
    {
        bridge = _bridge!;
        return !_disposed && bridge != null && bridge.IsRunning;
    }

    private void TrySendKeepAlive()
    {
        if (!TryGetBridge(out var bridge))
            return;

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var minTicks = (long)(System.Diagnostics.Stopwatch.Frequency * KeepAliveSeconds);
        if (_lastKeepAliveTicks != 0 && now - _lastKeepAliveTicks < minTicks)
            return;

        _lastKeepAliveTicks = now;
        var channel = GetOutgoingChannel();
        if (_role == global::NetRole.Client)
        {
            if (_hostSteamId.m_SteamID != 0UL)
                bridge.TrySend(_hostSteamId.m_SteamID, EP2PSend.k_EP2PSendReliable, channel, KeepAliveBytes, out _);
            return;
        }

        List<ulong> steamIds;
        lock (_peerSync)
            steamIds = new List<ulong>(_steamIdsByPeerId.Values);

        for (var i = 0; i < steamIds.Count; i++)
            bridge.TrySend(steamIds[i], EP2PSend.k_EP2PSendReliable, channel, KeepAliveBytes, out _);
    }

    private int GetOutgoingChannel()
    {
        return _role == global::NetRole.Host ? HostToClientChannel : ClientToHostChannel;
    }

    private static EP2PSend ResolveSendType(NetworkDelivery delivery)
    {
        return delivery == NetworkDelivery.Unreliable
            ? EP2PSend.k_EP2PSendUnreliable
            : EP2PSend.k_EP2PSendReliable;
    }
}
