// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using Spectre.Console;

[Collection(ConsoleOutputTestCollection.Name)]
public class ImportCommandTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("user")]
    [InlineData("json")]
    [InlineData("csv")]
    [InlineData("table")]
    public async Task ExecuteAsync_DryRun_UsesSelectedOutputFormat(string? format)
    {
        var file = Path.GetTempFileName();
        var originalOut = Console.Out;
        var originalConsole = AnsiConsole.Console;
        using var output = new StringWriter();
        using var shell = ShellInterpreter.CreateInstance();
        try
        {
            await File.WriteAllTextAsync(file, "[{\"id\":\"a\"},{\"id\":\"b\"}]", TestContext.Current.CancellationToken);
            Console.SetOut(output);
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(output),
            });
            AnsiConsole.Console.Profile.Width = 200;

            var command = new ImportCommand { File = file, DryRun = true, Format = format };
            var state = await command.ExecuteAsync(shell, new CommandState(), string.Empty, TestContext.Current.CancellationToken);

            Assert.Empty(output.ToString());
            var result = Assert.IsType<ShellJson>(state.Result).Value;
            Assert.Equal(0, result.GetProperty("imported").GetInt32());
            Assert.Equal(2, result.GetProperty("validated").GetInt32());
            Assert.True(result.GetProperty("dryRun").GetBoolean());

            shell.PrintState(state);

            var text = output.ToString();
            var summary = MessageService.GetArgsString("command-import-dry-run-success", "count", 2);
            if (format is null or "user")
            {
                Assert.Equal(summary + Environment.NewLine, text);
            }
            else
            {
                Assert.DoesNotContain(summary, text);
                if (format == "json")
                {
                    using var document = JsonDocument.Parse(text);
                    Assert.Equal(0, document.RootElement.GetProperty("imported").GetInt32());
                    Assert.Equal(2, document.RootElement.GetProperty("validated").GetInt32());
                }
                else
                {
                    if (format == "csv")
                    {
                        Assert.Contains("imported", text);
                    }
                    else
                    {
                        Assert.Equal(OutputFormat.Table, state.OutputFormat);
                        Assert.Equal(state.GenerateOutputText() + Environment.NewLine, text);
                    }

                    Assert.DoesNotContain("\"type\":", text);
                }
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
    [InlineData("items.json", (int)ImportFormat.Json)]
    [InlineData("items.jsonl", (int)ImportFormat.Json)]
    [InlineData("items", (int)ImportFormat.Json)]
    [InlineData("items.csv", (int)ImportFormat.Csv)]
    [InlineData("items.CSV", (int)ImportFormat.Csv)]
    public void ResolveFormat_ReadsJsonUnlessCsv(string file, int expected)
    {
        Assert.Equal((ImportFormat)expected, ImportCommand.ResolveFormat(file));
    }

    [Theory]
    [InlineData("[{\"id\":\"a\"},{\"id\":\"b\"}]")]
    [InlineData("\uFEFF  \r\n[\n    {\n        \"id\": \"a\"\n    },\n    {\n        \"id\": \"b\"\n    }\n]")]
    public async Task EnumerateJsonDocumentAsync_Array_YieldsItems(string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var ids = new List<string>();
        await foreach (var (_, element) in ImportCommand.EnumerateJsonDocumentAsync(stream, CancellationToken.None))
        {
            ids.Add(element.GetProperty("id").GetString()!);
        }

        Assert.Equal(["a", "b"], ids);
    }

    [Theory]
    [InlineData("{\"id\":\"a\",\"pk\":\"x\"}")]
    [InlineData("\uFEFF{\n    \"id\": \"a\",\n    \"pk\": \"x\"\n}\n")]
    public async Task EnumerateJsonDocumentAsync_SingleObject_YieldsOneItem(string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var items = new List<(int Index, JsonElement Item)>();
        await foreach (var item in ImportCommand.EnumerateJsonDocumentAsync(stream, CancellationToken.None))
        {
            items.Add(item);
        }

        var (index, element) = Assert.Single(items);
        Assert.Equal(1, index);
        Assert.Equal("a", element.GetProperty("id").GetString());
        Assert.Equal("x", element.GetProperty("pk").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n\t ")]
    [InlineData("\uFEFF")]
    public async Task EnumerateJsonDocumentAsync_EmptyInput_YieldsNothing(string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var count = 0;
        await foreach (var _ in ImportCommand.EnumerateJsonDocumentAsync(stream, CancellationToken.None))
        {
            count++;
        }

        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    public async Task EnumerateJsonDocumentAsync_ScalarDocument_Throws(string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var ex = await Assert.ThrowsAsync<CommandException>(() => CountJsonDocumentItemsAsync(stream));

        Assert.Contains("not a JSON object", ex.Message);
    }

    [Theory]
    [InlineData("{\"id\":\"1\"}\n{\"id\":\"2\"}\n", 1, 2)]
    [InlineData("{\"id\":\"1\"} {\"id\":\"2\"}", 1, 1)]
    [InlineData("{\"id\":\"1\"}{\"id\":\"2\"}", 1, 1)]
    [InlineData("\uFEFF \r\n{\n\"id\":\"1\"\n}\n\n{\n\"id\":\"2\"\n}\n", 2, 6)]
    [InlineData("{\"id\":\"1\"}\r\n\r\n{\"id\":\"2\"}", 1, 3)]
    [InlineData("{\"id\":\"1\"}\r\r{\"id\":\"2\"}", 1, 3)]
    public async Task EnumerateJsonDocumentAsync_ObjectSequence_YieldsItems(string content, int firstLine, int secondLine)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var items = new List<(int Index, string? Id)>();
        await foreach (var (index, item) in ImportCommand.EnumerateJsonDocumentAsync(stream, CancellationToken.None))
        {
            items.Add((index, item.GetProperty("id").GetString()));
        }

        Assert.Equal([(firstLine, "1"), (secondLine, "2")], items);
    }

    [Fact]
    public async Task EnumerateJsonDocumentAsync_LargeJsonLinesInput_YieldsBeforeReadingRestOfFile()
    {
        var content = new StringBuilder();
        for (var i = 0; i < 200_000; i++)
        {
            content.Append("{\"id\":\"").Append(i).Append("\",\"value\":\"padding\"}\n");
        }

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content.ToString()));

        await using var items = ImportCommand.EnumerateJsonDocumentAsync(stream, TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await items.MoveNextAsync());
        Assert.Equal("0", items.Current.Item.GetProperty("id").GetString());
        Assert.True(stream.Position < 1024 * 1024, $"Read {stream.Position} of {stream.Length} bytes.");
    }

    [Theory]
    [InlineData("[\"", "\"]")]
    [InlineData("\"", "\"")]
    [InlineData("{\"data\":\"", "")]
    public async Task EnumerateJsonDocumentAsync_InvalidTrailingValue_Throws(string prefix, string suffix)
    {
        var content = "{\"id\":\"first\"}\n" + prefix + new string('x', 4 * 1024 * 1024) + suffix;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        await Assert.ThrowsAsync<CommandException>(() => CountJsonDocumentItemsAsync(stream));
        if (prefix != "{\"data\":\"")
        {
            Assert.True(stream.Position < 64 * 1024, $"Read {stream.Position} of {stream.Length} bytes.");
        }
    }

    [Fact]
    public async Task EnumerateJsonDocumentAsync_LargeTrailingObject_IsImported()
    {
        var content = "{\"id\":\"first\"}\n{\"data\":\"" + new string('x', 4 * 1024 * 1024) + "\"}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        Assert.Equal(2, await CountJsonDocumentItemsAsync(stream));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(127)]
    [InlineData(4096)]
    public async Task EnumerateJsonDocumentAsync_LargeObjectInShortReads_PreservesContents(int chunkSize)
    {
        var text = new string('x', 2 * 1024 * 1024);
        var json = "{\"id\":\"first\",\"data\":\"" + text + "\"}\n{\"id\":\"last\"}";
        using var stream = new ChunkLimitedReadStream(Encoding.UTF8.GetBytes(json), chunkSize);
        var items = new List<JsonElement>();

        await foreach (var (_, item) in ImportCommand.EnumerateJsonDocumentAsync(stream, TestContext.Current.CancellationToken))
        {
            items.Add(item);
        }

        Assert.Equal(2, items.Count);
        Assert.Equal(text, items[0].GetProperty("data").GetString());
        Assert.Equal("last", items[1].GetProperty("id").GetString());
    }

    [Fact]
    public void ObjectBoundaryScanner_LargeObject_AdvancesOnlyAcrossNewBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"data\":\"" + new string('x', 2 * 1024 * 1024) + "\"}");
        ImportCommand.ObjectBoundaryScanner scanner = default;
        for (var length = 127; length < bytes.Length; length += 127)
        {
            Assert.False(scanner.Scan(bytes.AsSpan(0, length)));
            Assert.Equal(length, scanner.BytesScanned);
        }

        Assert.True(scanner.Scan(bytes));
        Assert.Equal(bytes.Length, scanner.BytesScanned);
    }

    [Theory]
    [InlineData("""{"a":"\"}","nested":[{}]}""")]
    [InlineData("""{"a":"\\","nested":{}}""")]
    [InlineData("""{"a":"\\\"{}[]","nested":[{"b":"x"}]}""")]
    [InlineData("""{"a":"\u0022}","nested":[]}""")]
    public async Task EnumerateJsonDocumentAsync_EscapesAndNestingAcrossReads_FindCorrectBoundary(string json)
    {
        using var stream = new ChunkLimitedReadStream(Encoding.UTF8.GetBytes(json + "\n{\"id\":\"last\"}"));

        Assert.Equal(2, await CountJsonDocumentItemsAsync(stream));
    }

    [Theory]
    [InlineData(63, true)]
    [InlineData(64, false)]
    public async Task EnumerateJsonDocumentAsync_ShortReads_PreservesDefaultDepthLimit(int arrayDepth, bool valid)
    {
        var json = "{\"nested\":" + new string('[', arrayDepth) + "0" + new string(']', arrayDepth) + "}";
        using var stream = new ChunkLimitedReadStream(Encoding.UTF8.GetBytes(json));
        if (valid)
        {
            Assert.Equal(1, await CountJsonDocumentItemsAsync(stream));
        }
        else
        {
            var error = await Assert.ThrowsAsync<CommandException>(() => CountJsonDocumentItemsAsync(stream));
            Assert.Contains("Line 1", error.Message);
            Assert.IsType<JsonException>(error.InnerException);
        }
    }

    [Theory]
    [InlineData("[{\"id\":\"a\"},")]
    [InlineData("[{\"id\":\"a\"}")]
    [InlineData("[{\"id\":\"a\"}]]")]
    [InlineData("[{\"id\":\"a\"},{\"id\":}]")]
    [InlineData("{\"id\":\"a\"")]
    [InlineData("{\"id\":\"a\"}\n{\"id\":}")]
    [InlineData("[{\"id\":\"a\"}]\n{\"id\":\"b\"}")]
    public async Task EnumerateJsonDocumentAsync_MalformedDocument_ReportsDocumentError(string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var ex = await Assert.ThrowsAsync<CommandException>(() => CountJsonDocumentItemsAsync(stream));

        Assert.Contains(content.StartsWith('[') ? "not a valid JSON document" : "not valid JSON", ex.Message);
        Assert.IsAssignableFrom<JsonException>(ex.InnerException);
    }

    [Theory]
    [InlineData("{\"id\":\"first\"}\n\n{\"id\":}", 3)]
    [InlineData("{\n\"id\":\"first\"\n}\r\n\r\n{\n\"id\":\n}", 5)]
    public async Task EnumerateJsonDocumentAsync_MalformedSequenceObject_ReportsPhysicalStartLine(string content, int expectedLine)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var error = await Assert.ThrowsAsync<CommandException>(() => CountJsonDocumentItemsAsync(stream));

        Assert.Contains($"Line {expectedLine} is not valid JSON", error.Message);
        Assert.IsAssignableFrom<JsonException>(error.InnerException);
    }

    [Fact]
    public async Task EnumerateJsonDocumentAsync_LargeObjectThenSmallObjects_YieldsAllItems()
    {
        var content = new StringBuilder("{\"data\":\"").Append('x', 2 * 1024 * 1024).Append("\"}\n");
        for (var index = 0; index < 20_000; index++)
        {
            content.Append("{\"id\":\"").Append(index).Append("\"}\n");
        }

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content.ToString()));
        var count = 0;
        await foreach (var (line, _) in ImportCommand.EnumerateJsonDocumentAsync(stream, TestContext.Current.CancellationToken))
        {
            Assert.Equal(++count, line);
        }

        Assert.Equal(20_001, count);
    }

    [Theory]
    [InlineData("{\"id\":\"a\"},\n{\"id\":\"b\"}")]
    [InlineData("{\"id\":\"a\"}\ntrailing garbage")]
    [InlineData("{\"id\":\"a\"}\n42")]
    [InlineData("{\"id\":\"a\"}\nnull")]
    public async Task EnumerateJsonDocumentAsync_NonObjectSequenceEntry_Throws(string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        await Assert.ThrowsAsync<CommandException>(() => CountJsonDocumentItemsAsync(stream));
    }

    [Theory]
    [InlineData(new byte[] { 0xEF })]
    [InlineData(new byte[] { 0xEF, 0xBB })]
    [InlineData(new byte[] { 0xEF, 0xBB, (byte)'[', (byte)']' })]
    [InlineData(new byte[] { (byte)' ', 0xEF, 0xBB, 0xBF })]
    public async Task EnumerateJsonDocumentAsync_IncompleteOrMisplacedByteOrderMark_Throws(byte[] content)
    {
        using var stream = new MemoryStream(content);

        await Assert.ThrowsAsync<CommandException>(() => CountJsonDocumentItemsAsync(stream));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("  []", 0)]
    [InlineData("\n[{\"id\":\"a\"},{\"id\":\"b\"}]", 2)]
    [InlineData(" {\"id\":\"a\"}", 1)]
    [InlineData("{\"id\":\"a\"}\n{\"id\":\"b\"}", 2)]
    public async Task EnumerateJsonDocumentAsync_ByteOrderMarkSplitAcrossReads_IsSkipped(string json, int expected)
    {
        using var stream = new ChunkLimitedReadStream([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(json)]);

        Assert.Equal(expected, await CountJsonDocumentItemsAsync(stream));
    }

    [Theory]
    [InlineData("utf-16", "{\"id\":\"a\",\"name\":\"\u00e9\"}\r\n{\"id\":\"b\"}\n", 2)]
    [InlineData("utf-16BE", "{\"id\":\"a\"}\n{\"id\":\"b\"}\n{\"id\":\"c\"}", 3)]
    [InlineData("utf-32", " [{\"id\":\"a\"},{\"id\":\"b\"}]", 2)]
    [InlineData("utf-32BE", "{\"id\":\"a\"}", 1)]
    [InlineData("utf-16", "", 0)]
    public async Task EnumerateJsonDocumentAsync_UnicodeByteOrderMark_IsTranscoded(string encodingName, string json, int expected)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        using var stream = new ChunkLimitedReadStream([.. encoding.GetPreamble(), .. encoding.GetBytes(json)]);

        var items = new List<(int Line, JsonElement Item)>();
        await foreach (var item in ImportCommand.EnumerateJsonDocumentAsync(stream, TestContext.Current.CancellationToken))
        {
            items.Add(item);
        }

        Assert.Equal(expected, items.Count);
        if (expected > 0)
        {
            Assert.Equal("a", items[0].Item.GetProperty("id").GetString());
        }

        if (encodingName == "utf-16" && expected > 0)
        {
            Assert.Equal("\u00e9", items[0].Item.GetProperty("name").GetString());
            Assert.Equal(2, items[1].Line);
        }
    }

    [Fact]
    public void ParseJsonLine_ValidObject_ReturnsClonedElement()
    {
        var element = ImportCommand.ParseJsonLine("{\"id\":\"1\",\"name\":\"abc\"}", 1);

        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        Assert.Equal("1", element.GetProperty("id").GetString());
        Assert.Equal("abc", element.GetProperty("name").GetString());
    }

    [Fact]
    public void ParseJsonLine_BlankLine_Throws()
    {
        var ex = Assert.Throws<CommandException>(() => ImportCommand.ParseJsonLine("   ", 7));
        Assert.Contains("7", ex.Message);
    }

    [Fact]
    public void ParseJsonLine_InvalidJson_ThrowsWithLineNumber()
    {
        var ex = Assert.Throws<CommandException>(() => ImportCommand.ParseJsonLine("{not json}", 4));
        Assert.Contains("4", ex.Message);
    }

    [Fact]
    public void ParseJsonLine_JsonArray_ThrowsNotObject()
    {
        var ex = Assert.Throws<CommandException>(() => ImportCommand.ParseJsonLine("[1,2,3]", 9));
        Assert.Contains("9", ex.Message);
    }

    [Fact]
    public void ParseJsonLine_JsonNumber_ThrowsNotObject()
    {
        var ex = Assert.Throws<CommandException>(() => ImportCommand.ParseJsonLine("42", 2));
        Assert.Contains("2", ex.Message);
    }

    [Fact]
    public async Task EnumerateJsonLinesAsync_YieldsAllValidLines()
    {
        var content = "{\"id\":\"1\"}\n{\"id\":\"2\"}\n{\"id\":\"3\"}\n";
        using var reader = new StringReader(content);

        var items = new List<(int LineNumber, string Id)>();
        await foreach (var (lineNumber, element) in ImportCommand.EnumerateJsonLinesAsync(reader, CancellationToken.None))
        {
            items.Add((lineNumber, element.GetProperty("id").GetString()!));
        }

        Assert.Equal(3, items.Count);
        Assert.Equal((1, "1"), items[0]);
        Assert.Equal((2, "2"), items[1]);
        Assert.Equal((3, "3"), items[2]);
    }

    [Fact]
    public async Task EnumerateJsonLinesAsync_SkipsBlankLinesAndTracksRealLineNumbers()
    {
        var content = "{\"id\":\"1\"}\n\n   \n{\"id\":\"2\"}\n";
        using var reader = new StringReader(content);

        var items = new List<(int LineNumber, string Id)>();
        await foreach (var (lineNumber, element) in ImportCommand.EnumerateJsonLinesAsync(reader, CancellationToken.None))
        {
            items.Add((lineNumber, element.GetProperty("id").GetString()!));
        }

        Assert.Equal(2, items.Count);
        Assert.Equal((1, "1"), items[0]);
        Assert.Equal((4, "2"), items[1]);
    }

    [Fact]
    public async Task EnumerateJsonLinesAsync_StopsOnInvalidLineByThrowing()
    {
        var content = "{\"id\":\"1\"}\nnope\n{\"id\":\"3\"}\n";
        using var reader = new StringReader(content);

        var ex = await Assert.ThrowsAsync<CommandException>(async () =>
        {
            await foreach (var _ in ImportCommand.EnumerateJsonLinesAsync(reader, CancellationToken.None))
            {
            }
        });

        Assert.Contains("2", ex.Message);
    }

    [Fact]
    public async Task EnumerateJsonLinesAsync_EmptyReader_YieldsNothing()
    {
        using var reader = new StringReader(string.Empty);

        var count = 0;
        await foreach (var _ in ImportCommand.EnumerateJsonLinesAsync(reader, CancellationToken.None))
        {
            count++;
        }

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task EnumerateArrayAsync_YieldsItemsWithOneBasedIndex()
    {
        var bytes = Encoding.UTF8.GetBytes("[{\"id\":\"a\"},{\"id\":\"b\"},{\"id\":\"c\"}]");
        using var stream = new MemoryStream(bytes);

        var items = new List<(int Index, string Id)>();
        await foreach (var (index, element) in ImportCommand.EnumerateArrayAsync(stream, CancellationToken.None))
        {
            items.Add((index, element.GetProperty("id").GetString()!));
        }

        Assert.Equal(3, items.Count);
        Assert.Equal((1, "a"), items[0]);
        Assert.Equal((2, "b"), items[1]);
        Assert.Equal((3, "c"), items[2]);
    }

    [Fact]
    public async Task EnumerateArrayAsync_NonObjectElement_Throws()
    {
        var bytes = Encoding.UTF8.GetBytes("[{\"id\":\"a\"},42]");
        using var stream = new MemoryStream(bytes);

        var ex = await Assert.ThrowsAsync<CommandException>(async () =>
        {
            await foreach (var _ in ImportCommand.EnumerateArrayAsync(stream, CancellationToken.None))
            {
            }
        });

        Assert.Contains("2", ex.Message);
    }

    [Fact]
    public async Task EnumerateArrayAsync_EmptyArray_YieldsNothing()
    {
        var bytes = Encoding.UTF8.GetBytes("[]");
        using var stream = new MemoryStream(bytes);

        var count = 0;
        await foreach (var _ in ImportCommand.EnumerateArrayAsync(stream, CancellationToken.None))
        {
            count++;
        }

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ExportThenImport_Array_RoundTripsContent()
    {
        var items = new[]
        {
            JsonSerializer.SerializeToElement(new { id = "1", value = 10 }),
            JsonSerializer.SerializeToElement(new { id = "2", value = 20 }),
        };

        using var buffer = new MemoryStream();
        var written = await ExportCommand.WriteArrayAsync(ToAsyncEnumerableAsync(items), buffer, CancellationToken.None);
        Assert.Equal(2, written);

        buffer.Position = 0;
        var readItems = new List<JsonElement>();
        await foreach (var (_, item) in ImportCommand.EnumerateJsonDocumentAsync(buffer, CancellationToken.None))
        {
            readItems.Add(item);
        }

        Assert.Equal(2, readItems.Count);
        Assert.Equal("1", readItems[0].GetProperty("id").GetString());
        Assert.Equal(10, readItems[0].GetProperty("value").GetInt32());
        Assert.Equal("2", readItems[1].GetProperty("id").GetString());
        Assert.Equal(20, readItems[1].GetProperty("value").GetInt32());
    }

    [Theory]
    [InlineData("/city", new[] { "city" })]
    [InlineData("/address/city", new[] { "address", "city" })]
    [InlineData("  /a/b/c  ", new[] { "a", "b", "c" })]
    [InlineData("/\"tenant id\"", new[] { "tenant id" })]
    [InlineData("/\"a/b\"/c", new[] { "a/b", "c" })]
    [InlineData("/\"say \\\"hi\\\"\"", new[] { "say \"hi\"" })]
    public void ParsePartitionKeySegments_SplitsPath(string path, string[] expected)
    {
        Assert.Equal(expected, ImportCommand.ParsePartitionKeySegments(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    public void ParsePartitionKeySegments_EmptyOrBlank_ReturnsNull(string? path)
    {
        Assert.Null(ImportCommand.ParsePartitionKeySegments(path));
    }

    [Fact]
    public void ParseCsv_ParsesHeaderAndRows()
    {
        var content = "id,name\n1,Alice\n2,Bob\n";

        var records = ImportCommand.ParseCsv(content, ',');

        Assert.Equal(3, records.Count);
        Assert.Equal(new[] { "id", "name" }, records[0]);
        Assert.Equal(new[] { "1", "Alice" }, records[1]);
        Assert.Equal(new[] { "2", "Bob" }, records[2]);
    }

    [Fact]
    public void ParseCsv_HandlesQuotedFieldsWithSeparatorsAndNewlines()
    {
        var content = "id,note\n1,\"a,b\"\n2,\"line1\nline2\"\n3,\"say \"\"hi\"\"\"\n";

        var records = ImportCommand.ParseCsv(content, ',');

        Assert.Equal(4, records.Count);
        Assert.Equal(new[] { "1", "a,b" }, records[1]);
        Assert.Equal(new[] { "2", "line1\nline2" }, records[2]);
        Assert.Equal(new[] { "3", "say \"hi\"" }, records[3]);
    }

    [Fact]
    public void ParseCsv_SkipsBlankLines()
    {
        var content = "id\n1\n\n2\n";

        var records = ImportCommand.ParseCsv(content, ',');

        Assert.Equal(3, records.Count);
        Assert.Equal(new[] { "id" }, records[0]);
        Assert.Equal(new[] { "1" }, records[1]);
        Assert.Equal(new[] { "2" }, records[2]);
    }

    [Fact]
    public void ParseCsvWithLines_TracksPhysicalStartLineAcrossEmbeddedNewlines()
    {
        var content = "id,note\n1,\"a,b\"\n2,\"line1\nline2\"\n3,ok\n";

        var records = ImportCommand.ParseCsvWithLines(content, ',');

        Assert.Equal(4, records.Count);
        Assert.Equal(1, records[0].StartLine);
        Assert.Equal(2, records[1].StartLine);
        Assert.Equal(3, records[2].StartLine);

        // Record 3 follows a quoted field with an embedded newline, so its physical
        // start line (5) differs from its record index (would be 4).
        Assert.Equal(5, records[3].StartLine);
        Assert.Equal(new[] { "3", "ok" }, records[3].Fields);
    }

    [Fact]
    public void BuildCsvObject_MapsColumnsToStringProperties()
    {
        var element = ImportCommand.BuildCsvObject(
            new[] { "id", "name" },
            new[] { "1", "Alice" },
            partitionKeySegments: null,
            lineNumber: 2);

        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        Assert.Equal("1", element.GetProperty("id").GetString());
        Assert.Equal("Alice", element.GetProperty("name").GetString());
    }

    [Fact]
    public void BuildCsvObject_EmptyUnnamedColumnIsIgnored()
    {
        var element = ImportCommand.BuildCsvObject(
            new[] { "id", "" },
            new[] { "1", "" },
            partitionKeySegments: null,
            lineNumber: 2);

        Assert.Equal("1", element.GetProperty("id").GetString());
        Assert.False(element.TryGetProperty("", out _));
    }

    [Fact]
    public void BuildCsvObject_PopulatedUnnamedColumnFailsWithLocation()
    {
        var error = Assert.Throws<CommandException>(() => ImportCommand.BuildCsvObject(
            new[] { "id", "" },
            new[] { "1", "lost" },
            partitionKeySegments: null,
            lineNumber: 7));

        Assert.Contains("7", error.Message);
        Assert.Contains("2", error.Message);
        Assert.Contains("header", error.Message);
    }

    [Fact]
    public async Task EnumerateCsvAsync_PopulatedUnnamedColumnReportsPhysicalStartLine()
    {
        var separator = Azure.Data.Cosmos.Shell.Core.ShellInterpreter.CSVSeparator;
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(filePath, $"id{separator}\"\"\n\"a\nb\"{separator}\"\"\n2{separator}lost\n", TestContext.Current.CancellationToken);

            var error = await Assert.ThrowsAsync<CommandException>(async () =>
            {
                await foreach (var _ in ImportCommand.EnumerateCsvAsync(filePath, null, TestContext.Current.CancellationToken))
                {
                    // Enumerate to trigger parsing and validation errors.
                }
            });

            Assert.Contains("4", error.Message);
            Assert.Contains("2", error.Message);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void ReadCsvRecords_ReadsValidRowsBeforeReportingMalformedRecord()
    {
        using var reader = new StringReader("id,name\n1,Alice\n2,\"unterminated");
        using var records = ImportCommand.ReadCsvRecords(reader, ',', TestContext.Current.CancellationToken).GetEnumerator();
        Assert.True(records.MoveNext());
        Assert.True(records.MoveNext());
        Assert.Equal(new[] { "1", "Alice" }, records.Current.Fields);
        var error = Assert.Throws<CommandException>(() => records.MoveNext());
        Assert.Contains("3", error.Message);
    }

    [Fact]
    public void ReadCsvRecords_CancellationStopsBetweenRecords()
    {
        using var reader = new StringReader("id,name\n1,Alice\n2,Bob");
        using var cancellation = new CancellationTokenSource();
        using var records = ImportCommand.ReadCsvRecords(reader, ',', cancellation.Token).GetEnumerator();
        Assert.True(records.MoveNext());
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => records.MoveNext());
    }

    [Fact]
    public async Task ReadCsvRecordsAsync_MatchesSynchronousRecordsAndStartLines()
    {
        const string content = "id,name\n1,\"multi\nline\"\n2,Bob\n";
        using var reader = new StringReader(content);
        var actual = new List<(int StartLine, List<string> Fields)>();
        await foreach (var record in ImportCommand.ReadCsvRecordsAsync(reader, ',', TestContext.Current.CancellationToken))
        {
            actual.Add(record);
        }

        var expected = ImportCommand.ParseCsvWithLines(content, ',');
        Assert.Equal(expected.Select(r => r.StartLine), actual.Select(r => r.StartLine));
        Assert.Equal(expected.Select(r => r.Fields), actual.Select(r => r.Fields));
    }

    [Fact]
    public async Task ReadCsvRecordsAsync_ReportsMalformedRecordWithPhysicalLine()
    {
        using var reader = new StringReader("id,name\n1,Alice\n2,\"unterminated");
        var error = await Assert.ThrowsAsync<CommandException>(async () =>
        {
            await foreach (var _ in ImportCommand.ReadCsvRecordsAsync(reader, ',', TestContext.Current.CancellationToken))
            {
                // Enumerate only to drive parsing until the malformed record throws.
            }
        });
        Assert.Contains("3", error.Message);
    }

    [Fact]
    public async Task ReadCsvRecordsAsync_CancellationStopsBetweenRecords()
    {
        using var reader = new StringReader("id,name\n1,Alice\n2,Bob");
        using var cancellation = new CancellationTokenSource();
        await using var records = ImportCommand.ReadCsvRecordsAsync(reader, ',', cancellation.Token).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await records.MoveNextAsync());
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await records.MoveNextAsync());
    }

    [Fact]
    public async Task ReadCsvRecordsAsync_CancellationInterruptsBlockedRecordRead()
    {
        using var reader = new BlockingAfterPrefixReader("id,name\n1,\"multi");
        using var cancellation = new CancellationTokenSource();
        await using var records = ImportCommand.ReadCsvRecordsAsync(reader, ',', cancellation.Token).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await records.MoveNextAsync());
        var move = records.MoveNextAsync().AsTask();
        await reader.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.False(move.IsCompleted);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => move.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    private sealed class BlockingAfterPrefixReader(string prefix) : TextReader
    {
        private bool prefixReturned;

        public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (!this.prefixReturned)
            {
                this.prefixReturned = true;
                prefix.AsSpan().CopyTo(buffer.Span);
                return prefix.Length;
            }

            this.Blocked.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(char[] buffer, int index, int count)
            => this.ReadAsync(buffer.AsMemory(index, count)).AsTask();
    }

    [Fact]
    public void ParseCsvWithLines_SkipsBlankLinesWithoutLosingPhysicalLineNumbers()
    {
        var records = ImportCommand.ParseCsvWithLines("id,name\n\n\n1,Alice\n", ',');
        Assert.Equal(4, records[1].StartLine);
    }

    [Fact]
    public void BuildCsvObject_SingleSegmentPartitionKey_StaysTopLevel()
    {
        var element = ImportCommand.BuildCsvObject(
            new[] { "id", "city" },
            new[] { "1", "Seattle" },
            new[] { "city" },
            lineNumber: 2);

        Assert.Equal("Seattle", element.GetProperty("city").GetString());
    }

    [Fact]
    public void BuildCsvObject_NestedPartitionKey_NestsMatchingColumn()
    {
        var element = ImportCommand.BuildCsvObject(
            new[] { "id", "city" },
            new[] { "1", "Seattle" },
            new[] { "address", "city" },
            lineNumber: 2);

        Assert.False(element.TryGetProperty("city", out _));
        Assert.Equal("Seattle", element.GetProperty("address").GetProperty("city").GetString());
        Assert.Equal("1", element.GetProperty("id").GetString());
    }

    [Fact]
    public void BuildCsvObject_NestedPartitionKey_ConflictingScalarColumn_Throws()
    {
        var ex = Assert.Throws<CommandException>(() => ImportCommand.BuildCsvObject(
            new[] { "id", "address", "city" },
            new[] { "1", "123 Main St", "Seattle" },
            new[] { "address", "city" },
            lineNumber: 2));

        Assert.Contains("address", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCsvObject_MissingValues_FillWithEmptyString()
    {
        var element = ImportCommand.BuildCsvObject(
            new[] { "id", "name", "extra" },
            new[] { "1" },
            partitionKeySegments: null,
            lineNumber: 2);

        Assert.Equal("1", element.GetProperty("id").GetString());
        Assert.Equal(string.Empty, element.GetProperty("name").GetString());
        Assert.Equal(string.Empty, element.GetProperty("extra").GetString());
    }

    private static async IAsyncEnumerable<JsonElement> ToAsyncEnumerableAsync(IEnumerable<JsonElement> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Yield();
        }
    }

    private static async Task<int> CountJsonDocumentItemsAsync(Stream stream)
    {
        var count = 0;
        await foreach (var _ in ImportCommand.EnumerateJsonDocumentAsync(stream, CancellationToken.None))
        {
            count++;
        }

        return count;
    }

    [Theory]
    [InlineData("{\"id\": @ ")]
    [InlineData("{\"id\":\"a\"}\n{\"id\": @ ")]
    [InlineData("{\"id\": [1,,")]
    public async Task EnumerateJsonDocumentAsync_EarlyInvalidObjectPrefix_FailsWithoutUnboundedRead(string prefix)
    {
        using var stream = new EndlessWhitespaceStream(Encoding.UTF8.GetBytes(prefix), limit: 16 * 1024 * 1024);

        await Assert.ThrowsAsync<CommandException>(() => CountJsonDocumentItemsAsync(stream));
        Assert.True(stream.Position <= 64 * 1024, $"Read {stream.Position} bytes before failing.");
    }

    private sealed class EndlessWhitespaceStream(byte[] prefix, long limit) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count) => this.Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (this.Position >= limit)
            {
                throw new InvalidOperationException("Input was read without bound.");
            }

            for (var i = 0; i < buffer.Length; i++)
            {
                var position = this.Position + i;
                buffer[i] = position < prefix.Length ? prefix[position] : (byte)' ';
            }

            this.Position += buffer.Length;
            return buffer.Length;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(this.Read(buffer.Span));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromResult(this.Read(buffer, offset, count));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ChunkLimitedReadStream(byte[] content, int chunkSize = 1) : MemoryStream(content)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, chunkSize));

        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, chunkSize)]);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => base.ReadAsync(buffer, offset, Math.Min(count, chunkSize), cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(buffer.Length, chunkSize)], cancellationToken);
    }
}
