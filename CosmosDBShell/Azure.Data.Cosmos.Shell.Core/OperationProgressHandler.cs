// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Net;

/// <summary>
/// Treats every response the local emulator actually sends as progress for the running
/// command's inactivity timeout. Request timeouts and unavailability do not count, because
/// the SDK synthesizes them when the emulator does not answer.
/// </summary>
internal sealed class OperationProgressHandler(ShellInterpreter shell) : RequestHandler
{
    public override async Task<ResponseMessage> SendAsync(RequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode is not (HttpStatusCode.RequestTimeout or HttpStatusCode.ServiceUnavailable))
        {
            shell.ReportOperationProgress();
        }

        return response;
    }
}
