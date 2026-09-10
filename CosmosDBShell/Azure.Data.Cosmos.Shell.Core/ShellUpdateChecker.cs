// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Net.Http;
using System.Text.Json;
using NuGet.Versioning;

internal static class ShellUpdateChecker
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(5),
        MaxResponseContentBufferSize = 5 * 1024 * 1024,
    };

    internal static Task<JsonElement> FetchReleasesAsync(CancellationToken token) => FetchReleasesAsync(Client, token);

    internal static async Task<JsonElement> FetchReleasesAsync(HttpClient client, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/Azure/CosmosDBShell/releases?per_page=100");
        request.Headers.UserAgent.ParseAdd("CosmosDBShell-doctor");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return document.RootElement.Clone();
    }

    internal static UpdateInfo CompareReleases(string currentVersion, JsonElement releases)
    {
        if (!NuGetVersion.TryParse(currentVersion, out var current))
        {
            return new("update-version-unknown", null);
        }

        NuGetVersion? latest = null;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean())
            {
                continue;
            }

            var tag = release.GetProperty("tag_name").GetString();
            if (!NuGetVersion.TryParse(tag?.TrimStart('v', 'V'), out var candidate))
            {
                continue;
            }

            if (release.GetProperty("prerelease").GetBoolean() && !candidate.IsPrerelease)
            {
                candidate = new NuGetVersion(candidate.Major, candidate.Minor, candidate.Patch, "preview");
            }

            if (!current.IsPrerelease && candidate.IsPrerelease)
            {
                continue;
            }

            if (latest == null || VersionComparer.VersionRelease.Compare(candidate, latest) > 0)
            {
                latest = candidate;
            }
        }

        return latest == null
            ? new("update-no-release", null)
            : new(VersionComparer.VersionRelease.Compare(latest, current) > 0 ? "update-available" : "update-current", latest.ToNormalizedString());
    }

    internal sealed record UpdateInfo(string Code, string? LatestVersion);
}