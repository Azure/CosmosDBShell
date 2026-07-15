// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;

    /// <summary>
    /// Handles persisting and retrieving connection profiles.
    /// </summary>
    public static class ProfileManager
    {
        private static readonly object SyncLock = new();

        private static string GetProfileFilePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cosmosdbshell",
                "profiles.json");
        }

        private static Dictionary<string, ConnectionProfile> LoadAllUnsafe()
        {
            var profileFilePath = GetProfileFilePath();
            if (!File.Exists(profileFilePath))
            {
                return new Dictionary<string, ConnectionProfile>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var json = File.ReadAllText(profileFilePath);
                var parsed = JsonSerializer.Deserialize<Dictionary<string, ConnectionProfile>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });

                var normalized = new Dictionary<string, ConnectionProfile>(StringComparer.OrdinalIgnoreCase);
                if (parsed is not null)
                {
                    foreach (var kvp in parsed)
                    {
                        normalized[kvp.Key] = kvp.Value;
                    }
                }

                return normalized;
            }
            catch (JsonException)
            {
                return new Dictionary<string, ConnectionProfile>(StringComparer.OrdinalIgnoreCase);
            }
            catch (IOException)
            {
                return new Dictionary<string, ConnectionProfile>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void SaveAllUnsafe(Dictionary<string, ConnectionProfile> all)
        {
            var profileFilePath = GetProfileFilePath();
            var dir = Path.GetDirectoryName(profileFilePath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(profileFilePath, json);
        }

        public static void SaveProfile(string name, ConnectionProfile profile)
        {
            lock (SyncLock)
            {
                var all = LoadAllUnsafe();
                all[name] = profile;
                SaveAllUnsafe(all);
            }
        }

        public static ConnectionProfile? GetProfile(string name)
        {
            lock (SyncLock)
            {
                var all = LoadAllUnsafe();
                return all.TryGetValue(name, out var profile) ? profile : null;
            }
        }

        public static bool DeleteProfile(string name)
        {
            lock (SyncLock)
            {
                var all = LoadAllUnsafe();
                if (!all.Remove(name))
                {
                    return false;
                }

                SaveAllUnsafe(all);
                return true;
            }
        }

        public static IReadOnlyDictionary<string, ConnectionProfile> ListProfiles()
        {
            lock (SyncLock)
            {
                return LoadAllUnsafe();
            }
        }
    }
}
