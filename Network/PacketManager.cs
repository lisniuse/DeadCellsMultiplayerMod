using System.Buffers;

namespace DeadCellsMultiplayerMod.Network;

internal interface INetworkPacketHandler
{
    void HandleHello(in HelloPacket packet, int peerId);
    void HandleReady(in ReadyPacket packet, int peerId);
    void HandleSeed(in SeedPacket packet, int peerId);
    void HandleRestart(in RestartPacket packet, int peerId);
    void HandleCoopState(in CoopStatePacket packet, int peerId);
    void HandleLaunchMode(in LaunchModePacket packet, int peerId);
    void HandleLegacyText(string line, int peerId);
}

internal sealed class PacketManager
{
    private const int DefaultPacketSize = 512;
    private const int MaxLegacyTextBytes = 16 * 1024 * 1024;
    private const int MaxCoopIdBytes = 128;

    public bool TryCreate(in HelloPacket packet, out PooledPacket? pooled)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(DefaultPacketSize);
        var writer = new PacketWriter(buffer);
        if (writer.TryWriteHeader(PacketOpcode.Hello) &&
            writer.TryWriteInt32(packet.UserId))
        {
            pooled = new PooledPacket(buffer, writer.Position, PacketOpcode.Hello);
            return true;
        }

        ArrayPool<byte>.Shared.Return(buffer);
        pooled = null;
        return false;
    }

    public bool TryCreate(in ReadyPacket packet, out PooledPacket? pooled)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(DefaultPacketSize);
        var writer = new PacketWriter(buffer);
        if (writer.TryWriteHeader(PacketOpcode.Ready) &&
            writer.TryWriteInt32(packet.UserId) &&
            writer.TryWriteBool(packet.Ready))
        {
            pooled = new PooledPacket(buffer, writer.Position, PacketOpcode.Ready);
            return true;
        }

        ArrayPool<byte>.Shared.Return(buffer);
        pooled = null;
        return false;
    }

    public bool TryCreate(in SeedPacket packet, out PooledPacket? pooled)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(DefaultPacketSize);
        var writer = new PacketWriter(buffer);
        if (writer.TryWriteHeader(PacketOpcode.Seed) &&
            writer.TryWriteInt32(packet.Seed))
        {
            pooled = new PooledPacket(buffer, writer.Position, PacketOpcode.Seed);
            return true;
        }

        ArrayPool<byte>.Shared.Return(buffer);
        pooled = null;
        return false;
    }

    public bool TryCreate(in RestartPacket packet, out PooledPacket? pooled)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(DefaultPacketSize);
        var writer = new PacketWriter(buffer);
        if (writer.TryWriteHeader(PacketOpcode.Restart) &&
            writer.TryWriteInt32(packet.Seed))
        {
            pooled = new PooledPacket(buffer, writer.Position, PacketOpcode.Restart);
            return true;
        }

        ArrayPool<byte>.Shared.Return(buffer);
        pooled = null;
        return false;
    }

    public bool TryCreate(in CoopStatePacket packet, out PooledPacket? pooled)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(DefaultPacketSize);
        var writer = new PacketWriter(buffer);
        if (writer.TryWriteHeader(PacketOpcode.CoopState) &&
            writer.TryWriteInt32(packet.UserId) &&
            writer.TryWriteStringUtf8(packet.CoopId, MaxCoopIdBytes) &&
            writer.TryWriteBool(packet.HasContinueSave))
        {
            pooled = new PooledPacket(buffer, writer.Position, PacketOpcode.CoopState);
            return true;
        }

        ArrayPool<byte>.Shared.Return(buffer);
        pooled = null;
        return false;
    }

    public bool TryCreate(in LaunchModePacket packet, out PooledPacket? pooled)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(DefaultPacketSize);
        var writer = new PacketWriter(buffer);
        if (writer.TryWriteHeader(PacketOpcode.LaunchMode) &&
            writer.TryWriteInt32(packet.Action) &&
            writer.TryWriteBool(packet.Custom) &&
            writer.TryWriteBool(packet.StreamEnabled) &&
            writer.TryWriteBool(packet.NewCoopWorldPrepared) &&
            writer.TryWriteStringUtf8(packet.CoopId, MaxCoopIdBytes) &&
            writer.TryWriteBool(packet.HostHasContinueSave))
        {
            pooled = new PooledPacket(buffer, writer.Position, PacketOpcode.LaunchMode);
            return true;
        }

        ArrayPool<byte>.Shared.Return(buffer);
        pooled = null;
        return false;
    }

    public bool TryCreateLegacyText(string line, out PooledPacket? pooled)
    {
        if (string.IsNullOrEmpty(line))
        {
            pooled = null;
            return false;
        }

        var payload = TrimLegacyLineTerminator(line.AsSpan());
        if (payload.Length == 0)
        {
            pooled = null;
            return false;
        }

        var byteCount = System.Text.Encoding.UTF8.GetByteCount(payload);
        if (byteCount <= 0 || byteCount > MaxLegacyTextBytes)
        {
            pooled = null;
            return false;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(8 + byteCount);
        var writer = new PacketWriter(buffer);
        if (writer.TryWriteHeader(PacketOpcode.LegacyText) &&
            writer.TryWriteInt32(byteCount))
        {
            var payloadOffset = writer.Position;
            var written = System.Text.Encoding.UTF8.GetBytes(payload, buffer.AsSpan(payloadOffset, byteCount));
            if (written == byteCount)
            {
                pooled = new PooledPacket(buffer, payloadOffset + byteCount, PacketOpcode.LegacyText);
                return true;
            }
        }

        ArrayPool<byte>.Shared.Return(buffer);
        pooled = null;
        return false;
    }

    private static ReadOnlySpan<char> TrimLegacyLineTerminator(ReadOnlySpan<char> line)
    {
        while (line.Length > 0)
        {
            var ch = line[^1];
            if (ch != '\n' && ch != '\r')
                break;

            line = line[..^1];
        }

        return line;
    }

    public bool TryDispatch(ReadOnlySpan<byte> data, INetworkPacketHandler handler, int peerId, out PacketOpcode opcode)
    {
        var reader = new PacketReader(data);
        if (!reader.TryReadHeader(out opcode))
            return false;

        switch (opcode)
        {
            case PacketOpcode.None:
                return true;

            case PacketOpcode.Hello:
                if (!reader.TryReadInt32(out var helloUserId))
                    return false;
                handler.HandleHello(new HelloPacket(helloUserId), peerId);
                return true;

            case PacketOpcode.Ready:
                if (!reader.TryReadInt32(out var readyUserId) ||
                    !reader.TryReadBool(out var ready))
                    return false;
                handler.HandleReady(new ReadyPacket(readyUserId, ready), peerId);
                return true;

            case PacketOpcode.Seed:
                if (!reader.TryReadInt32(out var seed))
                    return false;
                handler.HandleSeed(new SeedPacket(seed), peerId);
                return true;

            case PacketOpcode.Restart:
                if (!reader.TryReadInt32(out var restartSeed))
                    return false;
                handler.HandleRestart(new RestartPacket(restartSeed), peerId);
                return true;

            case PacketOpcode.CoopState:
                if (!reader.TryReadInt32(out var coopUserId) ||
                    !reader.TryReadStringUtf8(out var coopId, MaxCoopIdBytes) ||
                    !reader.TryReadBool(out var hasContinueSave))
                    return false;
                handler.HandleCoopState(new CoopStatePacket(coopUserId, coopId, hasContinueSave), peerId);
                return true;

            case PacketOpcode.LaunchMode:
                if (!reader.TryReadInt32(out var action) ||
                    !reader.TryReadBool(out var custom) ||
                    !reader.TryReadBool(out var streamEnabled) ||
                    !reader.TryReadBool(out var newCoopWorldPrepared) ||
                    !reader.TryReadStringUtf8(out var launchCoopId, MaxCoopIdBytes) ||
                    !reader.TryReadBool(out var hostHasContinueSave))
                    return false;
                handler.HandleLaunchMode(
                    new LaunchModePacket(action, custom, streamEnabled, newCoopWorldPrepared, launchCoopId, hostHasContinueSave),
                    peerId);
                return true;

            case PacketOpcode.LegacyText:
                if (!reader.TryReadStringUtf8Int32(out var line, MaxLegacyTextBytes))
                    return false;
                handler.HandleLegacyText(line, peerId);
                return true;

            default:
                return false;
        }
    }
}
