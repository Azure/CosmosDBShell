// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;

using Xunit;
using Xunit.Sdk;

/// <summary>
/// Base class for emulator integration tests that share an <see cref="EmulatorDatabaseFixture"/>.
/// Skips the test when the emulator is unavailable and provides common helpers for executing
/// commands and capturing redirected output.
/// </summary>
[Trait("Category", "Emulator")]
[Collection("Emulator")]
public abstract class EmulatorFixtureTestBase : IClassFixture<EmulatorDatabaseFixture>, IAsyncLifetime
{
    private readonly List<string> tempFiles = [];

    protected EmulatorFixtureTestBase(EmulatorDatabaseFixture fixture)
    {
        Fixture = fixture;
    }

    protected EmulatorDatabaseFixture Fixture { get; }

    protected ShellInterpreter Shell => Fixture.Shell;

    public virtual ValueTask InitializeAsync()
    {
        if (!Fixture.IsAvailable)
        {
            throw SkipException.ForSkip("Cosmos DB emulator not available");
        }

        return ValueTask.CompletedTask;
    }

    public virtual ValueTask DisposeAsync()
    {
        foreach (var file in tempFiles)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        return ValueTask.CompletedTask;
    }

    internal Task<CommandState> ExecuteAsync(string command)
    {
        return Shell.ExecuteCommandAsync(command, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Executes a command with stdout redirected to a temp file and returns the file content.
    /// The shell clears state.Result after printing, so capturing output via file redirect is
    /// the only reliable way to assert on printed JSON payloads.
    /// </summary>
    internal async Task<string> ExecuteWithOutputAsync(string command)
    {
        var outputFile = CreateTempFile();
        Shell.StdOutRedirect = outputFile;
        try
        {
            var state = await ExecuteAsync(command);
            Assert.False(state.IsError, IntegrationTestBase.FormatError(state));

            Assert.True(File.Exists(outputFile), $"Expected output file at {outputFile}");
            return await File.ReadAllTextAsync(outputFile);
        }
        finally
        {
            Shell.StdOutRedirect = null;
        }
    }

    internal string CreateTempFile(string extension = ".json")
    {
        var path = Path.Combine(Path.GetTempPath(), $"inttest-{Guid.NewGuid():N}{extension}");
        tempFiles.Add(path);
        return path;
    }

    internal static string FormatError(CommandState state) => IntegrationTestBase.FormatError(state);
}
