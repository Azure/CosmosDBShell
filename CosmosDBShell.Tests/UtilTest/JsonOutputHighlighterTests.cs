// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using System.Text.Json;

using Azure.Data.Cosmos.Shell.Core;

namespace CosmosShell.Tests.UtilTest;

public class JsonOutputHighlighterTests
{
    [Fact]
    public void Primitives_AreColoredByType()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("{ \"name\": \"alice\", \"age\": 42, \"active\": true, \"nick\": null }");

        var markup = JsonOutputHighlighter.BuildMarkup(element);

        // Property name uses the JSON property color (cyan).
        Assert.Contains("[cyan]\"name\"[/]", markup);

        // Each value type uses its dedicated helper from Theme.
        Assert.Contains("[violet]\"alice\"[/]", markup);
        Assert.Contains("[violet]42[/]", markup);
        Assert.Contains("[violet]true[/]", markup);
        Assert.Contains("[violet]null[/]", markup);

        // Outer braces use the depth-0 bracket color; comma and colon use the
        // shared punctuation color.
        var depth0 = Theme.GetBracketColor(0);
        Assert.Contains($"[{depth0}]{{[/]", markup);
        Assert.Contains($"[{depth0}]}}[/]", markup);
        Assert.Contains("[yellow]:[/]", markup);
        Assert.Contains("[yellow],[/]", markup);
    }

    [Fact]
    public void NestedObjectsAndArrays_AreIndented()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("{ \"items\": [1, 2] }");

        var markup = JsonOutputHighlighter.BuildMarkup(element);

        // Two-space indentation matching Utf8JsonWriter(Indented=true).
        Assert.Contains("\n  [cyan]\"items\"[/]", markup);
        Assert.Contains("\n    [violet]1[/]", markup);
        Assert.Contains("\n    [violet]2[/]", markup);
    }

    [Fact]
    public void EmptyObjectAndArray_RenderInline()
    {
        var emptyObject = JsonSerializer.Deserialize<JsonElement>("{}");
        var emptyArray = JsonSerializer.Deserialize<JsonElement>("[]");

        var depth0 = Theme.GetBracketColor(0);
        Assert.Equal($"[{depth0}]{{[/][{depth0}]}}[/]", JsonOutputHighlighter.BuildMarkup(emptyObject));
        Assert.Equal($"[{depth0}][[[/][{depth0}]]][/]", JsonOutputHighlighter.BuildMarkup(emptyArray));
    }

    [Fact]
    public void StringValues_AreJsonAndMarkupEscaped()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("{ \"q\": \"a\\\"b\" }");

        var markup = JsonOutputHighlighter.BuildMarkup(element);

        // The embedded quote stays JSON-escaped inside the markup token.
        Assert.Contains("[violet]\"a\\u0022b\"[/]", markup);
    }

    [Fact]
    public void NestedBrackets_CycleColorsByDepth()
    {
        // Depth 0 -> '{', depth 1 -> '[', depth 2 -> '{' (next nested object).
        var element = JsonSerializer.Deserialize<JsonElement>("{ \"a\": [ { \"b\": 1 } ] }");

        var markup = JsonOutputHighlighter.BuildMarkup(element);

        Assert.Contains($"[{Theme.GetBracketColor(0)}]{{[/]", markup);
        Assert.Contains($"[{Theme.GetBracketColor(1)}][[[/]", markup);
        Assert.Contains($"[{Theme.GetBracketColor(2)}]{{[/]", markup);

        // Closing brackets should use the same color as their matching opener.
        Assert.Contains($"[{Theme.GetBracketColor(2)}]}}[/]", markup);
        Assert.Contains($"[{Theme.GetBracketColor(1)}]]][/]", markup);
        Assert.Contains($"[{Theme.GetBracketColor(0)}]}}[/]", markup);
    }
}
