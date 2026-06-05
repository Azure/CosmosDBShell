// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System.Text.Json;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;

internal class FilterPathExpression : Expression
{
    public FilterPathExpression(Token rootToken, IReadOnlyList<FilterPathSegment> segments, int? length = null)
    {
        this.RootToken = rootToken ?? throw new ArgumentNullException(nameof(rootToken));
        this.Segments = segments ?? throw new ArgumentNullException(nameof(segments));
        this.ExpressionLength = length ?? rootToken.Length;
    }

    public Token RootToken { get; }

    public IReadOnlyList<FilterPathSegment> Segments { get; }

    public int ExpressionLength { get; }

    public override int Start => this.RootToken.Start;

    public override int Length => this.ExpressionLength;

    public static FilterPathExpression CreateShorthand(Token sourceToken, string propertyName)
    {
        return new FilterPathExpression(sourceToken, [new FilterPropertySegment(propertyName, false)], propertyName.Length);
    }

    public override Task<ShellObject> EvaluateAsync(ShellInterpreter interpreter, CommandState currentState, CancellationToken cancellationToken)
    {
        var evaluatedResult = currentState.Result?.ConvertShellObject(DataType.Json);
        if (evaluatedResult is not JsonElement root)
        {
            throw new CommandException(
                "filter",
                MessageService.GetString(
                    "command-filter-error-not_json",
                    new Dictionary<string, object> { { "context", this.RootToken.Value } }));
        }

        var values = new List<JsonElement> { root.Clone() };
        bool sequence = false;

        foreach (var segment in this.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = new List<JsonElement>();

            foreach (var value in values)
            {
                switch (segment)
                {
                    case FilterPropertySegment propertySegment:
                        if (value.ValueKind == JsonValueKind.Object)
                        {
                            if (value.TryGetProperty(propertySegment.Name, out var propertyValue))
                            {
                                next.Add(propertyValue.Clone());
                            }
                            else
                            {
                                next.Add(FilterExpressionUtilities.NullElement());
                            }
                        }
                        else if (propertySegment.Optional)
                        {
                            next.Add(FilterExpressionUtilities.NullElement());
                        }
                        else
                        {
                            throw new CommandException(
                                "filter",
                                MessageService.GetString(
                                    "command-filter-error-property_type",
                                    new Dictionary<string, object>
                                    {
                                        { "name", propertySegment.Name },
                                        { "type", FilterExpressionUtilities.DescribeKind(value.ValueKind) },
                                    }));
                        }

                        break;

                    case FilterIndexSegment indexSegment:
                        if (value.ValueKind == JsonValueKind.Array)
                        {
                            if (indexSegment.Index >= 0 && indexSegment.Index < value.GetArrayLength())
                            {
                                next.Add(value[indexSegment.Index].Clone());
                            }
                            else
                            {
                                next.Add(FilterExpressionUtilities.NullElement());
                            }
                        }
                        else if (indexSegment.Optional)
                        {
                            next.Add(FilterExpressionUtilities.NullElement());
                        }
                        else
                        {
                            throw new CommandException(
                                "filter",
                                MessageService.GetString(
                                    "command-filter-error-index_type",
                                    new Dictionary<string, object>
                                    {
                                        { "type", FilterExpressionUtilities.DescribeKind(value.ValueKind) },
                                        { "index", indexSegment.Index },
                                    }));
                        }

                        break;

                    case FilterIterateSegment iterateSegment:
                        if (value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in value.EnumerateArray())
                            {
                                next.Add(item.Clone());
                            }

                            sequence = true;
                        }
                        else if (iterateSegment.Optional)
                        {
                            next.Add(FilterExpressionUtilities.NullElement());
                        }
                        else
                        {
                            throw new CommandException(
                                "filter",
                                MessageService.GetString(
                                    "command-filter-error-iterate_type",
                                    new Dictionary<string, object> { { "type", FilterExpressionUtilities.DescribeKind(value.ValueKind) } }));
                        }

                        break;
                }
            }

            values = next;
        }

        if (sequence)
        {
            return Task.FromResult<ShellObject>(new ShellSequence(values));
        }

        if (values.Count == 0)
        {
            return Task.FromResult<ShellObject>(new ShellJson(FilterExpressionUtilities.NullElement()));
        }

        return Task.FromResult<ShellObject>(new ShellJson(values[0]));
    }

    public override void Accept(IAstVisitor visitor)
    {
        visitor.Visit(this);
    }
}