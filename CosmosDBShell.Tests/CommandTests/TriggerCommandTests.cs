// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Microsoft.Azure.Cosmos.Scripts;

/// <summary>
/// Unit tests for the pure helpers on <see cref="TriggerCommand"/>: subcommand
/// normalization and trigger type/operation parsing.
/// </summary>
public class TriggerCommandTests
{
    [Theory]
    [InlineData("LIST", "list")]
    [InlineData("  Show  ", "show")]
    [InlineData("Create", "create")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void NormalizeSubcommand_TrimsAndLowercases(string? input, string expected)
    {
        Assert.Equal(expected, TriggerCommand.NormalizeSubcommand(input));
    }

    [Theory]
    [InlineData("pre", TriggerType.Pre)]
    [InlineData("PRE", TriggerType.Pre)]
    [InlineData("  post  ", TriggerType.Post)]
    public void ParseTriggerType_ValidValues_AreParsed(string input, TriggerType expected)
    {
        Assert.Equal(expected, TriggerCommand.ParseTriggerType(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("middle")]
    public void ParseTriggerType_InvalidValues_Throw(string? input)
    {
        Assert.Throws<CommandException>(() => TriggerCommand.ParseTriggerType(input));
    }

    [Theory]
    [InlineData(null, TriggerOperation.All)]
    [InlineData("", TriggerOperation.All)]
    [InlineData("all", TriggerOperation.All)]
    [InlineData("Create", TriggerOperation.Create)]
    [InlineData("replace", TriggerOperation.Replace)]
    [InlineData("DELETE", TriggerOperation.Delete)]
    [InlineData("  update  ", TriggerOperation.Update)]
    public void ParseTriggerOperation_ValidValues_AreParsed(string? input, TriggerOperation expected)
    {
        Assert.Equal(expected, TriggerCommand.ParseTriggerOperation(input));
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("bogus")]
    public void ParseTriggerOperation_InvalidValues_Throw(string input)
    {
        Assert.Throws<CommandException>(() => TriggerCommand.ParseTriggerOperation(input));
    }
}
