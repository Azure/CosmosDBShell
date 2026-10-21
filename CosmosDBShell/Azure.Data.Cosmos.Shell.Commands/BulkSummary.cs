// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text.Json.Serialization;

internal sealed class BulkSummary
{
    internal const int MaxReportedErrors = 20;

    public string Type { get; } = "bulk";

    public long OperationCount { get; set; }

    public long Attempted { get; set; }

    public long Succeeded { get; set; }

    public long Failed { get; set; }

    public long Skipped { get; set; }

    public long Uncertain { get; set; }

    public double RequestCharge { get; set; }

    public bool DryRun { get; set; }

    public bool ResultIncomplete { get; set; }

    public bool BudgetExceeded { get; set; }

    public bool SelectionLimited { get; set; }

    public List<BulkOutcome> Errors { get; } = [];

    public bool Success => this.Failed == 0 && !this.ResultIncomplete;

    [JsonIgnore]
    public long Processed { get; set; }

    public void AddFailure(BulkOutcome outcome)
    {
        this.Failed++;
        if (outcome.Status == BulkOutcome.Uncertain)
        {
            this.Uncertain++;
        }

        if (this.Errors.Count < MaxReportedErrors)
        {
            this.Errors.Add(outcome);
        }
    }
}
