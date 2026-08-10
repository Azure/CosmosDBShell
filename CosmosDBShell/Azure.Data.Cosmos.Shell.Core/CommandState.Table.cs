// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Spectre.Console;

public partial class CommandState
{
    internal static string EscapeCSV(object obj)
    {
        var str = obj?.ToString() ?? string.Empty;
        return '"' + str.Replace("\"", "\"\"") + '"';
    }

    private static bool TryGetListProperty(JsonElement json, out JsonElement array)
    {
        foreach (var name in new[] { "values", "items", "databases", "containers" })
        {
            if (json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
            {
                array = value;
                return true;
            }
        }

        array = default;
        return false;
    }

    private static IEnumerable<KeyValuePair<string, string>> EnumerateFields(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                yield return new KeyValuePair<string, string>(prop.Name, prop.Value.ToString());
            }
        }
        else
        {
            yield return new KeyValuePair<string, string>("value", element.ToString());
        }
    }

    private static Table ResultToTable(IList<JsonElement> results)
    {
        var headers = new Dictionary<string, int>();
        var columns = new List<List<string?>>();
        var row = 0;
        foreach (var e in results)
        {
            var curColumn = 0;
            foreach (var field in EnumerateFields(e))
            {
                if (!headers.TryGetValue(field.Key, out var index))
                {
                    index = curColumn;
                    var list = new List<string?>
                    {
                        EscapeCSV(field.Key),
                    };
                    for (int i = 0; i < row; i++)
                    {
                        list.Add(null);
                    }

                    var keysToShift = new List<string>();
                    foreach (var kv in headers)
                    {
                        if (kv.Value >= curColumn)
                        {
                            keysToShift.Add(kv.Key);
                        }
                    }

                    foreach (var key in keysToShift)
                    {
                        headers[key] += 1;
                    }

                    headers[field.Key] = curColumn;
                    if (columns.Count <= curColumn)
                    {
                        columns.Add(list);
                    }
                    else
                    {
                        columns.Insert(curColumn, list);
                    }
                }

                var column = columns[index];

                // fill up missing items (if any)
                while (column.Count < row)
                {
                    column.Add(null);
                }

                column.Add(EscapeCSV(field.Value));
                curColumn += 1;
            }

            row += 1;
        }

        // ensure all columns have the same length
        foreach (var column in columns)
        {
            while (column.Count <= row)
            {
                column.Add(null);
            }
        }

        return new Table(columns);
    }

    private static Table FromTabular(TabularData data)
    {
        var columns = new List<List<string?>>();
        var headers = data.Headers;
        for (int c = 0; c < headers.Count; c++)
        {
            columns.Add(new List<string?> { EscapeCSV(headers[c]) });
        }

        foreach (var row in data.Rows)
        {
            for (int c = 0; c < columns.Count; c++)
            {
                var value = c < row.Count ? row[c] : string.Empty;
                columns[c].Add(EscapeCSV(value));
            }
        }

        return new Table(columns);
    }

    private class Table
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Table"/> class with the specified columns.
        /// </summary>
        /// <param name="columns">
        /// A list of columns, where each column is represented as a list of nullable strings.
        /// The first column's row count must match the row count of all other columns.
        /// </param>
        /// <exception cref="System.Diagnostics.Debug.AssertException">
        /// Thrown if any column does not have the same number of rows as the first column (in debug builds).
        /// </exception>
        public Table(List<List<string?>> columns)
        {
            for (int i = 1; i < columns.Count; i++)
            {
                Debug.Assert(columns[0].Count == columns[i].Count, $"Column 0 count ({columns[0].Count}) does not match column {i} count ({columns[i].Count})");
            }

            this.Cols = columns;
        }

        public int Rows => this.Cols.Count == 0 ? 0 : this.Cols[0].Count;

        public int Columns => this.Cols.Count;

        private List<List<string?>> Cols { get; }

        public string? this[int i, int j]
        {
            get
            {
                if (j >= this.Cols.Count)
                {
                    throw new IndexOutOfRangeException($"Invalid colum: {j} valid: 0..{this.Cols.Count}");
                }

                if (i >= this.Cols[j].Count)
                {
                    throw new IndexOutOfRangeException($"Invalid row: {i} valid: 0..{this.Cols[j].Count}");
                }

                return this.Cols[j][i];
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            var rows = this.Rows;
            var cols = this.Columns;
            var sep = ShellInterpreter.CSVSeparator;
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    if (j > 0)
                    {
                        sb.Append(sep);
                    }

                    if (this[i, j] != null)
                    {
                        sb.Append(this[i, j]);
                    }
                    else
                    {
                        sb.Append("\"\"");
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        public string ToGridString()
        {
            var rows = this.Rows;
            var cols = this.Columns;
            if (rows == 0 || cols == 0)
            {
                return string.Empty;
            }

            var table = new Spectre.Console.Table();
            for (int j = 0; j < cols; j++)
            {
                table.AddColumn(Markup.Escape(UnescapeCSV(this[0, j])));
            }

            for (int i = 1; i < rows; i++)
            {
                var cells = new string[cols];
                for (int j = 0; j < cols; j++)
                {
                    cells[j] = Markup.Escape(UnescapeCSV(this[i, j]));
                }

                table.AddRow(cells);
            }

            var writer = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(writer),
            });
            console.Write(table);
            return writer.ToString().TrimEnd();
        }

        private static string UnescapeCSV(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                return value[1..^1].Replace("\"\"", "\"");
            }

            return value;
        }
    }
}
