using System.Diagnostics;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Routes <see cref="System.Diagnostics.Trace"/> diagnostic output to standard
/// error for the lifetime of the scope, then restores the previous listener
/// configuration on disposal.
/// </summary>
/// <remarks>
/// The portrayal pipelines emit host/Lua diagnostics (for example the S-101
/// <c>S101LuaDataProvider</c> <c>[Lua]</c> / <c>[Host]</c> trace lines, including
/// the expected, spec-compliant <c>OBSTRN07</c> "Neither valueOfSounding or
/// defaultClearanceDepth have a value" fallbacks) via <c>Trace.WriteLine</c>.
/// These are silent by default — the framework <see cref="DefaultTraceListener"/>
/// does not write to the console — so they never pollute normal command output.
/// Activating this scope (the CLI does so when <c>--debug</c> is supplied)
/// surfaces them on stderr for deep-dive diagnostics without affecting the
/// PNG/stdout result.
/// </remarks>
internal sealed class DiagnosticTraceScope : IDisposable
{
    private readonly TextWriterTraceListener _listener;
    private readonly bool _previousAutoFlush;

    private DiagnosticTraceScope()
    {
        _previousAutoFlush = Trace.AutoFlush;
        Trace.AutoFlush = true;
        _listener = new TextWriterTraceListener(Console.Error, "cli-stderr");
        Trace.Listeners.Add(_listener);
    }

    /// <summary>
    /// Creates a scope that mirrors <see cref="System.Diagnostics.Trace"/> output
    /// to standard error.
    /// </summary>
    public static DiagnosticTraceScope ToStandardError() => new();

    /// <inheritdoc/>
    public void Dispose()
    {
        Trace.Listeners.Remove(_listener);
        _listener.Flush();
        _listener.Dispose();
        Trace.AutoFlush = _previousAutoFlush;
    }
}
