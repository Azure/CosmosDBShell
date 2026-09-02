// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Text;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Represents the state of a command in the Cosmos Shell, including output formatting and result handling.
/// </summary>
public partial class CommandState
{
    private OutputFormat outputFormat;

    /// <summary>
    /// Gets a value indicating whether this <see cref="CommandState"/> represents an error state.
    /// </summary>
    public virtual bool IsError => false;

    /// <summary>
    /// Gets the exit code for the command state. Default is 0.
    /// </summary>
    public virtual int ExitCode => 0;

    /// <summary>
    /// Gets or sets the output format for the command result. Assigning a value marks the
    /// format as explicitly chosen so <see cref="ShellInterpreter.PrintState"/> does not
    /// overwrite it with the session default.
    /// </summary>
    public OutputFormat OutputFormat
    {
        get => this.outputFormat;
        set
        {
            this.outputFormat = value;
            this.OutputFormatExplicitlySet = true;
        }
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="OutputFormat"/> was explicitly set by a
    /// command (via a per-command <c>--format</c> option or direct assignment). When false,
    /// <see cref="ShellInterpreter.PrintState"/> applies the session default format.
    /// </summary>
    internal bool OutputFormatExplicitlySet { get; private set; }

    internal ShellObject? Result { get; set; }

    internal bool OutputRendered { get; set; }

    /// <summary>
    /// Gets or sets an optional delegate that renders the result for a human on an
    /// interactive terminal. When set and the effective format is <see cref="OutputFormat.User"/>,
    /// <see cref="ShellInterpreter.PrintState"/> invokes it instead of printing raw JSON.
    /// It is never invoked for redirected, piped, or machine-mode output.
    /// </summary>
    internal Action? RenderUser { get; set; }

    /// <summary>
    /// Gets or sets an optional delegate that supplies a <see cref="TabularData"/> for the
    /// CSV and Table output formats. When set, <see cref="GenerateOutputText"/> renders that
    /// table (as CSV or a bordered grid depending on <see cref="OutputFormat"/>) instead of
    /// deriving columns from the JSON result. This lets a command control headers and row
    /// shape while sharing one source for both machine formats.
    /// </summary>
    internal Func<TabularData>? RenderTabular { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this result is a resumable page.
    /// </summary>
    internal bool IsPage { get; set; }

    /// <summary>
    /// Gets or sets the token for retrieving the next page, or <see langword="null"/> when the result is exhausted.
    /// </summary>
    internal string? ContinuationToken { get; set; }

    /// <summary>
    /// Gets or sets the Cosmos DB request charge (in RUs) consumed by the command, when applicable.
    /// Data-plane commands set this so consumers such as the MCP structured payload can report cost uniformly.
    /// </summary>
    internal double? RequestCharge { get; set; }

    internal bool BreakBlock { get; set; } = false;

    internal bool ContinueBlock { get; set; } = false;

    internal bool ReturnFunc { get; set; } = false;

    internal ShellObject? ReturnValue { get; set; } = null;

    internal void ResetOutputFormat()
    {
        this.outputFormat = default;
        this.OutputFormatExplicitlySet = false;
    }

    internal void SetFormat(string? outputFormat)
    {
        if (outputFormat == null)
        {
            return;
        }

        if (OutputFormats.TryParse(outputFormat, out var parsed))
        {
            this.OutputFormat = parsed;
            return;
        }

        throw new ArgumentException(MessageService.GetString("error-invalid_output_format", new Dictionary<string, object> { { "format", outputFormat } }));
    }

    internal string GenerateOutputText()
    {
        if (this.Result == null)
        {
            return string.Empty;
        }

        var evaluatedResult = this.Result.ConvertShellObject(DataType.Json);
        if (evaluatedResult == null)
        {
            throw new InvalidOperationException("Output result evaluation returned null");
        }

        var json = (JsonElement)evaluatedResult;
        switch (this.OutputFormat)
        {
            case OutputFormat.User:
            case OutputFormat.JSon:
                {
                    var options = new JsonWriterOptions
                    {
                        Indented = true,
                    };
                    using var stream = new MemoryStream();
                    using var writer = new Utf8JsonWriter(stream, options);

                    json.WriteTo(writer);

                    writer.Flush();
                    return Encoding.UTF8.GetString(stream.ToArray());
                }

            case OutputFormat.CSV:
                if (this.RenderTabular is { } csvProvider)
                {
                    return FromTabular(csvProvider()).ToString();
                }

                if (json.ValueKind == JsonValueKind.Object)
                {
                    if (TryGetListProperty(json, out var csvList))
                    {
                        return ResultToTable([.. csvList.EnumerateArray()]).ToString();
                    }

                    return ResultToTable([json]).ToString();
                }

                var table = ResultToTable(json.EnumerateArray().ToArray());
                return table.ToString();
            case OutputFormat.Table:
                if (this.RenderTabular is { } tableProvider)
                {
                    return FromTabular(tableProvider()).ToGridString();
                }

                if (json.ValueKind == JsonValueKind.Object)
                {
                    if (TryGetListProperty(json, out var tableList))
                    {
                        return ResultToTable([.. tableList.EnumerateArray()]).ToGridString();
                    }

                    return ResultToTable([json]).ToGridString();
                }

                return ResultToTable(json.EnumerateArray().ToArray()).ToGridString();
            default:
                throw new InvalidOperationException("OutputFormat invalid " + this.OutputFormat);
        }
    }
}
