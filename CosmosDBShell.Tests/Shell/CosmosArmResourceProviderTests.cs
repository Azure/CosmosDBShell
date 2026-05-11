// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using System;
using System.Threading;
using System.Threading.Tasks;

using Azure.Core;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Selector-style tests for <see cref="CosmosArmResourceProvider.TryCreateContextAsync"/>.
/// Mirrors the matrix coverage added in vscode-cosmosdb PR #3016 for `getControlPlane`,
/// scoped to the branches that do not require network calls (no real ARM client, no
/// subscription enumeration). The explicit-coordinates and discovery branches both
/// reach out to ARM and are exercised by integration tests instead.
/// </summary>
public class CosmosArmResourceProviderTests
{
    private static readonly Uri TestEndpoint = new("https://example.documents.azure.com:443/");

    [Fact]
    public async Task TryCreateContextAsync_NullCredential_ReturnsNull()
    {
        var context = await CosmosArmResourceProvider.TryCreateContextAsync(
            credential: null,
            dataPlaneEndpoint: TestEndpoint,
            subscriptionId: null,
            resourceGroupName: null,
            accountName: null,
            token: TestContext.Current.CancellationToken);

        Assert.Null(context);
    }

    [Fact]
    public async Task TryCreateContextAsync_NullCredential_IgnoresCoordinates()
    {
        // Account-key / static-token / emulator paths pass credential = null; any coordinates
        // supplied alongside them must not throw — the facade is expected to silently fall
        // back to the data plane.
        var context = await CosmosArmResourceProvider.TryCreateContextAsync(
            credential: null,
            dataPlaneEndpoint: TestEndpoint,
            subscriptionId: "sub",
            resourceGroupName: "rg",
            accountName: "acc",
            token: TestContext.Current.CancellationToken);

        Assert.Null(context);
    }

    [Theory]
    [InlineData("sub", null, null)]
    [InlineData(null, "rg", null)]
    [InlineData(null, null, "acc")]
    [InlineData("sub", "rg", null)]
    [InlineData("sub", null, "acc")]
    [InlineData(null, "rg", "acc")]
    [InlineData(" ", "rg", "acc")]
    public async Task TryCreateContextAsync_PartialCoordinates_Throws(string? subscription, string? resourceGroup, string? account)
    {
        var ex = await Assert.ThrowsAsync<ShellException>(() => CosmosArmResourceProvider.TryCreateContextAsync(
            credential: new NoOpTokenCredential(),
            dataPlaneEndpoint: TestEndpoint,
            subscriptionId: subscription,
            resourceGroupName: resourceGroup,
            accountName: account,
            token: TestContext.Current.CancellationToken));

        Assert.Equal(MessageService.GetString("error-arm-context-incomplete"), ex.Message);
    }

    [Fact]
    public void ArmContextLocalizationKeys_AreDefined()
    {
        // Guards against typos in the localization references used by TryCreateContextAsync
        // and the resource facade.
        Assert.False(string.IsNullOrWhiteSpace(MessageService.GetString("error-arm-context-incomplete")));
        Assert.False(string.IsNullOrWhiteSpace(MessageService.GetString("error-arm-context-ambiguous")));
        Assert.False(string.IsNullOrWhiteSpace(MessageService.GetString("error-arm-context-required")));
    }

    private sealed class NoOpTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("This credential is for static validation paths only.");
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("This credential is for static validation paths only.");
        }
    }
}
