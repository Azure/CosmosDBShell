// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.UtilTest;

using Azure.Data.Cosmos.Shell.Util;

public class QueryErrorLocatorTests
{
    [Fact]
    public void TryLocate_StructuredJsonWithStartEnd_ReturnsLocationAndMessage()
    {
        var query = "SELECT * FORM c";
        var message = "Message: {\"errors\":[{\"severity\":\"Error\",\"location\":{\"start\":9,\"end\":13},\"code\":\"SC2001\",\"message\":\"Identifier 'FORM' could not be resolved.\"}]} ActivityId: 00000000-0000-0000-0000-000000000000";

        var loc = QueryErrorLocator.TryLocate(query, message);

        Assert.NotNull(loc);
        Assert.Equal(1, loc!.Line);
        Assert.Equal(10, loc.Column); // 0-based start=9 -> 1-based column 10
        Assert.Equal(4, loc.Length);
        Assert.Equal("Identifier 'FORM' could not be resolved.", loc.Message);
    }

    [Fact]
    public void TryLocate_LegacyJsonErrorsArrayOfStrings_LocatesNearToken()
    {
        var query = "SELECT * FORM c";
        var message = "Message: {\"Errors\":[\"Syntax error, incorrect syntax near 'FORM'.\"]} ActivityId: ...";

        var loc = QueryErrorLocator.TryLocate(query, message);

        Assert.NotNull(loc);
        Assert.Equal(1, loc!.Line);
        Assert.Equal(10, loc.Column);
        Assert.Equal(4, loc.Length);
    }

    [Fact]
    public void TryLocate_PlainTextNearToken_LocatesToken()
    {
        var query = "SELECT * FROM c WHERE c.x = abc";
        var message = "Syntax error, incorrect syntax near 'abc'.";

        var loc = QueryErrorLocator.TryLocate(query, message);

        Assert.NotNull(loc);
        Assert.Equal(1, loc!.Line);
        Assert.Equal(29, loc.Column);
        Assert.Equal(3, loc.Length);
    }

    [Fact]
    public void TryLocate_MultilineQueryWithStructuredLocation_ReturnsCorrectLine()
    {
        var query = "SELECT *\nFORM c";
        // 'FORM' starts at absolute offset 9 in the query (0-based).
        var message = "{\"errors\":[{\"location\":{\"start\":9,\"end\":13},\"message\":\"x\"}]}";

        var loc = QueryErrorLocator.TryLocate(query, message);

        Assert.NotNull(loc);
        Assert.Equal(2, loc!.Line);
        Assert.Equal(1, loc.Column);
        Assert.Equal(4, loc.Length);
    }

    [Fact]
    public void TryLocate_UnknownShape_ReturnsNull()
    {
        var loc = QueryErrorLocator.TryLocate(
            "SELECT * FROM c",
            "Throttled: too many requests for partition X.");

        Assert.Null(loc);
    }

    [Fact]
    public void TryLocate_EmptyInputs_ReturnsNull()
    {
        Assert.Null(QueryErrorLocator.TryLocate(string.Empty, "near 'FORM'"));
        Assert.Null(QueryErrorLocator.TryLocate("SELECT *", string.Empty));
    }

    [Fact]
    public void TryLocate_TokenNotPresentInQuery_ReturnsNull()
    {
        var loc = QueryErrorLocator.TryLocate(
            "SELECT * FROM c",
            "Syntax error, incorrect syntax near 'WHERE'.");

        Assert.Null(loc);
    }
}
