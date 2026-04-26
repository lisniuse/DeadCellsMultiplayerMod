using System.Globalization;
using Newtonsoft.Json;
using IOFile = System.IO.File;
using IODirectory = System.IO.Directory;
using IOPath = System.IO.Path;

namespace DeadCellsMultiplayerMod
{
    internal static class MUser
    {
        private const int CurrentVersion = 1;
        private const int MaxCoopIdLength = 128;
        private const string MultiplayerSaveFolderName = "MSave";
        private const string MetadataExtension = ".coop.json";
        private static readonly object Sync = new();

        public static string? GetCurrentCoopId()
        {
            return GetCoopIdForSlot(null);
        }

        public static string? GetCoopIdForSlot(int? slot)
        {
            return TryLoad(slot, out var metadata)
                ? NormalizeCoopId(metadata.CoopId)
                : null;
        }

        public static string EnsureCoopIdForNewCoopWorld(string? lastHostId = null, int? lastSeed = null)
        {
            var coopId = Guid.NewGuid().ToString("N");
            SetCoopId(coopId, lastHostId, lastSeed);
            return coopId;
        }

        public static bool SetCoopId(string? coopId, string? lastHostId = null, int? lastSeed = null)
        {
            var normalized = NormalizeCoopId(coopId);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            lock (Sync)
            {
                try
                {
                    var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                    var path = GetMetadataPath(null);
                    var createdAt = now;
                    if (TryLoad(null, out var existing) && !string.IsNullOrWhiteSpace(existing.CreatedAtUtc))
                        createdAt = existing.CreatedAtUtc;

                    var metadata = new CoopMetadata
                    {
                        Version = CurrentVersion,
                        CoopId = normalized,
                        LastHostId = NormalizeMetadataValue(lastHostId),
                        LastSeed = lastSeed,
                        CreatedAtUtc = createdAt,
                        UpdatedAtUtc = now
                    };

                    IODirectory.CreateDirectory(IOPath.GetDirectoryName(path)!);
                    IOFile.WriteAllText(path, JsonConvert.SerializeObject(metadata, Formatting.Indented));
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static void ClearCoopId(int? slot = null)
        {
            lock (Sync)
            {
                try
                {
                    var path = GetMetadataPath(slot);
                    if (IOFile.Exists(path))
                        IOFile.Delete(path);
                }
                catch
                {
                }
            }
        }

        public static bool IsContinueCompatible(string? remoteCoopId, out string reason)
        {
            var localCoopId = GetCurrentCoopId();
            if (string.IsNullOrWhiteSpace(localCoopId))
            {
                reason = "No local coop id";
                return false;
            }

            var normalizedRemote = NormalizeCoopId(remoteCoopId);
            if (string.IsNullOrWhiteSpace(normalizedRemote))
            {
                reason = "Host coop id not received";
                return false;
            }

            if (!string.Equals(localCoopId, normalizedRemote, StringComparison.Ordinal))
            {
                reason = "Coop world mismatch";
                return false;
            }

            reason = "OK";
            return true;
        }

        public static string? NormalizeCoopId(string? coopId)
        {
            if (string.IsNullOrWhiteSpace(coopId))
                return null;

            var trimmed = coopId.Trim();
            if (trimmed.Length > MaxCoopIdLength)
                return null;

            for (var i = 0; i < trimmed.Length; i++)
            {
                var c = trimmed[i];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                    continue;

                return null;
            }

            return trimmed;
        }

        private static bool TryLoad(int? slot, out CoopMetadata metadata)
        {
            metadata = new CoopMetadata();
            lock (Sync)
            {
                try
                {
                    var path = GetMetadataPath(slot);
                    if (!IOFile.Exists(path))
                        return false;

                    var loaded = JsonConvert.DeserializeObject<CoopMetadata>(IOFile.ReadAllText(path));
                    if (loaded == null || string.IsNullOrWhiteSpace(NormalizeCoopId(loaded.CoopId)))
                        return false;

                    metadata = loaded;
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static string GetMetadataPath(int? slot)
        {
            var fileName = string.Create(
                CultureInfo.InvariantCulture,
                $"user_{ResolveSaveSlotNumber(slot)}{MetadataExtension}");
            return IOPath.Combine(GetMultiplayerSaveFolderPath(), fileName);
        }

        private static string GetMultiplayerSaveFolderPath()
        {
            return IOPath.Combine(GetSaveRootPath(), MultiplayerSaveFolderName);
        }

        private static int ResolveSaveSlotNumber(int? slot)
        {
            if (slot.HasValue && slot.Value >= 0)
                return slot.Value;

            try
            {
                var current = dc.Main.Class.ME?.options?.curSlot;
                if (current.HasValue && current.Value >= 0)
                    return current.Value;
            }
            catch
            {
            }

            return 0;
        }

        private static string GetSaveRootPath()
        {
            try
            {
                var saveRoot = dc.tool.File.Class.PATH?.ToString();
                if (!string.IsNullOrWhiteSpace(saveRoot))
                    return IOPath.GetFullPath(saveRoot);
            }
            catch
            {
            }

            try
            {
                return IOPath.GetFullPath("save");
            }
            catch
            {
                return IOPath.Combine(Environment.CurrentDirectory, "save");
            }
        }

        private static string? NormalizeMetadataValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = value.Trim()
                .Replace("|", "/", StringComparison.Ordinal)
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);

            return normalized.Length > 128 ? normalized[..128] : normalized;
        }

        private sealed class CoopMetadata
        {
            public int Version { get; set; } = CurrentVersion;
            public string CoopId { get; set; } = string.Empty;
            public string? LastHostId { get; set; }
            public int? LastSeed { get; set; }
            public string CreatedAtUtc { get; set; } = string.Empty;
            public string UpdatedAtUtc { get; set; } = string.Empty;
        }
    }
}
