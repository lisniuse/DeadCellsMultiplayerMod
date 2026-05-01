using System.Buffers.Binary;
using System.Text;

namespace DeadCellsMultiplayerMod.Network;

internal ref struct PacketWriter
{
    private const byte Magic0 = (byte)'D';
    private const byte Magic1 = (byte)'C';
    private const byte Version = 1;

    private readonly Span<byte> _buffer;

    public int Position { get; private set; }

    public PacketWriter(Span<byte> buffer)
    {
        _buffer = buffer;
        Position = 0;
    }

    public bool TryWriteHeader(PacketOpcode opcode)
    {
        return TryWriteByte(Magic0) &&
               TryWriteByte(Magic1) &&
               TryWriteByte(Version) &&
               TryWriteByte((byte)opcode);
    }

    public bool TryWriteByte(byte value)
    {
        if (Position >= _buffer.Length)
            return false;

        _buffer[Position++] = value;
        return true;
    }

    public bool TryWriteBool(bool value)
    {
        return TryWriteByte(value ? (byte)1 : (byte)0);
    }

    public bool TryWriteInt32(int value)
    {
        if (_buffer.Length - Position < sizeof(int))
            return false;

        BinaryPrimitives.WriteInt32LittleEndian(_buffer[Position..], value);
        Position += sizeof(int);
        return true;
    }

    public bool TryWriteStringUtf8(string? value, int maxByteLength)
    {
        if (string.IsNullOrEmpty(value))
            return TryWriteUInt16(0);

        var byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount > maxByteLength || byteCount > ushort.MaxValue)
            return false;

        if (_buffer.Length - Position < sizeof(ushort) + byteCount)
            return false;

        if (!TryWriteUInt16((ushort)byteCount))
            return false;

        var written = Encoding.UTF8.GetBytes(value, _buffer.Slice(Position, byteCount));
        Position += written;
        return written == byteCount;
    }

    private bool TryWriteUInt16(ushort value)
    {
        if (_buffer.Length - Position < sizeof(ushort))
            return false;

        BinaryPrimitives.WriteUInt16LittleEndian(_buffer[Position..], value);
        Position += sizeof(ushort);
        return true;
    }
}
