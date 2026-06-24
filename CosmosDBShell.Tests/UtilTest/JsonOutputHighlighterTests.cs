// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using System.Text.Json;

using Azure.Data.Cosmos.Shell.Core;

namespace CosmosShell.Tests.UtilTest;

[Collection(CosmosShell.Tests.Shell.ThemeStateTestCollection.Name)]
public class JsonOutputHighlighterTests
{
    [Fact]
    public void Primitives_AreColoredByType()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("{ \"name\": \"alice\", \"age\": 42, \"active\": true, \"nick\": null }");

        var markup = JsonOutputHighlighter.BuildMarkup(element);

        // Property name uses the JSON property color from Theme.
        Assert.Contains($"[{Theme.JsonPropertyColorName}]\"name\"[/]", markup);

        // Each value type uses its dedicated per-type literal color from Theme.
        Assert.Contains($"[{Theme.StringColorName}]\"alice\"[/]", markup);
        Assert.Contains($"[{Theme.NumberColorName}]42[/]", markup);
        Assert.Contains($"[{Theme.BooleanColorName}]true[/]", markup);
        Assert.Contains($"[{Theme.NullColorName}]null[/]", markup);

        // Outer braces use the depth-0 bracket color; comma and colon use the
        // shared punctuation color.
        var depth0 = Theme.GetBracketColor(0);
        Assert.Contains($"[{depth0}]{{[/]", markup);
        Assert.Contains($"[{depth0}]}}[/]", markup);
        Assert.Contains($"[{Theme.JsonPunctuationColorName}]:[/]", markup);
        Assert.Contains($"[{Theme.JsonPunctuationColorName}],[/]", markup);
    }

    [Fact]
    public void StringEscapes_AreColoredWithEscapeColor()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("{ \"k\": \"a\\nb\" }");

        var markup = JsonOutputHighlighter.BuildMarkup(element);

        // The backslash escape uses the dedicated escape color.
        Assert.Contains($"[{Theme.StringEscapeColorName}]\\n[/]", markup);
        // Surrounding string body still uses the string color.
        Assert.Contains($"[{Theme.StringColorName}]\"a[/]", markup);
        Assert.Contains($"[{Theme.StringColorName}]b\"[/]", markup);
    }

    [Fact]
    public void NestedObjectsAndArrays_AreIndented()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("{ \"items\": [1, 2] }");

        var markup = JsonOutputHighlighter.BuildMarkup(element);

        // Two-space indentation matching Utf8JsonWriter(Indented=true).
        Assert.Contains($"\n  [{Theme.JsonPropertyColorName}]\"items\"[/]", markup);
        Assert.Contains($"\n    [{Theme.NumberColorName}]1[/]", markup);
        Assert.Contains($"\n    [{Theme.NumberColorName}]2[/]", markup);
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

        // The embedded quote stays JSON-escaped (\u0022) and is colored as a string escape,
        // while the surrounding body keeps the string color.
        Assert.Contains($"[{Theme.StringColorName}]\"a[/]", markup);
        Assert.Contains($"[{Theme.StringEscapeColorName}]\\u0022[/]", markup);
        Assert.Contains($"[{Theme.StringColorName}]b\"[/]", markup);
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
