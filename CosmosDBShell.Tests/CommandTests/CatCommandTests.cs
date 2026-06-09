// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;

/// <summary>
/// Unit tests for <see cref="CatCommand"/>. The command reads a file from disk and
/// returns its contents as a <see cref="ShellText"/> result, throwing when the file
/// path is missing or does not exist. These paths run without a Cosmos DB connection.
/// </summary>
public class CatCommandTests : IDisposable
{
    private readonly string tempFile;

    public CatCommandTests()
    {
        this.tempFile = Path.Combine(Path.GetTempPath(), $"catcmd_{Guid.NewGuid():N}.txt");
    }

    public void Dispose()
    {
        if (File.Exists(this.tempFile))
        {
            File.Delete(this.tempFile);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ExecuteAsync_ExistingFile_ReturnsContentsAsShellText()
    {
        const string contents = "{ \"hello\": \"world\" }\nsecond line";
        await File.WriteAllTextAsync(this.tempFile, contents, TestContext.Current.CancellationToken);

        using var shell = ShellInterpreter.CreateInstance();
        var command = new CatCommand { FilePath = this.tempFile };

        var state = await command.ExecuteAsync(shell, new CommandState(), "cat", TestContext.Current.CancellationToken);

        var text = Assert.IsType<ShellText>(state.Result);
        Assert.Equal(contents, text.Text);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyFile_ReturnsEmptyShellText()
    {
        await File.WriteAllTextAsync(this.tempFile, string.Empty, TestContext.Current.CancellationToken);

        using var shell = ShellInterpreter.CreateInstance();
        var command = new CatCommand { FilePath = this.tempFile };

        var state = await command.ExecuteAsync(shell, new CommandState(), "cat", TestContext.Current.CancellationToken);

        var text = Assert.IsType<ShellText>(state.Result);
        Assert.Equal(string.Empty, text.Text);
    }

    [Fact]
    public async Task ExecuteAsync_NonExistentFile_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var command = new CatCommand { FilePath = this.tempFile };

        await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "cat", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_NullPath_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var command = new CatCommand { FilePath = null };

        await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "cat", CancellationToken.None));
    }

    [Fact]
    public async Task StateVisitors_ReturnZero()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var command = new CatCommand();

        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(0, await command.VisitConnectedStateAsync(null!, string.Empty, ct));
        Assert.Equal(0, await command.VisitContainerStateAsync(null!, string.Empty, ct));
        Assert.Equal(0, await command.VisitDatabaseStateAsync(null!, string.Empty, ct));
        Assert.Equal(0, await command.VisitDisconnectedStateAsync(null!, string.Empty, ct));
    }
}
