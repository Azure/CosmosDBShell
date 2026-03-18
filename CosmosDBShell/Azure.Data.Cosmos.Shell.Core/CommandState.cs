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
    /// <summary>
    /// Gets a value indicating whether this <see cref="CommandState"/> represents an error state.
    /// </summary>
    public virtual bool IsError => false;

    /// <summary>
    /// Gets or sets the output format for the command result.
    /// </summary>
    public OutputFormat OutputFormat { get; set; }

    internal ShellObject? Result { get; set; }

    internal bool IsPrinted { get; set; }

    internal bool BreakBlock { get; set; } = false;

    internal bool ContinueBlock { get; set; } = false;

    internal bool ReturnFunc { get; set; } = false;

    internal ShellObject? ReturnValue { get; set; } = null;

    internal void SetFormat(string? outputFormat)
    {
        if (outputFormat == null)
        {
            return;
        }

        if (string.Equals(outputFormat, "csv", StringComparison.OrdinalIgnoreCase))
        {
            this.OutputFormat = OutputFormat.CSV;
        }
        else if (string.Equals(outputFormat, "json", StringComparison.OrdinalIgnoreCase) || string.Equals(outputFormat, "js", StringComparison.OrdinalIgnoreCase))
        {
            this.OutputFormat = OutputFormat.JSon;
        }
        else
        {
            throw new ShellException(MessageService.GetString("error-invalid_output_format", new Dictionary<string, object> { { "format", outputFormat } }));
        }
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
                if (json.ValueKind == JsonValueKind.Object)
                {
                    if (json.TryGetProperty("documents", out var documents) && json.TryGetProperty("queryMetrics", out _))
                    {
                        var table2 = ResultToTable([.. documents.EnumerateArray()]);
                        return table2.ToString();
                    }

                    return ResultToTable([json]).ToString();
                }

                var table = ResultToTable(json.EnumerateArray().ToArray());
                return table.ToString();
            default:
                throw new InvalidOperationException("OutputFormat invalid " + this.OutputFormat);
        }
    }
}
