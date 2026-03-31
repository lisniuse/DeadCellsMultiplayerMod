using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ModCore.Utilities;
using Newtonsoft.Json;

namespace DeadCellsMultiplayerMod
{
    internal enum MobOutgoingKind
    {
        MobStates,
        MobEvents,
        MobDrawBatch
    }

    internal sealed class MobOutgoingJob
    {
        public required NetNode Net { get; init; }
        public MobOutgoingKind Kind { get; init; }
        public List<MobStateSnapshotWire>? States { get; init; }
        public List<MobEventUpdateWire>? Events { get; init; }
        public List<MobDrawWire>? Draws { get; init; }
    }

    /// <summary>
    /// Parent-side bridge: queues mob encode jobs and RPCs to <see cref="MobSyncWorker"/> over named pipes.
    /// </summary>
    internal sealed class MobSyncWorkerBridge : IDisposable
    {
        private const int StartupTimeoutMs = 15000;
        private const int PipeConnectPollMs = 25;
        private const int QueueCapacity = 256;
        private static readonly object StaticSync = new();
        private static MobSyncWorkerBridge? _instance;

        private readonly Process _process;
        private readonly NamedPipeServerStream _commandPipe;
        private readonly NamedPipeServerStream _eventPipe;
        private readonly StreamWriter _commandWriter;
        private readonly StreamReader _eventReader;
        private readonly object _commandSync = new();
        private readonly BlockingCollection<MobOutgoingJob> _queue;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _processorTask;
        private long _jobsProcessed;
        private long _jobsDroppedFallback;
        private int _maxQueueDepthObserved;

        private MobSyncWorkerBridge(
            Process process,
            NamedPipeServerStream commandPipe,
            NamedPipeServerStream eventPipe,
            StreamWriter commandWriter,
            StreamReader eventReader)
        {
            _process = process;
            _commandPipe = commandPipe;
            _eventPipe = eventPipe;
            _commandWriter = commandWriter;
            _eventReader = eventReader;
            _queue = new BlockingCollection<MobOutgoingJob>(QueueCapacity);
            _processorTask = Task.Run(ProcessLoop);
        }

        public bool IsRunning
        {
            get
            {
                try
                {
                    return !_process.HasExited;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool IsOutOfProcessDisabled()
        {
            var v = Environment.GetEnvironmentVariable(MobSyncWorkerEnvironment.EnvDisableOutOfProcess);
            return string.Equals(v, "0", StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryEnqueueMobStates(NetNode net, IReadOnlyList<NetNode.MobStateSnapshot> states)
        {
            if (IsOutOfProcessDisabled() || states == null || states.Count == 0)
                return false;

            var wires = new List<MobStateSnapshotWire>(states.Count);
            for (int i = 0; i < states.Count; i++)
            {
                var s = states[i];
                wires.Add(new MobStateSnapshotWire
                {
                    Index = s.Index,
                    X = s.X,
                    Y = s.Y,
                    Dir = s.Dir,
                    Life = s.Life,
                    MaxLife = s.MaxLife,
                    AnimPayload = s.AnimPayload,
                    Type = s.Type,
                    StatePayload = s.StatePayload
                });
            }

            return TryEnqueue(new MobOutgoingJob
            {
                Net = net,
                Kind = MobOutgoingKind.MobStates,
                States = wires
            });
        }

        public static bool TryEnqueueMobEvents(NetNode net, IReadOnlyList<NetNode.MobEventUpdate> updates)
        {
            if (IsOutOfProcessDisabled() || updates == null || updates.Count == 0)
                return false;

            var wires = new List<MobEventUpdateWire>(updates.Count);
            for (int i = 0; i < updates.Count; i++)
            {
                var u = updates[i];
                List<string>? evList = null;
                if (u.Events != null && u.Events.Count > 0)
                {
                    evList = new List<string>(u.Events.Count);
                    for (int j = 0; j < u.Events.Count; j++)
                        evList.Add(u.Events[j] ?? string.Empty);
                }

                wires.Add(new MobEventUpdateWire
                {
                    Index = u.Index,
                    X = u.X,
                    Y = u.Y,
                    Dir = u.Dir,
                    Events = evList,
                    Type = u.Type ?? string.Empty
                });
            }

            return TryEnqueue(new MobOutgoingJob
            {
                Net = net,
                Kind = MobOutgoingKind.MobEvents,
                Events = wires
            });
        }

        public static bool TryEnqueueMobDrawBatch(NetNode net, IReadOnlyList<NetNode.MobDraw> draws)
        {
            if (IsOutOfProcessDisabled() || draws == null || draws.Count == 0)
                return false;

            var wires = new List<MobDrawWire>(draws.Count);
            for (int i = 0; i < draws.Count; i++)
            {
                var d = draws[i];
                wires.Add(new MobDrawWire
                {
                    UserId = d.UserId,
                    MobIndex = d.MobIndex,
                    IsOutOfGame = d.IsOutOfGame,
                    IsOnScreen = d.IsOnScreen
                });
            }

            return TryEnqueue(new MobOutgoingJob
            {
                Net = net,
                Kind = MobOutgoingKind.MobDrawBatch,
                Draws = wires
            });
        }

        private static bool TryEnqueue(MobOutgoingJob job)
        {
            lock (StaticSync)
            {
                if (_instance == null || !_instance.IsRunning)
                {
                    if (!TryCreateInstance(out var bridge, out _))
                        return false;

                    _instance = bridge;
                }

                return _instance!.TryEnqueueInternal(job);
            }
        }

        private bool TryEnqueueInternal(MobOutgoingJob job)
        {
            if (!_queue.TryAdd(job, 0))
            {
                Interlocked.Increment(ref _jobsDroppedFallback);
                ModEntry.Instance?.Logger.Warning("[NetMod][MobSyncWorker] Queue full ({Cap}); fallback to in-process encode", QueueCapacity);
                return false;
            }

            var depth = _queue.Count;
            var prev = Volatile.Read(ref _maxQueueDepthObserved);
            while (depth > prev)
            {
                var replaced = Interlocked.CompareExchange(ref _maxQueueDepthObserved, depth, prev);
                if (replaced == prev)
                    break;
                prev = replaced;
            }

            if (depth >= QueueCapacity / 2)
                ModEntry.Instance?.Logger.Debug("[NetMod][MobSyncWorker] Queue depth={Depth} (cap={Cap})", depth, QueueCapacity);

            return true;
        }

        private void ProcessLoop()
        {
            try
            {
                foreach (var job in _queue.GetConsumingEnumerable(_cts.Token))
                {
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var dto = BuildCommandDto(job);
                        var json = JsonConvert.SerializeObject(dto);
                        string? wireLine = null;

                        lock (_commandSync)
                        {
                            _commandWriter.WriteLine(json);
                            _commandWriter.Flush();
                            var response = _eventReader.ReadLine();
                            if (response == null)
                                throw new IOException("Mob sync worker closed event pipe");

                            var evt = JsonConvert.DeserializeObject<MobSyncEventDto>(response);
                            if (evt == null)
                                throw new InvalidOperationException("Invalid worker response");

                            if (string.Equals(evt.Type, MobSyncEventTypes.Error, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidOperationException(evt.Message ?? "Worker error");

                            if (!string.Equals(evt.Type, MobSyncEventTypes.Line, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidOperationException("Unexpected worker response: " + evt.Type);

                            wireLine = evt.Line;
                        }

                        if (!string.IsNullOrEmpty(wireLine))
                            _ = job.Net.SendMobWireLine(wireLine);

                        Interlocked.Increment(ref _jobsProcessed);
                        sw.Stop();
                    }
                    catch (Exception ex)
                    {
                        ModEntry.Instance?.Logger.Warning(ex, "[NetMod][MobSyncWorker] Job failed; in-process fallback");
                        try
                        {
                            FallbackEncodeInProcess(job);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static void FallbackEncodeInProcess(MobOutgoingJob job)
        {
            switch (job.Kind)
            {
                case MobOutgoingKind.MobStates:
                    if (job.States == null)
                        return;
                    var states = new List<NetNode.MobStateSnapshot>(job.States.Count);
                    for (int i = 0; i < job.States.Count; i++)
                    {
                        var s = job.States[i];
                        states.Add(new NetNode.MobStateSnapshot(
                            s.Index, s.X, s.Y, s.Dir, s.Life, s.MaxLife,
                            s.AnimPayload, s.Type, s.StatePayload));
                    }

                    _ = job.Net.SendMobWireLine(MobWireCodec.BuildMobStatesLine(states));
                    break;
                case MobOutgoingKind.MobEvents:
                    if (job.Events == null)
                        return;
                    var evs = new List<NetNode.MobEventUpdate>(job.Events.Count);
                    for (int i = 0; i < job.Events.Count; i++)
                    {
                        var e = job.Events[i];
                        IReadOnlyList<string> ev = e.Events != null ? e.Events : (IReadOnlyList<string>)Array.Empty<string>();
                        evs.Add(new NetNode.MobEventUpdate(e.Index, e.X, e.Y, e.Dir, ev, e.Type));
                    }

                    _ = job.Net.SendMobWireLine(MobWireCodec.BuildMobEventsLine(evs));
                    break;
                case MobOutgoingKind.MobDrawBatch:
                    if (job.Draws == null)
                        return;
                    var draws = new List<NetNode.MobDraw>(job.Draws.Count);
                    for (int i = 0; i < job.Draws.Count; i++)
                    {
                        var d = job.Draws[i];
                        draws.Add(new NetNode.MobDraw(d.UserId, d.MobIndex, d.IsOutOfGame, d.IsOnScreen));
                    }

                    _ = job.Net.SendMobWireLine(MobWireCodec.BuildMobDrawLine(draws));
                    break;
            }
        }

        private static MobSyncCommandDto BuildCommandDto(MobOutgoingJob job)
        {
            return job.Kind switch
            {
                MobOutgoingKind.MobStates => new MobSyncCommandDto
                {
                    V = 1,
                    Cmd = MobSyncCommandKinds.MobStates,
                    States = job.States
                },
                MobOutgoingKind.MobEvents => new MobSyncCommandDto
                {
                    V = 1,
                    Cmd = MobSyncCommandKinds.MobEvents,
                    Events = job.Events
                },
                MobOutgoingKind.MobDrawBatch => new MobSyncCommandDto
                {
                    V = 1,
                    Cmd = MobSyncCommandKinds.MobDrawBatch,
                    Draws = job.Draws
                },
                _ => throw new InvalidOperationException("Unknown job kind")
            };
        }

        private static bool TryCreateInstance(out MobSyncWorkerBridge? bridge, out string error)
        {
            bridge = null;
            error = string.Empty;

            var commandPipeName = $"dccm_mobsync_cmd_{Guid.NewGuid():N}";
            var eventPipeName = $"dccm_mobsync_evt_{Guid.NewGuid():N}";

            NamedPipeServerStream? commandPipe = null;
            NamedPipeServerStream? eventPipe = null;
            Process? process = null;
            StreamWriter? commandWriter = null;
            StreamReader? eventReader = null;

            try
            {
                commandPipe = new NamedPipeServerStream(
                    commandPipeName,
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                eventPipe = new NamedPipeServerStream(
                    eventPipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                var startInfo = SteamConnect.BuildWorkerStartInfoForRuntime();
                startInfo.Environment[MobSyncWorkerEnvironment.EnvChildMode] = "1";
                startInfo.Environment[MobSyncWorkerEnvironment.EnvCommandPipe] = commandPipeName;
                startInfo.Environment[MobSyncWorkerEnvironment.EnvEventPipe] = eventPipeName;

                var assemblyPath = typeof(MobSyncWorkerBridge).Assembly.Location;
                if (string.IsNullOrWhiteSpace(assemblyPath))
                {
                    error = "Mob sync worker assembly path is unavailable";
                    return false;
                }

                var loadAssemblies = SteamConnect.BuildWorkerLoadAssembliesForRuntime(assemblyPath);
                process = WorkerProcessUtils.StartWorkerProcess(
                    typeof(SteamWorkerBootstrap).AssemblyQualifiedName!,
                    nameof(SteamWorkerBootstrap.WorkerEntry),
                    startInfo,
                    loadAssemblies);

                if (process == null)
                {
                    error = "Mob sync worker process was not started";
                    return false;
                }

                var cmdConnectTask = commandPipe.WaitForConnectionAsync();
                var evtConnectTask = eventPipe.WaitForConnectionAsync();
                if (!WaitForPipeConnections(cmdConnectTask, evtConnectTask, process, StartupTimeoutMs))
                {
                    error = BuildStartupError("Mob sync worker pipe connection timeout", process);
                    return false;
                }

                commandWriter = new StreamWriter(commandPipe, new UTF8Encoding(false), 65536, leaveOpen: true)
                {
                    AutoFlush = true
                };
                eventReader = new StreamReader(eventPipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 65536, leaveOpen: true);

                var readyLine = eventReader.ReadLine();
                if (string.IsNullOrWhiteSpace(readyLine))
                {
                    error = "Mob sync worker did not send ready";
                    return false;
                }

                var readyEvt = JsonConvert.DeserializeObject<MobSyncEventDto>(readyLine);
                if (readyEvt == null || !string.Equals(readyEvt.Type, MobSyncEventTypes.Ready, StringComparison.OrdinalIgnoreCase))
                {
                    error = "Mob sync worker ready handshake invalid: " + readyLine;
                    return false;
                }

                bridge = new MobSyncWorkerBridge(process, commandPipe, eventPipe, commandWriter, eventReader);
                commandPipe = null;
                eventPipe = null;
                process = null;
                commandWriter = null;
                eventReader = null;
                error = string.Empty;
                ModEntry.Instance?.Logger.Information("[NetMod][MobSyncWorker] Out-of-process encoder started (pid={Pid})", bridge._process.Id);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.ToString();
                return false;
            }
            finally
            {
                if (process != null)
                {
                    try
                    {
                        if (!process.HasExited)
                            process.Kill(true);
                    }
                    catch
                    {
                    }

                    try { process.Dispose(); } catch { }
                }

                try { commandWriter?.Dispose(); } catch { }
                try { eventReader?.Dispose(); } catch { }
                try { commandPipe?.Dispose(); } catch { }
                try { eventPipe?.Dispose(); } catch { }
            }
        }

        private static bool WaitForPipeConnections(Task commandTask, Task eventTask, Process process, int timeoutMs)
        {
            var timeoutAt = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < timeoutAt)
            {
                if (commandTask.IsCompleted && eventTask.IsCompleted)
                    return !(commandTask.IsFaulted || eventTask.IsFaulted || commandTask.IsCanceled || eventTask.IsCanceled);

                try
                {
                    if (process.HasExited)
                        return false;
                }
                catch
                {
                    return false;
                }

                Thread.Sleep(PipeConnectPollMs);
            }

            return false;
        }

        private static string BuildStartupError(string prefix, Process process)
        {
            int? exitCode = null;
            try
            {
                if (process.HasExited)
                    exitCode = process.ExitCode;
            }
            catch
            {
            }

            if (exitCode.HasValue)
                return $"{prefix} (exit={exitCode.Value})";

            return prefix;
        }

        public static void StopSingleton()
        {
            lock (StaticSync)
            {
                try
                {
                    _instance?.Dispose();
                }
                catch
                {
                }

                _instance = null;
            }
        }

        public void Dispose()
        {
            try
            {
                _queue.CompleteAdding();
            }
            catch
            {
            }

            try { _cts.Cancel(); } catch { }

            try
            {
                _processorTask?.Wait(2000);
            }
            catch
            {
            }

            try
            {
                if (!_process.HasExited)
                    _process.Kill(true);
            }
            catch
            {
            }

            try { _process.Dispose(); } catch { }
            try { _commandWriter.Dispose(); } catch { }
            try { _eventReader.Dispose(); } catch { }
            try { _commandPipe.Dispose(); } catch { }
            try { _eventPipe.Dispose(); } catch { }
            try { _cts.Dispose(); } catch { }

            ModEntry.Instance?.Logger.Debug(
                "[NetMod][MobSyncWorker] Stopped (processed={Processed}, dropped={Dropped}, maxDepth={MaxDepth})",
                Interlocked.Read(ref _jobsProcessed),
                Interlocked.Read(ref _jobsDroppedFallback),
                Volatile.Read(ref _maxQueueDepthObserved));
        }
    }
}
