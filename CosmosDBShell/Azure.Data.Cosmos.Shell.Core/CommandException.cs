// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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

        var message = GetSummaryMessage(exception);
        if (exception.InnerException != null)
        {
            var innerMessage = GetDisplayMessage(exception.InnerException);
            return message == innerMessage || message == exception.InnerException.Message
                ? innerMessage
                : string.Format("{0} ({1})", message, innerMessage);
        }

        return message;
    }

    /// <summary>
    /// Gets an exception's own message, replacing raw Azure/Cosmos service payloads
    /// with their actionable service message. Inner exceptions are not appended.
    /// </summary>
    internal static string GetSummaryMessage(Exception exception)
    {
        if (exception is not (global::Azure.RequestFailedException or CosmosException))
        {
            return exception.Message;
        }

        return IsRequestTimeout(exception)
            ? GetRequestTimeoutMessage()
            : ExtractServiceMessage(exception.Message, 0) ?? exception.Message;
    }

    private static string? ExtractServiceMessage(string message, int depth)
    {
        if (depth >= 8)
        {
            return null;
        }

        var json = message.TrimStart();
        if (json.StartsWith("Message:", StringComparison.OrdinalIgnoreCase))
        {
            json = json["Message:".Length..].TrimStart();
        }

        if (!json.StartsWith('{'))
        {
            return null;
        }

        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), new JsonReaderOptions { MaxDepth = 32 });
            using var document = JsonDocument.ParseValue(ref reader);
            return ExtractServiceMessage(document.RootElement, depth + 1);
        }
        catch (JsonException)
        {
            // Unrecognized service responses must retain the original diagnostic.
            return null;
        }
    }

    private static string? ExtractServiceMessage(JsonElement element, int depth)
    {
        if (depth >= 8)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var message = element.GetString();
            return string.IsNullOrWhiteSpace(message) ? null : ExtractServiceMessage(message, depth) ?? message;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("Errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
        {
            var messages = errors.EnumerateArray()
                .Where(error => error.ValueKind == JsonValueKind.String)
                .Select(error => error.GetString())
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .ToArray();
            if (messages.Length > 0)
            {
                return string.Join(Environment.NewLine, messages);
            }
        }

        if (element.TryGetProperty("error", out var error))
        {
            var message = ExtractServiceMessage(error, depth + 1);
            if (message != null)
            {
                return message;
            }
        }

        return element.TryGetProperty("message", out var serviceMessage)
            ? ExtractServiceMessage(serviceMessage, depth + 1)
            : null;
    }

    internal static string GetDisplayMessage(HttpStatusCode statusCode, string fallbackMessage)
    {
        return IsRequestTimeoutStatusCode(statusCode) ? GetRequestTimeoutMessage() : fallbackMessage;
    }

    internal static CommandException FromResponseStatus(string command, HttpStatusCode statusCode, string message)
    {
        var displayMessage = GetDisplayMessage(statusCode, message);
        if (!string.Equals(displayMessage, message, StringComparison.Ordinal))
        {
            return new CommandException(command, displayMessage, new Exception(message));
        }

        return new CommandException(command, displayMessage);
    }

    internal static bool IsConnectivityFailure(Exception exception)
    {
        if (exception is HttpRequestException or SocketException)
        {
            return true;
        }

        if (exception is CosmosException cosmosException
            && cosmosException.StatusCode is HttpStatusCode.RequestTimeout
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout)
        {
            return true;
        }

        if (exception is OperationCanceledException && IsRequestTimeout(exception))
        {
            return true;
        }

        return exception.InnerException != null && IsConnectivityFailure(exception.InnerException);
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
