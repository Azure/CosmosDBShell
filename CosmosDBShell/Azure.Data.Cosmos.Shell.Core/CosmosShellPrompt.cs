// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Azure.Data.Cosmos.Shell.States;

using RadLine;

using Spectre.Console;

internal class CosmosShellPrompt(ShellInterpreter shell) : ILineEditorPrompt, IStateVisitor<string, object?>
{
    internal const string PromptText = "CS ";
    private static readonly (Markup Markup, int Margin) ContinuationPrompt = (new Markup("[grey]...[/]"), 1);
    private readonly ShellInterpreter shell = shell ?? throw new ArgumentNullException(nameof(shell));
    private Markup prompt = new(string.Empty);
    private State? oldState;

    internal bool InContinuation { get; set; }

    (Markup Markup, int Margin) ILineEditorPrompt.GetPrompt(ILineEditorState state, int line)
    {
        // Show the continuation marker on any non-first row of the editor buffer.
        // This covers two cases: (1) the user is typing a parse-driven continuation
        // line that RadLine renders as row > 0, and (2) a multi-line entry was
        // recalled from history and RadLine is rendering its later rows. The
        // explicit InContinuation flag handles the third case where we start a
        // fresh ReadLine for the next line of a multi-line entry (so the new
        // row 0 still gets the continuation marker).
        if (line > 0 || this.InContinuation)
        {
            return ContinuationPrompt;
        }

        if (this.oldState != this.shell.State)
        {
            this.oldState = this.shell.State;
            this.prompt = new Markup(this.GetPromptString());
        }

        return (this.prompt, 1);
    }

    public string GetPromptString()
    {
        this.oldState ??= this.shell.State;
#pragma warning disable VSTHRD002 // Synchronously waiting - required by ILineEditorPrompt interface
        return this.oldState.AcceptAsync(this, null, default).Result ?? string.Empty;
#pragma warning restore VSTHRD002
    }

    Task<string> IStateVisitor<string, object?>.VisitConnectedStateAsync(ConnectedState state, object? data, CancellationToken token)
    {
        return Task.FromResult(Theme.ConnectedStatePromt(PromptText + ">"));
    }

    Task<string> IStateVisitor<string, object?>.VisitContainerStateAsync(ContainerState state, object? data, CancellationToken token)
    {
        var db = Markup.Escape(state.DatabaseName);
        var cn = Markup.Escape(state.ContainerName);
        return Task.FromResult($"{Theme.ConnectedStatePromt(PromptText)} {Theme.DatabaseNamePromt(db)}/{Theme.ContainerNamePromt(cn)} {Theme.ConnectedStatePromt(">")}");
    }

    Task<string> IStateVisitor<string, object?>.VisitDatabaseStateAsync(DatabaseState state, object? data, CancellationToken token)
    {
        var db = Markup.Escape(state.DatabaseName);
        return Task.FromResult($"{Theme.ConnectedStatePromt(PromptText)} {Theme.DatabaseNamePromt(db)} {Theme.ConnectedStatePromt(">")}");
    }

    Task<string> IStateVisitor<string, object?>.VisitDisconnectedStateAsync(DisconnectedState state, object? data, CancellationToken token)
    {
        return Task.FromResult(Theme.DisconnectedStatePromt(PromptText + ">"));
    }
}