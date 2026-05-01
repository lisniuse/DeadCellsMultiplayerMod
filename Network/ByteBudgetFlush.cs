namespace DeadCellsMultiplayerMod.Network;

internal readonly struct FlushResult
{
    public readonly int FlushedPackets;
    public readonly int SkippedPackets;
    public readonly int BytesSent;

    public FlushResult(int flushedPackets, int skippedPackets, int bytesSent)
    {
        FlushedPackets = flushedPackets;
        SkippedPackets = skippedPackets;
        BytesSent = bytesSent;
    }
}

internal static class ByteBudgetFlush
{
    public static FlushResult Flush(
        OutboundQueue queue,
        INetworkService service,
        int byteBudget,
        NetworkDiagnostics diagnostics)
    {
        var flushed = 0;
        var skipped = 0;
        var bytesSent = 0;

        while (queue.TryDequeue(out var item))
        {
            var length = item.Packet.Length;
            if (flushed > 0 && bytesSent + length > byteBudget)
            {
                skipped++;
                diagnostics.RecordSkipped(item.Packet.Opcode);
                item.Packet.Dispose();
                continue;
            }

            if (service.TrySend(item.Packet.Buffer, length, item.Delivery, item.ExcludePeerId, out var peerCount) && peerCount > 0)
            {
                flushed++;
                bytesSent += length * peerCount;
                diagnostics.RecordFlushed(item.Packet.Opcode, length * peerCount);
            }
            else
            {
                skipped++;
                diagnostics.RecordSkipped(item.Packet.Opcode);
            }

            item.Packet.Dispose();
        }

        if (flushed > 0 || skipped > 0)
            diagnostics.RecordFlush(flushed, skipped, bytesSent);

        return new FlushResult(flushed, skipped, bytesSent);
    }
}
