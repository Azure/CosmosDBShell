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
            while (true)
            {
                if (ex is CommandException ce && ce.InnerException is not null)
                {
                    ex = ce.InnerException;
                    continue;
                }

                if (ex is ShellException se && se.InnerException is not null)
                {
                    ex = se.InnerException;
                    continue;
                }

                break;
            }

            if (ex is Azure.Identity.AuthenticationFailedException
                || ex is Azure.Identity.CredentialUnavailableException
                || ex is System.Net.Http.HttpRequestException
                || ex is System.Net.Sockets.SocketException
                || ex is TimeoutException)
            {
                return 3;
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

            if (ex is Azure.Data.Cosmos.Shell.Parser.CommandNotFoundException)
            {
                return 2;
            }

            if (ex is ArgumentException)
            {
                return 2;
            }

            return 1;
        }
    }
}
