using System.Diagnostics;
using System.Net;
using DeadCellsMultiplayerMod;
using DeadCellsMultiplayerMod.Network;

public sealed partial class NetNode
{
    private const int BinaryFlushBudgetBytes = 32 * 1024;

    private bool StartLiteNetHostService(int port)
    {
        if (_useSteamTransport || _cts == null)
            return false;

        try
        {
            var service = new LiteNetNetworkService(_log, _networkDiagnostics);
            service.PacketReceived += OnBinaryPacketReceived;
            service.PeerConnected += OnLiteNetPeerConnected;
            service.PeerDisconnected += OnLiteNetPeerDisconnected;
            service.StartHost(port, ClientIds.Length, _cts.Token);
            _binaryNetwork = service;
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning("[NetNode] LiteNetLib host service disabled: {Message}", ex.Message);
            return false;
        }
    }

    private bool StartLiteNetClientService(IPEndPoint endpoint)
    {
        if (_useSteamTransport || _cts == null || endpoint.Port <= 0)
            return false;

        try
        {
            var service = new LiteNetNetworkService(_log, _networkDiagnostics);
            service.PacketReceived += OnBinaryPacketReceived;
            service.PeerConnected += OnLiteNetPeerConnected;
            service.PeerDisconnected += OnLiteNetPeerDisconnected;
            service.StartClient(endpoint.Address.ToString(), endpoint.Port, _cts.Token);
            _binaryNetwork = service;
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning("[NetNode] LiteNetLib client service disabled: {Message}", ex.Message);
            return false;
        }
    }

    private void DisposeLiteNetService()
    {
        var service = _binaryNetwork;
        _binaryNetwork = null;
        _steamNetwork = null;
        if (service != null)
        {
            try { service.PacketReceived -= OnBinaryPacketReceived; } catch { }
            try { service.PeerConnected -= OnLiteNetPeerConnected; } catch { }
            try { service.PeerDisconnected -= OnLiteNetPeerDisconnected; } catch { }
            try { service.Dispose(); } catch { }
        }
        _outboundQueue.Clear();
        lock (_binaryPeerLock)
            _binaryPeerUserIds.Clear();
    }

    private void OnBinaryPacketReceived(NetworkReceiveBuffer packet)
    {
        GameMenu.EnqueueMainThread(() =>
        {
            var opcode = PacketOpcode.None;
            var startTicks = _networkDiagnostics.IsEnabled ? Stopwatch.GetTimestamp() : 0;
            try
            {
                if (!_packetManager.TryDispatch(packet.Span, this, packet.PeerId, out opcode))
                    _networkDiagnostics.RecordDropped(opcode);
            }
            catch (Exception ex)
            {
                _networkDiagnostics.RecordDropped(opcode);
                _log.Warning("[NetNode] Binary packet dispatch failed: {Message}", ex.Message);
            }
            finally
            {
                if (startTicks != 0)
                    _networkDiagnostics.RecordDispatch(opcode, Stopwatch.GetTimestamp() - startTicks);
                packet.Dispose();
                _networkDiagnostics.LogIfDue(_log);
            }
        });
    }

    private bool TrySendBinaryHello()
    {
        if (_role != NetRole.Client || ID <= 0)
            return false;

        var startTicks = _networkDiagnostics.IsEnabled ? Stopwatch.GetTimestamp() : 0;
        if (!_packetManager.TryCreate(new HelloPacket(ID), out var packet) || packet == null)
            return false;

        if (startTicks != 0)
            _networkDiagnostics.RecordSerialization(packet.Opcode, Stopwatch.GetTimestamp() - startTicks, packet.Length);

        return TryQueueBinaryPacket(packet, NetworkDelivery.ReliableOrdered);
    }

    private bool TrySendBinaryReady(int userId, bool ready, int? excludePeerId = null, bool includeClientHello = true)
    {
        if (includeClientHello)
            TrySendBinaryHello();

        var startTicks = _networkDiagnostics.IsEnabled ? Stopwatch.GetTimestamp() : 0;
        if (!_packetManager.TryCreate(new ReadyPacket(userId, ready), out var packet) || packet == null)
            return false;

        if (startTicks != 0)
            _networkDiagnostics.RecordSerialization(packet.Opcode, Stopwatch.GetTimestamp() - startTicks, packet.Length);

        return TryQueueBinaryPacket(packet, NetworkDelivery.ReliableOrdered, excludePeerId);
    }

    private bool TrySendBinarySeed(int seed)
    {
        var startTicks = _networkDiagnostics.IsEnabled ? Stopwatch.GetTimestamp() : 0;
        if (!_packetManager.TryCreate(new SeedPacket(seed), out var packet) || packet == null)
            return false;

        if (startTicks != 0)
            _networkDiagnostics.RecordSerialization(packet.Opcode, Stopwatch.GetTimestamp() - startTicks, packet.Length);

        return TryQueueBinaryPacket(packet, NetworkDelivery.ReliableOrdered);
    }

    private bool TrySendBinaryRestart(int seed)
    {
        var startTicks = _networkDiagnostics.IsEnabled ? Stopwatch.GetTimestamp() : 0;
        if (!_packetManager.TryCreate(new RestartPacket(seed), out var packet) || packet == null)
            return false;

        if (startTicks != 0)
            _networkDiagnostics.RecordSerialization(packet.Opcode, Stopwatch.GetTimestamp() - startTicks, packet.Length);

        return TryQueueBinaryPacket(packet, NetworkDelivery.ReliableOrdered);
    }

    private bool TrySendBinaryCoopState(int userId, string? coopId, bool hasContinueSave, int? excludePeerId = null, bool includeClientHello = true)
    {
        if (includeClientHello)
            TrySendBinaryHello();

        var startTicks = _networkDiagnostics.IsEnabled ? Stopwatch.GetTimestamp() : 0;
        if (!_packetManager.TryCreate(new CoopStatePacket(userId, coopId, hasContinueSave), out var packet) || packet == null)
            return false;

        if (startTicks != 0)
            _networkDiagnostics.RecordSerialization(packet.Opcode, Stopwatch.GetTimestamp() - startTicks, packet.Length);

        return TryQueueBinaryPacket(packet, NetworkDelivery.ReliableOrdered, excludePeerId);
    }

    private bool TrySendBinaryLaunchMode(int action, bool custom, bool streamEnabled, bool newCoopWorldPrepared, string? coopId, bool hostHasContinueSave)
    {
        var startTicks = _networkDiagnostics.IsEnabled ? Stopwatch.GetTimestamp() : 0;
        if (!_packetManager.TryCreate(
                new LaunchModePacket(action, custom, streamEnabled, newCoopWorldPrepared, coopId, hostHasContinueSave),
                out var packet) ||
            packet == null)
        {
            return false;
        }

        if (startTicks != 0)
            _networkDiagnostics.RecordSerialization(packet.Opcode, Stopwatch.GetTimestamp() - startTicks, packet.Length);

        return TryQueueBinaryPacket(packet, NetworkDelivery.ReliableOrdered);
    }

    private bool TryQueueBinaryPacket(PooledPacket packet, NetworkDelivery delivery, int? excludePeerId = null)
    {
        var service = _binaryNetwork;
        if (service == null || !service.IsRunning || !service.HasPeers)
        {
            _networkDiagnostics.RecordSkipped(packet.Opcode);
            packet.Dispose();
            return false;
        }

        _outboundQueue.Enqueue(packet, delivery, excludePeerId);
        _networkDiagnostics.RecordQueued(packet.Opcode, _outboundQueue.Count);
        var result = ByteBudgetFlush.Flush(_outboundQueue, service, BinaryFlushBudgetBytes, _networkDiagnostics);
        _networkDiagnostics.LogIfDue(_log);
        return result.FlushedPackets > 0;
    }

    private bool TrySendLegacyLine(string line, NetworkDelivery delivery, int? targetPeerId = null, int? excludePeerId = null)
    {
        var startTicks = _networkDiagnostics.IsEnabled ? Stopwatch.GetTimestamp() : 0;
        if (!_packetManager.TryCreateLegacyText(line, out var packet) || packet == null)
            return false;

        if (startTicks != 0)
            _networkDiagnostics.RecordSerialization(packet.Opcode, Stopwatch.GetTimestamp() - startTicks, packet.Length);

        var service = _binaryNetwork;
        if (service == null || !service.IsRunning || !service.HasPeers)
        {
            _networkDiagnostics.RecordSkipped(packet.Opcode);
            packet.Dispose();
            return false;
        }

        var sent = false;
        if (targetPeerId.HasValue)
        {
            sent = service.TrySendToPeer(targetPeerId.Value, packet.Buffer, packet.Length, delivery);
            if (sent)
            {
                _networkDiagnostics.RecordFlushed(packet.Opcode, packet.Length);
                _networkDiagnostics.RecordFlush(1, 0, packet.Length);
            }
            else
            {
                _networkDiagnostics.RecordSkipped(packet.Opcode);
            }
        }
        else
        {
            _outboundQueue.Enqueue(packet, delivery, excludePeerId);
            packet = null;
            _networkDiagnostics.RecordQueued(PacketOpcode.LegacyText, _outboundQueue.Count);
            var result = ByteBudgetFlush.Flush(_outboundQueue, service, BinaryFlushBudgetBytes, _networkDiagnostics);
            sent = result.FlushedPackets > 0;
        }

        packet?.Dispose();
        _networkDiagnostics.LogIfDue(_log);
        return sent;
    }

    private void RegisterBinaryPeer(int peerId, int userId)
    {
        if (userId <= 0)
            return;

        if (_role == NetRole.Host)
        {
            lock (_clientsLock)
            {
                if (!_clients.ContainsKey(userId))
                    return;
            }
        }

        lock (_binaryPeerLock)
            _binaryPeerUserIds[peerId] = userId;
    }

    private bool TryResolveBinaryUserId(int peerId, int packetUserId, out int userId)
    {
        if (_role != NetRole.Host)
        {
            userId = packetUserId;
            return userId > 0;
        }

        lock (_binaryPeerLock)
        {
            if (_binaryPeerUserIds.TryGetValue(peerId, out userId))
                return true;
        }

        if (packetUserId <= 0)
        {
            userId = 0;
            return false;
        }

        lock (_clientsLock)
        {
            if (!_clients.ContainsKey(packetUserId))
            {
                userId = 0;
                return false;
            }
        }

        RegisterBinaryPeer(peerId, packetUserId);
        userId = packetUserId;
        return true;
    }

    private void RemoveBinaryPeersForUser(int userId)
    {
        if (userId <= 0)
            return;

        lock (_binaryPeerLock)
        {
            var removeIds = new List<int>();
            foreach (var kv in _binaryPeerUserIds)
            {
                if (kv.Value == userId)
                    removeIds.Add(kv.Key);
            }

            for (var i = 0; i < removeIds.Count; i++)
                _binaryPeerUserIds.Remove(removeIds[i]);
        }
    }

    void INetworkPacketHandler.HandleHello(in HelloPacket packet, int peerId)
    {
        RegisterBinaryPeer(peerId, packet.UserId);
    }

    void INetworkPacketHandler.HandleReady(in ReadyPacket packet, int peerId)
    {
        if (!TryResolveBinaryUserId(peerId, packet.UserId, out var effectiveId))
        {
            _networkDiagnostics.RecordDropped(PacketOpcode.Ready);
            return;
        }

        lock (_sync)
        {
            var state = GetOrCreateRemoteLocked(effectiveId);
            state.Ready = packet.Ready;
            state.HasRemote = true;
            _hasRemote = true;
            if (_primaryRemoteId == 0)
                _primaryRemoteId = effectiveId;
        }

        GameMenu.ReceiveRemoteReady(effectiveId, packet.Ready);

        if (_role == NetRole.Host)
        {
            TrySendBinaryReady(effectiveId, packet.Ready, excludePeerId: peerId, includeClientHello: false);
        }
    }

    void INetworkPacketHandler.HandleSeed(in SeedPacket packet, int peerId)
    {
        if (_role == NetRole.Host)
            return;

        lock (_sync)
            _hasRemote = true;

        GameMenu.ReceiveHostRunSeed(packet.Seed);
        _log.Information("[NetNode] Received host run seed {Seed}", packet.Seed);
    }

    void INetworkPacketHandler.HandleRestart(in RestartPacket packet, int peerId)
    {
        if (_role == NetRole.Host)
            return;

        lock (_sync)
            _hasRemote = true;

        GameMenu.ReceiveHostRunRestart(packet.Seed);
    }

    void INetworkPacketHandler.HandleCoopState(in CoopStatePacket packet, int peerId)
    {
        if (!TryResolveBinaryUserId(peerId, packet.UserId, out var effectiveId))
        {
            _networkDiagnostics.RecordDropped(PacketOpcode.CoopState);
            return;
        }

        lock (_sync)
        {
            var state = GetOrCreateRemoteLocked(effectiveId);
            state.CoopId = packet.CoopId;
            state.HasContinueSave = packet.HasContinueSave;
            state.HasRemote = true;
            _hasRemote = true;
            if (_primaryRemoteId == 0)
                _primaryRemoteId = effectiveId;
        }

        GameMenu.ReceiveRemoteCoopState(effectiveId, packet.CoopId, packet.HasContinueSave);

        if (_role == NetRole.Host)
        {
            TrySendBinaryCoopState(effectiveId, packet.CoopId, packet.HasContinueSave, excludePeerId: peerId, includeClientHello: false);
        }
    }

    void INetworkPacketHandler.HandleLaunchMode(in LaunchModePacket packet, int peerId)
    {
        if (_role == NetRole.Host)
            return;

        GameMenu.ReceiveLaunchMode(
            packet.Action,
            packet.Custom,
            packet.StreamEnabled,
            packet.NewCoopWorldPrepared,
            packet.CoopId,
            packet.HostHasContinueSave);
    }

    void INetworkPacketHandler.HandleLegacyText(string line, int peerId)
    {
        if (string.IsNullOrEmpty(line))
            return;

        var end = line.Length;
        while (end > 0)
        {
            var ch = line[end - 1];
            if (ch != '\n' && ch != '\r')
                break;

            end--;
        }

        if (end == 0)
            return;

        if (end != line.Length)
            line = line[..end];

        int? senderId = null;
        ClientConnection? sender = null;
        if (_role == NetRole.Host)
        {
            if (TryResolveBinaryUserId(peerId, 0, out var resolvedSenderId))
            {
                senderId = resolvedSenderId;
                lock (_clientsLock)
                    _clients.TryGetValue(resolvedSenderId, out sender);
            }
        }
        else
        {
            senderId = 1;
        }

        try
        {
            if (!HandleLine(line, senderId, out var forwardLine))
            {
                if (_role == NetRole.Host && sender != null)
                    CleanupHostClient(sender);
                else
                    CleanupClient();
                return;
            }

            if (_role == NetRole.Host && sender != null && forwardLine != null)
                ForwardLineToOtherClients(sender, forwardLine);
        }
        catch (Exception ex)
        {
            _log.Warning("[NetNode] LiteNetLib legacy line handling failed: {Message}", ex.Message);
        }
    }
}
