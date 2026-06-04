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

public class ExportCommandTests
{
    [Fact]
    public void SerializeJsonLine_ProducesCompactSingleLine()
    {
        var element = JsonSerializer.SerializeToElement(new
        {
            id = "1",
            value = 42,
            nested = new { tag = "a" },
        });

        var line = ExportCommand.SerializeJsonLine(element);

        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.Equal("{\"id\":\"1\",\"value\":42,\"nested\":{\"tag\":\"a\"}}", line);
    }

    [Fact]
    public void SerializeJsonLine_StripsSourceWhitespaceAndNewlines()
    {
        using var doc = JsonDocument.Parse("{\n  \"id\":\"1\",\n  \"name\": \"abc\"\n}");

        var line = ExportCommand.SerializeJsonLine(doc.RootElement);

        Assert.DoesNotContain('\n', line);
        Assert.Equal("{\"id\":\"1\",\"name\":\"abc\"}", line);
    }

    [Fact]
    public async Task WriteJsonLinesAsync_WritesOneItemPerLine()
    {
        var items = ToAsyncEnumerable(
            JsonSerializer.SerializeToElement(new { id = "1" }),
            JsonSerializer.SerializeToElement(new { id = "2" }),
            JsonSerializer.SerializeToElement(new { id = "3" }));

        using var writer = new StringWriter();
        writer.NewLine = "\n";

        var count = await ExportCommand.WriteJsonLinesAsync(items, writer, CancellationToken.None);

        Assert.Equal(3, count);
        var output = writer.ToString();
        var lines = output.TrimEnd('\n').Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Equal("{\"id\":\"1\"}", lines[0]);
        Assert.Equal("{\"id\":\"2\"}", lines[1]);
        Assert.Equal("{\"id\":\"3\"}", lines[2]);
    }

    [Fact]
    public async Task WriteJsonLinesAsync_WithNoItems_ProducesEmptyOutput()
    {
        var items = ToAsyncEnumerable();

        using var writer = new StringWriter();
        var count = await ExportCommand.WriteJsonLinesAsync(items, writer, CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public async Task WriteArrayAsync_WritesValidStreamingArray()
    {
        var items = ToAsyncEnumerable(
            JsonSerializer.SerializeToElement(new { id = "a" }),
            JsonSerializer.SerializeToElement(new { id = "b" }));

        using var buffer = new MemoryStream();
        var count = await ExportCommand.WriteArrayAsync(items, buffer, CancellationToken.None);

        Assert.Equal(2, count);
        buffer.Position = 0;
        using var doc = await JsonDocument.ParseAsync(buffer);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
        Assert.Equal("a", doc.RootElement[0].GetProperty("id").GetString());
        Assert.Equal("b", doc.RootElement[1].GetProperty("id").GetString());
    }

    [Fact]
    public async Task WriteArrayAsync_WithNoItems_ProducesEmptyArray()
    {
        var items = ToAsyncEnumerable();
        using var buffer = new MemoryStream();

        var count = await ExportCommand.WriteArrayAsync(items, buffer, CancellationToken.None);

        Assert.Equal(0, count);
        var text = Encoding.UTF8.GetString(buffer.ToArray());
        Assert.Equal("[]", text);
    }

    [Fact]
    public async Task WriteJsonLinesAsync_RoundTripsExoticValues()
    {
        var items = ToAsyncEnumerable(
            JsonSerializer.SerializeToElement(new { id = "1", text = "line\nwith\nnewlines" }),
            JsonSerializer.SerializeToElement(new { id = "2", numbers = new[] { 1, 2, 3 } }),
            JsonSerializer.SerializeToElement(new { id = "3", flag = (object?)null }));

        using var writer = new StringWriter();
        writer.NewLine = "\n";

        var count = await ExportCommand.WriteJsonLinesAsync(items, writer, CancellationToken.None);
        Assert.Equal(3, count);

        var lines = writer.ToString().TrimEnd('\n').Split('\n');
        Assert.Equal(3, lines.Length);

        // Each line must parse as a standalone JSON document.
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        }
    }

    [Theory]
    [InlineData("jsonl")]
    [InlineData("JSONL")]
    [InlineData("Jsonl")]
    [InlineData("jsonlines")]
    [InlineData("JsonLines")]
    [InlineData("array")]
    [InlineData("Array")]
    public void ExportFormat_ParsesDocumentedAliases(string value)
    {
        Assert.True(Enum.TryParse<ExportFormat>(value, ignoreCase: true, out _));
    }

    [Fact]
    public async Task WriteCsvAsync_WritesHeaderUnionAndRows()
    {
        var items = ToAsyncEnumerable(
            JsonSerializer.SerializeToElement(new { id = "1", name = "Alice" }),
            JsonSerializer.SerializeToElement(new { id = "2", city = "Seattle" }));

        using var writer = new StringWriter();
        writer.NewLine = "\n";

        var count = await ExportCommand.WriteCsvAsync(items, writer, ',', CancellationToken.None);

        Assert.Equal(2, count);
        var lines = writer.ToString().TrimEnd('\n').Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Equal("\"id\",\"name\",\"city\"", lines[0]);
        Assert.Equal("\"1\",\"Alice\",\"\"", lines[1]);
        Assert.Equal("\"2\",\"\",\"Seattle\"", lines[2]);
    }

    [Fact]
    public async Task WriteCsvAsync_EscapesSeparatorsQuotesAndNewlines()
    {
        var items = ToAsyncEnumerable(
            JsonSerializer.SerializeToElement(new { note = "a,b" }),
            JsonSerializer.SerializeToElement(new { note = "say \"hi\"" }),
            JsonSerializer.SerializeToElement(new { note = "line1\nline2" }));

        using var writer = new StringWriter();
        writer.NewLine = "\n";

        var count = await ExportCommand.WriteCsvAsync(items, writer, ',', CancellationToken.None);

        Assert.Equal(3, count);
        var output = writer.ToString();
        Assert.Contains("\"a,b\"", output);
        Assert.Contains("\"say \"\"hi\"\"\"", output);
        Assert.Contains("\"line1\nline2\"", output);
    }

    [Fact]
    public async Task WriteCsvAsync_NestedValuesWrittenAsCompactJson()
    {
        var items = ToAsyncEnumerable(
            JsonSerializer.SerializeToElement(new { id = "1", tags = new[] { 1, 2 }, nested = new { a = "b" } }));

        using var writer = new StringWriter();
        writer.NewLine = "\n";

        await ExportCommand.WriteCsvAsync(items, writer, ',', CancellationToken.None);

        var output = writer.ToString();
        Assert.Contains("\"[1,2]\"", output);
        Assert.Contains("\"{\"\"a\"\":\"\"b\"\"}\"", output);
    }

    [Fact]
    public async Task WriteCsvAsync_WithNoItems_ProducesEmptyOutput()
    {
        var items = ToAsyncEnumerable();

        using var writer = new StringWriter();
        var count = await ExportCommand.WriteCsvAsync(items, writer, ',', CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Equal(string.Empty, writer.ToString());
    }

    private static async IAsyncEnumerable<JsonElement> ToAsyncEnumerable(params JsonElement[] items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Yield();
        }
    }
}
