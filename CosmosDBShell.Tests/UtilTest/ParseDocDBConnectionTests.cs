// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using CommandLine;
using Azure.Data.Cosmos.Shell.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CosmosShell.Tests.UtilTest;

public class ParseDocDBConnectionTests
{
    [Fact]
    public void Test_EmptyDatabase()
    {
        ParsedDocDBConnectionString.TryParseDocDBConnectionString("AccountEndpoint=https://abcdef.documents.azure.com:443/;AccountKey=abcdef==", out var parsedCS);

        Assert.Equal("https://abcdef.documents.azure.com:443/", parsedCS?.Endpoint);
        Assert.Equal("abcdef==", parsedCS?.MasterKey);
        Assert.Null(parsedCS?.DatabaseName);
    }

    [Fact]
    public void Test_LeadingSpace()
    {
        ParsedDocDBConnectionString.TryParseDocDBConnectionString("    AccountEndpoint=https://abcdef.documents.azure.com:443/;AccountKey=abcdef==", out var parsedCS);

        Assert.Equal("https://abcdef.documents.azure.com:443/", parsedCS?.Endpoint);
        Assert.Equal("abcdef==", parsedCS?.MasterKey);
        Assert.Null(parsedCS?.DatabaseName);
    }

    [Fact]
    public void Test_LeadingSpace2()
    {
        ParsedDocDBConnectionString.TryParseDocDBConnectionString("AccountEndpoint=https://abcdef.documents.azure.com:443/;    AccountKey=abcdef==", out var parsedCS);

        Assert.Equal("https://abcdef.documents.azure.com:443/", parsedCS?.Endpoint);
        Assert.Equal("abcdef==", parsedCS?.MasterKey);
        Assert.Null(parsedCS?.DatabaseName);
    }

    [Fact]
    public void Test_TrailingSemicolon()
    {
        ParsedDocDBConnectionString.TryParseDocDBConnectionString("AccountEndpoint=https://abcdef.documents.azure.com:443/;    AccountKey=abcdef==", out var parsedCS);

        Assert.Equal("https://abcdef.documents.azure.com:443/", parsedCS?.Endpoint);
        Assert.Equal("abcdef==", parsedCS?.MasterKey);
        Assert.Null(parsedCS?.DatabaseName);
    }

    [Fact]
    public void Test_AccountKeyFirst()
    {
        ParsedDocDBConnectionString.TryParseDocDBConnectionString("AccountKey=abcdef==;AccountEndpoint=https://abcdef.documents.azure.com:443/", out var parsedCS);

        Assert.Equal("https://abcdef.documents.azure.com:443/", parsedCS?.Endpoint);
        Assert.Equal("abcdef==", parsedCS?.MasterKey);
        Assert.Null(parsedCS?.DatabaseName);
    }


    [Fact]
    public void Test_AccountKeyFirstTralingSemicolon()
    {
        ParsedDocDBConnectionString.TryParseDocDBConnectionString("AccountKey=abcdef==;AccountEndpoint=https://abcdef.documents.azure.com:443/;", out var parsedCS);

        Assert.Equal("https://abcdef.documents.azure.com:443/", parsedCS?.Endpoint);
        Assert.Equal("abcdef==", parsedCS?.MasterKey);
        Assert.Null(parsedCS?.DatabaseName);
    }

    [Fact]
    public void Test_LowerCase()
    {
        ParsedDocDBConnectionString.TryParseDocDBConnectionString("accountendpoint=https://abcdef.documents.azure.com:443/;accountkey=abcdef==", out var parsedCS);

        Assert.Equal("https://abcdef.documents.azure.com:443/", parsedCS?.Endpoint);
        Assert.Equal("abcdef==", parsedCS?.MasterKey);
        Assert.Null(parsedCS?.DatabaseName);
    }

    [Fact]
    public void TestEmptyDatabaseKey()
    {
        ParsedDocDBConnectionString.TryParseDocDBConnectionString("AccountKey=abcdef==;AccountEndpoint=https://abcdef.documents.azure.com:443/;Database=", out var parsedCS);
        Assert.Equal("https://abcdef.documents.azure.com:443/", parsedCS?.Endpoint);
        Assert.Equal("abcdef==", parsedCS?.MasterKey);
        Assert.Null(parsedCS?.DatabaseName);
    }

    [Fact]
    public void TestEmptyDatabaseKeyTrailingSemicolon()
    {
        ParsedDocDBConnectionString.TryParseDocDBConnectionString("AccountKey=abcdef==;AccountEndpoint=https://abcdef.documents.azure.com:443/;Database=;", out var parsedCS);
        Assert.Equal("https://abcdef.documents.azure.com:443/", parsedCS?.Endpoint);
        Assert.Equal("abcdef==", parsedCS?.MasterKey);
        Assert.Null(parsedCS?.DatabaseName);
    }

    [Fact]
    public void TestEmulator()
    {
        string emulatorPassword = "emulatorPW";
        ParsedDocDBConnectionString.TryParseDocDBConnectionString($"AccountKey={emulatorPassword};AccountEndpoint=https://abcdef.documents.azure.com:443/;Database=;", out var parsedCS);
        Assert.Equal("https://abcdef.documents.azure.com:443/", parsedCS?.Endpoint);
        Assert.Equal(emulatorPassword, parsedCS?.MasterKey);
        Assert.Null(parsedCS?.DatabaseName);
    }

    [Fact]
    public void TestWithOtherProperties()
    {
        ParsedDocDBConnectionString.TryParseDocDBConnectionString("AccountKey=abcdef==;AccountEndpoint=https://abcdef.documents.azure.com:443/;Other=FooBar;Database=;", out var parsedCS);
        Assert.Equal("https://abcdef.documents.azure.com:443/", parsedCS?.Endpoint);
        Assert.Equal("abcdef==", parsedCS?.MasterKey);
        Assert.Null(parsedCS?.DatabaseName);
    }


    [Fact]
    public void TestWithDBName()
    {
        ParsedDocDBConnectionString.TryParseDocDBConnectionString("AccountEndpoint=https://abcdef.documents.azure.com:443/;AccountKey=abcdef==;Database=abcd", out var parsedCS);
        Assert.Equal("https://abcdef.documents.azure.com:443/", parsedCS?.Endpoint);
        Assert.Equal("abcdef==", parsedCS?.MasterKey);
        Assert.Equal("abcd", parsedCS?.DatabaseName);
    }

    [Fact]
    public void TestDBWithTrailingSemicolon()
    {
        ParsedDocDBConnectionString.TryParseDocDBConnectionString("AccountEndpoint=https://abcdef.documents.azure.com:443/;AccountKey=abcdef==;Database=abcd;", out var parsedCS);
        Assert.Equal("https://abcdef.documents.azure.com:443/", parsedCS?.Endpoint);
        Assert.Equal("abcdef==", parsedCS?.MasterKey);
        Assert.Equal("abcd", parsedCS?.DatabaseName);
    }

    [Fact]
    public void TestWithMiddleDBName()
    {
        ParsedDocDBConnectionString.TryParseDocDBConnectionString("AccountEndpoint=https://abcdef.documents.azure.com:443/;Database=abcd;AccountKey=abcdef==", out var parsedCS);
        Assert.Equal("https://abcdef.documents.azure.com:443/", parsedCS?.Endpoint);
        Assert.Equal("abcdef==", parsedCS?.MasterKey);
        Assert.Equal("abcd", parsedCS?.DatabaseName);

    }

    [Fact]
    public void TestDBWithTrailingAndDBFirst()
    {
        ParsedDocDBConnectionString.TryParseDocDBConnectionString("Database=abcd;AccountEndpoint=https://abcdef.documents.azure.com:443/;AccountKey=abcdef==;", out var parsedCS);
        Assert.Equal("https://abcdef.documents.azure.com:443/", parsedCS?.Endpoint);
        Assert.Equal("abcdef==", parsedCS?.MasterKey);
        Assert.Equal("abcd", parsedCS?.DatabaseName);
    }

    [Fact]
    public void TestDBWithOther()
    {
        ParsedDocDBConnectionString.TryParseDocDBConnectionString("AccountEndpoint=https://abcdef.documents.azure.com:443/;AccountKey=abcdef==;Other=3lvccl;Database=abcd", out var parsedCS);
        Assert.Equal("https://abcdef.documents.azure.com:443/", parsedCS?.Endpoint);
        Assert.Equal("abcdef==", parsedCS?.MasterKey);
        Assert.Equal("abcd", parsedCS?.DatabaseName);
    }

    [Fact]
    public void TestIsPlainUrl_Https()
    {
        Assert.True(ParsedDocDBConnectionString.IsPlainUrl("https://localhost:8081"));
    }

    [Fact]
    public void TestIsPlainUrl_Http()
    {
        Assert.True(ParsedDocDBConnectionString.IsPlainUrl("http://localhost:8081"));
    }

    [Fact]
    public void TestIsPlainUrl_ConnectionString()
    {
        Assert.False(ParsedDocDBConnectionString.IsPlainUrl("AccountEndpoint=https://localhost:8081/;AccountKey=abc"));
    }

    [Fact]
    public void TestIsPlainUrl_Null()
    {
        Assert.False(ParsedDocDBConnectionString.IsPlainUrl(null));
    }

    [Fact]
    public void TestIsPlainUrl_Empty()
    {
        Assert.False(ParsedDocDBConnectionString.IsPlainUrl(string.Empty));
    }

    [Fact]
    public void TestBuildEmulatorConnectionString()
    {
        var result = ParsedDocDBConnectionString.BuildEmulatorConnectionString("https://localhost:8081/");
        Assert.Contains("AccountEndpoint=https://localhost:8081/", result);
        Assert.Contains($"AccountKey={ParsedDocDBConnectionString.EmulatorAccountKey}", result);

        // The result should be parseable as a valid connection string
        Assert.True(ParsedDocDBConnectionString.TryParseDocDBConnectionString(result, out var parsed));
        Assert.Equal("https://localhost:8081/", parsed?.Endpoint);
        Assert.Equal(ParsedDocDBConnectionString.EmulatorAccountKey, parsed?.MasterKey);
    }

    [Fact]
    public void TestEmulatorKeyConstant()
    {
        // The well-known Cosmos DB emulator key should be a non-empty base64 string
        Assert.False(string.IsNullOrWhiteSpace(ParsedDocDBConnectionString.EmulatorAccountKey));
        Assert.EndsWith("==", ParsedDocDBConnectionString.EmulatorAccountKey);
    }

    [Fact]
    public void TestIsLocalEmulatorEndpoint_PlainUrl()
    {
        Assert.True(ParsedDocDBConnectionString.IsLocalEmulatorEndpoint("https://localhost:8081"));
        Assert.True(ParsedDocDBConnectionString.IsLocalEmulatorEndpoint("https://127.0.0.1:8081"));
    }

    [Fact]
    public void TestIsLocalEmulatorEndpoint_ConnectionStringWithoutKey()
    {
        Assert.True(ParsedDocDBConnectionString.IsLocalEmulatorEndpoint("AccountEndpoint=https://localhost:8081/;"));
    }

    [Fact]
    public void TestPlainLocalhostUrlNotParsedAsConnectionString()
    {
        // A plain URL should NOT parse as a connection string (no AccountEndpoint= prefix)
        Assert.False(ParsedDocDBConnectionString.TryParseDocDBConnectionString("https://localhost:8081", out _));
    }
}