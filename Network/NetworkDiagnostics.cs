using System.Diagnostics;
using System.Text;
using Serilog;

namespace DeadCellsMultiplayerMod.Network;

internal sealed class NetworkDiagnostics
{
    private const int OpcodeSlots = 256;
    private static readonly long LogIntervalTicks = Stopwatch.Frequency * 5L;
    private static readonly long SettingRefreshTicks = Stopwatch.Frequency;

    private readonly long[] _opcodeSent = new long[OpcodeSlots];
    private readonly long[] _opcodeReceived = new long[OpcodeSlots];

    private volatile bool _enabled;
    private long _nextSettingRefreshTicks;
    private long _nextLogTicks;
    private long _lastLogTicks;

    private long _serializationTicks;
    private long _serializationPackets;
    private long _serializationBytes;
    private long _sendTicks;
    private long _sendPackets;
    private long _receiveTicks;
    private long _receivePackets;
    private long _dispatchTicks;
    private long _dispatchPackets;
    private long _queuedPackets;
    private long _flushedPackets;
    private long _droppedPackets;
    private long _skippedPackets;
    private long _coalescedPackets;
    private long _bytesSent;
    private long _bytesReceived;
    private long _flushBytes;
    private long _flushes;
    private long _queuedGauge;

    public bool IsEnabled
    {
        get
        {
            var now = Stopwatch.GetTimestamp();
            if (now >= Interlocked.Read(ref _nextSettingRefreshTicks))
            {
                _enabled = MultiplayerSettingsStorage.ShowPerfLogs;
                Interlocked.Exchange(ref _nextSettingRefreshTicks, now + SettingRefreshTicks);
            }

            return _enabled;
        }
    }

    public void RecordSerialization(PacketOpcode opcode, long elapsedTicks, int bytes)
    {
        if (!IsEnabled)
            return;

        Interlocked.Add(ref _serializationTicks, elapsedTicks);
        Interlocked.Increment(ref _serializationPackets);
        Interlocked.Add(ref _serializationBytes, bytes);
        IncrementOpcode(_opcodeSent, opcode);
    }

    public void RecordSend(long elapsedTicks, int bytes)
    {
        if (!IsEnabled)
            return;

        Interlocked.Add(ref _sendTicks, elapsedTicks);
        Interlocked.Increment(ref _sendPackets);
        Interlocked.Add(ref _bytesSent, bytes);
    }

    public void RecordReceive(PacketOpcode opcode, long elapsedTicks, int bytes)
    {
        if (!IsEnabled)
            return;

        Interlocked.Add(ref _receiveTicks, elapsedTicks);
        Interlocked.Increment(ref _receivePackets);
        Interlocked.Add(ref _bytesReceived, bytes);
        IncrementOpcode(_opcodeReceived, opcode);
    }

    public void RecordDispatch(PacketOpcode opcode, long elapsedTicks)
    {
        if (!IsEnabled)
            return;

        Interlocked.Add(ref _dispatchTicks, elapsedTicks);
        Interlocked.Increment(ref _dispatchPackets);
        IncrementOpcode(_opcodeReceived, opcode);
    }

    public void RecordQueued(PacketOpcode opcode, int queuedCount, bool outbound = true)
    {
        if (!IsEnabled)
            return;

        Interlocked.Increment(ref _queuedPackets);
        Interlocked.Exchange(ref _queuedGauge, queuedCount);
        IncrementOpcode(outbound ? _opcodeSent : _opcodeReceived, opcode);
    }

    public void RecordFlushed(PacketOpcode opcode, int bytes)
    {
        if (!IsEnabled)
            return;

        Interlocked.Increment(ref _flushedPackets);
        IncrementOpcode(_opcodeSent, opcode);
    }

    public void RecordFlush(int flushedPackets, int skippedPackets, int bytesSent)
    {
        if (!IsEnabled)
            return;

        Interlocked.Increment(ref _flushes);
        Interlocked.Add(ref _flushBytes, bytesSent);
    }

    public void RecordDropped(PacketOpcode opcode)
    {
        if (!IsEnabled)
            return;

        Interlocked.Increment(ref _droppedPackets);
        IncrementOpcode(_opcodeReceived, opcode);
    }

    public void RecordSkipped(PacketOpcode opcode)
    {
        if (!IsEnabled)
            return;

        Interlocked.Increment(ref _skippedPackets);
        IncrementOpcode(_opcodeSent, opcode);
    }

    public void RecordCoalesced(PacketOpcode opcode)
    {
        if (!IsEnabled)
            return;

        Interlocked.Increment(ref _coalescedPackets);
        IncrementOpcode(_opcodeSent, opcode);
    }

    public void LogIfDue(ILogger log)
    {
        if (!IsEnabled)
            return;

        var now = Stopwatch.GetTimestamp();
        var next = Interlocked.Read(ref _nextLogTicks);
        if (next != 0 && now < next)
            return;

        if (Interlocked.Exchange(ref _nextLogTicks, now + LogIntervalTicks) > now)
            return;

        var previousLogTicks = Interlocked.Exchange(ref _lastLogTicks, now);
        var elapsedSeconds = previousLogTicks == 0
            ? LogIntervalTicks / (double)Stopwatch.Frequency
            : Math.Max(0.001, (now - previousLogTicks) / (double)Stopwatch.Frequency);

        var serializationTicks = Interlocked.Exchange(ref _serializationTicks, 0);
        var serializationPackets = Interlocked.Exchange(ref _serializationPackets, 0);
        var serializationBytes = Interlocked.Exchange(ref _serializationBytes, 0);
        var sendTicks = Interlocked.Exchange(ref _sendTicks, 0);
        var sendPackets = Interlocked.Exchange(ref _sendPackets, 0);
        var receiveTicks = Interlocked.Exchange(ref _receiveTicks, 0);
        var receivePackets = Interlocked.Exchange(ref _receivePackets, 0);
        var dispatchTicks = Interlocked.Exchange(ref _dispatchTicks, 0);
        var dispatchPackets = Interlocked.Exchange(ref _dispatchPackets, 0);
        var queuedPackets = Interlocked.Exchange(ref _queuedPackets, 0);
        var flushedPackets = Interlocked.Exchange(ref _flushedPackets, 0);
        var droppedPackets = Interlocked.Exchange(ref _droppedPackets, 0);
        var skippedPackets = Interlocked.Exchange(ref _skippedPackets, 0);
        var coalescedPackets = Interlocked.Exchange(ref _coalescedPackets, 0);
        var bytesSent = Interlocked.Exchange(ref _bytesSent, 0);
        var bytesReceived = Interlocked.Exchange(ref _bytesReceived, 0);
        var flushBytes = Interlocked.Exchange(ref _flushBytes, 0);
        var flushes = Interlocked.Exchange(ref _flushes, 0);
        var queuedGauge = Interlocked.Read(ref _queuedGauge);
        var opcodeSummary = BuildOpcodeSummary();

        log.Information(
            "[NetDiag] ser={SerPackets} avgSerUs={AvgSerUs:F2} avgSize={AvgSize:F1} send={SendPackets} avgSendUs={AvgSendUs:F2} recv={RecvPackets} avgRecvUs={AvgRecvUs:F2} dispatch={DispatchPackets} avgDispatchUs={AvgDispatchUs:F2} queued={QueuedPackets}/{QueuedGauge} flushed={FlushedPackets} flushes={Flushes} skipped={SkippedPackets} dropped={DroppedPackets} coalesced={CoalescedPackets} bytesSent={BytesSent} avgBytesFlush={AvgBytesFlush:F1} rxBps={RxBps:F1} ops={Ops}",
            serializationPackets,
            AverageMicroseconds(serializationTicks, serializationPackets),
            serializationPackets == 0 ? 0 : serializationBytes / (double)serializationPackets,
            sendPackets,
            AverageMicroseconds(sendTicks, sendPackets),
            receivePackets,
            AverageMicroseconds(receiveTicks, receivePackets),
            dispatchPackets,
            AverageMicroseconds(dispatchTicks, dispatchPackets),
            queuedPackets,
            queuedGauge,
            flushedPackets,
            flushes,
            skippedPackets,
            droppedPackets,
            coalescedPackets,
            bytesSent,
            flushes == 0 ? 0 : flushBytes / (double)flushes,
            bytesReceived / elapsedSeconds,
            opcodeSummary);
    }

    private static void IncrementOpcode(long[] counters, PacketOpcode opcode)
    {
        var index = (int)opcode;
        if ((uint)index >= OpcodeSlots)
            return;

        Interlocked.Increment(ref counters[index]);
    }

    private static double AverageMicroseconds(long ticks, long count)
    {
        return count == 0 ? 0 : ticks * 1_000_000.0 / Stopwatch.Frequency / count;
    }

    private string BuildOpcodeSummary()
    {
        var builder = new StringBuilder(128);
        for (var i = 0; i < OpcodeSlots; i++)
        {
            var sent = Interlocked.Exchange(ref _opcodeSent[i], 0);
            var received = Interlocked.Exchange(ref _opcodeReceived[i], 0);
            if (sent == 0 && received == 0)
                continue;

            if (builder.Length > 0)
                builder.Append(',');

            builder.Append((PacketOpcode)i);
            builder.Append(":s");
            builder.Append(sent);
            builder.Append("/r");
            builder.Append(received);
        }

        return builder.Length == 0 ? "none" : builder.ToString();
    }
}
