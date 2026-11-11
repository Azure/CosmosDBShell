// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;
using Spectre.Console;

/// <summary>
/// Unit tests for <see cref="BucketCommand"/>. Covers the pure validation helper and
/// the offline state visitors that do not require a live Cosmos DB connection.
/// </summary>
[Collection(ConsoleOutputTestCollection.Name)]
public class BucketCommandTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("3", false)]
    [InlineData("0", false)]
    [InlineData(null, true)]
    [InlineData("3", true)]
    [InlineData("0", true)]
    public async Task ClientSelection_UsesExecutingShellOutput(string? action, bool machineMode)
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var client = CreateTestClient();
        client.ClientOptions.ThroughputBucket = 2;
        shell.State = new DatabaseState("TestDatabase", client);
        shell.Options = new Program.CosmosShellOptions { Output = machineMode ? "json" : "user", Quiet = machineMode };
        var singletonOptions = ShellInterpreter.Instance.Options;
        var originalOut = Console.Out;
        var originalConsole = AnsiConsole.Console;
        using var output = new StringWriter();
        try
        {
            ShellInterpreter.Instance.Options = new Program.CosmosShellOptions { Output = machineMode ? "user" : "json", Quiet = !machineMode };
            Console.SetOut(output);
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(output),
            });
            var state = await new BucketCommand { Action = action }.ExecuteAsync(
                shell, new CommandState(), "bucket", TestContext.Current.CancellationToken);
            shell.PrintState(state);

            if (machineMode)
            {
                using var result = JsonDocument.Parse(output.ToString());
                var selection = result.RootElement.GetProperty("clientBucket");
                if (action == "0")
                {
                    Assert.Equal(JsonValueKind.Null, selection.ValueKind);
                }
                else
                {
                    Assert.Equal(action == "3" ? 3 : 2, selection.GetInt32());
                }
            }
            else
            {
                Assert.NotEmpty(output.ToString());
                Assert.DoesNotContain("clientBucket", output.ToString());
            }
        }
        finally
        {
            Console.SetOut(originalOut);
            AnsiConsole.Console = originalConsole;
            ShellInterpreter.Instance.Options = singletonOptions;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void CheckBucket_WithinRange_ReturnsTrue(int bucket)
    {
        Assert.True(BucketCommand.CheckBucket(bucket));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(99)]
    public void CheckBucket_OutOfRange_ReturnsFalse(int bucket)
    {
        Assert.False(BucketCommand.CheckBucket(bucket));
    }

    [Fact]
    public async Task ExecuteAsync_Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new BucketCommand { Action = "3" };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "bucket 3", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_Connected_ThrowsNotInDatabase()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ConnectedState(CreateTestClient());
        var command = new BucketCommand { Action = "3" };

        await Assert.ThrowsAsync<NotInDatabaseException>(
            () => command.ExecuteAsync(shell, new CommandState(), "bucket 3", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_InDatabase_SetsBucket()
    {
        var client = CreateTestClient();
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", client);
        var command = new BucketCommand { Action = "3" };

        var state = await command.ExecuteAsync(shell, new CommandState(), "bucket 3", CancellationToken.None);

        Assert.False(state.IsError);
        Assert.Equal(3, client.ClientOptions.ThroughputBucket);
        Assert.Equal(3, Assert.IsType<ShellJson>(state.Result).Value.GetProperty("clientBucket").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_InDatabase_ZeroResetsBucket()
    {
        var client = CreateTestClient();
        client.ClientOptions.ThroughputBucket = 4;
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", client);
        var command = new BucketCommand { Action = "0" };

        var state = await command.ExecuteAsync(shell, new CommandState(), "bucket 0", CancellationToken.None);

        Assert.False(state.IsError);
        Assert.Null(client.ClientOptions.ThroughputBucket);
        Assert.Equal(JsonValueKind.Null, Assert.IsType<ShellJson>(state.Result).Value.GetProperty("clientBucket").ValueKind);
    }

    [Fact]
    public async Task ExecuteAsync_InContainer_NoArgs_ShowsCurrent()
    {
        var client = CreateTestClient();
        client.ClientOptions.ThroughputBucket = 2;
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("TestContainer", "TestDatabase", client);
        var command = new BucketCommand();

        var state = await command.ExecuteAsync(shell, new CommandState(), "bucket", CancellationToken.None);

        Assert.False(state.IsError);
        Assert.Equal(2, Assert.IsType<ShellJson>(state.Result).Value.GetProperty("clientBucket").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_InDatabase_InvalidValue_ThrowsCommandException()
    {
        var client = CreateTestClient();
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", client);
        var command = new BucketCommand { Action = "99" };

        await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "bucket 99", CancellationToken.None));

        Assert.Null(client.ClientOptions.ThroughputBucket);
    }

    [Fact]
    public async Task ExecuteAsync_InDatabase_InvalidSubcommand_ThrowsCommandException()
    {
        var client = CreateTestClient();
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", client);
        var command = new BucketCommand { Action = "bogus" };

        await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "bucket bogus", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_InDatabase_ShowWithoutContainer_ThrowsCommandException()
    {
        var client = CreateTestClient();
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", client);
        var command = new BucketCommand { Action = "show" };

        await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "bucket show", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_Connected_ServerActionWithoutDatabase_ThrowsNotInDatabase()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ConnectedState(CreateTestClient());
        var command = new BucketCommand { Action = "show" };

        await Assert.ThrowsAsync<NotInDatabaseException>(
            () => command.ExecuteAsync(shell, new CommandState(), "bucket show", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_InDatabase_ClientSelectionWithExtraArgs_ThrowsCommandException()
    {
        var client = CreateTestClient();
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", client);
        var command = new BucketCommand { Action = "3", Id = 1 };

        await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "bucket 3", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_InDatabase_ShowWithExtraArgs_ThrowsCommandException()
    {
        var client = CreateTestClient();
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", client);
        var command = new BucketCommand { Action = "show", Id = 3 };

        await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "bucket show 3", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_InDatabase_ClearWithPercent_ThrowsCommandException()
    {
        var client = CreateTestClient();
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDatabase", client);
        var command = new BucketCommand { Action = "clear", Id = 3, Percent = 50 };

        await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "bucket clear 3 50", CancellationToken.None));
    }

    private static CosmosClient CreateTestClient()
    {
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString("https://localhost:8081/");
        return new CosmosClient(connectionString);
    }
}
