// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;
using Microsoft.Azure.Cosmos;
using Spectre.Console;

public class CosmosShellPromptTests
{
    // Well-known Cosmos DB emulator key. Used only to construct a CosmosClient
    // offline; no network calls are made by these tests.
    private const string FakeKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    [Fact]
    public void GetPromptString_WhenDisconnected_ShowsOfflineLabelAndMarker()
    {
        var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();

        var prompt = new CosmosShellPrompt(shell).GetPromptString();

        Assert.Contains(CosmosShellPrompt.OfflineAccountName, prompt);
        Assert.Contains(CosmosShellPrompt.PromptMarker, prompt);
    }

    [Fact]
    public void GetPromptString_InContainerState_EscapesMarkupCharactersOnce()
    {
        using var client = new CosmosClient("https://example.documents.azure.com:443/", FakeKey);
        var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("Con[tainer]", "Db[Name]", client);

        var prompt = new CosmosShellPrompt(shell).GetPromptString();

        Assert.Contains(Markup.Escape("Db[Name]"), prompt);
        Assert.Contains(Markup.Escape("Con[tainer]"), prompt);
        Assert.DoesNotContain(Markup.Escape(Markup.Escape("Db[Name]")), prompt);
        Assert.DoesNotContain(Markup.Escape(Markup.Escape("Con[tainer]")), prompt);
    }

    [Fact]
    public void GetPromptString_InDatabaseState_EscapesMarkupCharactersOnce()
    {
        using var client = new CosmosClient("https://example.documents.azure.com:443/", FakeKey);
        var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("Db[Name]", client);

        var prompt = new CosmosShellPrompt(shell).GetPromptString();

        Assert.Contains(Markup.Escape("Db[Name]"), prompt);
        Assert.DoesNotContain(Markup.Escape(Markup.Escape("Db[Name]")), prompt);
    }
}
