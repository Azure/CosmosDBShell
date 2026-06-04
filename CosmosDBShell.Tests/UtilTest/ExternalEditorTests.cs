// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.UtilTest;

using Azure.Data.Cosmos.Shell.Util;

// Several tests below mutate process-wide environment variables (VISUAL/EDITOR).
// xUnit parallelizes across test classes, so this collection disables
// parallelization to prevent races with other tests that read those variables.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ExternalEditorTestCollection
{
    public const string Name = "External editor environment tests";
}

[Collection(ExternalEditorTestCollection.Name)]
public class ExternalEditorTests
{
    [Fact]
    public void Resolve_ExplicitEditor_TakesPrecedence()
    {
        var invocation = ExternalEditor.Resolve("code --wait");

        Assert.NotNull(invocation);
        Assert.Equal("code", invocation!.FileName);
        Assert.Equal("--wait", invocation.PrefixArgs);
    }

    [Fact]
    public void Resolve_QuotedExecutablePath_IsParsedAsSingleFileName()
    {
        var invocation = ExternalEditor.Resolve("\"C:\\Program Files\\App\\editor.exe\" --wait");

        Assert.NotNull(invocation);
        Assert.Equal("C:\\Program Files\\App\\editor.exe", invocation!.FileName);
        Assert.Equal("--wait", invocation.PrefixArgs);
    }

    [Fact]
    public void Resolve_FallsBackToPlatformDefault_WhenNothingConfigured()
    {
        var visual = Environment.GetEnvironmentVariable("VISUAL");
        var editor = Environment.GetEnvironmentVariable("EDITOR");
        try
        {
            Environment.SetEnvironmentVariable("VISUAL", null);
            Environment.SetEnvironmentVariable("EDITOR", null);

            var invocation = ExternalEditor.Resolve(null);

            Assert.NotNull(invocation);
            var expected = OperatingSystem.IsWindows() ? "notepad" : "nano";
            Assert.Equal(expected, invocation!.FileName);
            Assert.Null(invocation.PrefixArgs);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VISUAL", visual);
            Environment.SetEnvironmentVariable("EDITOR", editor);
        }
    }

    [Fact]
    public void Resolve_UsesEditorEnvironmentVariable_WhenNoExplicitEditor()
    {
        var visual = Environment.GetEnvironmentVariable("VISUAL");
        var editor = Environment.GetEnvironmentVariable("EDITOR");
        try
        {
            Environment.SetEnvironmentVariable("VISUAL", null);
            Environment.SetEnvironmentVariable("EDITOR", "vim");

            var invocation = ExternalEditor.Resolve(null);

            Assert.NotNull(invocation);
            Assert.Equal("vim", invocation!.FileName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VISUAL", visual);
            Environment.SetEnvironmentVariable("EDITOR", editor);
        }
    }

    [Fact]
    public void BuildArguments_QuotesPathWithSpaces()
    {
        var invocation = ExternalEditor.EditorInvocation.Parse("code --wait");

        Assert.NotNull(invocation);
        Assert.Equal("--wait \"my script.csh\"", invocation!.BuildArguments("my script.csh"));
        Assert.Equal("--wait deploy.csh", invocation.BuildArguments("deploy.csh"));
    }

    [Fact]
    public void BuildArguments_NoPrefixArgs_ReturnsPathOnly()
    {
        var invocation = ExternalEditor.EditorInvocation.Parse("nano");

        Assert.NotNull(invocation);
        Assert.Equal("deploy.csh", invocation!.BuildArguments("deploy.csh"));
        Assert.Equal("\"a b.csh\"", invocation.BuildArguments("a b.csh"));
    }

    [Fact]
    public void DisplayName_IncludesPrefixArgs()
    {
        Assert.Equal("code --wait", ExternalEditor.EditorInvocation.Parse("code --wait")!.DisplayName);
        Assert.Equal("nano", ExternalEditor.EditorInvocation.Parse("nano")!.DisplayName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullOrWhitespace_ReturnsNull(string? raw)
    {
        Assert.Null(ExternalEditor.EditorInvocation.Parse(raw));
    }
}
