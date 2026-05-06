// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util;

using System.Text.RegularExpressions;

/// <summary>
/// Represents a parsed DocumentDB connection string, containing the endpoint, master key, and database name.
/// </summary>
public class ParsedDocDBConnectionString
{
    /// <summary>
    /// The well-known account key used by the Cosmos DB Emulator.
    /// This key is publicly documented by Microsoft and has no security value.
    /// See: https://learn.microsoft.com/en-us/azure/cosmos-db/emulator.
    /// </summary>
    internal const string EmulatorAccountKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    /// <summary>
    /// Initializes a new instance of the <see cref="ParsedDocDBConnectionString"/> class.
    /// </summary>
    /// <param name="endpoint">The endpoint URL of the DocumentDB.</param>
    /// <param name="masterKey">The master key for authentication, if available.</param>
    /// <param name="databaseName">The name of the database, if specified.</param>
    public ParsedDocDBConnectionString(string endpoint, string? masterKey, string? databaseName)
    {
        this.Endpoint = endpoint;
        this.MasterKey = masterKey;
        this.DatabaseName = databaseName;
    }

    /// <summary>
    /// Gets the endpoint URL of the DocumentDB.
    /// </summary>
    public string Endpoint { get; private set; }

    /// <summary>
    /// Gets the master key for authentication, if available.
    /// </summary>
    public string? MasterKey { get; private set; }

    /// <summary>
    /// Gets the name of the database, if specified.
    /// </summary>
    public string? DatabaseName { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the master key is available.
    /// </summary>
    public bool HasMasterKey => this.MasterKey != null;

    /// <summary>
    /// Determines whether the specified connection string or endpoint refers to a local endpoint
    /// (localhost, 127.0.0.1, or the Cosmos DB emulator).
    /// </summary>
    /// <param name="connectionStringOrEndpoint">The connection string or endpoint URL to check.</param>
    /// <returns><c>true</c> if the endpoint is local; otherwise, <c>false</c>.</returns>
    public static bool IsLocalEmulatorEndpoint(string? connectionStringOrEndpoint)
    {
        if (string.IsNullOrEmpty(connectionStringOrEndpoint))
        {
            return false;
        }

        // Resolve to a clean endpoint URL whether the input is a full connection string
        // ("AccountEndpoint=...;...") or a plain URL.
        string? endpoint;
        if (TryParseDocDBConnectionString(connectionStringOrEndpoint, out var parsed))
        {
            endpoint = parsed!.Endpoint;
        }
        else if (IsPlainUrl(connectionStringOrEndpoint))
        {
            endpoint = connectionStringOrEndpoint;
        }
        else
        {
            return false;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return System.Net.IPAddress.TryParse(uri.Host, out var ip) && System.Net.IPAddress.IsLoopback(ip);
    }

    /// <summary>
    /// Tries to parse a DocumentDB connection string into a <see cref="ParsedDocDBConnectionString"/> object.
    /// </summary>
    /// <param name="connectionString">The connection string to parse.</param>
    /// <param name="dBConnectionString">The resulting parsed connection string object if parsing succeeds; otherwise, null.</param>
    /// <returns>False if the connection string is invalid; otherwise, true.</returns>
    public static bool TryParseDocDBConnectionString(string connectionString, out ParsedDocDBConnectionString? dBConnectionString)
    {
        var endpoint = GetPropertyFromConnectionString(connectionString, "AccountEndpoint");
        var masterKey = GetPropertyFromConnectionString(connectionString, "AccountKey");
        var databaseName = GetPropertyFromConnectionString(connectionString, "Database");

        if (endpoint != null)
        {
            dBConnectionString = new ParsedDocDBConnectionString(endpoint, masterKey, databaseName);
        }
        else
        {
            dBConnectionString = null;
        }

        return dBConnectionString != null;
    }

    /// <summary>
    /// Determines whether the given input is a plain URL (starts with http:// or https://).
    /// </summary>
    /// <param name="input">The input string to check.</param>
    /// <returns><c>true</c> if the input is a plain URL; otherwise, <c>false</c>.</returns>
    public static bool IsPlainUrl(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }

        return false;
    }

    /// <summary>
    /// Builds a full emulator connection string from an endpoint URL.
    /// Uses the provided account key or falls back to the well-known emulator key.
    /// Always includes DisableServerCertificateValidation=True.
    /// </summary>
    /// <param name="endpoint">The emulator endpoint URL.</param>
    /// <param name="accountKey">An optional account key. If null, the well-known emulator key is used.</param>
    /// <returns>A connection string with AccountEndpoint, AccountKey, and DisableServerCertificateValidation=True.</returns>
    public static string BuildEmulatorConnectionString(string endpoint, string? accountKey = null)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uriResult) ||
            (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Invalid endpoint URL format.", nameof(endpoint));
        }

        string sanitizedEndpoint = uriResult.GetComponents(UriComponents.HttpRequestUrl, UriFormat.Unescaped);
        string key = accountKey ?? EmulatorAccountKey;
        return $"AccountEndpoint={sanitizedEndpoint};AccountKey={key};DisableServerCertificateValidation=True;";
    }

    /// <summary>
    /// Extracts a clean endpoint URL from a connection string or plain URL.
    /// </summary>
    /// <param name="connectionStringOrUrl">A connection string (with AccountEndpoint=) or a plain URL.</param>
    /// <returns>The endpoint URL.</returns>
    /// <exception cref="ArgumentException">Thrown when the endpoint cannot be determined.</exception>
    public static string ExtractEndpoint(string connectionStringOrUrl)
    {
        if (TryParseDocDBConnectionString(connectionStringOrUrl, out var parsed))
        {
            return parsed!.Endpoint;
        }

        if (IsPlainUrl(connectionStringOrUrl))
        {
            return connectionStringOrUrl;
        }

        throw new ArgumentException("Cannot determine endpoint from the provided connection string or URL.", nameof(connectionStringOrUrl));
    }

    /// <summary>
    /// Extracts a property value from a connection string based on the specified property name.
    /// </summary>
    /// <param name="connectionString">The connection string to parse.</param>
    /// <param name="property">The name of the property to extract.</param>
    /// <returns>The value of the property if found; otherwise, null.</returns>
    public static string? GetPropertyFromConnectionString(string connectionString, string property)
    {
        var regex = new Regex($"{property}=([^;]+)", RegexOptions.IgnoreCase);
        var match = regex.Match(connectionString);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        return null;
    }
}
