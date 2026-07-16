// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Net;
using System.Net.Sockets;
using global::Azure;
using global::Azure.Identity;
using Microsoft.Azure.Cosmos;

/// <summary>
/// Stable, machine-readable process exit codes for CI/CD consumers.
/// </summary>
/// <remarks>
/// These values form a public contract. Do not repurpose an existing code;
/// add a new one for a new failure category so scripts that branch on the
/// exit code keep working.
/// </remarks>
public static class ShellExitCode
{
    /// <summary>The command or script completed successfully.</summary>
    public const int Success = 0;

    /// <summary>A failure that does not fit a more specific category.</summary>
    public const int GeneralFailure = 1;

    /// <summary>Authentication or authorization failed (invalid or missing credentials, forbidden access).</summary>
    public const int AuthenticationFailure = 2;

    /// <summary>A network or service connectivity error prevented the request from completing.</summary>
    public const int ConnectionError = 3;

    /// <summary>The shell could not parse the command or script (syntax error).</summary>
    public const int SyntaxError = 4;

    /// <summary>
    /// Maps a completed <see cref="CommandState"/> to its process exit code.
    /// </summary>
    /// <param name="state">The state returned from executing a command or script.</param>
    /// <returns>The exit code that best describes the outcome.</returns>
    public static int FromCommandState(CommandState? state)
    {
        if (state is null || !state.IsError)
        {
            return Success;
        }

        if (state is ParserErrorCommandState)
        {
            return SyntaxError;
        }

        if (state is ErrorCommandState error)
        {
            return FromException(error.Exception);
        }

        return GeneralFailure;
    }

    /// <summary>
    /// Classifies an exception into one of the stable exit-code categories.
    /// </summary>
    /// <param name="exception">The exception to classify, or <see langword="null"/>.</param>
    /// <returns>The matching exit code, defaulting to <see cref="GeneralFailure"/>.</returns>
    public static int FromException(Exception? exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (IsAuthentication(ex))
            {
                return AuthenticationFailure;
            }

            if (IsConnection(ex))
            {
                return ConnectionError;
            }
        }

        return GeneralFailure;
    }

    private static bool IsAuthentication(Exception ex)
    {
        return ex switch
        {
            AuthenticationFailedException => true,
            CosmosException cosmos => cosmos.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            RequestFailedException req => req.Status is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden,
            _ => false,
        };
    }

    private static bool IsConnection(Exception ex)
    {
        return ex switch
        {
            HttpRequestException => true,
            SocketException => true,
            OperationCanceledException => LooksLikeCosmosTimeout(ex.Message),
            CosmosException cosmos => cosmos.StatusCode is HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.RequestTimeout
                or HttpStatusCode.GatewayTimeout,
            RequestFailedException req => req.Status is (int)HttpStatusCode.ServiceUnavailable
                or (int)HttpStatusCode.RequestTimeout
                or (int)HttpStatusCode.GatewayTimeout,
            _ => false,
        };
    }

    private static bool LooksLikeCosmosTimeout(string message)
    {
        return message.Contains("Cancellation Token has expired", StringComparison.OrdinalIgnoreCase)
            || message.Contains("CosmosOperationCanceledException", StringComparison.OrdinalIgnoreCase)
            || message.Contains("request timed out", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ReceiveTimeout", StringComparison.OrdinalIgnoreCase)
            || message.Contains("https://aka.ms/cosmosdb-tsg-request-timeout", StringComparison.OrdinalIgnoreCase);
    }
}
