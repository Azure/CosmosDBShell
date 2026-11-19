// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Text.Json;

using Azure.Data.Cosmos.Shell.Parser;

public class FilterIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task Filter_PipelineProjectsQuotedProperties()
    {
        Shell.SetVariable("data", new ShellJson(JsonSerializer.SerializeToElement(new
        {
            items = new[]
            {
                new Dictionary<string, object?> { ["Volcano Name"] = "Abu", ["Country"] = "Japan", ["Region"] = "Honshu-Japan" },
                new Dictionary<string, object?> { ["Volcano Name"] = "Acamarachi", ["Country"] = "Chile", ["Region"] = "Chile-N" },
            },
        })));

        var outputFile = CaptureOutputFile();
        var state = await RunScriptAsync("echo $data | filter '.items | map({\"Volcano Name\": .[\"Volcano Name\"], Country})'");

        Assert.False(state.IsError, FormatError(state));
        var output = await ReadRedirectAsync(outputFile);
        var json = JsonDocument.Parse(output).RootElement;
        Assert.Equal(JsonValueKind.Array, json.ValueKind);
        Assert.Equal("Abu", json[0].GetProperty("Volcano Name").GetString());
        Assert.Equal("Japan", json[0].GetProperty("Country").GetString());
        Assert.False(json[0].TryGetProperty("Region", out _));
        Assert.Equal("Acamarachi", json[1].GetProperty("Volcano Name").GetString());
        Assert.Equal("Chile", json[1].GetProperty("Country").GetString());
    }

    [Fact]
    public async Task Filter_PipelineContainsStringLiteralReturnsJsonBoolean()
    {
        Shell.SetVariable("data", new ShellJson(JsonSerializer.SerializeToElement(new
        {
            tags = new[] { "dev", "prod" },
        })));

        var outputFile = CaptureOutputFile();
        var state = await RunScriptAsync("echo $data | filter '.tags | contains(\"prod\")'");

        Assert.False(state.IsError, FormatError(state));
        var output = await ReadRedirectAsync(outputFile);
        var json = JsonDocument.Parse(output).RootElement;
        Assert.Equal(JsonValueKind.True, json.ValueKind);
    }

    [Fact]
    public async Task Filter_PipelineTypeReturnsJsonString()
    {
        Shell.SetVariable("data", new ShellJson(JsonSerializer.SerializeToElement(new
        {
            id = "item-1",
        })));

        var outputFile = CaptureOutputFile();
        var state = await RunScriptAsync("echo $data | filter 'type'");

        Assert.False(state.IsError, FormatError(state));
        var output = await ReadRedirectAsync(outputFile);
        var json = JsonDocument.Parse(output).RootElement;
        Assert.Equal(JsonValueKind.String, json.ValueKind);
        Assert.Equal("object", json.GetString());
    }
}
