using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Azure.Data.Cosmos.Shell.Core
{
    /// <summary>
    /// Represents a saved connection profile (non‑secret information only).
    /// </summary>
    public class ConnectionProfile
    {
        public string Endpoint { get; set; } = string.Empty;
        public string? Mode { get; set; }
        public string? LoginHint { get; set; }
        public string? TenantId { get; set; }
        public string? ManagedIdentityClientId { get; set; }
    }

    /// <summary>
    /// Handles persisting and retrieving connection profiles.
    /// </summary>
    public static class ProfileManager
    {
        private static readonly object _lock = new();
        private static readonly string _profileFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cosmosdbshell",
            "profiles.json");

        private static Dictionary<string, ConnectionProfile> LoadAll()
        {
            lock (_lock)
            {
                if (!File.Exists(_profileFilePath))
                {
                    return new Dictionary<string, ConnectionProfile>(StringComparer.OrdinalIgnoreCase);
                }

                var json = File.ReadAllText(_profileFilePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, ConnectionProfile>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                }) ?? new Dictionary<string, ConnectionProfile>(StringComparer.OrdinalIgnoreCase);
                return new Dictionary<string, ConnectionProfile>(dict, StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void SaveAll(Dictionary<string, ConnectionProfile> all)
        {
            lock (_lock)
            {
                var dir = Path.GetDirectoryName(_profileFilePath)!;
                Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_profileFilePath, json);
            }
        }

        public static void SaveProfile(string name, ConnectionProfile profile)
        {
            var all = LoadAll();
            all[name] = profile;
            SaveAll(all);
        }

        public static ConnectionProfile? GetProfile(string name)
        {
            var all = LoadAll();
            return all.TryGetValue(name, out var profile) ? profile : null;
        }

        public static void DeleteProfile(string name)
        {
            var all = LoadAll();
            if (all.Remove(name))
            {
                SaveAll(all);
            }
        }

        public static IReadOnlyDictionary<string, ConnectionProfile> ListProfiles()
        {
            return LoadAll();
        }
    }
}
