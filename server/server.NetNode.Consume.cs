using DeadCellsMultiplayerMod.Interaction;

public sealed partial class NetNode
{
    public bool TryGetRemote(out int remoteId, out double rx, out double ry)
    {
        lock (_sync)
        {
            if (_primaryRemoteId != 0 && _remotes.TryGetValue(_primaryRemoteId, out var state) && state.HasRemote)
            {
                remoteId = state.Id;
                rx = state.X;
                ry = state.Y;
                return true;
            }
            remoteId = 0;
            rx = 0;
            ry = 0;
            return false;
        }
    }

    public bool TryConsumeRemoteSnapshot(out List<RemoteSnapshot> snapshot)
    {
        lock (_sync)
        {
            if (_remotes.Count == 0)
            {
                snapshot = new List<RemoteSnapshot>();
                return false;
            }

            snapshot = new List<RemoteSnapshot>(_remotes.Count);
            foreach (var state in _remotes.Values)
            {
                if (!state.HasRemote)
                    continue;

                var hasAnim = state.HasAnim;
                var hasHeadAnim = state.HasHeadAnim;
                var hasRoom = state.HasRoom;
                var anim = hasAnim ? state.Anim : null;
                var animQueue = hasAnim ? state.AnimQueue : null;
                var animG = hasAnim ? state.AnimG : null;
                var headAnim = hasHeadAnim ? state.HeadAnim : null;
                var roomLevelId = hasRoom ? state.RoomLevelId : null;
                var roomId = hasRoom ? state.RoomId : null;

                snapshot.Add(new RemoteSnapshot(
                    state.Id,
                    state.X,
                    state.Y,
                    state.Dir,
                    state.LevelId,
                    roomLevelId,
                    roomId,
                    hasRoom,
                    anim,
                    animQueue,
                    animG,
                    hasAnim,
                    state.Username,
                    headAnim,
                    hasHeadAnim));

                if (hasAnim)
                    state.HasAnim = false;
                if (hasHeadAnim)
                    state.HasHeadAnim = false;
            }

            return snapshot.Count > 0;
        }
    }

    public bool TryConsumeRemoteWeaponSnapshots(out List<RemoteWeaponSnapshot> snapshot)
    {
        lock (_sync)
        {
            if (_remotes.Count == 0)
            {
                snapshot = new List<RemoteWeaponSnapshot>();
                return false;
            }

            snapshot = new List<RemoteWeaponSnapshot>();
            foreach (var state in _remotes.Values)
            {
                if (!state.HasRemote || !state.HasWeaponUpdate)
                    continue;

                int? ammo = state.WeaponAmmo != int.MinValue ? state.WeaponAmmo : (int?)null;
                snapshot.Add(new RemoteWeaponSnapshot(state.Id, state.WeaponKind, state.WeaponSlot, state.WeaponPermanentId, ammo));
                state.HasWeaponUpdate = false;
            }

            return snapshot.Count > 0;
        }
    }

    public bool TryConsumeRemoteAttacks(out List<RemoteAttack> attacks)
    {
        lock (_sync)
        {
            if (_pendingAttacks.Count == 0)
            {
                attacks = new List<RemoteAttack>();
                return false;
            }

            attacks = new List<RemoteAttack>(_pendingAttacks);
            _pendingAttacks.Clear();
            return attacks.Count > 0;
        }
    }

    public bool TryConsumeChatMessages(out List<RemoteChatMessage> messages)
    {
        lock (_sync)
        {
            if (_pendingChatMessages.Count == 0)
            {
                messages = new List<RemoteChatMessage>();
                return false;
            }

            messages = new List<RemoteChatMessage>(_pendingChatMessages);
            _pendingChatMessages.Clear();
            return messages.Count > 0;
        }
    }

    public void ClearMobSyncQueues()
    {
        lock (_sync)
        {
            _pendingMobStates.Clear();
            _pendingMobMoves.Clear();
            _pendingMobCharges.Clear();
            _pendingMobHits.Clear();
            _pendingMobDies.Clear();
            _pendingMobAttacks.Clear();
            _pendingMobDraws.Clear();
        }
    }

    public bool TryConsumeMobStates(out List<MobStateSnapshot> snapshot)
    {
        lock (_sync)
        {
            return TryConsumePendingListLocked(ref _pendingMobStates, out snapshot);
        }
    }

    public bool TryConsumeMobMoves(out List<MobMoveSnapshot> moves)
    {
        lock (_sync)
        {
            return TryConsumePendingListLocked(ref _pendingMobMoves, out moves);
        }
    }

    public bool TryConsumeMobCharges(out List<MobChargeSnapshot> charges)
    {
        lock (_sync)
        {
            return TryConsumePendingListLocked(ref _pendingMobCharges, out charges);
        }
    }

    public bool TryConsumeMobHits(out List<MobHit> hits)
    {
        lock (_sync)
        {
            return TryConsumePendingListLocked(ref _pendingMobHits, out hits);
        }
    }

    public bool TryConsumeMobDies(out List<MobDie> dies)
    {
        lock (_sync)
        {
            return TryConsumePendingListLocked(ref _pendingMobDies, out dies);
        }
    }

    public bool TryConsumeMobAttacks(out List<MobAttack> attacks)
    {
        lock (_sync)
        {
            return TryConsumePendingListLocked(ref _pendingMobAttacks, out attacks);
        }
    }

    public bool TryConsumeMobDraws(out List<MobDraw> draws)
    {
        lock (_sync)
        {
            return TryConsumePendingListLocked(ref _pendingMobDraws, out draws);
        }
    }

    private static bool TryConsumePendingListLocked<T>(ref List<T> pending, out List<T> snapshot)
    {
        if (pending.Count == 0)
        {
            snapshot = new List<T>();
            return false;
        }

        snapshot = pending;
        pending = new List<T>(snapshot.Count);
        return true;
    }

    public bool TryConsumeExitReadyStates(out List<ExitReadyState> states)
    {
        lock (_sync)
        {
            if (_pendingExitReadyStates.Count == 0)
            {
                states = new List<ExitReadyState>();
                return false;
            }

            states = new List<ExitReadyState>(_pendingExitReadyStates);
            _pendingExitReadyStates.Clear();
            return states.Count > 0;
        }
    }

    public bool TryConsumeBossCineLevelIds(out List<string> levelIds)
    {
        lock (_sync)
        {
            if (_pendingBossCineLevelIds.Count == 0)
            {
                levelIds = new List<string>();
                return false;
            }

            levelIds = new List<string>(_pendingBossCineLevelIds);
            _pendingBossCineLevelIds.Clear();
            return levelIds.Count > 0;
        }
    }

    public bool TryConsumeBossHeroTeleportEvents(out List<BossHeroTeleportEvent> events)
    {
        lock (_sync)
        {
            if (_pendingBossHeroTeleports.Count == 0)
            {
                events = new List<BossHeroTeleportEvent>();
                return false;
            }

            events = new List<BossHeroTeleportEvent>(_pendingBossHeroTeleports);
            _pendingBossHeroTeleports.Clear();
            return events.Count > 0;
        }
    }

    public bool TryConsumePlayerDownStates(out List<PlayerDownState> states)
    {
        lock (_sync)
        {
            if (_pendingPlayerDownStates.Count == 0)
            {
                states = new List<PlayerDownState>();
                return false;
            }

            states = new List<PlayerDownState>(_pendingPlayerDownStates);
            _pendingPlayerDownStates.Clear();
            return states.Count > 0;
        }
    }

    public bool TryConsumePlayerReviveRequests(out List<PlayerReviveRequest> requests)
    {
        lock (_sync)
        {
            if (_pendingPlayerReviveRequests.Count == 0)
            {
                requests = new List<PlayerReviveRequest>();
                return false;
            }

            requests = new List<PlayerReviveRequest>(_pendingPlayerReviveRequests);
            _pendingPlayerReviveRequests.Clear();
            return requests.Count > 0;
        }
    }

    public bool TryConsumeInterDoorEvents(out List<InterDoorEvent> events)
    {
        lock (_sync)
        {
            if (_pendingInterDoorEvents.Count == 0)
            {
                events = new List<InterDoorEvent>();
                return false;
            }

            events = new List<InterDoorEvent>(_pendingInterDoorEvents);
            _pendingInterDoorEvents.Clear();
            return events.Count > 0;
        }
    }

    public bool TryConsumeInterElevatorEvents(out List<InterElevatorEvent> events)
    {
        lock (_sync)
        {
            if (_pendingInterElevatorEvents.Count == 0)
            {
                events = new List<InterElevatorEvent>();
                return false;
            }

            events = new List<InterElevatorEvent>(_pendingInterElevatorEvents);
            _pendingInterElevatorEvents.Clear();
            return events.Count > 0;
        }
    }

    public bool TryConsumeInterPressurePlateEvents(out List<InterPressurePlateEvent> events)
    {
        lock (_sync)
        {
            if (_pendingInterPressurePlateEvents.Count == 0)
            {
                events = new List<InterPressurePlateEvent>();
                return false;
            }

            events = new List<InterPressurePlateEvent>(_pendingInterPressurePlateEvents);
            _pendingInterPressurePlateEvents.Clear();
            return events.Count > 0;
        }
    }

    public bool TryConsumeInterTreasureChestEvents(out List<InterTreasureChestEvent> events)
    {
        lock (_sync)
        {
            if (_pendingInterTreasureChestEvents.Count == 0)
            {
                events = new List<InterTreasureChestEvent>();
                return false;
            }

            events = new List<InterTreasureChestEvent>(_pendingInterTreasureChestEvents);
            _pendingInterTreasureChestEvents.Clear();
            return events.Count > 0;
        }
    }

    public bool TryConsumeInterVineLadderEvents(out List<InterVineLadderEvent> events)
    {
        lock (_sync)
        {
            if (_pendingInterVineLadderEvents.Count == 0)
            {
                events = new List<InterVineLadderEvent>();
                return false;
            }

            events = new List<InterVineLadderEvent>(_pendingInterVineLadderEvents);
            _pendingInterVineLadderEvents.Clear();
            return events.Count > 0;
        }
    }

    public bool TryConsumeInterTeleportEvents(out List<InterTeleportEvent> events)
    {
        lock (_sync)
        {
            if (_pendingInterTeleportEvents.Count == 0)
            {
                events = new List<InterTeleportEvent>();
                return false;
            }

            events = new List<InterTeleportEvent>(_pendingInterTeleportEvents);
            _pendingInterTeleportEvents.Clear();
            return events.Count > 0;
        }
    }

    public bool TryConsumeInterBreakableGroundEvents(out List<InterBreakableGroundEvent> events)
    {
        lock (_sync)
        {
            if (_pendingInterBreakableGroundEvents.Count == 0)
            {
                events = new List<InterBreakableGroundEvent>();
                return false;
            }

            events = new List<InterBreakableGroundEvent>(_pendingInterBreakableGroundEvents);
            _pendingInterBreakableGroundEvents.Clear();
            return events.Count > 0;
        }
    }

    public bool TryConsumeBossRuneUpdateCells(out List<InterBossRuneUpdateCellsEvent> events)
    {
        lock (_sync)
        {
            if (_pendingBossRuneUpdateCells.Count == 0)
            {
                events = new List<InterBossRuneUpdateCellsEvent>();
                return false;
            }

            events = new List<InterBossRuneUpdateCellsEvent>(_pendingBossRuneUpdateCells);
            _pendingBossRuneUpdateCells.Clear();
            return events.Count > 0;
        }
    }

    public bool TryConsumeInterPortalEvents(out List<InterPortalEvent> events)
    {
        lock (_sync)
        {
            if (_pendingInterPortalEvents.Count == 0)
            {
                events = new List<InterPortalEvent>();
                return false;
            }

            events = new List<InterPortalEvent>(_pendingInterPortalEvents);
            _pendingInterPortalEvents.Clear();
            return events.Count > 0;
        }
    }

    public bool TryGetRemoteHpSnapshots(out List<RemoteHpSnapshot> snapshot)
    {
        lock (_sync)
        {
            if (_remotes.Count == 0)
            {
                snapshot = new List<RemoteHpSnapshot>();
                return false;
            }

            snapshot = new List<RemoteHpSnapshot>(_remotes.Count);
            foreach (var state in _remotes.Values)
            {
                if (!state.HasRemote)
                    continue;

                snapshot.Add(new RemoteHpSnapshot(state.Id, state.Life, state.MaxLife, state.Lif, state.BonusLife, state.Recover, state.Username));
            }

            return snapshot.Count > 0;
        }
    }

    public bool TryGetRemoteUserSnapshots(out List<RemoteUserSnapshot> snapshot)
    {
        lock (_sync)
        {
            if (_remotes.Count == 0)
            {
                snapshot = new List<RemoteUserSnapshot>();
                return false;
            }

            snapshot = new List<RemoteUserSnapshot>(_remotes.Count);
            foreach (var state in _remotes.Values)
            {
                if (!state.HasRemote)
                    continue;

                snapshot.Add(new RemoteUserSnapshot(state.Id, state.Username));
            }

            return snapshot.Count > 0;
        }
    }

    public bool TryGetRemoteLevelId(out string? levelId)
    {
        lock (_sync)
        {
            if (_primaryRemoteId != 0 && _remotes.TryGetValue(_primaryRemoteId, out var state))
            {
                levelId = state.LevelId;
                return state.HasRemote && !string.IsNullOrEmpty(levelId);
            }
            levelId = null;
            return false;
        }
    }

    public bool TryGetRemoteHP(out int life, out int maxLife, out int lif, out int bonusLife, out int recover)
    {
        lock (_sync)
        {
            if (_primaryRemoteId != 0 && _remotes.TryGetValue(_primaryRemoteId, out var state))
            {
                life = state.Life;
                maxLife = state.MaxLife;
                lif = state.Lif;
                bonusLife = state.BonusLife;
                recover = state.Recover;
                return state.HasRemote;
            }
            life = 0;
            maxLife = 0;
            lif = 0;
            bonusLife = 0;
            recover = 0;
            return false;
        }
    }

    public bool TryGetRemoteAnim(out string? anim, out int? queueAnim, out bool? g)
    {
        lock (_sync)
        {
            if (_primaryRemoteId != 0 && _remotes.TryGetValue(_primaryRemoteId, out var state))
            {
                if (!state.HasAnim)
                {
                    anim = null;
                    queueAnim = null;
                    g = null;
                    return false;
                }
                anim = state.Anim;
                queueAnim = state.AnimQueue;
                g = state.AnimG;
                state.HasAnim = false;
                return state.HasRemote && anim != null;
            }
            anim = null;
            queueAnim = null;
            g = null;
            return false;
        }
    }
}
