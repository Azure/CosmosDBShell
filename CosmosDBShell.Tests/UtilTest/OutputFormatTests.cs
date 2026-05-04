// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CosmosShell.Tests.Shell;

public class OutputFormatTests
{
    [Fact]
    void TestJSon()
    {
        var input = "{ \"id\": 12, \"Hello\": \"World\", \"Answer\": 53 }";
        var element = JsonSerializer.Deserialize<JsonElement>(input);

        var commandState = new CommandState();
        commandState.Result = new ShellJson(element);
        commandState.OutputFormat = OutputFormat.JSon;

        var output = commandState.GenerateOutputText();

        Assert.Equal(StripWS(input), StripWS(output));
    }

    [Fact]
    void TestCSV()
    {
        var input = "{ \"id\": 12, \"Hello\": \"World\", \"Answer\": 53 }";
        var element = JsonSerializer.Deserialize<JsonElement>(input);

        var commandState = new CommandState();
        commandState.Result = new ShellJson(element);
        commandState.OutputFormat = OutputFormat.CSV;

        var output = commandState.GenerateOutputText();

        Assert.Equal("\"id\";\"Hello\";\"Answer\"" + Environment.NewLine + "\"12\";\"World\";\"53\"", output.TrimEnd());
    }

    [Fact]
    void TestTable()
    {
        var input = "{ \"id\": 12, \"Hello\": \"World\", \"Answer\": 53 }";
        var element = JsonSerializer.Deserialize<JsonElement>(input);

        var commandState = new CommandState();
        commandState.Result = new ShellJson(element);
        commandState.OutputFormat = OutputFormat.Table;

        var output = commandState.GenerateOutputText();

        Assert.Contains("id", output);
        Assert.Contains("Hello", output);
        Assert.Contains("Answer", output);
        Assert.Contains("World", output);
        Assert.Contains("---", output);
        Assert.DoesNotContain("\"", output);
    }

    [Fact]
    void TestTableItems()
    {
        var input = """
        {
            "items": [
                { "id": 12, "name": "alpha", "answer": 53 },
                { "id": 13, "name": "beta" }
            ]
        }
        """;
        var element = JsonSerializer.Deserialize<JsonElement>(input);

        var commandState = new CommandState();
        commandState.Result = new ShellJson(element);
        commandState.OutputFormat = OutputFormat.Table;

        var output = commandState.GenerateOutputText();
        var lines = output.TrimEnd('\r', '\n').Split(Environment.NewLine);

        Assert.Equal(4, lines.Length);
        Assert.Equal("id  name   answer", lines[0]);
        Assert.Equal("--  -----  ------", lines[1]);
        Assert.Equal("12  alpha  53    ", lines[2]);
        Assert.Equal("13  beta         ", lines[3]);
    }

    [Fact]
    void TestSetFormatTable()
    {
        var commandState = new CommandState();
        commandState.SetFormat("table");
        Assert.Equal(OutputFormat.Table, commandState.OutputFormat);

        commandState.SetFormat("TABLE");
        Assert.Equal(OutputFormat.Table, commandState.OutputFormat);

        commandState.SetFormat("tbl");
        Assert.Equal(OutputFormat.Table, commandState.OutputFormat);
    }

    private string StripWS(string input)
    {
        var sb = new StringBuilder();

        foreach (var c in input)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
