using Newtonsoft.Json;
using Serilog;

namespace DeadCellsMultiplayerMod.UI
{
    internal sealed class MenuConfigData
    {
        public string User { get; set; } = "guest";
        public string LastIp { get; set; } = "127.0.0.1";
        public int LastPort { get; set; } = 1234;
        public string PlayerId { get; set; } = Guid.NewGuid().ToString("N");
    }

    internal static class MenuConfigService
    {
        public static string GetConfigPath()
        {
            var baseDir = AppContext.BaseDirectory;
            var root = Directory.GetParent(baseDir)?.Parent?.Parent?.Parent?.FullName ?? baseDir;
            var dir = Path.Combine(root, "mods", "DeadCellsMultiplayerMod");
            return Path.Combine(dir, "config.json");
        }

        public static string CleanUsername(string? value)
        {
            var cleaned = string.IsNullOrWhiteSpace(value) ? "guest" : value.Trim();
            cleaned = cleaned.Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
            return cleaned.Length == 0 ? "guest" : cleaned;
        }

        public static string GetDefaultUsername()
        {
            var steamName = SteamPersonaService.TryGetSteamPersonaName();
            if (!string.IsNullOrWhiteSpace(steamName))
                return CleanUsername(steamName);
            try
            {
                var env = Environment.UserName;
                if (!string.IsNullOrWhiteSpace(env))
                    return CleanUsername(env);
            }
            catch { }
            return "guest";
        }

        public static MenuConfigData? Load(ILogger? log)
        {
            try
            {
                var path = GetConfigPath();
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var cfg = JsonConvert.DeserializeObject<MenuConfigJson>(json);
                    if (cfg != null)
                    {
                        return new MenuConfigData
                        {
                            User = CleanUsername(string.IsNullOrWhiteSpace(cfg.user) ? GetDefaultUsername() : cfg.user),
                            LastIp = string.IsNullOrWhiteSpace(cfg.last_ip) ? "127.0.0.1" : cfg.last_ip.Trim(),
                            LastPort = cfg.last_port <= 0 || cfg.last_port > 65535 ? 1234 : cfg.last_port,
                            PlayerId = string.IsNullOrWhiteSpace(cfg.player_id) ? Guid.NewGuid().ToString("N") : cfg.player_id.Trim()
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                log?.Warning("[NetMod] Failed to load config: {Message}", ex.Message);
            }

            return new MenuConfigData
            {
                User = CleanUsername(GetDefaultUsername()),
                LastIp = "127.0.0.1",
                LastPort = 1234,
                PlayerId = Guid.NewGuid().ToString("N")
            };
        }

        public static void Save(MenuConfigData data, ILogger? log)
        {
            try
            {
                var path = GetConfigPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var cfg = new MenuConfigJson
                {
                    user = data.User,
                    last_ip = data.LastIp,
                    last_port = data.LastPort,
                    player_id = data.PlayerId
                };
                var json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                log?.Warning("[NetMod] Failed to save config: {Message}", ex.Message);
            }
        }

        private sealed class MenuConfigJson
        {
            public string user { get; set; } = "guest";
            public string last_ip { get; set; } = "127.0.0.1";
            public int last_port { get; set; } = 1234;
            public string player_id { get; set; } = Guid.NewGuid().ToString("N");
        }
    }
}
