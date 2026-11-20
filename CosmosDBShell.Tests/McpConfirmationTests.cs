// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

[Collection(CosmosShell.Tests.Shell.ThemeStateTestCollection.Name)]
public class McpConfirmationTests
{
    [Theory]
    [InlineData("dir", "accept", null)]
    [InlineData("cat", "accept", null)]
    [InlineData("dir", "decline", null)]
    [InlineData("cat", "decline", null)]
    [InlineData("dir", "accept", "2025-11-25")]
    [InlineData("cat", "accept", "2025-11-25")]
    [InlineData("dir", "decline", "2025-11-25")]
    [InlineData("cat", "decline", "2025-11-25")]
    public async Task LocalFilesystemRead_OverHttp_RequiresApproval(string commandName, string action, string? requestedVersion)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var host = McpServer.CreateHost(new Program.CosmosShellOptions { McpPort = 0 });
        await host.StartAsync(timeout.Token);
        var directory = Path.Join(Path.GetTempPath(), $"mcp-local-read-{Guid.NewGuid():N}");
        var file = Path.Join(directory, "sample.txt");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(file, "approved local content", timeout.Token);
            var prompts = new List<string?>();
            var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(address.TrimEnd('/') + "/") });
            await using var client = await McpClient.CreateAsync(
                transport,
                new McpClientOptions
                {
                    ProtocolVersion = requestedVersion,
                    Handlers = new McpClientHandlers
                    {
                        ElicitationHandler = (request, _) =>
                        {
                            prompts.Add(request?.Message);
                            return ValueTask.FromResult(new ElicitResult { Action = action });
                        },
                    },
                },
                cancellationToken: timeout.Token);
            var arguments = commandName == "dir"
                ? new Dictionary<string, object?> { ["directory"] = directory, ["filter"] = "*.txt" }
                : new Dictionary<string, object?> { ["path"] = file };
            var result = await client.CallToolAsync(commandName, arguments, cancellationToken: timeout.Token);

            var prompt = Assert.Single(prompts);
            Assert.Contains("Confirm local filesystem access", prompt);
            Assert.Contains(ShellLiteral.Quote(commandName == "dir" ? directory : file), prompt);
            Assert.Contains(Directory.GetCurrentDirectory(), prompt);
            Assert.DoesNotContain("destructive", prompt);
            Assert.Equal(action != "accept", result.IsError == true);
            using var document = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
            if (action == "accept")
            {
                if (commandName == "cat")
                {
                    Assert.Equal("approved local content", document.RootElement.GetProperty("result").GetString());
                }
                else
                {
                    var entry = Assert.Single(document.RootElement.GetProperty("result").GetProperty("values").EnumerateArray());
                    Assert.Equal("sample.txt", entry.GetProperty("name").GetString());
                }
            }
            else
            {
                Assert.Contains("was not approved", document.RootElement.GetProperty("error").GetString());
                Assert.False(document.RootElement.TryGetProperty("result", out _));
            }
        }
        finally
        {
            await host.StopAsync(TestContext.Current.CancellationToken);
            File.Delete(file);
            Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData("run", false, 1, "was not approved")]
    [InlineData("run", true, 0, "not connected")]
    [InlineData("delete", true, 0, "not connected")]
    [InlineData("begin", false, 0, "MCP supports only the stateless 'bulk run'")]
    [InlineData("execute", false, 0, "MCP supports only the stateless 'bulk run'")]
    public async Task Bulk_McpRequiresConfirmationExceptDryRunAndRejectsStatefulSubcommands(string subcommand, bool dryRun, int expectedPrompts, string expectedText)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var host = McpServer.CreateHost(new Program.CosmosShellOptions { McpPort = 0 });
        await host.StartAsync(timeout.Token);
        try
        {
            var prompts = 0;
            var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(address.TrimEnd('/') + "/") });
            await using var client = await McpClient.CreateAsync(
                transport,
                new McpClientOptions
                {
                    Handlers = new McpClientHandlers
                    {
                        ElicitationHandler = (_, _) =>
                        {
                            prompts++;
                            return ValueTask.FromResult(new ElicitResult { Action = "decline" });
                        },
                    },
                },
                cancellationToken: timeout.Token);
            var arguments = new Dictionary<string, object?> { ["subcommand"] = subcommand, ["yes"] = true };
            if (subcommand == "run")
            {
                arguments["data"] = "[{\"op\":\"delete\",\"id\":\"1\",\"partitionKey\":\"a\"}]";
            }

            if (subcommand == "delete")
            {
                arguments["where"] = "true";
            }

            if (dryRun)
            {
                arguments["dry-run"] = true;
            }

            var result = await client.CallToolAsync("bulk", arguments, cancellationToken: timeout.Token);
            Assert.Equal(expectedPrompts, prompts);
            Assert.True(result.IsError);
            var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            using var document = JsonDocument.Parse(text);
            Assert.Contains(expectedText, document.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await host.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("patch")]
    public async Task Bulk_DryRunSaveRequiresConfirmationAndDeclineCreatesNoFile(string subcommand)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var host = McpServer.CreateHost(new Program.CosmosShellOptions { McpPort = 0 });
        await host.StartAsync(timeout.Token);
        var path = Path.Join(Path.GetTempPath(), $"bulk-confirm-{Guid.NewGuid():N}.jsonl");
        try
        {
            var prompts = 0;
            var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(address.TrimEnd('/') + "/") });
            await using var client = await McpClient.CreateAsync(
                transport,
                new McpClientOptions
                {
                    Handlers = new McpClientHandlers
                    {
                        ElicitationHandler = (_, _) =>
                        {
                            prompts++;
                            return ValueTask.FromResult(new ElicitResult { Action = "decline" });
                        },
                    },
                },
                cancellationToken: timeout.Token);
            var arguments = new Dictionary<string, object?>
            {
                ["subcommand"] = subcommand,
                ["where"] = "true",
                ["dry-run"] = true,
                ["save"] = path,
                ["yes"] = true,
            };
            if (subcommand == "patch")
            {
                arguments["operations"] = "[{\"op\":\"remove\",\"path\":\"/old\"}]";
            }

            var result = await client.CallToolAsync("bulk", arguments, cancellationToken: timeout.Token);
            Assert.Equal(1, prompts);
            Assert.True(result.IsError);
            Assert.False(File.Exists(path));
            using var document = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
            Assert.Contains("was not approved", document.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            await host.StopAsync(TestContext.Current.CancellationToken);
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(null, "2026-07-28")] // Stateless request; confirmation uses native multi-round-trip requests.
    [InlineData("2025-11-25", "2025-11-25")] // Initialize handshake; confirmation is sent over the session.
    public async Task DestructiveCommand_OverHttp_AsksClientAndHonorsDecline(string? requestedVersion, string negotiatedVersion)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var host = McpServer.CreateHost(new Program.CosmosShellOptions { McpPort = 0 });
        await host.StartAsync(timeout.Token);
        try
        {
            var prompts = new List<string?>();
            var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(address.TrimEnd('/') + "/"),
            });
            await using var client = await McpClient.CreateAsync(
                transport,
                new McpClientOptions
                {
                    ProtocolVersion = requestedVersion,
                    Handlers = new McpClientHandlers
                    {
                        ElicitationHandler = (request, _) =>
                        {
                            prompts.Add(request?.Message);
                            return ValueTask.FromResult(new ElicitResult { Action = "decline" });
                        },
                    },
                },
                cancellationToken: timeout.Token);
            Assert.Equal(negotiatedVersion, client.NegotiatedProtocolVersion);

            var result = await client.CallToolAsync(
                "rmdb",
                new Dictionary<string, object?> { ["name"] = "McpConfirmationTestDb" },
                cancellationToken: timeout.Token);

            var prompt = Assert.Single(prompts);
            Assert.Contains("rmdb \"McpConfirmationTestDb\"", prompt);
            Assert.True(result.IsError);
            using var document = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
            Assert.Contains("was not approved by the user", document.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            await host.StopAsync(TestContext.Current.CancellationToken);
        }
    }
}
