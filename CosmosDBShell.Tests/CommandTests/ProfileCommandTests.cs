// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;

public class ProfileCommandTests
{
    [Theory]
    [InlineData("command-profile-description")]
    [InlineData("command-profile-description-action")]
    [InlineData("command-profile-description-name")]
    [InlineData("command-profile-save-missing-name")]
    [InlineData("command-profile-use-missing-name")]
    [InlineData("command-profile-delete-missing-name")]
    [InlineData("command-profile-invalid-name")]
    [InlineData("command-profile-save-not-connected")]
    [InlineData("command-profile-saved")]
    [InlineData("command-profile-unknown")]
    [InlineData("command-profile-delete-not-found")]
    [InlineData("command-profile-deleted")]
    [InlineData("command-profile-unknown-action")]
    [InlineData("command-profile-list-col-name")]
    [InlineData("command-profile-list-col-endpoint")]
    [InlineData("command-profile-list-col-mode")]
    [InlineData("command-profile-list-mode-default")]
    public void LocalizationKeys_AreDefined(string key)
    {
        Assert.False(string.IsNullOrWhiteSpace(MessageService.GetString(key)));
    }

    [Fact]
    public async Task ExecuteAsync_NoAction_DefaultsToList()
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var userProfile = new TemporaryUserProfileScope();
        var command = new ProfileCommand();

        var result = await command.ExecuteAsync(shell, new CommandState(), "profile", CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(result.IsPrinted);
    }

    [Fact]
    public async Task ExecuteAsync_Save_InvalidName_ReturnsValidationError()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var command = new ProfileCommand { Action = "save", Name = "bad name" };

        var result = await command.ExecuteAsync(shell, new CommandState(), "profile save \"bad name\"", CancellationToken.None);

        var error = Assert.IsType<ErrorCommandState>(result);
        Assert.Contains("Invalid profile name", error.Exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_Save_NotConnected_ReturnsError()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var command = new ProfileCommand { Action = "save", Name = "dev" };

        var result = await command.ExecuteAsync(shell, new CommandState(), "profile save dev", CancellationToken.None);

        var error = Assert.IsType<ErrorCommandState>(result);
        Assert.Contains("Not connected", error.Exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_Use_InvalidName_ReturnsValidationError()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var command = new ProfileCommand { Action = "use", Name = "bad name" };

        var result = await command.ExecuteAsync(shell, new CommandState(), "profile use \"bad name\"", CancellationToken.None);

        var error = Assert.IsType<ErrorCommandState>(result);
        Assert.Contains("Invalid profile name", error.Exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_Delete_InvalidName_ReturnsValidationError()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var command = new ProfileCommand { Action = "delete", Name = "bad name" };

        var result = await command.ExecuteAsync(shell, new CommandState(), "profile delete \"bad name\"", CancellationToken.None);

        var error = Assert.IsType<ErrorCommandState>(result);
        Assert.Contains("Invalid profile name", error.Exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_Delete_MissingProfile_ReturnsError()
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var userProfile = new TemporaryUserProfileScope();
        var command = new ProfileCommand { Action = "delete", Name = "missing-profile" };

        var result = await command.ExecuteAsync(shell, new CommandState(), "profile delete missing-profile", CancellationToken.None);

        var error = Assert.IsType<ErrorCommandState>(result);
        Assert.Contains("was not found", error.Exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TemporaryUserProfileScope : IDisposable
    {
        private readonly string? originalUserProfile;
        private readonly string? originalHome;
        private readonly string temporaryPath;

        public TemporaryUserProfileScope()
        {
            this.originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            this.originalHome = Environment.GetEnvironmentVariable("HOME");
            this.temporaryPath = Path.Combine(Path.GetTempPath(), $"cosmosdbshell-profile-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(this.temporaryPath);
            Environment.SetEnvironmentVariable("USERPROFILE", this.temporaryPath);
            Environment.SetEnvironmentVariable("HOME", this.temporaryPath);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("USERPROFILE", this.originalUserProfile);
            Environment.SetEnvironmentVariable("HOME", this.originalHome);
            if (Directory.Exists(this.temporaryPath))
            {
                Directory.Delete(this.temporaryPath, recursive: true);
            }
        }
    }
}
