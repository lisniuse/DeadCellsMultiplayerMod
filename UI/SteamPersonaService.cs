using System;
using System.IO;
using Microsoft.Win32;

namespace DeadCellsMultiplayerMod.UI
{
    internal static class SteamPersonaService
    {
        public static string? TryGetSteamPersonaName()
        {
            string? steamPath = null;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key != null)
                {
                    steamPath = key.GetValue("SteamPath") as string;
                    if (string.IsNullOrWhiteSpace(steamPath))
                        steamPath = key.GetValue("InstallPath") as string;
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(steamPath))
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                    if (key != null)
                        steamPath = key.GetValue("InstallPath") as string;
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(steamPath))
                return null;

            string loginUsersPath = Path.Combine(steamPath, "config", "loginusers.vdf");
            if (!File.Exists(loginUsersPath))
                return null;

            return TryParseMostRecentPersonaName(loginUsersPath);
        }

        public static string? TryParseMostRecentPersonaName(string path)
        {
            try
            {
                var lines = File.ReadAllLines(path);
                int depth = 0;
                bool pendingUserBlock = false;
                bool inUserBlock = false;
                int userBlockDepth = 0;
                bool isMostRecent = false;
                string? personaCandidate = null;

                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0)
                        continue;

                    if (!inUserBlock && depth == 1 && IsQuotedKeyOnly(line))
                    {
                        pendingUserBlock = true;
                        isMostRecent = false;
                        personaCandidate = null;
                    }

                    if (line.StartsWith("{", StringComparison.Ordinal))
                    {
                        depth++;
                        if (pendingUserBlock && depth == 2)
                        {
                            inUserBlock = true;
                            userBlockDepth = depth;
                            pendingUserBlock = false;
                        }
                        continue;
                    }

                    if (line.StartsWith("}", StringComparison.Ordinal))
                    {
                        if (inUserBlock && depth == userBlockDepth)
                        {
                            if (isMostRecent && !string.IsNullOrWhiteSpace(personaCandidate))
                                return personaCandidate;
                            inUserBlock = false;
                            personaCandidate = null;
                            isMostRecent = false;
                        }
                        depth = Math.Max(0, depth - 1);
                        continue;
                    }

                    if (!inUserBlock)
                        continue;

                    if (TryParseVdfPair(line, out var key, out var value))
                    {
                        if (key.Equals("PersonaName", StringComparison.OrdinalIgnoreCase))
                            personaCandidate = value;
                        else if (key.Equals("MostRecent", StringComparison.OrdinalIgnoreCase))
                            isMostRecent = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch { }

            return null;
        }

        public static bool IsQuotedKeyOnly(string line)
        {
            if (!line.StartsWith("\"", StringComparison.Ordinal))
                return false;
            int secondQuote = line.IndexOf('"', 1);
            if (secondQuote < 0)
                return false;
            int thirdQuote = line.IndexOf('"', secondQuote + 1);
            return thirdQuote < 0;
        }

        public static bool TryParseVdfPair(string line, out string key, out string value)
        {
            key = string.Empty;
            value = string.Empty;
            if (!line.StartsWith("\"", StringComparison.Ordinal))
                return false;
            int keyEnd = line.IndexOf('"', 1);
            if (keyEnd < 0)
                return false;
            int valueStart = line.IndexOf('"', keyEnd + 1);
            if (valueStart < 0)
                return false;
            int valueEnd = line.IndexOf('"', valueStart + 1);
            if (valueEnd < 0)
                return false;
            key = line.Substring(1, keyEnd - 1);
            value = line.Substring(valueStart + 1, valueEnd - valueStart - 1);
            return true;
        }
    }
}
