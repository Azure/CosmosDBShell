// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Azure.Cosmos;

/// <summary>
/// Writes timestamped diagnostic entries to a log file so users can capture shell
/// activity (command execution, timing, errors, and connection events) for
/// troubleshooting, performance analysis, and auditing. Enabled via the
/// <c>--diagnostics</c> CLI option. All writes are serialized and IO failures are
/// swallowed so diagnostics never disrupt the interactive session.
/// </summary>
internal sealed class DiagnosticLog : IDisposable
{
    private const int TagWidth = 8;

    private static readonly Regex JwtPattern = new(
        "eyJ[A-Za-z0-9_-]+\\.eyJ[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+",
        RegexOptions.Compiled);

    private static readonly Regex BearerPattern = new(
        "(?i)(Bearer\\s+)[A-Za-z0-9._~+/=-]+",
        RegexOptions.Compiled);

    private static readonly Regex KeyTokenPattern = new(
        "(?i)(AccountKey|SharedAccessKey|SharedAccessSignature|password|passwd|pwd|sig|token|secret|apikey|api[_-]?key)(\\s*[=:]\\s*\"?)[^;\\s\"',}&]+",
        RegexOptions.Compiled);

    private readonly object gate = new();

    private readonly List<string> secrets = new();

    private StreamWriter? writer;

    private bool disposed;

    private DiagnosticLog(string path, StreamWriter writer)
    {
        this.Path = path;
        this.writer = writer;
    }

    /// <summary>
    /// Gets the resolved path of the diagnostic log file.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Creates a diagnostic log at <paramref name="path"/> and writes the session
    /// metadata header.
    /// </summary>
    /// <param name="path">The fully-qualified path of the log file to create.</param>
    /// <returns>The initialized <see cref="DiagnosticLog"/>.</returns>
    public static DiagnosticLog Create(string path)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true,
        };

        var log = new DiagnosticLog(path, writer);
        log.WriteHeader();
        return log;
    }

    /// <summary>
    /// Records a successful connection to a Cosmos DB account.
    /// </summary>
    /// <param name="endpoint">The account endpoint that was connected to.</param>
    /// <param name="mode">The connection mode negotiated for the client.</param>
    public void LogConnect(Uri endpoint, ConnectionMode mode)
    {
        var endpointText = endpoint.GetComponents(
            UriComponents.AbsoluteUri | UriComponents.StrongPort,
            UriFormat.UriEscaped);
        this.WriteLine("CONNECT", $"{endpointText} (mode={mode})");
    }

    /// <summary>
    /// Records that a command is about to be executed.
    /// </summary>
    /// <param name="command">The command text.</param>
    public void LogCommand(string command)
    {
        this.WriteLine("CMD", this.Flatten(command));
    }

    /// <summary>
    /// Records the outcome and elapsed time of a command.
    /// </summary>
    /// <param name="succeeded"><c>true</c> when the command completed without error.</param>
    /// <param name="elapsedMilliseconds">The wall-clock execution time in milliseconds.</param>
    /// <param name="command">The command text.</param>
    public void LogResult(bool succeeded, double elapsedMilliseconds, string command)
    {
        var status = succeeded ? "[OK]" : "[FAIL]";
        var elapsed = elapsedMilliseconds.ToString("0.0", CultureInfo.InvariantCulture);
        this.WriteLine("RESULT", $"{status} {elapsed}ms | {this.Flatten(command)}");
    }

    /// <summary>
    /// Records that a command was canceled before it completed.
    /// </summary>
    /// <param name="elapsedMilliseconds">The wall-clock execution time in milliseconds.</param>
    /// <param name="command">The command text.</param>
    public void LogCancelled(double elapsedMilliseconds, string command)
    {
        var elapsed = elapsedMilliseconds.ToString("0.0", CultureInfo.InvariantCulture);
        this.WriteLine("RESULT", $"[CANCELLED] {elapsed}ms | {this.Flatten(command)}");
    }

    /// <summary>
    /// Records the exception raised by a failed command.
    /// </summary>
    /// <param name="command">The command text.</param>
    /// <param name="exception">The exception that was raised.</param>
    public void LogError(string command, Exception exception)
    {
        this.WriteLine("ERROR", $"{this.Flatten(command)} -> {exception.GetType().Name}: {this.Flatten(exception.Message)}");
    }

    /// <summary>
    /// Registers a literal secret value (such as an account key or connection string)
    /// that must be redacted from every subsequent log entry. Both the raw and
    /// URL-encoded forms are masked.
    /// </summary>
    /// <param name="value">The secret literal to redact; ignored when null or empty.</param>
    public void AddSecret(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        lock (this.gate)
        {
            if (!this.secrets.Contains(value))
            {
                this.secrets.Add(value);

                // Longest-first so a short secret that is a substring of a longer one
                // cannot pre-empt the longer match and leave a partial leak.
                this.secrets.Sort(static (a, b) => b.Length.CompareTo(a.Length));
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (this.gate)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            try
            {
                this.writer?.Dispose();
            }
            catch (IOException)
            {
            }
            finally
            {
                this.writer = null;
            }
        }
    }

    internal static string MaskSecrets(string value, IReadOnlyList<string> secrets)
    {
        foreach (var secret in secrets)
        {
            if (string.IsNullOrEmpty(secret))
            {
                continue;
            }

            foreach (var form in new[] { secret, Uri.EscapeDataString(secret) })
            {
                value = Regex.Replace(value, Regex.Escape(form), "redacted:secret", RegexOptions.IgnoreCase);
            }
        }

        value = JwtPattern.Replace(value, "redacted:jwt");
        value = BearerPattern.Replace(value, "$1redacted");
        value = KeyTokenPattern.Replace(value, "$1$2redacted");
        return value;
    }

    private string Flatten(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string[] snapshot;
        lock (this.gate)
        {
            snapshot = this.secrets.ToArray();
        }

        var redacted = MaskSecrets(value, snapshot);
        return redacted.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private void WriteHeader()
    {
        lock (this.gate)
        {
            if (this.writer is null)
            {
                return;
            }

            try
            {
                this.writer.WriteLine("# CosmosDB Shell Diagnostic Log");
                this.writer.WriteLine($"# Started: {DateTimeOffset.Now:O}");
                this.writer.WriteLine($"# Machine: {Environment.MachineName}");
                this.writer.WriteLine($"# OS: {Environment.OSVersion.VersionString}");
                this.writer.WriteLine($"# Runtime: {Environment.Version}");
                this.writer.WriteLine(new string('-', 80));
            }
            catch (IOException)
            {
            }
        }
    }

    private void WriteLine(string tag, string message)
    {
        lock (this.gate)
        {
            if (this.writer is null)
            {
                return;
            }

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            try
            {
                this.writer.WriteLine($"[{timestamp}] [{tag.PadRight(TagWidth)}] {message}");
            }
            catch (IOException)
            {
            }
        }
    }
}
