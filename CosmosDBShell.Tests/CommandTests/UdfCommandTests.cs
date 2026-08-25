// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Commands;

/// <summary>
/// Unit tests for the pure helpers on <see cref="UdfCommand"/>.
/// </summary>
public class UdfCommandTests
{
    [Theory]
    [InlineData("LIST", "list")]
    [InlineData("  Show  ", "show")]
    [InlineData("Create", "create")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void NormalizeSubcommand_TrimsAndLowercases(string? input, string expected)
    {
        Assert.Equal(expected, UdfCommand.NormalizeSubcommand(input));
    }

    [Fact]
    public void DefaultUserDefinedFunctionBody_IsValidSeedTemplate()
    {
        var body = UdfCommand.DefaultUserDefinedFunctionBody();

        Assert.False(string.IsNullOrWhiteSpace(body));
        Assert.Contains("function tax", body);
        Assert.Contains("return income", body);
    }
}
