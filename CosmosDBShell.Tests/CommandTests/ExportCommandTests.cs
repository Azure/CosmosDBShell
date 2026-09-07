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
using Microsoft.Azure.Cosmos;
using NSubstitute;

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
        var items = ToAsyncEnumerableAsync(
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
        var items = ToAsyncEnumerableAsync();

        using var writer = new StringWriter();
        var count = await ExportCommand.WriteJsonLinesAsync(items, writer, CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public async Task WriteArrayAsync_WritesValidStreamingArray()
    {
        var items = ToAsyncEnumerableAsync(
            JsonSerializer.SerializeToElement(new { id = "a" }),
            JsonSerializer.SerializeToElement(new { id = "b" }));

        using var buffer = new MemoryStream();
        var count = await ExportCommand.WriteArrayAsync(items, buffer, CancellationToken.None);

        Assert.Equal(2, count);
        buffer.Position = 0;
        using var doc = await JsonDocument.ParseAsync(buffer, cancellationToken: CancellationToken.None);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
        Assert.Equal("a", doc.RootElement[0].GetProperty("id").GetString());
        Assert.Equal("b", doc.RootElement[1].GetProperty("id").GetString());
    }

    [Fact]
    public async Task WriteArrayAsync_WithNoItems_ProducesEmptyArray()
    {
        var items = ToAsyncEnumerableAsync();
        using var buffer = new MemoryStream();

        var count = await ExportCommand.WriteArrayAsync(items, buffer, CancellationToken.None);

        Assert.Equal(0, count);
        var text = Encoding.UTF8.GetString(buffer.ToArray());
        Assert.Equal("[]", text);
    }

    [Fact]
    public async Task WriteJsonLinesAsync_RoundTripsExoticValues()
    {
        var items = ToAsyncEnumerableAsync(
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
        var items = ToAsyncEnumerableAsync(
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
        var items = ToAsyncEnumerableAsync(
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
        var items = ToAsyncEnumerableAsync(
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
        var items = ToAsyncEnumerableAsync();

        using var writer = new StringWriter();
        var count = await ExportCommand.WriteCsvAsync(items, writer, ',', CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task WriteFileAsync_FailurePreservesExistingFileAndRemovesTemporaryFile(int format)
    {
        var directory = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Join(directory, "export.json");
        try
        {
            await File.WriteAllTextAsync(path, "previous export", TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<IOException>(() => ExportCommand.WriteFileAsync(
                FailingItemsAsync(), (ExportFormat)format, path, true, CancellationToken.None));
            Assert.Equal("previous export", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WriteFileAsync_RespectsOverwriteFlag(bool overwrite)
    {
        var directory = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Join(directory, "export.json");
        try
        {
            await File.WriteAllTextAsync(path, "previous export", TestContext.Current.CancellationToken);
            Task<int> ExportAsync() => ExportCommand.WriteFileAsync(
                ToAsyncEnumerableAsync(JsonSerializer.SerializeToElement(new { id = "1" })),
                ExportFormat.JsonLines, path, overwrite, CancellationToken.None);
            if (overwrite)
            {
                Assert.Equal(1, await ExportAsync());
                Assert.Equal("{\"id\":\"1\"}\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            }
            else
            {
                await Assert.ThrowsAsync<IOException>(ExportAsync);
                Assert.Equal("previous export", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            }

            Assert.Single(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task WriteFileAsync_CancellationPreservesExistingFile()
    {
        var directory = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Join(directory, "export.json");
        using var cancellation = new CancellationTokenSource();
        try
        {
            await File.WriteAllTextAsync(path, "previous export", TestContext.Current.CancellationToken);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExportCommand.WriteFileAsync(
                ToAsyncEnumerableAsync(JsonSerializer.SerializeToElement(new { id = "1" })),
                ExportFormat.Array, path, true, cancellation.Token));
            Assert.Equal("previous export", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task EnumerateAsync_LimitAtPageBoundaryDoesNotFetchAnotherPage()
    {
        using var iterator = Substitute.For<FeedIterator<JsonElement>>();
        var response = Substitute.For<FeedResponse<JsonElement>>();
        response.GetEnumerator().Returns(_ => ((IEnumerable<JsonElement>)new[] { JsonSerializer.SerializeToElement(new { id = "1" }) }).GetEnumerator());
        response.RequestCharge.Returns(3);
        iterator.HasMoreResults.Returns(true);
        iterator.ReadNextAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(response));
        var charge = 0.0;
        var count = 0;
        await foreach (var item in ExportCommand.EnumerateAsync(iterator, 1, value => charge += value, CancellationToken.None))
        {
            count++;
        }

        Assert.Equal(1, count);
        Assert.Equal(3, charge);
        await iterator.Received(1).ReadNextAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteCsvAsync_DoesNotRetainSourceDocuments()
    {
        using var writer = new StringWriter();
        Assert.Equal(200, await ExportCommand.WriteCsvAsync(TransientItemsAsync(), writer, ',', TestContext.Current.CancellationToken));
        Assert.Contains("199", writer.ToString());
    }

    [Fact]
    public void DeleteTemporaryFile_DoesNotThrowWhenPathIsDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("cosmos-export-test-");
        try
        {
            ExportCommand.DeleteTemporaryFile(directory.FullName);
            Assert.True(directory.Exists);
        }
        finally
        {
            directory.Delete();
        }
    }

    [Fact]
    public async Task WriteArrayAsync_FlushesIncrementallyWithoutFlushingEachItem()
    {
        using var stream = new MemoryStream();
        var item = JsonSerializer.SerializeToElement(new { value = new string('x', 1024) });
        async IAsyncEnumerable<JsonElement> ItemsAsync()
        {
            yield return item;
            Assert.Equal(0, stream.Length);
            for (var index = 0; index < 128; index++)
            {
                yield return item;
            }

            Assert.True(stream.Length >= 64 * 1024);
            await Task.Yield();
        }

        Assert.Equal(129, await ExportCommand.WriteArrayAsync(ItemsAsync(), stream, TestContext.Current.CancellationToken));
        using var result = JsonDocument.Parse(stream.ToArray());
        Assert.Equal(129, result.RootElement.GetArrayLength());
    }

    private static async IAsyncEnumerable<JsonElement> TransientItemsAsync()
    {
        for (var index = 0; index < 200; index++)
        {
            using var document = JsonDocument.Parse($"{{\"id\":{index}}}");
            yield return document.RootElement;
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<JsonElement> FailingItemsAsync()
    {
        yield return JsonSerializer.SerializeToElement(new { id = "1" });
        await Task.Yield();
        throw new IOException("simulated read failure");
    }

    private static async IAsyncEnumerable<JsonElement> ToAsyncEnumerableAsync(params JsonElement[] items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Yield();
        }
    }
}
