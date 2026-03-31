using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json;

namespace DeadCellsMultiplayerMod
{
    /// <summary>
    /// Out-of-process mob wire encoder (no Hashlink / dc). Parent sends JSON commands; worker replies with encoded lines.
    /// </summary>
    internal static class MobSyncWorker
    {
        private const int PipeConnectTimeoutMs = 15000;

        public static void WorkerEntry()
        {
            var commandPipeName = Environment.GetEnvironmentVariable(MobSyncWorkerEnvironment.EnvCommandPipe) ?? string.Empty;
            var eventPipeName = Environment.GetEnvironmentVariable(MobSyncWorkerEnvironment.EnvEventPipe) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(commandPipeName) || string.IsNullOrWhiteSpace(eventPipeName))
                throw new InvalidOperationException("Mob sync worker pipe names are missing");

            using var commandPipe = new NamedPipeClientStream(".", commandPipeName, PipeDirection.In, PipeOptions.Asynchronous);
            using var eventPipe = new NamedPipeClientStream(".", eventPipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            commandPipe.Connect(PipeConnectTimeoutMs);
            eventPipe.Connect(PipeConnectTimeoutMs);

            using var commandReader = new StreamReader(commandPipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 65536, leaveOpen: true);
            using var eventWriter = new StreamWriter(eventPipe, new UTF8Encoding(false), 65536, leaveOpen: true)
            {
                AutoFlush = true
            };

            WriteEvent(eventWriter, new MobSyncEventDto { Type = MobSyncEventTypes.Ready, V = 1 });

            while (true)
            {
                var line = commandReader.ReadLine();
                if (line == null)
                    break;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var wireLine = ProcessCommandLine(line);
                    WriteEvent(eventWriter, new MobSyncEventDto { Type = MobSyncEventTypes.Line, Line = wireLine, V = 1 });
                }
                catch (Exception ex)
                {
                    WriteEvent(eventWriter, new MobSyncEventDto
                    {
                        Type = MobSyncEventTypes.Error,
                        Message = ex.ToString(),
                        V = 1
                    });
                }
            }
        }

        private static void WriteEvent(StreamWriter writer, MobSyncEventDto dto)
        {
            writer.WriteLine(JsonConvert.SerializeObject(dto));
        }

        internal static string ProcessCommandLine(string jsonLine)
        {
            var cmd = JsonConvert.DeserializeObject<MobSyncCommandDto>(jsonLine);
            if (cmd == null || string.IsNullOrWhiteSpace(cmd.Cmd))
                throw new InvalidOperationException("Invalid mob sync command");

            switch (cmd.Cmd.Trim())
            {
                case MobSyncCommandKinds.MobStates:
                    return EncodeMobStates(cmd);
                case MobSyncCommandKinds.MobEvents:
                    return EncodeMobEvents(cmd);
                case MobSyncCommandKinds.MobDrawBatch:
                    return EncodeMobDrawBatch(cmd);
                default:
                    throw new InvalidOperationException("Unknown mob sync command: " + cmd.Cmd);
            }
        }

        private static string EncodeMobStates(MobSyncCommandDto cmd)
        {
            if (cmd.States == null || cmd.States.Count == 0)
                throw new InvalidOperationException("mob_states requires states");

            var list = new List<NetNode.MobStateSnapshot>(cmd.States.Count);
            for (int i = 0; i < cmd.States.Count; i++)
            {
                var s = cmd.States[i];
                list.Add(new NetNode.MobStateSnapshot(
                    s.Index,
                    s.X,
                    s.Y,
                    s.Dir,
                    s.Life,
                    s.MaxLife,
                    s.AnimPayload,
                    s.Type,
                    s.StatePayload));
            }

            return MobWireCodec.BuildMobStatesLine(list);
        }

        private static string EncodeMobEvents(MobSyncCommandDto cmd)
        {
            if (cmd.Events == null || cmd.Events.Count == 0)
                throw new InvalidOperationException("mob_events requires events");

            var list = new List<NetNode.MobEventUpdate>(cmd.Events.Count);
            for (int i = 0; i < cmd.Events.Count; i++)
            {
                var e = cmd.Events[i];
                IReadOnlyList<string> ev = e.Events != null ? e.Events : (IReadOnlyList<string>)Array.Empty<string>();
                list.Add(new NetNode.MobEventUpdate(e.Index, e.X, e.Y, e.Dir, ev, e.Type));
            }

            return MobWireCodec.BuildMobEventsLine(list);
        }

        private static string EncodeMobDrawBatch(MobSyncCommandDto cmd)
        {
            if (cmd.Draws == null || cmd.Draws.Count == 0)
                throw new InvalidOperationException("mob_draw_batch requires draws");

            var list = new List<NetNode.MobDraw>(cmd.Draws.Count);
            for (int i = 0; i < cmd.Draws.Count; i++)
            {
                var d = cmd.Draws[i];
                list.Add(new NetNode.MobDraw(d.UserId, d.MobIndex, d.IsOutOfGame, d.IsOnScreen));
            }

            return MobWireCodec.BuildMobDrawLine(list);
        }
    }
}
