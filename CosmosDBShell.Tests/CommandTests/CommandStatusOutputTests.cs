// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Net;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;
using NSubstitute;
using Spectre.Console;

[Collection(ConsoleOutputTestCollection.Name)]
public class CommandStatusOutputTests
{
    public static IEnumerable<object?[]> StatusCases()
    {
        string[] commands =
        [
            "mkitem", "mkitem-array", "upsert", "upsert-array", "replace", "replace-array",
            "patch", "import", "export", "ttl", "ttl-show", "ttl-analytical", "ttl-analytical-show",
            "conflict", "conflict-show", "index",
        ];
        string?[] formats = [null, "user", "json", "csv", "table"];
        foreach (var command in commands)
        {
            foreach (var format in formats)
            {
                yield return [command, format];
            }
        }
    }

    [Theory]
    [MemberData(nameof(StatusCases))]
    public async Task Status_UsesReadableTextUnlessStructuredOutputRequested(string name, string? format)
    {
        var file = Path.GetTempFileName();
        var originalOut = Console.Out;
        var originalConsole = AnsiConsole.Console;
        using var output = new StringWriter();
        using var shell = CreateShell();
        shell.Options = new Program.CosmosShellOptions { Output = format };
        try
        {
            await File.WriteAllTextAsync(file, "[{\"id\":\"a\",\"pk\":\"x\"}]", TestContext.Current.CancellationToken);
            Console.SetOut(output);
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(output),
            });

            var command = CreateCommand(name, file);
            var state = await command.ExecuteAsync(shell, new CommandState(), string.Empty, TestContext.Current.CancellationToken);

            Assert.Empty(output.ToString());
            var result = Assert.IsType<ShellJson>(state.Result).Value;
            shell.PrintState(state);
            var text = output.ToString();

            Assert.NotEmpty(text);
            if (format is null or "user")
            {
                Assert.DoesNotContain("{", text);
                Assert.DoesNotContain("\"type\"", text);
            }
            else if (format == "json")
            {
                using var document = JsonDocument.Parse(text);
                Assert.True(JsonElement.DeepEquals(result, document.RootElement));
            }
            else
            {
                Assert.DoesNotContain("{", text);
                Assert.DoesNotContain("RU charge:", text);
            }

            if (name == "export")
            {
                using var exported = JsonDocument.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
                Assert.Equal(JsonValueKind.Array, exported.RootElement.ValueKind);
                Assert.Equal("a", exported.RootElement[0].GetProperty("id").GetString());
            }
        }
        finally
        {
            Console.SetOut(originalOut);
            AnsiConsole.Console = originalConsole;
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("json", false)]
    [InlineData("csv", false)]
    [InlineData("table", false)]
    [InlineData("user", false)]
    [InlineData("json", true)]
    [InlineData("csv", true)]
    public async Task ImportCommandLine_FormatControlsSummaryAndInputIsDetected(string format, bool legacy)
    {
        var file = Path.GetTempFileName();
        using var shell = CreateShell();
        var redirect = Path.GetTempFileName();
        shell.StdOutRedirect = redirect;
        try
        {
            var content = legacy ? "{\"id\":\"a\"}\n{\"id\":\"b\"}\n" : "[{\"id\":\"a\"},{\"id\":\"b\"}]";
            await File.WriteAllTextAsync(file, content, TestContext.Current.CancellationToken);

            var state = await shell.ExecuteCommandAsync(
                $"import {JsonSerializer.Serialize(file)} --dry-run --format={format}",
                TestContext.Current.CancellationToken);

            Assert.False(state.IsError);
            var result = Assert.IsType<ShellJson>(state.Result).Value;
            Assert.Equal(2, result.GetProperty("imported").GetInt32());
            Assert.True(OutputFormats.TryParse(format, out var expectedFormat));
            Assert.Equal(expectedFormat, state.OutputFormat);
            Assert.NotEmpty(await File.ReadAllTextAsync(redirect, TestContext.Current.CancellationToken));
        }
        finally
        {
            shell.StdOutRedirect = null;
            File.Delete(file);
            File.Delete(redirect);
        }
    }

    [Theory]
    [InlineData("json", ".json")]
    [InlineData("csv", ".json")]
    [InlineData("table", ".json")]
    [InlineData("json", ".csv")]
    [InlineData("csv", ".csv")]
    public async Task ExportCommandLine_ExtensionSelectsFileAndFormatSelectsResult(string format, string extension)
    {
        var file = Path.Join(Path.GetTempPath(), $"cosmos-export-{Guid.NewGuid():N}{extension}");
        var redirect = Path.GetTempFileName();
        using var shell = CreateShell();
        shell.StdOutRedirect = redirect;
        try
        {
            var state = await shell.ExecuteCommandAsync(
                $"export {JsonSerializer.Serialize(file)} --format={format}", TestContext.Current.CancellationToken);

            Assert.False(state.IsError);
            Assert.True(OutputFormats.TryParse(format, out var expectedFormat));
            Assert.Equal(expectedFormat, state.OutputFormat);
            var exported = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
            if (extension == ".csv")
            {
                Assert.Equal("\"id\"\n\"a\"\n", exported);
            }
            else
            {
                using var document = JsonDocument.Parse(exported);
                Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
                Assert.Equal("a", document.RootElement[0].GetProperty("id").GetString());
            }

            var summary = await File.ReadAllTextAsync(redirect, TestContext.Current.CancellationToken);
            Assert.Contains("exported", summary);
            Assert.DoesNotContain("RU charge:", summary);
        }
        finally
        {
            shell.StdOutRedirect = null;
            File.Delete(file);
            File.Delete(redirect);
        }
    }

    [Theory]
    [InlineData("user", "json")]
    [InlineData("json", "csv")]
    [InlineData("csv", "table")]
    public async Task Import_LocalFormatOverridesShellDefault(string localFormat, string globalFormat)
    {
        var file = Path.GetTempFileName();
        using var shell = CreateShell();
        shell.Options = new Program.CosmosShellOptions { Output = globalFormat };
        try
        {
            await File.WriteAllTextAsync(file, "{\"id\":\"a\"}", TestContext.Current.CancellationToken);
            var command = new ImportCommand { File = file, DryRun = true, Format = localFormat };

            var state = await command.ExecuteAsync(shell, new CommandState(), string.Empty, TestContext.Current.CancellationToken);

            Assert.True(state.OutputFormatExplicitlySet);
            Assert.True(OutputFormats.TryParse(localFormat, out var expectedFormat));
            Assert.Equal(expectedFormat, state.OutputFormat);
            Assert.Equal(1, Assert.IsType<ShellJson>(state.Result).Value.GetProperty("imported").GetInt32());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Import_JsonFormatAutomaticallyReadsLegacyInput()
    {
        var file = Path.GetTempFileName();
        using var shell = CreateShell();
        try
        {
            await File.WriteAllTextAsync(file, "{\"id\":\"a\"}\n{\"id\":\"b\"}", TestContext.Current.CancellationToken);
            var command = new ImportCommand { File = file, DryRun = true, Format = "json" };

            var state = await command.ExecuteAsync(shell, new CommandState(), string.Empty, TestContext.Current.CancellationToken);

            Assert.Equal(2, Assert.IsType<ShellJson>(state.Result).Value.GetProperty("imported").GetInt32());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData(null, false, "command-ttl-status-disabled")]
    [InlineData(-1, false, "command-ttl-status-no-default")]
    [InlineData(600, false, "command-ttl-status-enabled")]
    [InlineData(0, true, "command-ttl-analytical-status-disabled")]
    [InlineData(-1, true, "command-ttl-analytical-status-indefinite")]
    [InlineData(600, true, "command-ttl-analytical-status-enabled")]
    public async Task TtlShow_RendersReadableConfiguration(int? seconds, bool analytical, string messageKey)
    {
        var originalOut = Console.Out;
        using var output = new StringWriter();
        using var shell = CreateShell(seconds);
        try
        {
            Console.SetOut(output);
            var command = new TtlCommand { Subcommand = "show", Analytical = analytical };
            var state = await command.ExecuteAsync(shell, new CommandState(), string.Empty, TestContext.Current.CancellationToken);

            shell.PrintState(state);

            var expected = MessageService.GetArgsString(messageKey, "container", "items", "seconds", seconds ?? 0);
            Assert.Equal(expected + Environment.NewLine, output.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private static CosmosCommand CreateCommand(string name, string file)
    {
        const string item = "{\"id\":\"a\",\"pk\":\"x\"}";
        return name switch
        {
            "mkitem" => new MakeItemCommand { Data = item },
            "mkitem-array" => new MakeItemCommand { Data = "[" + item + "]" },
            "upsert" => new MakeItemCommand { Data = item, Force = true },
            "upsert-array" => new MakeItemCommand { Data = "[" + item + "]", Force = true },
            "replace" => new ReplaceCommand { Data = item },
            "replace-array" => new ReplaceCommand { Data = "[" + item + "]" },
            "patch" => new PatchCommand { Op = "set", Id = "a", Key = "x", Path = "/value", Value = "1" },
            "import" => new ImportCommand { File = file },
            "export" => new ExportCommand { File = file, Force = true },
            "ttl" => new TtlCommand { Subcommand = "set", Seconds = 3600 },
            "ttl-show" => new TtlCommand { Subcommand = "show" },
            "ttl-analytical" => new TtlCommand { Subcommand = "set", Seconds = 3600, Analytical = true },
            "ttl-analytical-show" => new TtlCommand { Subcommand = "show", Analytical = true },
            "conflict" => new ConflictCommand { Subcommand = "set", Mode = "lastWriterWins", Path = "/_ts" },
            "conflict-show" => new ConflictCommand { Subcommand = "show" },
            "index" => new IndexCommand { Subcommand = "set", Mode = "consistent" },
            _ => throw new ArgumentException("Unknown test command.", nameof(name)),
        };
    }

    private static ShellInterpreter CreateShell(int? ttl = 3600)
    {
        var client = Substitute.For<CosmosClient>();
        client.Endpoint.Returns(new Uri("https://example.documents.azure.com"));
        var database = Substitute.For<Database>();
        var container = Substitute.For<Container>();
        client.GetDatabase("db").Returns(database);
        database.GetContainer("items").Returns(container);
        var databaseResponse = Substitute.For<DatabaseResponse>();
        databaseResponse.StatusCode.Returns(HttpStatusCode.OK);
        database.ReadAsync(Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>()).Returns(databaseResponse);
        var properties = new ContainerProperties("items", "/pk")
        {
            DefaultTimeToLive = ttl,
            AnalyticalStoreTimeToLiveInSeconds = ttl,
            ConflictResolutionPolicy = new ConflictResolutionPolicy
            {
                Mode = ConflictResolutionMode.LastWriterWins,
                ResolutionPath = "/_ts",
            },
        };
        var containerResponse = Substitute.For<ContainerResponse>();
        containerResponse.StatusCode.Returns(HttpStatusCode.OK);
        containerResponse.Resource.Returns(properties);
        container.ReadContainerAsync(Arg.Any<ContainerRequestOptions>(), Arg.Any<CancellationToken>()).Returns(containerResponse);
        container.ReplaceContainerAsync(Arg.Any<ContainerProperties>(), Arg.Any<ContainerRequestOptions>(), Arg.Any<CancellationToken>()).Returns(containerResponse);
        container.CreateItemStreamAsync(Arg.Any<Stream>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ResponseMessage(HttpStatusCode.Created));
        container.UpsertItemStreamAsync(Arg.Any<Stream>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ResponseMessage(HttpStatusCode.OK));
        container.ReplaceItemStreamAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ResponseMessage(HttpStatusCode.OK));
        var patched = Substitute.For<ItemResponse<JsonElement>>();
        patched.StatusCode.Returns(HttpStatusCode.OK);
        container.PatchItemAsync<JsonElement>(Arg.Any<string>(), Arg.Any<PartitionKey>(), Arg.Any<IReadOnlyList<PatchOperation>>(), Arg.Any<PatchItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(patched);
        var iterator = Substitute.For<FeedIterator<JsonElement>>();
        var page = Substitute.For<FeedResponse<JsonElement>>();
        page.GetEnumerator().Returns(_ => ((IEnumerable<JsonElement>)[JsonSerializer.SerializeToElement(new { id = "a" })]).GetEnumerator());
        iterator.HasMoreResults.Returns(true, false);
        iterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(page);
        container.GetItemQueryIterator<JsonElement>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<QueryRequestOptions>()).Returns(iterator);

        var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("items", "db", client);
        return shell;
    }
}
