// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Core;
using Microsoft.Azure.Cosmos;

public class ConnectCommandTests
{
    [Fact]
    public async Task ConnectAsync_CanceledToken_CancelsConnectionAttempt()
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => shell.ConnectAsync(
            "AccountEndpoint=https://example.documents.azure.com:443/;AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=;",
            mode: ConnectionMode.Gateway,
            token: cancellationTokenSource.Token));
    }
}
