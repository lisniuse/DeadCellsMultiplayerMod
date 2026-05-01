using System.Buffers.Binary;
using System.Text;

namespace DeadCellsMultiplayerMod.Network;

internal ref struct PacketReader
{
    private const byte Magic0 = (byte)'D';
    private const byte Magic1 = (byte)'C';
    private const byte Version = 1;

    private readonly ReadOnlySpan<byte> _buffer;

    public int Position { get; private set; }

    public PacketReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        Position = 0;
    }

    public bool TryReadHeader(out PacketOpcode opcode)
    {
        opcode = PacketOpcode.None;
        if (!TryReadByte(out var magic0) ||
            !TryReadByte(out var magic1) ||
            !TryReadByte(out var version) ||
            !TryReadByte(out var opcodeByte))
        {
            return false;
        }

        if (magic0 != Magic0 || magic1 != Magic1 || version != Version)
            return false;

        opcode = (PacketOpcode)opcodeByte;
        return true;
    }

    public bool TryReadByte(out byte value)
    {
        value = 0;
        if (Position >= _buffer.Length)
            return false;

        value = _buffer[Position++];
        return true;
    }

    public bool TryReadBool(out bool value)
    {
        value = false;
        if (!TryReadByte(out var raw))
            return false;

        value = raw != 0;
        return true;
    }

    public bool TryReadInt32(out int value)
    {
        value = 0;
        if (_buffer.Length - Position < sizeof(int))
            return false;

        value = BinaryPrimitives.ReadInt32LittleEndian(_buffer[Position..]);
        Position += sizeof(int);
        return true;
    }

    public bool TryReadStringUtf8(out string value, int maxByteLength)
    {
        value = string.Empty;
        if (!TryReadUInt16(out var byteCount))
            return false;

        if (byteCount == 0)
            return true;

        if (byteCount > maxByteLength || _buffer.Length - Position < byteCount)
            return false;

        value = Encoding.UTF8.GetString(_buffer.Slice(Position, byteCount));
        Position += byteCount;
        return true;
    }

    public bool TryReadStringUtf8Int32(out string value, int maxByteLength)
    {
        value = string.Empty;
        if (!TryReadInt32(out var byteCount))
            return false;

        if (byteCount == 0)
            return true;

        if (byteCount < 0 || byteCount > maxByteLength || _buffer.Length - Position < byteCount)
            return false;

        value = Encoding.UTF8.GetString(_buffer.Slice(Position, byteCount));
        Position += byteCount;
        return true;
    }

    private bool TryReadUInt16(out ushort value)
    {
        value = 0;
        if (_buffer.Length - Position < sizeof(ushort))
            return false;

        value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer[Position..]);
        Position += sizeof(ushort);
        return true;
    }
}
