// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using Azure.Data.Cosmos.Shell.Core;

// Opt-in: the emulator cannot establish serverless behavior, so this needs a real serverless account.
[Trait("Category", "LiveServerless")]
public class ServerlessCreationSmokeTests : IntegrationTestBase
{
    internal const string ConnectionStringVariable = "COSMOSDB_SHELL_SERVERLESS_TEST_CONNECTION_STRING";

    [Fact]
    public async Task MkdbAndMkcon_WithoutThroughputOptions_SucceedOnServerlessAccount()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(connectionString), $"Set {ConnectionStringVariable} to a serverless account connection string to run this test.");

        await Shell.ConnectAsync(connectionString!, null, token: TestContext.Current.CancellationToken);
        var databaseName = $"shell-serverless-{Guid.NewGuid():N}";
        try
        {
            var database = await RunScriptAsync($"mkdb {databaseName}");
            Assert.False(database is ErrorCommandState, FormatError(database));

            var container = await RunScriptAsync($"mkcon Items /pk --database {databaseName}");
            Assert.False(container is ErrorCommandState, FormatError(container));

            var rejected = await RunScriptAsync($"mkcon Rejected /pk --database {databaseName} --ru 400");
            Assert.IsType<ErrorCommandState>(rejected);
        }
        finally
        {
            await RunScriptAsync($"rmdb {databaseName} true");
        }
    }
}
