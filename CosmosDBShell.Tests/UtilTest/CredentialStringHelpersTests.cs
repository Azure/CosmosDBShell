// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using Azure.Data.Cosmos.Shell.Util;

namespace CosmosShell.Tests.UtilTest;

public class CredentialStringHelpersTests
{
    [Fact]
    public void TryParseAccountKey_WithKeyPrefix_ReturnsKey()
    {
        var result = CredentialStringHelpers.TryParseAccountKey("key=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM==", out var key);

        Assert.True(result);
        Assert.Equal("C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM==", key);
    }

    [Fact]
    public void TryParseAccountKey_WithKeyPrefixUppercase_ReturnsKey()
    {
        var result = CredentialStringHelpers.TryParseAccountKey("KEY=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM==", out var key);

        Assert.True(result);
        Assert.Equal("C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM==", key);
    }

    [Fact]
    public void TryParseAccountKey_RawBase64Key_ReturnsKey()
    {
        var result = CredentialStringHelpers.TryParseAccountKey("C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM==", out var key);

        Assert.True(result);
        Assert.Equal("C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM==", key);
    }

    [Fact]
    public void TryParseAccountKey_Null_ReturnsFalse()
    {
        var result = CredentialStringHelpers.TryParseAccountKey(null, out var key);

        Assert.False(result);
        Assert.Equal(string.Empty, key);
    }

    [Fact]
    public void TryParseAccountKey_Empty_ReturnsFalse()
    {
        var result = CredentialStringHelpers.TryParseAccountKey(string.Empty, out var key);

        Assert.False(result);
        Assert.Equal(string.Empty, key);
    }

    [Fact]
    public void TryParseAccountKey_KeyPrefixEmptyValue_ReturnsFalse()
    {
        var result = CredentialStringHelpers.TryParseAccountKey("key=", out var key);

        Assert.False(result);
    }
}
