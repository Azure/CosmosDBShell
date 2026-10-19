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
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;
using NSubstitute;

public class ExportCommandTests
{
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
    public async Task WriteArrayAsync_WritesIndentedArrayLikeVsCodeExport()
    {
        var items = ToAsyncEnumerableAsync(
            JsonSerializer.SerializeToElement(new { id = "a", nested = new { pk = "x" } }),
            JsonSerializer.SerializeToElement(new { id = "b" }));
        using var buffer = new MemoryStream();

        await ExportCommand.WriteArrayAsync(items, buffer, CancellationToken.None);

        Assert.Equal(
            "[\n    {\n        \"id\": \"a\",\n        \"nested\": {\n            \"pk\": \"x\"\n        }\n    },\n    {\n        \"id\": \"b\"\n    }\n]",
            Encoding.UTF8.GetString(buffer.ToArray()));
    }
    [Fact]
    public async Task WriteFileAsync_FailurePreservesExistingFileAndRemovesTemporaryFile()
    {
        var directory = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Join(directory, "export.json");
        try
        {
            await File.WriteAllTextAsync(path, "previous export", TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<IOException>(() => ExportCommand.WriteFileAsync(
                FailingItemsAsync(), path, true, CancellationToken.None));
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
                path, overwrite, CancellationToken.None);
            if (overwrite)
            {
                Assert.Equal(1, await ExportAsync());
                Assert.Equal("[\n    {\n        \"id\": \"1\"\n    }\n]", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            }
            else
            {
                var exception = await Assert.ThrowsAsync<IOException>(ExportAsync);
                Assert.Equal(
                    MessageService.GetArgsString("command-export-error-file_exists", "file", Path.GetFullPath(path)),
                    exception.Message);
                var items = Substitute.For<IAsyncEnumerable<JsonElement>>();
                await Assert.ThrowsAsync<IOException>(() => ExportCommand.WriteFileAsync(
                    items, path, false, TestContext.Current.CancellationToken));
                items.DidNotReceive().GetAsyncEnumerator(Arg.Any<CancellationToken>());
                Assert.Equal("previous export", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            }

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
    public async Task WriteFileAsync_ExistingDirectoryDoesNotEnumerateItems(bool overwrite)
    {
        var directory = Directory.CreateTempSubdirectory("cosmos-export-test-");
        try
        {
            var items = Substitute.For<IAsyncEnumerable<JsonElement>>();

            var exception = await Assert.ThrowsAsync<IOException>(() => ExportCommand.WriteFileAsync(
                items, directory.FullName, overwrite, TestContext.Current.CancellationToken));

            Assert.Equal(
                MessageService.GetArgsString("command-export-error-destination_directory", "file", Path.GetFullPath(directory.FullName)),
                exception.Message);
            Assert.Contains("Specify a file path instead", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("--force", exception.Message, StringComparison.Ordinal);
            items.DidNotReceive().GetAsyncEnumerator(Arg.Any<CancellationToken>());
        }
        finally
        {
            directory.Delete();
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
                path, true, cancellation.Token));
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
    public async Task WriteArrayAsync_DoesNotRetainSourceDocuments()
    {
        using var stream = new MemoryStream();
        Assert.Equal(200, await ExportCommand.WriteArrayAsync(TransientItemsAsync(), stream, TestContext.Current.CancellationToken));
        using var document = JsonDocument.Parse(stream.ToArray());
        Assert.Equal(199, document.RootElement[199].GetProperty("id").GetInt32());
    }

    [Fact]
    public void DeleteTemporaryFile_DoesNotThrowWhenDirectoryIsMissing()
    {
        var directory = Directory.CreateTempSubdirectory("cosmos-export-test-");
        var path = Path.Join(directory.FullName, "export.tmp");
        directory.Delete();

        Assert.IsAssignableFrom<IOException>(new DirectoryNotFoundException());
        ExportCommand.DeleteTemporaryFile(path);
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
