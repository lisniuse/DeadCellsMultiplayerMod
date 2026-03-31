using System.Collections.Generic;
using Newtonsoft.Json;

namespace DeadCellsMultiplayerMod
{
    internal static class MobSyncWorkerEnvironment
    {
        /// <summary>When set to "1", child process runs MobSync worker loop.</summary>
        public const string EnvChildMode = "DCCM_MOB_SYNC_WORKER_CHILD";

        public const string EnvCommandPipe = "DCCM_MOB_SYNC_CMD_PIPE";
        public const string EnvEventPipe = "DCCM_MOB_SYNC_EVT_PIPE";

        /// <summary>Parent: set to "0" to disable out-of-process mob encode (always in-process).</summary>
        public const string EnvDisableOutOfProcess = "DCCM_MOB_SYNC_WORKER";

        /// <summary>Optional profiling hook: same-process encode on thread pool (not wired to NetNode in v1).</summary>
        public const string EnvAsyncInProcEncode = "DCCM_MOB_SYNC_ASYNC_INPROC";
    }

    internal sealed class MobSyncCommandDto
    {
        [JsonProperty("v")]
        public int V { get; set; } = 1;

        [JsonProperty("cmd")]
        public string Cmd { get; set; } = "";

        [JsonProperty("states")]
        public List<MobStateSnapshotWire>? States { get; set; }

        [JsonProperty("events")]
        public List<MobEventUpdateWire>? Events { get; set; }

        [JsonProperty("draws")]
        public List<MobDrawWire>? Draws { get; set; }
    }

    internal sealed class MobStateSnapshotWire
    {
        [JsonProperty("index")]
        public int Index { get; set; }

        [JsonProperty("x")]
        public double X { get; set; }

        [JsonProperty("y")]
        public double Y { get; set; }

        [JsonProperty("dir")]
        public int Dir { get; set; }

        [JsonProperty("life")]
        public int Life { get; set; }

        [JsonProperty("maxLife")]
        public int MaxLife { get; set; }

        [JsonProperty("animPayload")]
        public string AnimPayload { get; set; } = "";

        [JsonProperty("type")]
        public string Type { get; set; } = "";

        [JsonProperty("statePayload")]
        public string StatePayload { get; set; } = "";
    }

    internal sealed class MobEventUpdateWire
    {
        [JsonProperty("index")]
        public int Index { get; set; }

        [JsonProperty("x")]
        public double X { get; set; }

        [JsonProperty("y")]
        public double Y { get; set; }

        [JsonProperty("dir")]
        public int Dir { get; set; }

        [JsonProperty("events")]
        public List<string>? Events { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; } = "";
    }

    internal sealed class MobDrawWire
    {
        [JsonProperty("userId")]
        public int UserId { get; set; }

        [JsonProperty("mobIndex")]
        public int MobIndex { get; set; }

        [JsonProperty("isOutOfGame")]
        public bool IsOutOfGame { get; set; }

        [JsonProperty("isOnScreen")]
        public bool IsOnScreen { get; set; }
    }

    internal sealed class MobSyncEventDto
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "";

        [JsonProperty("line")]
        public string? Line { get; set; }

        [JsonProperty("message")]
        public string? Message { get; set; }

        [JsonProperty("v")]
        public int V { get; set; } = 1;
    }

    internal static class MobSyncCommandKinds
    {
        public const string MobStates = "mob_states";
        public const string MobEvents = "mob_events";
        public const string MobDrawBatch = "mob_draw_batch";
    }

    internal static class MobSyncEventTypes
    {
        public const string Ready = "ready";
        public const string Line = "line";
        public const string Error = "error";
    }
}
