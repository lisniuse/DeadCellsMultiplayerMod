using DeadCellsMultiplayerMod;
using DeadCellsMultiplayerMod.Network;
using Steamworks;

public sealed partial class NetNode
{
    internal bool TrySetSteamHostRichPresence(ulong lobbyId)
    {
        if (!_useSteamTransport || _role != NetRole.Host || _steamNetwork == null)
            return false;

        var connect = lobbyId == 0UL ? string.Empty : $"+connect_lobby {lobbyId}";
        if (!_steamNetwork.TrySetRichPresence("connect", connect, out var error))
        {
            if (!string.IsNullOrWhiteSpace(error))
                _log.Warning("[NetNode] Steam worker set rich presence failed: {Error}", error);
            return false;
        }

        return true;
    }

    internal bool TryClearSteamRichPresence()
    {
        if (!_useSteamTransport || _role != NetRole.Host || _steamNetwork == null)
            return false;

        if (!_steamNetwork.TryClearRichPresence(out var error))
        {
            if (!string.IsNullOrWhiteSpace(error))
                _log.Warning("[NetNode] Steam worker clear rich presence failed: {Error}", error);
            return false;
        }

        return true;
    }

    private void StartSteamHost()
    {
        _cts = new CancellationTokenSource();
        if (!StartSteamNetworkService())
        {
            GameMenu.EnqueueMainThreadCoalesced("net:client-connect-failed", GameMenu.NotifyClientConnectFailed);
            return;
        }

        _log.Information("[NetNode] Host started with Steam P2P transport (packet service)");
    }

    private void StartSteamClient()
    {
        _cts = new CancellationTokenSource();
        if (_steamHostId.m_SteamID == 0UL)
        {
            _log.Warning("[NetNode] Steam client host id is missing");
            GameMenu.EnqueueMainThreadCoalesced("net:client-connect-failed", GameMenu.NotifyClientConnectFailed);
            return;
        }

        if (!StartSteamNetworkService())
        {
            GameMenu.EnqueueMainThreadCoalesced("net:client-connect-failed", GameMenu.NotifyClientConnectFailed);
            return;
        }

        var localSteamId = _steamNetwork?.LocalSteamId ?? 0UL;
        if (localSteamId != 0UL && localSteamId == _steamHostId.m_SteamID)
        {
            _log.Warning(
                "[NetNode] Steam P2P requires two different Steam accounts. Host and client both use SteamId={SteamId}. " +
                "Use a second Steam account (e.g. family sharing or another PC) to test multiplayer.",
                _steamHostId.m_SteamID);
            GameMenu.EnqueueMainThreadCoalesced("net:client-connect-failed", GameMenu.NotifyClientConnectFailed);
            return;
        }

        _log.Information("[NetNode] Client started with Steam P2P transport (packet service)");
        _ = Task.Run(() => ConnectWithRetrySteamServiceAsync(_cts.Token));
    }

    private bool StartSteamNetworkService()
    {
        if (_cts == null)
            return false;

        try
        {
            var service = new SteamNetworkService(
                _log,
                _networkDiagnostics,
                _role,
                _steamHostId,
                _steamHostPort,
                _role == NetRole.Host ? SteamConnect.ResolveBestHostIp() : null);

            service.PacketReceived += OnBinaryPacketReceived;
            service.PeerConnected += OnLiteNetPeerConnected;
            service.PeerDisconnected += OnLiteNetPeerDisconnected;

            if (_role == NetRole.Host)
                service.StartHost(_steamHostPort, ClientIds.Length, _cts.Token);
            else
                service.StartClient(_steamHostId.m_SteamID.ToString(System.Globalization.CultureInfo.InvariantCulture), 0, _cts.Token);

            _steamNetwork = service;
            _binaryNetwork = service;
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning("[NetNode] Steam network service failed to start: {Message}", ex.Message);
            return false;
        }
    }

    private async Task ConnectWithRetrySteamServiceAsync(CancellationToken ct)
    {
        var maxAttempts = GameMenu.ClientConnectMaxAttempts;
        var attempt = 0;

        while (!ct.IsCancellationRequested && attempt < maxAttempts)
        {
            attempt++;
            GameMenu.EnqueueMainThreadCoalesced("net:client-connect-attempt", () => GameMenu.NotifyClientConnectAttempt(attempt));
            _log.Information("[NetNode] Steam client connecting to hostSteamId={HostSteamId}", _steamHostId.m_SteamID);

            _ = SendLineSafe("HELLO\n");

            var startedAt = DateTime.UtcNow;
            while (!ct.IsCancellationRequested && DateTime.UtcNow - startedAt < TimeSpan.FromSeconds(6))
            {
                bool connected;
                lock (_sync)
                    connected = _hasRemote && ID > 0;

                if (connected)
                {
                    GameMenu.EnqueueMainThreadCoalesced("net:remote-connected", () =>
                    {
                        GameMenu.NetRef = this;
                        GameMenu.SetRole(_role);
                        GameMenu.NotifyRemoteConnected(_role);
                    });
                    return;
                }

                await Task.Delay(150, ct).ConfigureAwait(false);
            }

            _log.Warning(
                "[NetNode] Steam client attempt {Attempt}/{Max}: no WELCOME/ID received within 6s",
                attempt,
                maxAttempts);

            if (attempt < maxAttempts)
                await Task.Delay(1500, ct).ConfigureAwait(false);
        }

        if (!ct.IsCancellationRequested)
        {
            _log.Warning("[NetNode] Steam client connection failed: no WELCOME/ID received within 6s");
            GameMenu.EnqueueMainThreadCoalesced("net:client-connect-failed", GameMenu.NotifyClientConnectFailed);
        }
    }
}
