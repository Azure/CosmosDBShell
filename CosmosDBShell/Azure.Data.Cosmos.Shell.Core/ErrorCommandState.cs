// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Azure.Data.Cosmos.Shell.Util;

internal class ErrorCommandState(Exception exception) : CommandState
{
    public Exception Exception { get; init; } = exception;

    public override bool IsError => true;

    public override int ExitCode
    {
        get
        {
            var ex = this.Exception;
            if (ex is CommandException ce)
            {
                ex = ce.InnerException ?? ce;
            }

            if (ex is Microsoft.Azure.Cosmos.CosmosException cosmosEx)
            {
                return cosmosEx.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => 3,
                    System.Net.HttpStatusCode.Forbidden => 3,
                    System.Net.HttpStatusCode.NotFound => 4,
                    System.Net.HttpStatusCode.TooManyRequests => 5,
                    _ => 1,
                };
            }

            if (ex is ArgumentException)
            {
                return 2;
            }

            return 1;
        }
    }
}
