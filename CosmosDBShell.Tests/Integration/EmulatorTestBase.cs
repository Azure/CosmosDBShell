// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Net.Http;
using System.Threading.Tasks;

using Xunit;
using Xunit.Sdk;

[Trait("Category", "Emulator")]
[Collection("Emulator")]
public abstract class EmulatorTestBase : IntegrationTestBase, IAsyncLifetime
{
    internal const string EmulatorEndpoint = "https://localhost:8081";

    internal const string EmulatorConnectionString =
        "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private static bool? emulatorAvailable;

    public async ValueTask InitializeAsync()
    {
        if (emulatorAvailable == null)
        {
            emulatorAvailable = await ProbeEmulatorAsync();
        }

        if (!emulatorAvailable.Value)
        {
            throw SkipException.ForSkip("Cosmos DB emulator not available");
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    internal async Task ConnectToEmulatorAsync()
    {
        var connectionString = Azure.Data.Cosmos.Shell.Util.ParsedDocDBConnectionString.BuildEmulatorConnectionString(EmulatorEndpoint);
        await Shell.ConnectAsync(connectionString, null);
    }

    private static async Task<bool> ProbeEmulatorAsync()
    {
        var envVar = Environment.GetEnvironmentVariable("COSMOS_EMULATOR_AVAILABLE");
        if (string.Equals(envVar, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            using var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync(new Uri($"{EmulatorEndpoint}/"));
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
