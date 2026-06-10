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

public class ImportCommandTests
{
    [Theory]
    [InlineData("[", (int)ImportFormat.Array)]
    [InlineData("  [", (int)ImportFormat.Array)]
    [InlineData("\n\n[", (int)ImportFormat.Array)]
    [InlineData("\uFEFF[", (int)ImportFormat.Array)]
    [InlineData("{", (int)ImportFormat.JsonLines)]
    [InlineData("  {", (int)ImportFormat.JsonLines)]
    [InlineData("\uFEFF{", (int)ImportFormat.JsonLines)]
    [InlineData("", (int)ImportFormat.JsonLines)]
    [InlineData("   \n\t  ", (int)ImportFormat.JsonLines)]
    public void DetectFormat_ChoosesBasedOnFirstNonWhitespaceChar(string leading, int expected)
    {
        Assert.Equal((ImportFormat)expected, ImportCommand.DetectFormat(leading));
    }

    [Fact]
    public void DetectFormat_WithNull_ReturnsJsonLines()
    {
        Assert.Equal(ImportFormat.JsonLines, ImportCommand.DetectFormat(null!));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("Auto")]
    [InlineData("jsonl")]
    [InlineData("JSONL")]
    [InlineData("jsonlines")]
    [InlineData("JsonLines")]
    [InlineData("array")]
    [InlineData("Array")]
    public void ImportFormat_ParsesDocumentedAliases(string value)
    {
        Assert.True(Enum.TryParse<ImportFormat>(value, ignoreCase: true, out _));
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
    public async Task ExportThenImport_JsonLines_RoundTripsContent()
    {
        var items = new[]
        {
            JsonSerializer.SerializeToElement(new { id = "1", name = "first" }),
            JsonSerializer.SerializeToElement(new { id = "2", name = "second" }),
            JsonSerializer.SerializeToElement(new { id = "3", name = "third" }),
        };

        using var writer = new StringWriter();
        writer.NewLine = "\n";
        var written = await ExportCommand.WriteJsonLinesAsync(ToAsyncEnumerableAsync(items), writer, CancellationToken.None);
        Assert.Equal(3, written);

        using var reader = new StringReader(writer.ToString());
        var readItems = new List<JsonElement>();
        await foreach (var (_, item) in ImportCommand.EnumerateJsonLinesAsync(reader, CancellationToken.None))
        {
            readItems.Add(item);
        }

        Assert.Equal(3, readItems.Count);
        for (var i = 0; i < items.Length; i++)
        {
            Assert.Equal(items[i].GetProperty("id").GetString(), readItems[i].GetProperty("id").GetString());
            Assert.Equal(items[i].GetProperty("name").GetString(), readItems[i].GetProperty("name").GetString());
        }
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
        await foreach (var (_, item) in ImportCommand.EnumerateArrayAsync(buffer, CancellationToken.None))
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
            partitionKeySegments: null);

        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        Assert.Equal("1", element.GetProperty("id").GetString());
        Assert.Equal("Alice", element.GetProperty("name").GetString());
    }

    [Fact]
    public void BuildCsvObject_SingleSegmentPartitionKey_StaysTopLevel()
    {
        var element = ImportCommand.BuildCsvObject(
            new[] { "id", "city" },
            new[] { "1", "Seattle" },
            new[] { "city" });

        Assert.Equal("Seattle", element.GetProperty("city").GetString());
    }

    [Fact]
    public void BuildCsvObject_NestedPartitionKey_NestsMatchingColumn()
    {
        var element = ImportCommand.BuildCsvObject(
            new[] { "id", "city" },
            new[] { "1", "Seattle" },
            new[] { "address", "city" });

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
            new[] { "address", "city" }));

        Assert.Contains("address", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCsvObject_MissingValues_FillWithEmptyString()
    {
        var element = ImportCommand.BuildCsvObject(
            new[] { "id", "name", "extra" },
            new[] { "1" },
            partitionKeySegments: null);

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
}
