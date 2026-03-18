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
