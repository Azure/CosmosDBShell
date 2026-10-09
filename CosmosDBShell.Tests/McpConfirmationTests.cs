// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Mcp;
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
    [InlineData("run", false, 1, "was not approved")]
    [InlineData("run", true, 0, "not connected")]
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
