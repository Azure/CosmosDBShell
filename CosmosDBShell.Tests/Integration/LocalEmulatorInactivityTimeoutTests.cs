// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

public class LocalEmulatorInactivityTimeoutTests : ConnectedEmulatorTestBase
{
    private string databaseName = null!;
    private Container container = null!;
    private string inputPath = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var token = TestContext.Current.CancellationToken;
        this.databaseName = $"InactivityTimeout_{Guid.NewGuid():N}";
        CreatedDatabases.Add(this.databaseName);
        var database = (await this.CosmosClient.CreateDatabaseAsync(this.databaseName, cancellationToken: token)).Database;
        this.container = (await database.CreateContainerAsync("items", "/pk", cancellationToken: token)).Container;
        var options = ShellInterpreter.CreateClientOptions(ConnectionMode.Gateway, isEmulator: true);
        options.ServerCertificateCustomValidationCallback = (_, _, _) => true;
        options.CustomHandlers.Add(new OperationProgressHandler(Shell));
        options.CustomHandlers.Add(new PacedRequestHandler());
        Shell.Connect(new CosmosClient(ParsedDocDBConnectionString.BuildEmulatorConnectionString(EmulatorEndpoint), options));
        Shell.Options = new Program.CosmosShellOptions { Output = "json" };
        var navigation = await this.ExecuteAsync($"cd /{this.databaseName}/items");
        Assert.False(navigation.IsError, FormatError(navigation));
        this.inputPath = Path.GetTempFileName();
        await File.WriteAllLinesAsync(
            this.inputPath,
            Enumerable.Range(0, 300).Select(index => JsonSerializer.Serialize(new
            {
                id = index.ToString(CultureInfo.InvariantCulture),
                pk = $"p{index % 10}",
            })),
            token);
    }

    public override void Dispose()
    {
        if (this.inputPath is not null)
        {
            File.Delete(this.inputPath);
        }

        base.Dispose();
    }

    [Fact]
    public async Task Import_LongerThanTimeoutButMakingProgress_Completes()
    {
        Shell.LocalEmulatorInactivityTimeout = TimeSpan.FromSeconds(1);

        var watch = Stopwatch.StartNew();
        var state = await this.ExecuteAsync($"import {ShellQuotedPath(this.inputPath)} --concurrency=1");
        watch.Stop();

        Assert.False(state.IsError, FormatError(state));
        Assert.True(watch.Elapsed > Shell.LocalEmulatorInactivityTimeout, $"Import finished in {watch.Elapsed}; the test did not exceed the timeout.");
        Assert.Equal(300, GetJson(state).GetProperty("imported").GetInt32());
        Assert.IsNotType<DisconnectedState>(Shell.State);
        var count = await this.container.GetItemLinqQueryable<JsonElement>().CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(300, count.Resource);
    }

    [Fact]
    public async Task RmWildcard_LongerThanTimeoutButMakingProgress_Completes()
    {
        var import = await this.ExecuteAsync($"import {ShellQuotedPath(this.inputPath)}");
        Assert.False(import.IsError, FormatError(import));
        Shell.LocalEmulatorInactivityTimeout = TimeSpan.FromSeconds(1);

        var watch = Stopwatch.StartNew();
        var state = await this.ExecuteAsync("rm *");
        watch.Stop();

        Assert.False(state.IsError, FormatError(state));
        Assert.True(watch.Elapsed > Shell.LocalEmulatorInactivityTimeout, $"rm finished in {watch.Elapsed}; the test did not exceed the timeout.");
        Assert.IsNotType<DisconnectedState>(Shell.State);
        var count = await this.container.GetItemLinqQueryable<JsonElement>().CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, count.Resource);
    }

    [Fact]
    public async Task Script_LongerThanTimeout_CountsEveryEmulatorResponseAsProgress()
    {
        Shell.LocalEmulatorInactivityTimeout = TimeSpan.FromSeconds(1);
        var script = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 200).Select(index => $$"""mkitem '{"id":"script-{{index}}","pk":"s{{index % 5}}"}'"""));

        var watch = Stopwatch.StartNew();
        var state = await this.ExecuteAsync(script);
        watch.Stop();

        Assert.False(state.IsError, FormatError(state));
        Assert.True(watch.Elapsed > Shell.LocalEmulatorInactivityTimeout, $"Script finished in {watch.Elapsed}; the test did not exceed the timeout.");
        Assert.IsNotType<DisconnectedState>(Shell.State);
        var count = await this.container.GetItemLinqQueryable<JsonElement>().CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(200, count.Resource);
    }

    [Fact]
    public async Task Command_WithoutProgress_StillTimesOut()
    {
        Shell.LocalEmulatorInactivityTimeout = TimeSpan.FromMilliseconds(1);

        var state = await this.ExecuteAsync($"import {ShellQuotedPath(this.inputPath)} --concurrency=1");

        Assert.True(state.IsError);
        Assert.Contains("timed out", GetErrorMessage(state), StringComparison.OrdinalIgnoreCase);
        Assert.IsType<DisconnectedState>(Shell.State);
    }

    private sealed class PacedRequestHandler : RequestHandler
    {
        public override async Task<ResponseMessage> SendAsync(RequestMessage request, CancellationToken cancellationToken)
        {
            // Make the elapsed-time assertions independent of emulator or CI machine speed.
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
