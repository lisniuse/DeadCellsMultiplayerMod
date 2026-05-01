using DeadCellsMultiplayerMod;

public sealed partial class NetNode
{
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts?.Cancel(); } catch { }
        List<ClientConnection> clients;
        lock (_clientsLock)
        {
            clients = new List<ClientConnection>(_clients.Count);
            foreach (var c in _clients.Values)
                clients.Add(c);
            _clients.Clear();
            _connectedClientCount = 0;
        }
        foreach (var client in clients)
        {
            try { client.Dispose(); } catch { }
            if (client.AssignedId >= 2)
            {
                lock (UsedClientIds)
                {
                    UsedClientIds.Remove(client.AssignedId);
                }
            }
        }

        DisposeLiteNetService();
        GameDataSync.Seed = 0;
        lock (_hostCacheSync)
        {
            _cachedHostSeed = null;
            _cachedHostBossRune = null;
            _cachedHostSerializerSeq = null;
            _cachedHostSerializerUid = null;
            _cachedHostLevelDescPayload = null;
            _cachedHostLevelSeedPayload = null;
            _cachedHostHeroSkin = null;
            _cachedHostHeroHeadSkin = null;
            _cachedHostLevelGraphPayload = null;
            _cachedHostLevelGraphsByLevelId.Clear();
        }
        lock (_sync)
        {
            _remotes.Clear();
            _primaryRemoteId = 0;
            _hasRemote = false;
            _connectedClientCount = 0;
            _pendingAttacks.Clear();
            _pendingMobStates.Clear();
            _pendingMobMoves.Clear();
            _pendingMobCharges.Clear();
            _pendingMobHits.Clear();
            _pendingMobDies.Clear();
            _pendingMobAttacks.Clear();
            _pendingMobDraws.Clear();
            _pendingExitReadyStates.Clear();
            _pendingBossCineLevelIds.Clear();
            _pendingBossHeroTeleports.Clear();
            _pendingPlayerDownStates.Clear();
            _pendingPlayerReviveRequests.Clear();
        }
    }
}
