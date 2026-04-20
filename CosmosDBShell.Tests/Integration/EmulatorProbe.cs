// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Net.Http;
using System.Threading.Tasks;

using Xunit.Sdk;

/// <summary>
/// Shared helper for detecting Cosmos DB emulator availability across integration tests.
/// </summary>
internal static class EmulatorProbe
{
    private const string EnvVarName = "COSMOS_EMULATOR_AVAILABLE";

    /// <summary>
    /// Returns <c>true</c> if the emulator appears to be reachable, either because the
    /// <c>COSMOS_EMULATOR_AVAILABLE</c> environment variable is set to <c>true</c> or because
    /// the emulator endpoint responds to an HTTP GET.
    /// </summary>
    public static async Task<bool> IsAvailableAsync()
    {
        var envVar = Environment.GetEnvironmentVariable(EnvVarName);
        if (string.Equals(envVar, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            using var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

            // The emulator returns 401 Unauthorized for unauthenticated GETs on "/",
            // which is still a clear signal that it is reachable.
            using var response = await client.GetAsync(new Uri($"{EmulatorTestBase.EmulatorEndpoint}/"));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Throws a <see cref="SkipException"/> if the emulator is not available, so that the
    /// current test is skipped rather than failing.
    /// </summary>
    public static async Task EnsureAvailableAsync()
    {
        if (!await IsAvailableAsync())
        {
            throw SkipException.ForSkip("Cosmos DB emulator not available");
        }
    }
}
