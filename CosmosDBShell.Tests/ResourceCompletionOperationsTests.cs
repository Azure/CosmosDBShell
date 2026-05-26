// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.States;
using ModelContextProtocol.Protocol;

public class ResourceCompletionOperationsTests
{
    [Fact]
    public void Templates_AreDeclaredForCompletion()
    {
        var uris = ResourceCompletionOperations.Templates.Select(t => t.UriTemplate).ToHashSet();

        Assert.Contains(ResourceCompletionOperations.DatabaseContainersTemplate, uris);
        Assert.Contains(ResourceCompletionOperations.ContainerIndexingPolicyTemplate, uris);

        foreach (var template in ResourceCompletionOperations.Templates)
        {
            Assert.False(string.IsNullOrWhiteSpace(template.Name));
            Assert.False(string.IsNullOrWhiteSpace(template.Description));
            Assert.Equal("application/json", template.MimeType);
        }
    }

    [Fact]
    public async Task CompleteAsync_WhenRefIsNotResourceTemplate_ReturnsEmpty()
    {
        var parameters = new CompleteRequestParams
        {
            Ref = new PromptReference { Name = "something" },
            Argument = new Argument { Name = "database", Value = string.Empty },
        };

        var result = await ResourceCompletionOperations.CompleteAsync(parameters, CancellationToken.None);

        Assert.Empty(result.Completion.Values);
        Assert.Equal(0, result.Completion.Total);
        Assert.False(result.Completion.HasMore);
    }

    [Fact]
    public async Task CompleteAsync_WhenTemplateIsUnknown_ReturnsEmpty()
    {
        var parameters = new CompleteRequestParams
        {
            Ref = new ResourceTemplateReference { Uri = "cosmos://does/not/exist/{x}" },
            Argument = new Argument { Name = "database", Value = string.Empty },
        };

        var result = await ResourceCompletionOperations.CompleteAsync(parameters, CancellationToken.None);

        Assert.Empty(result.Completion.Values);
    }

    [Fact]
    public async Task CompleteAsync_WhenShellIsDisconnected_ReturnsEmpty()
    {
        var originalState = ShellInterpreter.Instance.State;
        ShellInterpreter.Instance.State = new DisconnectedState();
        try
        {
            var parameters = new CompleteRequestParams
            {
                Ref = new ResourceTemplateReference { Uri = ResourceCompletionOperations.DatabaseContainersTemplate },
                Argument = new Argument { Name = "database", Value = string.Empty },
            };

            var result = await ResourceCompletionOperations.CompleteAsync(parameters, CancellationToken.None);

            Assert.Empty(result.Completion.Values);
        }
        finally
        {
            ShellInterpreter.Instance.State = originalState;
        }
    }

    [Fact]
    public async Task CompleteAsync_WhenArgumentNameIsUnsupported_ReturnsEmpty()
    {
        var parameters = new CompleteRequestParams
        {
            Ref = new ResourceTemplateReference { Uri = ResourceCompletionOperations.DatabaseContainersTemplate },
            Argument = new Argument { Name = "unknown", Value = string.Empty },
        };

        var result = await ResourceCompletionOperations.CompleteAsync(parameters, CancellationToken.None);

        Assert.Empty(result.Completion.Values);
    }
}
