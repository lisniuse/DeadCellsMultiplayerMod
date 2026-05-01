using System.Buffers;

namespace DeadCellsMultiplayerMod.Network;

internal sealed class PooledPacket : IDisposable
{
    public byte[] Buffer { get; private set; }
    public int Length { get; private set; }
    public PacketOpcode Opcode { get; }

    private bool _returned;

    public PooledPacket(byte[] buffer, int length, PacketOpcode opcode)
    {
        Buffer = buffer;
        Length = length;
        Opcode = opcode;
    }

    public void Dispose()
    {
        if (_returned)
            return;

        _returned = true;
        var buffer = Buffer;
        Buffer = Array.Empty<byte>();
        Length = 0;
        if (buffer.Length > 0)
            ArrayPool<byte>.Shared.Return(buffer);
    }
}
