namespace DeadCellsMultiplayerMod.Network;

internal enum PacketOpcode : byte
{
    None = 0,
    Hello = 1,
    Ready = 10,
    Seed = 11,
    Restart = 12,
    CoopState = 13,
    LaunchMode = 14,
    LegacyText = 255
}
