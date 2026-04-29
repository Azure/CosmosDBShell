// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Net;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Represents errors that occur during command execution in the Cosmos Shell.
/// </summary>
public class CommandException : ShellException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommandException"/> class with a specified command and innerException.
    /// </summary>
    /// <param name="command">The command that caused the innerException.</param>
    /// <param name="innerException">The innerException that occurred.</param>
    public CommandException(string command, Exception innerException)
        : base(GetMessage(innerException), innerException)
    {
        this.Command = command;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandException"/> class with a specified command and error message.
    /// </summary>
    /// <param name="command">The command that caused the innerException.</param>
    /// <param name="message">The error message.</param>
    public CommandException(string command, string message)
        : base(message)
    {
        this.Command = command;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandException"/> class with a specified command and error message.
    /// </summary>
    /// <param name="command">The command that caused the innerException.</param>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that is the cause of the current exception, or null if no inner exception is specified.</param>
    public CommandException(string command, string message, Exception? innerException)
        : base(message, innerException)
    {
        this.Command = command;
    }

    /// <summary>
    /// Gets the command that caused the innerException.
    /// </summary>
    public string Command { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{this.Command}: {base.ToString()}";
    }

    internal static string GetDisplayMessage(Exception exception)
    {
        if (IsRequestTimeout(exception))
        {
            return GetRequestTimeoutMessage();
        }

        if (exception.InnerException != null)
        {
            return string.Format("{0} ({1})", exception.Message, GetDisplayMessage(exception.InnerException));
        }

        return exception.Message;
    }

    internal static string GetDisplayMessage(HttpStatusCode statusCode, string fallbackMessage)
    {
        return IsRequestTimeoutStatusCode(statusCode) ? GetRequestTimeoutMessage() : fallbackMessage;
    }

    private static string GetMessage(Exception exception)
    {
        return GetDisplayMessage(exception);
    }

    private static string GetRequestTimeoutMessage()
    {
        return MessageService.GetString("error-request_timeout");
    }

    private static bool IsRequestTimeout(Exception exception)
    {
        if (exception is CosmosException cosmosException && IsRequestTimeoutStatusCode(cosmosException.StatusCode))
        {
            return true;
        }

        if (exception is OperationCanceledException && LooksLikeCosmosTimeout(exception.Message))
        {
            return true;
        }

        return exception.InnerException != null && IsRequestTimeout(exception.InnerException);
    }

    private static bool IsRequestTimeoutStatusCode(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout;
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
