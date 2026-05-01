namespace DeadCellsMultiplayerMod.Network;

internal readonly struct HelloPacket
{
    public readonly int UserId;

    public HelloPacket(int userId)
    {
        UserId = userId;
    }
}

internal readonly struct ReadyPacket
{
    public readonly int UserId;
    public readonly bool Ready;

    public ReadyPacket(int userId, bool ready)
    {
        UserId = userId;
        Ready = ready;
    }
}

internal readonly struct SeedPacket
{
    public readonly int Seed;

    public SeedPacket(int seed)
    {
        Seed = seed;
    }
}

internal readonly struct RestartPacket
{
    public readonly int Seed;

    public RestartPacket(int seed)
    {
        Seed = seed;
    }
}

internal readonly struct CoopStatePacket
{
    public readonly int UserId;
    public readonly string CoopId;
    public readonly bool HasContinueSave;

    public CoopStatePacket(int userId, string? coopId, bool hasContinueSave)
    {
        UserId = userId;
        CoopId = coopId ?? string.Empty;
        HasContinueSave = hasContinueSave;
    }
}

internal readonly struct LaunchModePacket
{
    public readonly int Action;
    public readonly bool Custom;
    public readonly bool StreamEnabled;
    public readonly bool NewCoopWorldPrepared;
    public readonly string CoopId;
    public readonly bool HostHasContinueSave;

    public LaunchModePacket(
        int action,
        bool custom,
        bool streamEnabled,
        bool newCoopWorldPrepared,
        string? coopId,
        bool hostHasContinueSave)
    {
        Action = action;
        Custom = custom;
        StreamEnabled = streamEnabled;
        NewCoopWorldPrepared = newCoopWorldPrepared;
        CoopId = coopId ?? string.Empty;
        HostHasContinueSave = hostHasContinueSave;
    }
}
