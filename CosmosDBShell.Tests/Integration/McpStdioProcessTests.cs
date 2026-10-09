// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using ModelContextProtocol.Protocol;

public class McpStdioProcessTests
{
    [Fact]
    public async Task StdioQuiet_PreservesWarningsAndProtocolResults()
    {
        await using var server = new ServerProcess(["--quiet", "--theme", "STDIO_WARNING_THEME"]);
        await server.InitializeAsync("2025-11-25");
        var echo = await server.CallToolAsync(2, "echo", new { messages = new[] { "QUIET_STDIO_RESULT" } });
        Assert.Equal("QUIET_STDIO_RESULT", echo.GetProperty("result").GetProperty("structuredContent").GetProperty("result").GetString());
        var error = await server.CallToolAsync(3, "help", new { command = "QUIET_MISSING_COMMAND" });
        Assert.True(error.GetProperty("result").GetProperty("isError").GetBoolean());
        await server.CompleteAsync();
        Assert.Equal(0, server.ExitCode);
        Assert.Contains("STDIO_WARNING_THEME", server.StdErr);
        Assert.DoesNotContain("QUIET_STDIO_RESULT", server.StdErr);
        Assert.DoesNotContain("\u001b", server.StdErr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--help", "USAGE:")]
    [InlineData("--version", "CosmosDBShell")]
    public async Task Stdio_ExplicitHelpAndVersion_UseStderr(string argument, string expected)
    {
        await using var server = new ServerProcess([argument]);
        await server.CompleteAsync();
        Assert.Equal(0, server.ExitCode);
        Assert.Equal(0, server.MessageCount);
        Assert.Contains(expected, server.StdErr);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stdio_DoesNotCreateOrModifyHistory(bool seedHistory)
    {
        await using var server = new ServerProcess(seedHistory: seedHistory);
        var historyFile = Path.Join(server.ConfigDirectory, "cmd_history");
        var original = seedHistory ? await File.ReadAllBytesAsync(historyFile, TestContext.Current.CancellationToken) : null;
        await server.InitializeAsync("2025-11-25");
        await server.CallToolAsync(2, "echo", new { messages = new[] { "NO_HISTORY_MARKER" } });
        await server.CompleteAsync();
        Assert.Equal(0, server.ExitCode);
        Assert.False(File.Exists(historyFile + ".lock"));
        if (original is not null)
        {
            Assert.Equal(original, await File.ReadAllBytesAsync(historyFile, TestContext.Current.CancellationToken));
        }
        else
        {
            Assert.False(File.Exists(historyFile));
        }
    }

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task Stdio_ToolsResourcesAndDiagnostics_KeepStdoutProtocolOnly(string version)
    {
        await using var server = new ServerProcess();
        await server.InitializeAsync(version);

        var tools = await server.RequestAsync(2, "tools/list", new { });
        var toolNames = tools.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .ToList();
        Assert.Contains("echo", toolNames);
        Assert.Contains("help", toolNames);
        Assert.DoesNotContain("jq", toolNames);
        Assert.DoesNotContain("theme", toolNames);

        var echo = await server.CallToolAsync(3, "echo", new { messages = new[] { "STDIO_RESULT" } });
        Assert.Equal("STDIO_RESULT", echo.GetProperty("result").GetProperty("structuredContent").GetProperty("result").GetString());

        var helpList = await server.CallToolAsync(4, "help", new { });
        var helpCommands = helpList.GetProperty("result").GetProperty("structuredContent").GetProperty("result")
            .GetProperty("commands").EnumerateArray()
            .Select(command => command.GetProperty("command").GetString())
            .ToList();
        Assert.Contains("echo", helpCommands);
        Assert.DoesNotContain("jq", helpCommands);
        Assert.DoesNotContain("theme", helpCommands);
        foreach (var name in new[] { "exit", "edit", "watch", "welcome", "sproc", "udf", "trigger" })
        {
            Assert.DoesNotContain(name, toolNames);
            Assert.DoesNotContain(name, helpCommands);
        }

        foreach (var name in new[] { "delete", "rm", "rmdb", "rmcon" })
        {
            Assert.Contains(name, toolNames);
            Assert.Contains(name, helpCommands);
        }

        Assert.Equal(toolNames.Order(), helpCommands.Order());

        var help = await server.CallToolAsync(5, "help", new { command = "STDIO_MISSING_COMMAND" });
        Assert.True(help.GetProperty("result").GetProperty("isError").GetBoolean());

        var unknown = await server.CallToolAsync(6, "STDIO_UNKNOWN_TOOL", new { });
        Assert.True(unknown.GetProperty("result").GetProperty("isError").GetBoolean());

        var resource = await server.RequestAsync(7, "resources/read", new { uri = "cosmos://shell/current-location" });
        var content = resource.GetProperty("result").GetProperty("contents")[0].GetProperty("text").GetString()!;
        using var location = JsonDocument.Parse(content);
        Assert.Equal(JsonValueKind.Null, location.RootElement.GetProperty("currentLocation").ValueKind);

        var status = await server.CallToolAsync(8, "version", new { });
        var statusResult = status.GetProperty("result").GetProperty("structuredContent").GetProperty("result");
        Assert.True(statusResult.GetProperty("mcpEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, statusResult.GetProperty("mcpPort").ValueKind);

        await server.CompleteAsync();
        Assert.Equal(0, server.ExitCode);
        Assert.Contains("STDIO_MISSING_COMMAND", server.StdErr);
        Assert.DoesNotContain("STDIO_RESULT", server.StdErr);
    }

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    public async Task Stdio_LocationSubscription_RoundTripsAndEndsOnEof(string version)
    {
        await using var server = new ServerProcess();
        await server.InitializeAsync(version);
        var subscription = await server.RequestAsync(2, "resources/subscribe", new { uri = "cosmos://shell/current-location" });
        Assert.True(subscription.TryGetProperty("result", out _), subscription.ToString());
        var invalid = await server.RequestAsync(3, "resources/subscribe", new { uri = "cosmos://docs/scripting" });
        Assert.Equal(-32602, invalid.GetProperty("error").GetProperty("code").GetInt32());
        var unsubscribe = await server.RequestAsync(4, "resources/unsubscribe", new { uri = "cosmos://shell/current-location" });
        Assert.True(unsubscribe.TryGetProperty("result", out _), unsubscribe.ToString());
        var resubscribe = await server.RequestAsync(5, "resources/subscribe", new { uri = "cosmos://shell/current-location" });
        Assert.True(resubscribe.TryGetProperty("result", out _), resubscribe.ToString());
        await server.CompleteAsync();
        Assert.Equal(0, server.ExitCode);
    }

    [Fact]
    public async Task Stdio_LatestSubscriptionListen_AcknowledgesAndEndsOnEof()
    {
        await using var server = new ServerProcess();
        await server.InitializeAsync("2026-07-28");
        await server.WriteRequestAsync(2, "subscriptions/listen", new
        {
            notifications = new { resourceSubscriptions = new[] { "cosmos://shell/current-location" } },
        });
        var acknowledgement = await server.ReadMessageAsync();
        Assert.Equal(NotificationMethods.SubscriptionsAcknowledgedNotification, acknowledgement.GetProperty("method").GetString());
        Assert.Equal("cosmos://shell/current-location",
            acknowledgement.GetProperty("params").GetProperty("notifications").GetProperty("resourceSubscriptions")[0].GetString());
        await server.CompleteAsync();
        Assert.Equal(0, server.ExitCode);
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("cancel")]
    [InlineData("accept")]
    public async Task Stdio_DestructiveConfirmation_UsesProtocolEvenWithForce(string action)
    {
        await using var server = new ServerProcess();
        await server.InitializeAsync("2025-11-25", new { elicitation = new { form = new { } } });
        await server.WriteAsync(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new { name = "rmdb", arguments = new { name = "StdioConfirmationDb", force = true } },
        });
        var prompt = await server.ReadMessageAsync();
        Assert.Equal("elicitation/create", prompt.GetProperty("method").GetString());
        Assert.Contains("StdioConfirmationDb", prompt.GetProperty("params").GetProperty("message").GetString());
        await server.WriteAsync(new
        {
            jsonrpc = "2.0",
            id = prompt.GetProperty("id"),
            result = new { action, content = new { } },
        });
        var response = await server.ReadResponseAsync(2);
        Assert.True(response.GetProperty("result").GetProperty("isError").GetBoolean());
        var error = response.GetProperty("result").GetProperty("structuredContent").GetProperty("error").GetString();
        if (action == "accept")
        {
            Assert.DoesNotContain("was not approved", error);
            Assert.DoesNotContain("does not support", error);
        }
        else
        {
            Assert.Contains("was not approved", error);
        }

        await server.CompleteAsync();
        Assert.Equal(0, server.ExitCode);
    }

    [Fact]
    public async Task Stdio_ClientWithoutElicitation_RefusesDestructiveTool()
    {
        await using var server = new ServerProcess();
        await server.InitializeAsync("2025-11-25");
        var response = await server.CallToolAsync(2, "rmdb", new { name = "StdioConfirmationDb", force = true });
        Assert.True(response.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Contains("does not support confirmation", response.GetProperty("result").GetProperty("structuredContent").GetProperty("error").GetString());
        await server.CompleteAsync();
        Assert.Equal(0, server.ExitCode);
    }

    [Fact]
    public async Task Stdio_EofWhileAwaitingConfirmation_ExitsCleanly()
    {
        await using var server = new ServerProcess();
        await server.InitializeAsync("2025-11-25", new { elicitation = new { form = new { } } });
        await server.WriteRequestAsync(2, "tools/call", new
        {
            name = "rmdb",
            arguments = new { name = "StdioConfirmationDb" },
        });
        var prompt = await server.ReadMessageAsync();
        Assert.Equal("elicitation/create", prompt.GetProperty("method").GetString());
        await server.CompleteAsync();
        Assert.Equal(0, server.ExitCode);
    }

    [Fact]
    public async Task Stdio_CancelPendingConfirmation_RemainsResponsive()
    {
        await using var server = new ServerProcess();
        await server.InitializeAsync("2025-11-25", new { elicitation = new { form = new { } } });
        await server.WriteRequestAsync(2, "tools/call", new
        {
            name = "rmdb",
            arguments = new { name = "CancelledStdioDb" },
        });
        var prompt = await server.ReadMessageAsync();
        Assert.Equal("elicitation/create", prompt.GetProperty("method").GetString());
        await server.WriteAsync(new
        {
            jsonrpc = "2.0",
            method = "notifications/cancelled",
            @params = new { requestId = 2, reason = "stdio cancellation test" },
        });
        await server.WriteRequestAsync(3, "tools/call", new
        {
            name = "echo",
            arguments = new { messages = new[] { "AFTER_STDIO_CANCELLATION" } },
        });
        var response = await server.ReadResponseAsync(3, cancelledIds: [2]);
        Assert.Equal("AFTER_STDIO_CANCELLATION",
            response.GetProperty("result").GetProperty("structuredContent").GetProperty("result").GetString());
        await server.CompleteAsync();
        Assert.Equal(0, server.ExitCode);
        Assert.DoesNotContain("An exception occurred", server.StdErr);
    }

    [Fact]
    public async Task Stdio_EofDuringStartupConnection_ExitsWithoutWaitingForNetwork()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        await using var server = new ServerProcess([
            "--connect",
            $"AccountEndpoint=https://127.0.0.1:{endpoint.Port}/;AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=;",
        ]);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
        await server.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(0, server.ExitCode);
        Assert.Equal(0, server.MessageCount);
        Assert.DoesNotContain("timed out", server.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stdio_StartupFailure_ExitsWithStdinStillOpen()
    {
        await using var server = new ServerProcess(["--connect", "not-an-endpoint"]);
        await server.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(2, server.ExitCode);
        Assert.Equal(0, server.MessageCount);
        Assert.NotEmpty(server.StdErr);
    }

    [Fact]
    public async Task Stdio_CancelRunningAndQueuedTools_ReleasesShell()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        await using var server = new ServerProcess();
        await server.InitializeAsync("2025-11-25");
        await server.WriteRequestAsync(2, "tools/call", new
        {
            name = "connect",
            arguments = new
            {
                connectionString = $"AccountEndpoint=https://127.0.0.1:{endpoint.Port}/;AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=;",
            },
        });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
        await server.WriteRequestAsync(3, "tools/call", new
        {
            name = "echo",
            arguments = new { messages = new[] { "CANCELLED_QUEUED_RESULT" } },
        });
        foreach (var requestId in new[] { 3, 2 })
        {
            await server.WriteAsync(new
            {
                jsonrpc = "2.0",
                method = "notifications/cancelled",
                @params = new { requestId, reason = "stdio cancellation test" },
            });
        }

        await server.WriteRequestAsync(4, "tools/call", new
        {
            name = "echo",
            arguments = new { messages = new[] { "AFTER_QUEUED_CANCELLATION" } },
        });
        var response = await server.ReadResponseAsync(4, cancelledIds: [2, 3]);
        Assert.Equal("AFTER_QUEUED_CANCELLATION",
            response.GetProperty("result").GetProperty("structuredContent").GetProperty("result").GetString());
        await server.CompleteAsync();
        Assert.Equal(0, server.ExitCode);
        Assert.DoesNotContain("An exception occurred", server.StdErr);
    }

    [Theory]
    [InlineData("theme", "show")]
    [InlineData("theme", "edit")]
    [InlineData("jq", null)]
    [InlineData("exit", null)]
    [InlineData("edit", null)]
    [InlineData("watch", null)]
    [InlineData("welcome", null)]
    [InlineData("sproc", null)]
    [InlineData("udf", null)]
    [InlineData("trigger", null)]
    public async Task Stdio_RemovedCommands_AreUnknownToolsAndHelp(string tool, string? action)
    {
        await using var server = new ServerProcess();
        await server.InitializeAsync("2025-11-25");
        var arguments = action is null ? new Dictionary<string, object>() : new Dictionary<string, object> { ["action"] = action };
        var response = await server.CallToolAsync(2, tool, arguments);
        Assert.True(response.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Contains("Could not find command", response.GetProperty("result").GetProperty("structuredContent").GetProperty("error").GetString());
        var help = await server.CallToolAsync(3, "help", new { command = tool });
        Assert.True(help.GetProperty("result").GetProperty("isError").GetBoolean());
        await server.CompleteAsync();
        Assert.Equal(0, server.ExitCode);
    }

    [Theory]
    [InlineData("--mcp")]
    [InlineData("--lsp")]
    [InlineData("--stdio")]
    [InlineData("--clear-history")]
    [InlineData("-c")]
    [InlineData("-k")]
    public async Task Stdio_ConflictingModes_FailOnStderrWithEmptyStdout(string option)
    {
        await using var server = new ServerProcess(option is "-c" or "-k" ? [option, "echo SHOULD_NOT_RUN"] : [option]);
        await server.CompleteAsync();
        Assert.Equal(2, server.ExitCode);
        Assert.Equal(0, server.MessageCount);
        Assert.Contains("cannot be combined", server.StdErr);
        Assert.DoesNotContain("SHOULD_NOT_RUN", server.StdErr);
    }

    [Theory]
    [InlineData("--help", null, 0)]
    [InlineData("--version", null, 0)]
    [InlineData("--unknown-option", null, 2)]
    [InlineData("--container", "orphan", 2)]
    [InlineData("--connect", "not-an-endpoint", 2)]
    [InlineData("--mcp-stdio=false", null, 2)]
    [InlineData("--lsp=true", null, 2)]
    [InlineData("--stdio=true", null, 2)]
    public async Task Stdio_StartupOutput_IsStderrOnly(string option, string? value, int expectedExitCode)
    {
        await using var server = new ServerProcess(value is null ? [option] : [option, value]);
        await server.CompleteAsync();
        Assert.Equal(expectedExitCode, server.ExitCode);
        Assert.Equal(0, server.MessageCount);
        Assert.NotEmpty(server.StdErr);
    }

    [Fact]
    public async Task Stdio_EofWithoutInitialization_ExitsCleanly()
    {
        await using var server = new ServerProcess();
        await server.CompleteAsync();
        Assert.Equal(0, server.ExitCode);
        Assert.Equal(0, server.MessageCount);
    }

    private sealed class ServerProcess : IAsyncDisposable
    {
        private readonly string configDirectory = Path.Join(Path.GetTempPath(), $"cosmosshell-stdio-{Guid.NewGuid():N}");
        private readonly CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        private readonly Channel<JsonElement> messages = Channel.CreateUnbounded<JsonElement>();
        private readonly Process process;
        private readonly Task outputTask;
        private readonly Task<string> errorTask;

        private JsonObject? requestMeta;

        public ServerProcess(string[]? extraArguments = null, bool seedHistory = false)
        {
            if (seedHistory)
            {
                Directory.CreateDirectory(this.configDirectory);
                File.WriteAllText(Path.Join(this.configDirectory, "cmd_history"), "echo EXISTING_HISTORY\n");
            }

            this.timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var shellDll = Path.Join(AppContext.BaseDirectory, "CosmosDBShell.dll");
            Assert.True(File.Exists(shellDll), $"Missing shell executable: {shellDll}");
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add(shellDll);
            startInfo.ArgumentList.Add("--mcp-stdio");
            foreach (var argument in extraArguments ?? [])
            {
                startInfo.ArgumentList.Add(argument);
            }

            startInfo.Environment["COSMOSDB_SHELL_CONFIG_DIR"] = this.configDirectory;
            startInfo.Environment.Remove("COSMOSDB_SHELL_ACCOUNT_KEY");
            startInfo.Environment.Remove("COSMOSDB_SHELL_TOKEN");
            startInfo.Environment.Remove("COSMOSDB_SHELL_FORMAT");
            this.process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start stdio server.");
            this.outputTask = this.CaptureOutputAsync();
            this.errorTask = this.process.StandardError.ReadToEndAsync();
        }

        public List<JsonElement> Notifications { get; } = [];

        public string ConfigDirectory => this.configDirectory;

        public int MessageCount { get; private set; }

        public int ExitCode => this.process.ExitCode;

        public string StdErr { get; private set; } = string.Empty;

        public async Task InitializeAsync(string version, object? capabilities = null)
        {
            if (version == "2026-07-28")
            {
                this.requestMeta = new JsonObject
                {
                    ["io.modelcontextprotocol/protocolVersion"] = version,
                    ["io.modelcontextprotocol/clientCapabilities"] = JsonSerializer.SerializeToNode(capabilities ?? new { }),
                    ["io.modelcontextprotocol/clientInfo"] = new JsonObject { ["name"] = "stdio-process-test", ["version"] = "1.0" },
                };
                var discover = await this.RequestAsync(1, "server/discover", new { });
                Assert.Contains(discover.GetProperty("result").GetProperty("supportedVersions").EnumerateArray(),
                    supported => supported.GetString() == version);
                return;
            }

            var response = await this.RequestAsync(1, "initialize", new
            {
                protocolVersion = version,
                capabilities = capabilities ?? new { },
                clientInfo = new { name = "stdio-process-test", version = "1.0" },
            });
            Assert.Equal(version, response.GetProperty("result").GetProperty("protocolVersion").GetString());
            await this.WriteAsync(new { jsonrpc = "2.0", method = "notifications/initialized" });
        }

        public Task<JsonElement> CallToolAsync(int id, string name, object arguments)
            => this.RequestAsync(id, "tools/call", new { name, arguments });

        public async Task<JsonElement> RequestAsync(int id, string method, object parameters)
        {
            await this.WriteRequestAsync(id, method, parameters);
            return await this.ReadResponseAsync(id);
        }

        public async Task WriteRequestAsync(int id, string method, object parameters)
        {
            var requestParameters = JsonSerializer.SerializeToNode(parameters)!.AsObject();
            if (this.requestMeta is not null)
            {
                requestParameters["_meta"] = this.requestMeta.DeepClone();
            }

            await this.WriteAsync(new { jsonrpc = "2.0", id, method, @params = requestParameters });
        }

        public async Task WriteAsync(object message)
        {
            await this.process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), this.timeout.Token);
            await this.process.StandardInput.FlushAsync(this.timeout.Token);
        }

        public async Task<JsonElement> ReadMessageAsync()
        {
            var message = await this.messages.Reader.ReadAsync(this.timeout.Token);
            if (message.TryGetProperty("method", out _) && !message.TryGetProperty("id", out _))
            {
                this.Notifications.Add(message);
            }

            return message;
        }

        public async Task<JsonElement> ReadResponseAsync(int id, int[]? cancelledIds = null)
        {
            while (true)
            {
                var message = await this.ReadMessageAsync();
                if (message.TryGetProperty("id", out var responseId))
                {
                    Assert.False(message.TryGetProperty("method", out _), $"Unexpected server request: {message}");
                    if (cancelledIds?.Contains(responseId.GetInt32()) == true)
                    {
                        Assert.True(message.TryGetProperty("error", out _)
                            || message.GetProperty("result").GetProperty("isError").GetBoolean(),
                            $"Cancelled request returned success: {message}");
                        continue;
                    }

                    Assert.Equal(id, responseId.GetInt32());
                    return message;
                }
            }
        }

#pragma warning disable VSTHRD003 // Both stream-draining tasks are started by this helper and complete when the child exits.
        public async Task CompleteAsync()
        {
            this.process.StandardInput.Close();
            await this.WaitForExitAsync();
        }

        public async Task WaitForExitAsync()
        {
            await this.process.WaitForExitAsync(this.timeout.Token);
            await this.outputTask;
            this.StdErr = await this.errorTask;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!this.process.HasExited)
                {
                    this.process.Kill(entireProcessTree: true);
                    await this.process.WaitForExitAsync(TestContext.Current.CancellationToken);
                }

                await this.outputTask;
                await this.errorTask;
            }
            finally
            {
                this.process.Dispose();
                this.timeout.Dispose();
                if (Directory.Exists(this.configDirectory))
                {
                    Directory.Delete(this.configDirectory, recursive: true);
                }
            }
#pragma warning restore VSTHRD003
        }

        private async Task CaptureOutputAsync()
        {
            Exception? failure = null;
            try
            {
                while (await this.process.StandardOutput.ReadLineAsync() is { } line)
                {
                    using var document = JsonDocument.Parse(line);
                    var message = document.RootElement.Clone();
                    Assert.Equal("2.0", message.GetProperty("jsonrpc").GetString());
                    Assert.True(message.TryGetProperty("method", out _) || message.TryGetProperty("result", out _) || message.TryGetProperty("error", out _),
                        $"Unexpected non-protocol output: {line}");
                    this.MessageCount++;
                    await this.messages.Writer.WriteAsync(message, this.timeout.Token);
                }
            }
            catch (Exception ex)
            {
                failure = ex;
                throw;
            }
            finally
            {
                this.messages.Writer.TryComplete(failure);
            }
        }
    }
}
