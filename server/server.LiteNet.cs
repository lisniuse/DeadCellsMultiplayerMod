using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DeadCellsMultiplayerMod;

public sealed partial class NetNode
{
    // ================= LiteNetLib HOST =================
    private void StartHost()
    {
        _cts = new CancellationTokenSource();
        if (!StartLiteNetHostService(_bindEp.Port))
        {
            GameMenu.EnqueueMainThreadCoalesced("net:client-connect-failed", GameMenu.NotifyClientConnectFailed);
            return;
        }

        _log.Information("[NetNode] LiteNetLib host started OK. Bound to {0}:{1}", _bindEp.Address, _bindEp.Port);
    }

    // ================= LiteNetLib CLIENT =================
    private void StartClient()
    {
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ConnectWithRetryLiteNetAsync(_cts.Token));
    }

    private async Task ConnectWithRetryLiteNetAsync(CancellationToken ct)
    {
        var maxAttempts = GameMenu.ClientConnectMaxAttempts;
        var attempt = 0;

        while (!ct.IsCancellationRequested && attempt < maxAttempts)
        {
            attempt++;
            GameMenu.EnqueueMainThreadCoalesced("net:client-connect-attempt", () => GameMenu.NotifyClientConnectAttempt(attempt));

            DisposeLiteNetService();
            _log.Information("[NetNode] LiteNetLib client connecting to {dest}", _destEp);

            if (!StartLiteNetClientService(_destEp))
            {
                if (attempt >= maxAttempts)
                    break;

                await Task.Delay(1500, ct).ConfigureAwait(false);
                continue;
            }

            var startedAt = DateTime.UtcNow;
            while (!ct.IsCancellationRequested && DateTime.UtcNow - startedAt < TimeSpan.FromSeconds(6))
            {
                if (_binaryNetwork?.HasPeers == true)
                    return;

                await Task.Delay(100, ct).ConfigureAwait(false);
            }

            if (attempt < maxAttempts)
                await Task.Delay(1500, ct).ConfigureAwait(false);
        }

        if (!ct.IsCancellationRequested)
        {
            DisposeLiteNetService();
            GameMenu.EnqueueMainThreadCoalesced("net:client-connect-failed", GameMenu.NotifyClientConnectFailed);
        }
    }

    private void OnLiteNetPeerConnected(int peerId)
    {
        if (_disposed)
            return;

        if (_role == NetRole.Host)
        {
            AcceptLiteNetClient(peerId);
            return;
        }

        lock (_sync)
        {
            _hasRemote = true;
            _connectedClientCount = 1;
            if (_primaryRemoteId == 0)
                _primaryRemoteId = 1;
        }

        if (_useSteamTransport)
            _log.Information("[NetNode] Steam client peer is reachable");
        else
            _log.Information("[NetNode] LiteNetLib client connected to {dest}", _destEp);
        _ = SendLineSafe("HELLO\n");

        if (!_useSteamTransport)
        {
            GameMenu.EnqueueMainThread(() =>
            {
                GameMenu.NetRef = this;
                GameMenu.SetRole(_role);
                GameMenu.NotifyRemoteConnected(_role);
            });
        }
    }

    private void OnLiteNetPeerDisconnected(int peerId)
    {
        if (_disposed)
            return;

        if (_role == NetRole.Host)
        {
            ClientConnection? connection = null;
            lock (_clientsLock)
            {
                foreach (var c in _clients.Values)
                {
                    if (c.PeerId == peerId)
                    {
                        connection = c;
                        break;
                    }
                }
            }

            if (connection != null)
                CleanupHostClient(connection);
            return;
        }

        CleanupClient();
    }

    private void AcceptLiteNetClient(int peerId)
    {
        if (!TryTakeNextUnusedClientId(out var assignedId))
        {
            _log.Warning("[NetNode] Max players reached, kicking LiteNetLib peer {PeerId}", peerId);
            _binaryNetwork?.DisconnectPeer(peerId);
            return;
        }

        var connection = new ClientConnection(peerId, assignedId);
        lock (_clientsLock)
        {
            _clients[assignedId] = connection;
            _connectedClientCount = _clients.Count;
        }

        lock (_sync)
        {
            if (_primaryRemoteId == 0)
                _primaryRemoteId = assignedId;
            _hasRemote = true;
        }

        RegisterBinaryPeer(peerId, assignedId);
        _log.Information("[NetNode] {Transport} host accepted peer={PeerId} assignedId={AssignedId}", _useSteamTransport ? "Steam" : "LiteNetLib", peerId, assignedId);

        _ = SendInitialStateToLiteNetClient(connection);

        GameMenu.EnqueueMainThreadCoalesced("net:remote-connected", () =>
        {
            GameMenu.NetRef = this;
            GameMenu.SetRole(_role);
            GameMenu.NotifyRemoteConnected(_role);
        });
    }

    private async Task SendInitialStateToLiteNetClient(ClientConnection connection)
    {
        await SendLineToClientSafe(connection, "WELCOME\n").ConfigureAwait(false);

        int? cachedBossRune;
        int? cachedSeed;
        int? cachedSerializerSeq;
        int? cachedSerializerUid;
        string? cachedLevelDescPayload;
        string? cachedLevelSeedPayload;
        string? cachedLevelGraphPayload;
        string? cachedCoopId;
        bool cachedHasContinueSave;
        string? cachedHeroSkin;
        string? cachedHeroHeadSkin;
        lock (_hostCacheSync)
        {
            cachedBossRune = _cachedHostBossRune;
            cachedSeed = _cachedHostSeed;
            cachedSerializerSeq = _cachedHostSerializerSeq;
            cachedSerializerUid = _cachedHostSerializerUid;
            cachedLevelDescPayload = _cachedHostLevelDescPayload;
            cachedLevelSeedPayload = _cachedHostLevelSeedPayload;
            cachedLevelGraphPayload = _cachedHostLevelGraphPayload;
            cachedCoopId = _cachedHostCoopId;
            cachedHasContinueSave = _cachedHostHasContinueSave;
            cachedHeroSkin = _cachedHostHeroSkin;
            cachedHeroHeadSkin = _cachedHostHeroHeadSkin;
        }

        if (cachedSerializerSeq.HasValue && cachedSerializerUid.HasValue)
            await SendLineToClientSafe(connection, $"HXSYNC|{cachedSerializerSeq.Value}|{cachedSerializerUid.Value}\n").ConfigureAwait(false);

        if (cachedBossRune.HasValue)
            await SendLineToClientSafe(connection, $"BOSSRUNE|{cachedBossRune.Value}\n").ConfigureAwait(false);

        if (cachedSeed.HasValue)
            await SendLineToClientSafe(connection, $"SEED|{cachedSeed.Value}\n").ConfigureAwait(false);

        if (cachedCoopId != null)
            await SendLineToClientSafe(connection, BuildCoopStateLine(1, cachedCoopId, cachedHasContinueSave)).ConfigureAwait(false);

        if (cachedLevelDescPayload != null)
            await SendLineToClientSafe(connection, $"LDESC|{cachedLevelDescPayload}\n").ConfigureAwait(false);

        if (cachedLevelSeedPayload != null)
            await SendLineToClientSafe(connection, $"LSEED|{cachedLevelSeedPayload}\n").ConfigureAwait(false);

        if (cachedLevelGraphPayload != null)
            await SendLineToClientSafe(connection, $"LGRAPH|{cachedLevelGraphPayload}\n").ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(cachedHeroSkin))
            await SendLineToClientSafe(connection, BuildTaggedLine("SKIN", 1, cachedHeroSkin)).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(cachedHeroHeadSkin))
            await SendLineToClientSafe(connection, BuildTaggedLine("HEAD", 1, cachedHeroHeadSkin)).ConfigureAwait(false);

        await SendLineToClientSafe(connection, $"ID|{connection.AssignedId}\n").ConfigureAwait(false);
        await SendKnownUsersToClientSafe(connection).ConfigureAwait(false);

        if (_role == NetRole.Host && TryBuildLocalHpLine(out var localHpLine))
            await SendLineToClientSafe(connection, localHpLine).ConfigureAwait(false);
    }

    private void ForwardLineToOtherClients(ClientConnection sender, string line)
    {
        List<ClientConnection> snapshot;
        lock (_clientsLock)
        {
            snapshot = new List<ClientConnection>(_clients.Count);
            foreach (var c in _clients.Values)
            {
                if (c.AssignedId != sender.AssignedId)
                    snapshot.Add(c);
            }
        }

        foreach (var client in snapshot)
            _ = SendLineToClientSafe(client, line);
    }

    private void CleanupHostClient(ClientConnection sender)
    {
        try { _binaryNetwork?.DisconnectPeer(sender.PeerId); } catch { }

        bool hasClients;
        lock (_clientsLock)
        {
            _clients.Remove(sender.AssignedId);
            _connectedClientCount = _clients.Count;
            hasClients = _connectedClientCount > 0;
        }

        if (sender.AssignedId >= 2)
        {
            lock (UsedClientIds)
                UsedClientIds.Remove(sender.AssignedId);
        }

        RemoveBinaryPeersForUser(sender.AssignedId);

        lock (_sync)
        {
            RemoveRemoteLocked(sender.AssignedId);
            _pendingAttacks.RemoveAll(a => a.Id == sender.AssignedId);
            _pendingChatMessages.RemoveAll(m => m.Id == sender.AssignedId);
            _pendingMobHits.RemoveAll(h => h.UserId == sender.AssignedId);
            _pendingMobDies.RemoveAll(d => d.UserId == sender.AssignedId);
            _pendingExitReadyStates.RemoveAll(s => s.UserId == sender.AssignedId);
            _pendingPlayerDownStates.RemoveAll(s => s.UserId == sender.AssignedId);
            _pendingPlayerReviveRequests.RemoveAll(s => s.ReviverId == sender.AssignedId || s.TargetId == sender.AssignedId);
            _hasRemote = hasClients;
        }

        if (!hasClients)
        {
            bool stillEmpty;
            lock (_clientsLock)
                stillEmpty = _clients.Count == 0;
            if (stillEmpty)
                GameMenu.EnqueueMainThreadCoalesced("net:remote-disconnected", () => GameMenu.NotifyRemoteDisconnected(_role));
        }
    }

    private Task SendLineToClientSafe(ClientConnection connection, string line)
    {
        TrySendLegacyLine(line, ResolveLegacyDelivery(line), targetPeerId: connection.PeerId);
        return Task.CompletedTask;
    }

    private async Task SendKnownUsersToClientSafe(ClientConnection connection)
    {
        List<RemoteState> snapshot;
        lock (_sync)
        {
            if (_remotes.Count == 0)
                return;
            snapshot = new List<RemoteState>(_remotes.Values);
        }

        foreach (var state in snapshot)
        {
            var username = state.Username;
            if (string.IsNullOrWhiteSpace(username))
                continue;

            await SendLineToClientSafe(connection, BuildTaggedLine("USER", state.Id, username)).ConfigureAwait(false);
            await SendLineToClientSafe(connection, BuildReadyLine(state.Id, state.Ready)).ConfigureAwait(false);
            await SendLineToClientSafe(connection, BuildCoopStateLine(state.Id, state.CoopId, state.HasContinueSave)).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(state.Skin))
                await SendLineToClientSafe(connection, BuildTaggedLine("SKIN", state.Id, state.Skin)).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(state.Head))
                await SendLineToClientSafe(connection, BuildTaggedLine("HEAD", state.Id, state.Head)).ConfigureAwait(false);
        }
    }
}
