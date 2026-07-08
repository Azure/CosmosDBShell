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
    public static Func<Exception, int?>? CustomExitCodeMapper { get; set; }

    public Exception Exception { get; init; } = exception;

    public override bool IsError => true;

    public override int ExitCode
    {
        get
        {
            var ex = this.Exception;
            while (ex is Azure.Data.Cosmos.Shell.Core.CommandException ce && ce.InnerException != null)
            {
                ex = ce.InnerException;
            }

            if (ex is System.ArgumentException || ex is Azure.Data.Cosmos.Shell.Core.UnknownOptionException)
            {
                return 2;
            }

            if (ex is Azure.Data.Cosmos.Shell.Core.NotConnectedException)
            {
                return 3;
            }

            if (CustomExitCodeMapper != null)
            {
                var mapped = CustomExitCodeMapper(ex);
                if (mapped.HasValue)
                {
                    return mapped.Value;
                }
            }

            return 1;
        }
    }

}
