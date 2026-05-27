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
        var written = await ExportCommand.WriteJsonLinesAsync(ToAsyncEnumerable(items), writer, CancellationToken.None);
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
        var written = await ExportCommand.WriteArrayAsync(ToAsyncEnumerable(items), buffer, CancellationToken.None);
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

    private static async IAsyncEnumerable<JsonElement> ToAsyncEnumerable(IEnumerable<JsonElement> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Yield();
        }
    }
}
