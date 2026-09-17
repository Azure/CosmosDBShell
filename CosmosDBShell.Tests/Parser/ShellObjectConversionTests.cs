// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Conversion tests for the <see cref="ShellObject"/> value types
/// (<see cref="ShellJson"/>, <see cref="ShellIdentifier"/>, <see cref="ShellSequence"/>).
/// These are pure functions that map a value to each <see cref="DataType"/>, including
/// the error branches for unconvertible values.
/// </summary>
public class ShellObjectConversionTests
{
    [Theory]
    [InlineData((int)DataType.Boolean, "conversion-error-text-boolean", "conversion-error-identifier-boolean")]
    [InlineData((int)DataType.Number, "conversion-error-text-number", "conversion-error-identifier-number")]
    [InlineData((int)DataType.Decimal, "conversion-error-text-double", "conversion-error-identifier-double")]
    public void InvalidStringConversions_UseLocalizedMessages(int targetType, string textKey, string identifierKey)
    {
        var target = (DataType)targetType;
        const string value = "not-a-value";
        var textError = Assert.Throws<InvalidOperationException>(() => new ShellText(value).ConvertShellObject(target));
        var identifierError = Assert.Throws<InvalidOperationException>(() => new ShellIdentifier(value).ConvertShellObject(target));

        Assert.Equal(MessageService.GetArgsString(textKey, "value", value), textError.Message);
        Assert.Equal(MessageService.GetArgsString(identifierKey, "value", value), identifierError.Message);
    }

    [Theory]
    [InlineData((int)DataType.Boolean, "conversion-error-json-boolean")]
    [InlineData((int)DataType.Number, "conversion-error-json-number")]
    [InlineData((int)DataType.Decimal, "conversion-error-json-decimal")]
    public void InvalidJsonConversions_UseLocalizedMessages(int targetType, string key)
    {
        var target = (DataType)targetType;
        var error = Assert.Throws<InvalidOperationException>(() => Json("{}").ConvertShellObject(target));

        Assert.Equal(MessageService.GetArgsString(key, "kind", JsonValueKind.Object), error.Message);
    }

    [Fact]
    public void InvalidJsonText_UsesLocalizedMessageAndPreservesParseDetails()
    {
        const string value = "{not json";
        var parseError = Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(value));
        var textError = Assert.Throws<InvalidOperationException>(() => new ShellText(value).ConvertShellObject(DataType.Json));
        var identifierError = Assert.Throws<InvalidOperationException>(() => new ShellIdentifier(value).ConvertShellObject(DataType.Json));

        Assert.Equal(MessageService.GetArgsString("conversion-error-text-json", "value", value, "error", parseError.Message), textError.Message);
        Assert.Equal(MessageService.GetArgsString("conversion-error-identifier-json", "value", value, "error", parseError.Message), identifierError.Message);
    }

    [Fact]
    public void UnsupportedTargets_UseLocalizedMessages()
    {
        var target = (DataType)int.MaxValue;
        var values = new (ShellObject Value, string Key)[]
        {
            (new ShellBool(true), "conversion-error-boolean-type"),
            (new ShellNumber(1), "conversion-error-number-type"),
            (new ShellDecimal(1.5), "conversion-error-decimal-type"),
            (new ShellText("value"), "conversion-error-text-type"),
            (new ShellIdentifier("value"), "conversion-error-identifier-type"),
            (Json("{}"), "conversion-error-json-type"),
        };

        foreach (var (value, key) in values)
        {
            var error = Assert.Throws<InvalidOperationException>(() => value.ConvertShellObject(target));
            Assert.Equal(MessageService.GetArgsString(key, "type", target), error.Message);
        }
    }

    private static ShellJson Json(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return new ShellJson(doc.RootElement.Clone());
    }

    [Fact]
    public void ShellJson_NumberToAllTypes()
    {
        var json = Json("42");
        Assert.Equal("42", json.ConvertShellObject(DataType.Text));
        Assert.Equal(42, json.ConvertShellObject(DataType.Number));
        Assert.Equal(42m, json.ConvertShellObject(DataType.Decimal));
        Assert.Equal(true, json.ConvertShellObject(DataType.Boolean));
    }

    [Fact]
    public void ShellJson_ZeroNumber_IsFalse()
    {
        Assert.Equal(false, Json("0").ConvertShellObject(DataType.Boolean));
    }

    [Fact]
    public void ShellJson_StringValue_TextReturnsUnquoted()
    {
        var json = Json("\"hello\"");
        Assert.Equal("hello", json.ConvertShellObject(DataType.Text));
    }

    [Theory]
    [InlineData("\"true\"", true)]
    [InlineData("\"1\"", true)]
    [InlineData("\"yes\"", true)]
    [InlineData("\"false\"", false)]
    [InlineData("\"0\"", false)]
    [InlineData("\"no\"", false)]
    [InlineData("\"\"", false)]
    public void ShellJson_StringToBoolean(string raw, bool expected)
    {
        Assert.Equal(expected, Json(raw).ConvertShellObject(DataType.Boolean));
    }

    [Theory]
    [InlineData("\"123\"", 123)]
    public void ShellJson_StringToNumber(string raw, int expected)
    {
        Assert.Equal(expected, Json(raw).ConvertShellObject(DataType.Number));
    }

    [Fact]
    public void ShellJson_StringToDecimal()
    {
        Assert.Equal(15m, Json("\"15\"").ConvertShellObject(DataType.Decimal));
    }

    [Fact]
    public void ShellJson_TrueFalseLiterals_ToBoolean()
    {
        Assert.Equal(true, Json("true").ConvertShellObject(DataType.Boolean));
        Assert.Equal(false, Json("false").ConvertShellObject(DataType.Boolean));
    }

    [Fact]
    public void ShellJson_ObjectToText_ReturnsRawJson()
    {
        var json = Json("{\"id\":\"1\"}");
        var text = Assert.IsType<string>(json.ConvertShellObject(DataType.Text));
        Assert.Contains("\"id\"", text);
    }

    [Fact]
    public void ShellJson_JsonToJson_ReturnsElement()
    {
        var json = Json("[1,2,3]");
        var element = Assert.IsType<JsonElement>(json.ConvertShellObject(DataType.Json));
        Assert.Equal(JsonValueKind.Array, element.ValueKind);
    }

    [Fact]
    public void ShellJson_NonNumericStringToNumber_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Json("\"abc\"").ConvertShellObject(DataType.Number));
    }

    [Fact]
    public void ShellJson_ObjectToBoolean_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Json("{}").ConvertShellObject(DataType.Boolean));
    }

    [Fact]
    public void ShellJson_ObjectToDecimal_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Json("{}").ConvertShellObject(DataType.Decimal));
    }

    [Fact]
    public void ShellIdentifier_Text_ReturnsValue()
    {
        Assert.Equal("abc", new ShellIdentifier("abc").ConvertShellObject(DataType.Text));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    [InlineData("", false)]
    public void ShellIdentifier_ToBoolean(string value, bool expected)
    {
        Assert.Equal(expected, new ShellIdentifier(value).ConvertShellObject(DataType.Boolean));
    }

    [Fact]
    public void ShellIdentifier_ToNumber()
    {
        Assert.Equal(7, new ShellIdentifier("7").ConvertShellObject(DataType.Number));
    }

    [Fact]
    public void ShellIdentifier_ToDecimal()
    {
        Assert.Equal(25d, new ShellIdentifier("25").ConvertShellObject(DataType.Decimal));
    }

    [Fact]
    public void ShellIdentifier_ToJson_ParsesValue()
    {
        var element = Assert.IsType<JsonElement>(new ShellIdentifier("{\"a\":1}").ConvertShellObject(DataType.Json));
        Assert.Equal(1, element.GetProperty("a").GetInt32());
    }

    [Fact]
    public void ShellIdentifier_InvalidBoolean_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new ShellIdentifier("maybe").ConvertShellObject(DataType.Boolean));
    }

    [Fact]
    public void ShellIdentifier_InvalidNumber_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new ShellIdentifier("abc").ConvertShellObject(DataType.Number));
    }

    [Fact]
    public void ShellIdentifier_InvalidDecimal_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new ShellIdentifier("abc").ConvertShellObject(DataType.Decimal));
    }

    [Fact]
    public void ShellIdentifier_InvalidJson_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new ShellIdentifier("{not json").ConvertShellObject(DataType.Json));
    }

    [Fact]
    public void ShellSequence_ToJson_ReturnsArray()
    {
        var seq = SequenceOf("1", "2", "3");
        var element = Assert.IsType<JsonElement>(seq.ConvertShellObject(DataType.Json));
        Assert.Equal(JsonValueKind.Array, element.ValueKind);
        Assert.Equal(3, element.GetArrayLength());
    }

    [Fact]
    public void ShellSequence_ToText_ReturnsRawArrayJson()
    {
        var seq = SequenceOf("1", "2");
        var text = Assert.IsType<string>(seq.ConvertShellObject(DataType.Text));
        Assert.StartsWith("[", text);
        Assert.EndsWith("]", text);
    }

    private static ShellSequence SequenceOf(params string[] rawElements)
    {
        var elements = new List<JsonElement>();
        foreach (var raw in rawElements)
        {
            using var doc = JsonDocument.Parse(raw);
            elements.Add(doc.RootElement.Clone());
        }

        return new ShellSequence(elements);
    }
}
