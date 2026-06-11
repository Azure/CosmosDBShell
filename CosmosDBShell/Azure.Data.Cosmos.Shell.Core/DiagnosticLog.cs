// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Globalization;
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

    private readonly object gate = new();

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
        this.WriteLine("CMD", Flatten(command));
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
        this.WriteLine("RESULT", $"{status} {elapsed}ms | {Flatten(command)}");
    }

    /// <summary>
    /// Records the exception raised by a failed command.
    /// </summary>
    /// <param name="command">The command text.</param>
    /// <param name="exception">The exception that was raised.</param>
    public void LogError(string command, Exception exception)
    {
        this.WriteLine("ERROR", $"{Flatten(command)} -> {exception.GetType().Name}: {Flatten(exception.Message)}");
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

    private static string Flatten(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
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
