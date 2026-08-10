// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Net;
using System.Net.Sockets;

using global::Azure;
using global::Azure.Data.Cosmos.Shell.Parser;
using global::Azure.Identity;

using Microsoft.Azure.Cosmos;

/// <summary>
/// Stable, machine-readable process exit codes for CI/CD and automation consumers.
/// </summary>
/// <remarks>
/// These values form a public contract. Do not repurpose an existing code; add a new
/// one for a new failure category so scripts that branch on the exit code keep working.
/// </remarks>
public static class ShellExitCode
{
    /// <summary>The command or script completed successfully.</summary>
    public const int Success = 0;

    /// <summary>A failure that does not fit a more specific category.</summary>
    public const int GeneralFailure = 1;

    /// <summary>CLI parse failure, script syntax error, or invalid/unknown arguments.</summary>
    public const int UsageError = 2;

    /// <summary>Authentication or authorization failed (missing/invalid credentials, 401/403).</summary>
    public const int AuthFailure = 3;

    /// <summary>Network or service connectivity error prevented the request from completing.</summary>
    public const int ConnectionError = 4;

    /// <summary>The requested resource was not found (404).</summary>
    public const int NotFound = 5;

    /// <summary>The request was throttled (429 / RU budget exceeded).</summary>
    public const int Throttled = 6;

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

        // Specialized states (parser / error) override ExitCode; honor that.
        return state.ExitCode;
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
            // Peel our own wrappers so the underlying SDK/identity failure classifies.
            if ((ex is CommandException || ex is ShellException) && ex.InnerException is not null)
            {
                continue;
            }

            if (IsUsage(ex))
            {
                return UsageError;
            }

            if (IsAuthentication(ex))
            {
                return AuthFailure;
            }

            if (IsNotFound(ex))
            {
                return NotFound;
            }

            if (IsThrottled(ex))
            {
                return Throttled;
            }

            if (IsConnection(ex))
            {
                return ConnectionError;
            }
        }

        return GeneralFailure;
    }

    private static bool IsUsage(Exception ex)
    {
        return ex is CommandNotFoundException
            or PositionalException
            or ArgumentException;
    }

    private static bool IsAuthentication(Exception ex)
    {
        // CredentialUnavailableException derives from AuthenticationFailedException.
        return ex switch
        {
            AuthenticationFailedException => true,
            CosmosException cosmos => cosmos.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            RequestFailedException req => req.Status is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden,
            _ => false,
        };
    }

    private static bool IsNotFound(Exception ex)
    {
        return ex switch
        {
            CosmosException cosmos => cosmos.StatusCode == HttpStatusCode.NotFound,
            RequestFailedException req => req.Status == (int)HttpStatusCode.NotFound,
            _ => false,
        };
    }

    private static bool IsThrottled(Exception ex)
    {
        return ex switch
        {
            CosmosException cosmos => cosmos.StatusCode == HttpStatusCode.TooManyRequests,
            RequestFailedException req => req.Status == (int)HttpStatusCode.TooManyRequests,
            _ => false,
        };
    }

    private static bool IsConnection(Exception ex)
    {
        return ex switch
        {
            HttpRequestException => true,
            SocketException => true,
            TimeoutException => true,
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
