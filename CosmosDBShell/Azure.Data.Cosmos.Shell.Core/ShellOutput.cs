// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Spectre.Console;
using Spectre.Console.Rendering;

internal enum ShellMessageKind
{
    Information,
    Progress,
    Warning,
    Error,
    RequiredInstruction,
    Result,
}

internal sealed class ShellOutput(
    Func<bool> quiet,
    Func<bool> stdio,
    Func<bool> machineMode,
    Func<TextWriter>? standardOutput = null,
    Func<TextWriter>? standardError = null,
    Func<IAnsiConsole>? console = null)
{
    private readonly object consoleCacheLock = new();

    private TextWriter? cachedPlainWriter;

    private IAnsiConsole? cachedPlainConsole;

    // Early failures and terminal initialization diagnostics always use stderr.
    internal static ShellOutput StandardError { get; } = new(() => false, () => false, () => true);

    /// <summary>
    /// Clears the interactive terminal. Machine mode and MCP stdio have no screen to clear,
    /// and emitting terminal control sequences there would corrupt their output streams.
    /// </summary>
    internal void ClearScreen()
    {
        if (!machineMode() && !stdio())
        {
            (console?.Invoke() ?? AnsiConsole.Console).Clear();
        }
    }

    /// <summary>
    /// Writes the interpreter's final, already formatted command result. Unlike
    /// <see cref="ShellMessageKind.Result"/> presentation, this is the machine-mode
    /// stdout contract; MCP stdio returns results through the protocol instead.
    /// </summary>
    internal void WriteResult(string text)
    {
        if (!stdio())
        {
            (standardOutput?.Invoke() ?? Console.Out).WriteLine(text);
        }
    }

    internal void WriteLine(ShellMessageKind kind, string message = "", params object[] args)
    {
        if (this.ShouldPresent(kind))
        {
            var writer = this.GetWriter(kind);
            if (args.Length == 0)
            {
                writer.WriteLine(message);
            }
            else
            {
                writer.WriteLine(message, args);
            }
        }
    }

    internal void Write(ShellMessageKind kind, string message, params object[] args)
    {
        if (this.ShouldPresent(kind))
        {
            var writer = this.GetWriter(kind);
            if (args.Length == 0)
            {
                writer.Write(message);
            }
            else
            {
                writer.Write(message, args);
            }
        }
    }

    internal void MarkupLine(ShellMessageKind kind, string markup, params object[] args)
    {
        if (this.ShouldPresent(kind))
        {
            var target = this.GetConsole(kind);
            if (args.Length == 0)
            {
                target.MarkupLine(markup);
            }
            else
            {
                target.MarkupLine(markup, args);
            }
        }
    }

    internal void Markup(ShellMessageKind kind, string markup, params object[] args)
    {
        if (this.ShouldPresent(kind))
        {
            var target = this.GetConsole(kind);
            if (args.Length == 0)
            {
                target.Markup(markup);
            }
            else
            {
                target.Markup(markup, args);
            }
        }
    }

    internal void RenderLine(ShellMessageKind kind, string text = "")
    {
        if (this.ShouldPresent(kind))
        {
            this.GetConsole(kind).WriteLine(text);
        }
    }

    internal void Render(ShellMessageKind kind, IRenderable renderable)
    {
        if (this.ShouldPresent(kind))
        {
            this.GetConsole(kind).Write(renderable);
        }
    }

    internal void WriteException(ShellMessageKind kind, Exception exception, ExceptionSettings? settings = null)
    {
        if (this.ShouldPresent(kind))
        {
            this.GetConsole(kind).WriteException(exception, settings ?? new ExceptionSettings());
        }
    }

    private bool ShouldPresent(ShellMessageKind kind)
        => OutputPolicy.ShouldPresent(kind, quiet(), stdio(), machineMode());

    private TextWriter GetWriter(ShellMessageKind kind)
        => OutputPolicy.UseStandardError(kind, machineMode(), stdio())
            ? standardError?.Invoke() ?? Console.Error
            : standardOutput?.Invoke() ?? Console.Out;

    private IAnsiConsole GetConsole(ShellMessageKind kind)
    {
        if (!machineMode() && !OutputPolicy.UseStandardError(kind, machineMode(), stdio()))
        {
            return console?.Invoke() ?? AnsiConsole.Console;
        }

        var writer = this.GetWriter(kind);
        lock (this.consoleCacheLock)
        {
            if (!ReferenceEquals(writer, this.cachedPlainWriter) || this.cachedPlainConsole is null)
            {
                this.cachedPlainWriter = writer;
                this.cachedPlainConsole = AnsiConsole.Create(new AnsiConsoleSettings
                {
                    Ansi = AnsiSupport.No,
                    ColorSystem = ColorSystemSupport.NoColors,
                    Interactive = InteractionSupport.No,
                    Out = new AnsiConsoleOutput(writer),
                });
            }

            return this.cachedPlainConsole;
        }
    }
}
