// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using System.Text.Json;
using System.Text.Json.Nodes;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

using RadLine;

public class CommandResultTest
{
    [Fact]
    public void TestCSV()
    {
        var state = new CommandState()
        {
            OutputFormat = OutputFormat.CSV
        };

        var items = new JsonArray
        {
            JsonDocument.Parse(@"{
  ""id"": ""test"",
  ""value"": 3
}").RootElement,
            JsonDocument.Parse(@"
{
  ""id"": ""test2"",
  ""value"": 4
}
").RootElement
        };

        // Convert JsonArray to JsonElement
        var jsonElement = JsonSerializer.SerializeToElement(items);
        state.Result = new ShellJson(jsonElement);

        Assert.Equal("\"id\";\"value\"" + Environment.NewLine + "\"test\";\"3\"" + Environment.NewLine + "\"test2\";\"4\"" + Environment.NewLine, state.GenerateOutputText());
    }

    [Fact]
    public void TestIncompleteItems()
    {
        var state = new CommandState()
        {
            OutputFormat = OutputFormat.CSV
        };

        var items = new JsonArray
        {
            JsonDocument.Parse(@"{
  ""id"": ""test"",
  ""value"": 3
}").RootElement,
            JsonDocument.Parse(@"{
  ""id"": ""test2"",
  ""value"": 3,
  ""myValue"": ""Hello World""
}").RootElement
        };

        // Convert JsonArray to JsonElement
        var jsonElement = JsonSerializer.SerializeToElement(items);
        state.Result = new ShellJson(jsonElement);

        Assert.Equal("\"id\";\"value\";\"myValue\"" + Environment.NewLine + "\"test\";\"3\";\"\"" + Environment.NewLine + "\"test2\";\"3\";\"Hello World\"" + Environment.NewLine, state.GenerateOutputText());
    }


    [Fact]
    public void TestIncompleteItems2()
    {
        var state = new CommandState()
        {
            OutputFormat = OutputFormat.CSV
        };

        var items = new JsonArray
        {
            JsonDocument.Parse(@"{
  ""id"": ""test2"",
  ""value"": 3,
  ""myValue"": ""Hello World""
}").RootElement,
            JsonDocument.Parse(@"{
  ""id"": ""test"",
  ""value"": 3
}").RootElement
        };

        // Convert JsonArray to JsonElement
        var jsonElement = JsonSerializer.SerializeToElement(items);
        state.Result = new ShellJson(jsonElement);

        Assert.Equal("\"id\";\"value\";\"myValue\"" + Environment.NewLine + "\"test2\";\"3\";\"Hello World\"" + Environment.NewLine + "\"test\";\"3\";\"\"" + Environment.NewLine, state.GenerateOutputText());
    }

    [Fact]
    public void TestIncompleteItems3()
    {
        var state = new CommandState()
        {
            OutputFormat = OutputFormat.CSV
        };

        var items = new JsonArray
        {
            JsonDocument.Parse(@"{
  ""id"": ""test"",
  ""value"": 3
}").RootElement,
            JsonDocument.Parse(@"{
  ""id"": ""test2"",
  ""value"": 3,
  ""myValue"": ""Hello World""
}").RootElement,
            JsonDocument.Parse(@"{
  ""id"": ""test3"",
  ""value"": 5
}").RootElement
        };

        // Convert JsonArray to JsonElement
        var jsonElement = JsonSerializer.SerializeToElement(items);
        state.Result = new ShellJson(jsonElement);

        Assert.Equal("\"id\";\"value\";\"myValue\"" + Environment.NewLine + "\"test\";\"3\";\"\"" + Environment.NewLine + "\"test2\";\"3\";\"Hello World\"" + Environment.NewLine + "\"test3\";\"5\";\"\"" + Environment.NewLine, state.GenerateOutputText());
    }


    [Fact]
    public void TestIncompleteItems_Case2()
    {
        var state = new CommandState()
        {
            OutputFormat = OutputFormat.CSV
        };

        var items = new JsonArray
        {
            JsonDocument.Parse(@" {
    ""product_id"": ""96bd76ec8810374ed1b65e291975717f"",
    ""product_category_name"": ""perfumes"",
    ""product_photos_qty"": ""1"",
    ""product_weight_g"": ""154"",
    ""product_length_cm"": ""64"",
    ""product_height_cm"": ""9"",
    ""product_width_cm"": ""12"",
    ""id"": ""d4061a67-5890-48e6-a99a-1e567a9f2e13"",
    ""maximum"": 0,
    ""_rid"": ""CWcDAO43XqcBAAAAAAAAAA=="",
    ""_self"": ""dbs/CWcDAA==/colls/CWcDAO43Xqc=/docs/CWcDAO43XqcBAAAAAAAAAA==/"",
    ""_etag"": ""\u0022c7003202-0000-0700-0000-67d2ba6e0000\u0022"",
    ""_attachments"": ""attachments/"",
    ""_ts"": 1741863534
  }").RootElement,
            JsonDocument.Parse(@"{
    ""product_id"": ""96bd76ec8810374ed1b65e291975717f"",
    ""product_category_name"": ""perfumes"",
    ""product_photos_qty"": ""1"",
    ""product_weight_g"": ""154"",
    ""product_length_cm"": ""64"",
    ""product_height_cm"": ""9"",
    ""product_width_cm"": ""12"",
    ""id"": ""d4061a67-5890-48e6-a99a-1e567a9f2e13"",
    ""maximum"": 0,
    ""_rid"": ""CWcDAO43XqcBAAAAAAAAAA=="",
    ""_self"": ""dbs/CWcDAA==/colls/CWcDAO43Xqc=/docs/CWcDAO43XqcBAAAAAAAAAA==/"",
    ""_etag"": ""\u0022c7003202-0000-0700-0000-67d2ba6e0000\u0022"",
    ""_attachments"": ""attachments/"",
    ""_ts"": 1741863534
  }").RootElement
        };

        // Convert JsonArray to JsonElement
        var jsonElement = JsonSerializer.SerializeToElement(items);
        state.Result = new ShellJson(jsonElement);

        Assert.Equal(
            "\"product_id\";\"product_category_name\";\"product_photos_qty\";\"product_weight_g\";\"product_length_cm\";\"product_height_cm\";\"product_width_cm\";\"id\";\"maximum\";\"_rid\";\"_self\";\"_etag\";\"_attachments\";\"_ts\"" + Environment.NewLine +
            "\"96bd76ec8810374ed1b65e291975717f\";\"perfumes\";\"1\";\"154\";\"64\";\"9\";\"12\";\"d4061a67-5890-48e6-a99a-1e567a9f2e13\";\"0\";\"CWcDAO43XqcBAAAAAAAAAA==\";\"dbs/CWcDAA==/colls/CWcDAO43Xqc=/docs/CWcDAO43XqcBAAAAAAAAAA==/\";\"\"\"c7003202-0000-0700-0000-67d2ba6e0000\"\"\";\"attachments/\";\"1741863534\"" + Environment.NewLine +
            "\"96bd76ec8810374ed1b65e291975717f\";\"perfumes\";\"1\";\"154\";\"64\";\"9\";\"12\";\"d4061a67-5890-48e6-a99a-1e567a9f2e13\";\"0\";\"CWcDAO43XqcBAAAAAAAAAA==\";\"dbs/CWcDAA==/colls/CWcDAO43Xqc=/docs/CWcDAO43XqcBAAAAAAAAAA==/\";\"\"\"c7003202-0000-0700-0000-67d2ba6e0000\"\"\";\"attachments/\";\"1741863534\"" + Environment.NewLine,
            state.GenerateOutputText());
    }
}