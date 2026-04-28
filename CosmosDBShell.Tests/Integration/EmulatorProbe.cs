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
#if COSMOS_REQUIRE_EMULATOR
    private const bool RequireEmulatorTests = true;
#else
    private const bool RequireEmulatorTests = false;
#endif

    /// <summary>
    /// Returns <c>true</c> only if the emulator endpoint responds to an HTTP GET.
    /// </summary>
    public static async Task<bool> IsAvailableAsync()
    {
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
    /// Skips the current test when the emulator is optional, or fails early with a clear
    /// exception when the test run was compiled with <c>CosmosRequireEmulator=true</c>.
    /// </summary>
    public static async Task EnsureAvailableAsync()
    {
        if (!await IsAvailableAsync())
        {
            if (RequireEmulatorTests)
            {
                throw new EmulatorUnavailableException();
            }

            throw SkipException.ForSkip("Cosmos DB emulator not available");
        }
    }

    private sealed class EmulatorUnavailableException : Exception
    {
        public EmulatorUnavailableException()
            : base("Cosmos DB emulator is required for this test run, but https://localhost:8081 did not respond.")
        {
        }
    }
}
