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

        Assert.Contains("id", output);
        Assert.Contains("name", output);
        Assert.Contains("answer", output);
        Assert.Contains("12", output);
        Assert.Contains("alpha", output);
        Assert.Contains("13", output);
        Assert.Contains("beta", output);
        Assert.DoesNotContain("\"", output);
    }

    [Fact]
    void TestTableCustomHeaderProvider()
    {
        var commandState = new CommandState();
        commandState.OutputFormat = OutputFormat.Table;
        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { type = "container", values = new[] { "pktest", "ToDoList" } }));
        commandState.RenderTabular = () =>
        {
            var tabular = new TabularData("Container");
            tabular.AddRow("pktest");
            tabular.AddRow("ToDoList");
            return tabular;
        };

        var output = commandState.GenerateOutputText();

        Assert.Contains("Container", output);
        Assert.Contains("pktest", output);
        Assert.Contains("ToDoList", output);
        Assert.DoesNotContain("value", output);
    }

    [Fact]
    void TestCsvCustomHeaderProvider()
    {
        var commandState = new CommandState();
        commandState.OutputFormat = OutputFormat.CSV;
        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { type = "container", values = new[] { "pktest", "ToDoList" } }));
        commandState.RenderTabular = () =>
        {
            var tabular = new TabularData("Container");
            tabular.AddRow("pktest");
            tabular.AddRow("ToDoList");
            return tabular;
        };

        var output = commandState.GenerateOutputText().TrimEnd();

        var expected = "\"Container\"" + Environment.NewLine + "\"pktest\"" + Environment.NewLine + "\"ToDoList\"";
        Assert.Equal(expected, output);
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

    [Fact]
    async Task CommandStatement_ClearsRenderTabularFromPriorStatement()
    {
        // A prior command's RenderTabular delegate must not leak into the next
        // statement that reuses the same shared CommandState; otherwise CSV/Table
        // output for the later command would render the earlier command's table.
        using var shell = ShellInterpreter.CreateInstance();
        var lexer = new Lexer("echo \"b\"");
        var parser = new StatementParser(lexer);
        var statements = parser.ParseStatements();

        var state = new CommandState();
        state.RenderTabular = () => new TabularData("Leaked");

        state = await statements[0].RunAsync(shell, state, TestContext.Current.CancellationToken);

        Assert.Null(state.RenderTabular);
    }

    [Fact]
    async Task AssignmentStatement_ClearsRenderTabularFromPriorStatement()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var lexer = new Lexer("$x = 1");
        var parser = new StatementParser(lexer);
        var statements = parser.ParseStatements();

        var state = new CommandState();
        state.RenderTabular = () => new TabularData("Leaked");

        state = await statements[0].RunAsync(shell, state, TestContext.Current.CancellationToken);

        Assert.Null(state.RenderTabular);
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
