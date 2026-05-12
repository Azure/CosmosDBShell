//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Parser;
using global::Azure.Data.Cosmos.Shell.Core;
using Spectre.Console;

[CosmosCommand("ftab")]
[CosmosExample("query \"SELECT c.id, c.name FROM c\" | ftab", Description = "Display query results in tabular format")]
[CosmosExample("query \"SELECT c.id, c.name FROM c\" | ftab -f name,id -take 5", Description = "Select columns and limit rendered rows")]
[CosmosExample("query \"SELECT c.id, c.name FROM c\" | ftab -sort name:desc", Description = "Sort rows by a field before rendering")]
[CosmosExample("query \"SELECT c.type, c.name FROM c\" | ftab -colorize type:error:red", Description = "Colorize matching cells in terminal output")]
[CosmosExample("query \"SELECT c.id, c.name FROM c\" | ftab -format markdown", Description = "Render table output as markdown")]
[CosmosExample("query \"SELECT c.id, c.name FROM c\" | ftab -format html > table.html", Description = "Render table output as HTML")]
[CosmosExample("ls | ftab", Description = "Display list results as a table")]
internal class FtabCommand : CosmosCommand
{
    private static readonly JsonSerializerOptions JsonSerializerOptionsIndented = new()
    {
        WriteIndented = true,
    };

    internal enum FtabOutputFormat
    {
        Default,
        Markdown,
        Html,
    }

    [CosmosOption("fields", "f")]
    public string? Fields { get; init; }

    [CosmosOption("take")]
    public int? Take { get; init; }

    [CosmosOption("sort")]
    public string? Sort { get; init; }

    [CosmosOption("colorize")]
    public string? Colorize { get; init; }

    [CosmosOption("format")]
    public string? Format { get; init; }

    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        if (commandState.Result == null)
        {
            throw new CommandException("ftab", "The ftab command requires piped JSON input.");
        }

        if (this.Take < 0)
        {
            throw new CommandException("ftab", "The -take option must be zero or greater.");
        }

        var evaluated = commandState.Result.ConvertShellObject(DataType.Json);
        if (evaluated is not JsonElement json)
        {
            throw new CommandException("ftab", "The ftab command can only render JSON input.");
        }

        var rows = NormalizeRows(json);
        var fields = ParseFields(this.Fields);
        var format = ParseFormat(this.Format);
        var colorizeRules = ParseColorizeRules(this.Colorize);
        var sortedRows = SortRows(rows, this.Sort);
        var tableModel = BuildTable(sortedRows, fields, this.Take);

        if (format != FtabOutputFormat.Default && colorizeRules.Count > 0)
        {
            throw new CommandException("ftab", "The -colorize option is only supported with -format default.");
        }

        if (format == FtabOutputFormat.Default && string.IsNullOrEmpty(shell.StdOutRedirect))
        {
            RenderSpectreTable(tableModel.Headers, tableModel.Rows, colorizeRules);
        }
        else
        {
            var output = format switch
            {
                FtabOutputFormat.Markdown => RenderMarkdown(tableModel.Headers, tableModel.Rows),
                FtabOutputFormat.Html => RenderHtml(tableModel.Headers, tableModel.Rows),
                _ => RenderPlainText(tableModel.Headers, tableModel.Rows),
            };

            if (!string.IsNullOrEmpty(shell.StdOutRedirect))
            {
                shell.Redirect(output);
            }
            else
            {
                ShellInterpreter.WriteLine(output);
            }
        }

        commandState.IsPrinted = true;
        return Task.FromResult(commandState);
    }

    internal static FtabOutputFormat ParseFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format) || string.Equals(format, "default", StringComparison.OrdinalIgnoreCase))
        {
            return FtabOutputFormat.Default;
        }

        if (string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase) || string.Equals(format, "md", StringComparison.OrdinalIgnoreCase))
        {
            return FtabOutputFormat.Markdown;
        }

        if (string.Equals(format, "html", StringComparison.OrdinalIgnoreCase))
        {
            return FtabOutputFormat.Html;
        }

        throw new CommandException("ftab", "The -format option must be one of: default, markdown, html.");
    }

    internal static IReadOnlyList<ColorizeRule> ParseColorizeRules(string? colorize)
    {
        if (string.IsNullOrWhiteSpace(colorize))
        {
            return Array.Empty<ColorizeRule>();
        }

        var rules = new List<ColorizeRule>();
        foreach (var ruleText in colorize.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = ruleText.Split(':', 3, StringSplitOptions.TrimEntries);
            if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[2]))
            {
                throw new CommandException("ftab", "The -colorize option must use the format 'field:value:style' with multiple rules separated by ';'.");
            }

            rules.Add(new ColorizeRule(parts[0], parts[1], parts[2]));
        }

        return rules;
    }

    internal static (string Field, bool Descending) ParseSort(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return (string.Empty, false);
        }

        var parts = sort.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
        {
            throw new CommandException("ftab", "The -sort option must be in the form 'field' or 'field:asc|desc'.");
        }

        var descending = false;
        if (parts.Length == 2)
        {
            if (string.Equals(parts[1], "asc", StringComparison.OrdinalIgnoreCase))
            {
                descending = false;
            }
            else if (string.Equals(parts[1], "desc", StringComparison.OrdinalIgnoreCase))
            {
                descending = true;
            }
            else
            {
                throw new CommandException("ftab", "The -sort option direction must be 'asc' or 'desc'.");
            }
        }

        return (parts[0], descending);
    }

    internal static string FormatStyledCell(string header, string cell, IReadOnlyList<ColorizeRule> rules)
    {
        var escaped = Markup.Escape(cell);
        foreach (var rule in rules)
        {
            if (string.Equals(rule.Field, header, StringComparison.Ordinal) && string.Equals(rule.Value, cell, StringComparison.Ordinal))
            {
                return $"[{rule.Style}]{escaped}[/]";
            }
        }

        return escaped;
    }

    internal static string RenderMarkdown(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        sb.Append("| ");
        sb.Append(string.Join(" | ", headers.Select(FormatMarkdownCell)));
        sb.AppendLine(" |");
        sb.Append("| ");
        sb.Append(string.Join(" | ", headers.Select(_ => "---")));
        sb.AppendLine(" |");

        foreach (var row in rows)
        {
            sb.Append("| ");
            sb.Append(string.Join(" | ", row.Select(FormatMarkdownCell)));
            sb.AppendLine(" |");
        }

        return sb.ToString().TrimEnd();
    }

    internal static string RenderHtml(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<table border=\"1\" cellspacing=\"0\" cellpadding=\"4\">");
        sb.AppendLine("  <thead>");
        sb.AppendLine("    <tr>");
        foreach (var header in headers)
        {
            sb.Append("      <th>");
            sb.Append(FormatHtmlCell(header));
            sb.AppendLine("</th>");
        }

        sb.AppendLine("    </tr>");
        sb.AppendLine("  </thead>");
        sb.AppendLine("  <tbody>");
        foreach (var row in rows)
        {
            sb.AppendLine("    <tr>");
            foreach (var cell in row)
            {
                sb.Append("      <td><pre style=\"margin:0\">");
                sb.Append(FormatHtmlCell(cell));
                sb.AppendLine("</pre></td>");
            }

            sb.AppendLine("    </tr>");
        }

        sb.AppendLine("  </tbody>");
        sb.Append("</table>");
        return sb.ToString();
    }

    internal static List<JsonElement> SortRows(List<JsonElement> rows, string? sort)
    {
        var sortSpec = ParseSort(sort);
        if (string.IsNullOrEmpty(sortSpec.Field))
        {
            return rows;
        }

        var rowObjects = rows.All(row => row.ValueKind == JsonValueKind.Object);
        if (!rowObjects)
        {
            if (!string.Equals(sortSpec.Field, "value", StringComparison.Ordinal))
            {
                throw new CommandException("ftab", "The -sort option can only use 'value' when the input rows are not JSON objects.");
            }

            return sortSpec.Descending
                ? rows.OrderByDescending(row => row, JsonElementComparer.Instance).ToList()
                : rows.OrderBy(row => row, JsonElementComparer.Instance).ToList();
        }

        return sortSpec.Descending
            ? rows.OrderByDescending(row => GetSortValue(row, sortSpec.Field), JsonElementComparer.Instance).ToList()
            : rows.OrderBy(row => GetSortValue(row, sortSpec.Field), JsonElementComparer.Instance).ToList();
    }

    private static List<JsonElement> NormalizeRows(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Object)
        {
            if (json.TryGetProperty("documents", out var documents) && documents.ValueKind == JsonValueKind.Array)
            {
                return documents.EnumerateArray().ToList();
            }

            if (json.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                return items.EnumerateArray().ToList();
            }

            return new List<JsonElement> { json };
        }

        if (json.ValueKind == JsonValueKind.Array)
        {
            return json.EnumerateArray().ToList();
        }

        return new List<JsonElement> { json };
    }

    private static List<string>? ParseFields(string? fields)
    {
        if (string.IsNullOrWhiteSpace(fields))
        {
            return null;
        }

        var parsed = fields
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .ToList();

        return parsed.Count == 0 ? null : parsed;
    }

    private static JsonElement GetSortValue(JsonElement row, string field)
    {
        return row.TryGetProperty(field, out var property) ? property : default;
    }

    private static (IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows) BuildTable(List<JsonElement> rows, List<string>? fields, int? take)
    {
        if (take.HasValue)
        {
            rows = rows.Take(take.Value).ToList();
        }

        var rowObjects = rows.All(row => row.ValueKind == JsonValueKind.Object);
        if (fields != null && !rowObjects)
        {
            throw new CommandException("ftab", "The -f option can only be used when the input rows are JSON objects.");
        }

        List<string> headers;
        if (fields != null)
        {
            headers = fields;
        }
        else if (rowObjects && rows.Count > 0)
        {
            headers = new List<string>();
            foreach (var row in rows)
            {
                foreach (var property in row.EnumerateObject())
                {
                    if (!headers.Contains(property.Name, StringComparer.Ordinal))
                    {
                        headers.Add(property.Name);
                    }
                }
            }

            if (headers.Count == 0)
            {
                headers.Add("value");
            }
        }
        else
        {
            headers = new List<string> { "value" };
        }

        var cells = new List<IReadOnlyList<string>>();
        foreach (var row in rows)
        {
            if (rowObjects)
            {
                var currentRow = new List<string>(headers.Count);
                foreach (var header in headers)
                {
                    if (row.TryGetProperty(header, out var property))
                    {
                        currentRow.Add(FormatCell(property));
                    }
                    else
                    {
                        currentRow.Add(string.Empty);
                    }
                }

                cells.Add(currentRow);
            }
            else
            {
                cells.Add(new List<string> { FormatCell(row) });
            }
        }

        return (headers, cells);
    }

    private static string FormatCell(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Undefined => string.Empty,
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Object => JsonSerializer.Serialize(value, JsonSerializerOptionsIndented),
            JsonValueKind.Array => JsonSerializer.Serialize(value, JsonSerializerOptionsIndented),
            _ => value.ToString(),
        };
    }

    private static void RenderSpectreTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, IReadOnlyList<ColorizeRule> colorizeRules)
    {
        var table = new Table();
        foreach (var header in headers)
        {
            table.AddColumn(new TableColumn(Theme.FormatSectionHeader(header)));
        }

        foreach (var row in rows)
        {
            table.AddRow(row.Select((cell, index) => FormatStyledCell(headers[index], cell, colorizeRules)).ToArray());
        }

        AnsiConsole.Write(table);
    }

    private static string RenderPlainText(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var normalizedRows = rows
            .Select(row => row.Select(cell => SplitLines(cell)).ToList())
            .ToList();

        var widths = new int[headers.Count];
        for (int columnIndex = 0; columnIndex < headers.Count; columnIndex++)
        {
            widths[columnIndex] = SplitLines(headers[columnIndex]).Max(line => line.Length);
        }

        foreach (var row in normalizedRows)
        {
            for (int columnIndex = 0; columnIndex < headers.Count; columnIndex++)
            {
                foreach (var line in row[columnIndex])
                {
                    widths[columnIndex] = Math.Max(widths[columnIndex], line.Length);
                }
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine(BuildBorder('┌', '┬', '┐', widths));
        AppendLogicalRow(sb, headers.Select(SplitLines).ToList(), widths);
        sb.AppendLine(BuildBorder('├', '┼', '┤', widths));

        for (int rowIndex = 0; rowIndex < normalizedRows.Count; rowIndex++)
        {
            AppendLogicalRow(sb, normalizedRows[rowIndex], widths);
            sb.AppendLine(BuildBorder(rowIndex == normalizedRows.Count - 1 ? '└' : '├', rowIndex == normalizedRows.Count - 1 ? '┴' : '┼', rowIndex == normalizedRows.Count - 1 ? '┘' : '┤', widths));
        }

        if (normalizedRows.Count == 0)
        {
            sb.AppendLine(BuildBorder('└', '┴', '┘', widths));
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendLogicalRow(StringBuilder sb, IReadOnlyList<IReadOnlyList<string>> row, IReadOnlyList<int> widths)
    {
        var height = row.Max(cell => cell.Count);
        for (int lineIndex = 0; lineIndex < height; lineIndex++)
        {
            sb.Append('│');
            for (int columnIndex = 0; columnIndex < widths.Count; columnIndex++)
            {
                var text = lineIndex < row[columnIndex].Count ? row[columnIndex][lineIndex] : string.Empty;
                sb.Append(' ');
                sb.Append(text.PadRight(widths[columnIndex]));
                sb.Append(' ');
                sb.Append('│');
            }

            sb.AppendLine();
        }
    }

    private static string BuildBorder(char left, char middle, char right, IReadOnlyList<int> widths)
    {
        var sb = new StringBuilder();
        sb.Append(left);
        for (int index = 0; index < widths.Count; index++)
        {
            sb.Append(new string('─', widths[index] + 2));
            sb.Append(index == widths.Count - 1 ? right : middle);
        }

        return sb.ToString();
    }

    private static List<string> SplitLines(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .ToList();
    }

    private static string FormatMarkdownCell(string value)
    {
        return value
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r\n", "<br/>", StringComparison.Ordinal)
            .Replace("\n", "<br/>", StringComparison.Ordinal);
    }

    private static string FormatHtmlCell(string value)
    {
        return WebUtility.HtmlEncode(value)
            .Replace("\r\n", "<br/>", StringComparison.Ordinal)
            .Replace("\n", "<br/>", StringComparison.Ordinal);
    }

    private sealed class JsonElementComparer : IComparer<JsonElement>
    {
        public static JsonElementComparer Instance { get; } = new();

        public int Compare(JsonElement left, JsonElement right)
        {
            if (left.ValueKind == JsonValueKind.Undefined && right.ValueKind == JsonValueKind.Undefined)
            {
                return 0;
            }

            if (left.ValueKind == JsonValueKind.Undefined)
            {
                return -1;
            }

            if (right.ValueKind == JsonValueKind.Undefined)
            {
                return 1;
            }

            if (left.ValueKind == JsonValueKind.Null && right.ValueKind == JsonValueKind.Null)
            {
                return 0;
            }

            if (left.ValueKind == JsonValueKind.Null)
            {
                return -1;
            }

            if (right.ValueKind == JsonValueKind.Null)
            {
                return 1;
            }

            if (TryGetDecimal(left, out var leftNumber) && TryGetDecimal(right, out var rightNumber))
            {
                return leftNumber.CompareTo(rightNumber);
            }

            if (left.ValueKind == JsonValueKind.True || left.ValueKind == JsonValueKind.False)
            {
                if (right.ValueKind == JsonValueKind.True || right.ValueKind == JsonValueKind.False)
                {
                    return left.GetBoolean().CompareTo(right.GetBoolean());
                }
            }

            return string.Compare(GetComparableString(left), GetComparableString(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string GetComparableString(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Object => JsonSerializer.Serialize(value, JsonSerializerOptionsIndented),
                JsonValueKind.Array => JsonSerializer.Serialize(value, JsonSerializerOptionsIndented),
                _ => value.ToString(),
            };
        }

        private static bool TryGetDecimal(JsonElement value, out decimal number)
        {
            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.TryGetDecimal(out number);
            }

            number = default;
            return false;
        }
    }
}