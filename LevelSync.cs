using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using dc.level;
using Rand = dc.libs.Rand;

namespace DeadCellsMultiplayerMod
{
    internal partial class GameDataSync
    {
        private static readonly object _levelGraphLock = new();
        private static readonly Dictionary<string, LevelGraphSync> _remoteLevelGraphs = new(StringComparer.Ordinal);

        private sealed class LevelGraphSync
        {
            public int V { get; set; } = 1;
            public string LevelId { get; set; } = string.Empty;
            public string? RootUid { get; set; }
            public int ZLinkId { get; set; }
            public double? PostGraphRandSeed { get; set; }
            public List<LevelGraphNodeSync> Nodes { get; set; } = new();
        }

        private sealed class LevelGraphNodeSync
        {
            public string Uid { get; set; } = string.Empty;
            public string? ParentUid { get; set; }
            public string? SubTeleportUid { get; set; }
            public bool IsZRoot { get; set; }
            public string RType { get; set; } = string.Empty;
            public int Group { get; set; }
            public int Id { get; set; }
            public int Flags { get; set; }
            public string? ForcedTemplateId { get; set; }
            public string? ExitLevel { get; set; }
            public string? ExitName { get; set; }
            public int? ExitColor { get; set; }
            public int ChildPriority { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
            public int SpawnDistance { get; set; }
            public double FillerWeight { get; set; }
            public int? ParentLinkConstraint { get; set; }
            public List<string>? ChildrenUids { get; set; }
            public List<string>? ZChildrenUids { get; set; }
            public List<int>? Npcs { get; set; }
            public List<LevelGraphZLinkSync>? ZLinks { get; set; }
            public LevelGraphGenDataSync? GenData { get; set; }
        }

        private sealed class LevelGraphZLinkSync
        {
            public int Id { get; set; }
            public string DestUid { get; set; } = string.Empty;
            public string? DoorId { get; set; }
            public int? ContentClue { get; set; }
        }

        private sealed class LevelGraphGenDataSync
        {
            public string? SpecificBiome { get; set; }
            public bool? ZDoorLock { get; set; }
            public bool? ForcePauseTimer { get; set; }
            public bool? ShouldBeFlipped { get; set; }
            public int? GenSubTeleportTo { get; set; }
            public LevelGraphZDoorTypeSync? ZDoorType { get; set; }
        }

        private sealed class LevelGraphZDoorTypeSync
        {
            public int RawIndex { get; set; }
            public int? IntParam0 { get; set; }
            public double? DoubleParam0 { get; set; }
        }

        public static void ReceiveLevelGraph(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return;

            try
            {
                var graph = JsonSerializer.Deserialize<LevelGraphSync>(payload);
                if (graph == null || string.IsNullOrWhiteSpace(graph.LevelId))
                    return;

                lock (_levelGraphLock)
                {
                    _remoteLevelGraphs[graph.LevelId] = graph;
                }

                _log?.Information("[NetMod] Received level graph for {LevelId} ({Count} nodes)", graph.LevelId, graph.Nodes?.Count ?? 0);
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] Failed to parse level graph sync: {Message}", ex.Message);
            }
        }

        public static void SendLevelGraph(string levelId, RoomNode? root, LevelStruct? graph, Rand? rng, NetNode? net)
        {
            if (net == null || !net.IsAlive)
            {
                _log?.Information("[NetMod] Skip level graph send for {LevelId}: net unavailable", levelId);
                return;
            }

            if (graph == null || string.IsNullOrWhiteSpace(levelId))
            {
                _log?.Warning("[NetMod] Skip level graph send: invalid graph/levelId (level={LevelId})", levelId);
                return;
            }

            try
            {
                var sync = CaptureLevelGraph(levelId, graph);
                if (sync == null)
                {
                    _log?.Warning("[NetMod] CaptureLevelGraph returned null for {LevelId} (allLen={AllLen})", levelId, graph.all?.length ?? -1);
                    return;
                }

                if (sync.Nodes.Count == 0)
                {
                    _log?.Warning("[NetMod] Captured empty level graph for {LevelId} (allLen={AllLen})", levelId, graph.all?.length ?? -1);
                    return;
                }

                try
                {
                    sync.RootUid = root?.uid?.ToString();
                }
                catch
                {
                }

                try
                {
                    if (rng != null)
                        sync.PostGraphRandSeed = rng.seed;
                }
                catch
                {
                }

                var json = JsonSerializer.Serialize(sync);
                net.SendLevelGraph(json);
                _log?.Information("[NetNode] Sent level graph for {LevelId} ({Count} nodes, postRand={PostRand})",
                    levelId,
                    sync.Nodes.Count,
                    sync.PostGraphRandSeed.HasValue ? sync.PostGraphRandSeed.Value.ToString(CultureInfo.InvariantCulture) : "n/a");
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] Failed to send level graph for {LevelId}: {Message}", levelId, ex.Message);
            }
        }

        public static bool TryApplyRemoteLevelGraph(string levelId, LevelStruct? graph, Rand? rng, int timeoutMs, out RoomNode? appliedRoot, out string reason)
        {
            reason = string.Empty;
            appliedRoot = null;
            if (graph == null || string.IsNullOrWhiteSpace(levelId))
            {
                reason = "invalid arguments";
                return false;
            }

            if (!TryWaitGetRemoteLevelGraph(levelId, timeoutMs, out var remoteGraph))
            {
                reason = "remote graph not received";
                return false;
            }

            if (remoteGraph == null || remoteGraph.Nodes == null || remoteGraph.Nodes.Count == 0)
            {
                reason = "remote graph payload empty";
                return false;
            }

            var applied = ApplyLevelGraph(graph, remoteGraph, out appliedRoot, out reason);
            if (applied && rng != null && remoteGraph.PostGraphRandSeed.HasValue)
            {
                try
                {
                    rng.seed = remoteGraph.PostGraphRandSeed.Value;
                }
                catch (Exception ex)
                {
                    applied = false;
                    reason = "failed to apply post-graph rand seed: " + ex.Message;
                }
            }

            ConsumeRemoteLevelGraph(levelId);
            if (!applied)
                return false;

            return true;
        }

        private static bool TryWaitGetRemoteLevelGraph(string levelId, int timeoutMs, out LevelGraphSync? graph)
        {
            graph = null;
            if (TryGetRemoteLevelGraph(levelId, out graph))
                return true;

            if (timeoutMs <= 0)
                return false;

            var deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                Thread.Sleep(2);
                if (TryGetRemoteLevelGraph(levelId, out graph))
                    return true;
            }

            return false;
        }

        private static bool TryGetRemoteLevelGraph(string levelId, out LevelGraphSync? graph)
        {
            lock (_levelGraphLock)
            {
                if (_remoteLevelGraphs.TryGetValue(levelId, out var found))
                {
                    graph = found;
                    return true;
                }
            }

            graph = null;
            return false;
        }

        private static void ConsumeRemoteLevelGraph(string levelId)
        {
            if (string.IsNullOrWhiteSpace(levelId))
                return;

            lock (_levelGraphLock)
            {
                _remoteLevelGraphs.Remove(levelId);
            }
        }
    }
}
