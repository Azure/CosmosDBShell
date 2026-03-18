// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using Azure.Data.Cosmos.Shell.Commands;

namespace CosmosShell.Tests.CommandTests;

public class MakeItemCommandTests
{
    [Fact]
    public void ParseJsonDictionary()
    {
        var result = MakeItemCommand.ParseJson("{ \"foo\": \"Bar\", \"test\":42}");
        var dict = result as IDictionary<string, object>;
        Assert.NotNull(dict);
        Assert.Equal(2, dict.Count);
        Assert.Equal(typeof(string), dict["foo"].GetType());
        Assert.Equal("Bar", dict["foo"]);
        Assert.Equal(42, (int)dict["test"]);
    }

    [Fact]
    public void ParseJsonList()
    {
        var result = MakeItemCommand.ParseJson("[{ \"foo\": \"Bar\", \"test\":42}]");
        var list = result as IList<object>;
        Assert.NotNull(list);
        Assert.Single(list);
        var dict = list[0] as Dictionary<string, object>;
        Assert.NotNull(dict);
        Assert.Equal("Bar", dict["foo"]);
        Assert.Equal(42, (int)dict["test"]);
    }
}
