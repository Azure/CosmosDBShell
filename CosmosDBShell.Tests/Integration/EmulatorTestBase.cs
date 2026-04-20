// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Threading.Tasks;

using Xunit;

[Trait("Category", "Emulator")]
[Collection("Emulator")]
public abstract class EmulatorTestBase : IntegrationTestBase, IAsyncLifetime
{
    internal const string EmulatorEndpoint = "https://localhost:8081";

    public virtual async ValueTask InitializeAsync()
    {
        await EmulatorProbe.EnsureAvailableAsync();
    }

    public virtual ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    internal async Task ConnectToEmulatorAsync()
    {
        var connectionString = Azure.Data.Cosmos.Shell.Util.ParsedDocDBConnectionString.BuildEmulatorConnectionString(EmulatorEndpoint);
        await Shell.ConnectAsync(connectionString, null);
    }
}
