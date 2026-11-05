// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;

// Environment changes must not race with other CSV exports.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CsvScalarColumnEnvironmentTestCollection
{
    public const string Name = "CSV scalar column environment tests";
}

[Collection(CsvScalarColumnEnvironmentTestCollection.Name)]
public class CsvScalarColumnEnvironmentTests
{
    private const string EnvironmentVariable = "COSMOSDB_SHELL_CSV_SCALAR_COLUMN";

    [Theory]
    [InlineData(null, "\"\"")]
    [InlineData("scalar", "\"scalar\"")]
    [InlineData("scalar,\"name", "\"scalar,\"\"name\"")]
    public async Task WriteCsvAsync_OmittedScalarColumn_UsesEnvironmentOrEmptyDefault(string? configuredHeader, string expectedHeader)
    {
        var previous = Environment.GetEnvironmentVariable(EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(EnvironmentVariable, configuredHeader);
            using var writer = new StringWriter { NewLine = "\n" };

            var count = await ExportCommand.WriteCsvAsync(
                ScalarRowsAsync(), writer, ',', TestContext.Current.CancellationToken);

            Assert.Equal(1, count);
            Assert.Equal($"{expectedHeader}\n\"text\"\n", writer.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvironmentVariable, previous);
        }
    }

    [Theory]
    [InlineData("", "\"\"")]
    [InlineData("explicit", "\"explicit\"")]
    public async Task WriteCsvAsync_ExplicitScalarColumn_OverridesEnvironment(string explicitHeader, string expectedHeader)
    {
        var previous = Environment.GetEnvironmentVariable(EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(EnvironmentVariable, "configured");
            using var writer = new StringWriter { NewLine = "\n" };

            var count = await ExportCommand.WriteCsvAsync(
                ScalarRowsAsync(), writer, ',', TestContext.Current.CancellationToken, explicitHeader);

            Assert.Equal(1, count);
            Assert.Equal($"{expectedHeader}\n\"text\"\n", writer.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvironmentVariable, previous);
        }
    }

    private static async IAsyncEnumerable<JsonElement> ScalarRowsAsync()
    {
        yield return JsonSerializer.SerializeToElement("text");
        await Task.CompletedTask;
    }
}
