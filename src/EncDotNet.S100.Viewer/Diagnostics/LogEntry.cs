using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// One entry in the in-memory <see cref="LogBuffer"/> (#1080): a log message
/// the viewer wrote, or a run of identical consecutive messages collapsed
/// into one entry with a <see cref="Count"/>.
/// </summary>
/// <param name="Sequence">
/// Increases by one for each new entry and never repeats, even across
/// <see cref="LogBuffer.Clear"/>. A collapsed repeat keeps the sequence of
/// the entry it was folded into.
/// </param>
/// <param name="Timestamp">When the first message of the entry was written (UTC).</param>
/// <param name="LastTimestamp">When the latest repeat was written (UTC); equals <paramref name="Timestamp"/> for a single message.</param>
/// <param name="Level">The log level.</param>
/// <param name="Category">The logger category, usually the writing type's full name.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Exception">The exception's text (type, message and stack trace), or <see langword="null"/>.</param>
/// <param name="Count">How many identical consecutive messages this entry stands for.</param>
internal sealed record LogEntry(
    long Sequence,
    DateTimeOffset Timestamp,
    DateTimeOffset LastTimestamp,
    LogLevel Level,
    string Category,
    string Message,
    string? Exception,
    int Count = 1)
{
    /// <summary>The friendly area name for <see cref="Category"/> (see <see cref="LogAreas"/>).</summary>
    public string Area => LogAreas.Resolve(Category);

    /// <summary>Whether a new message repeats this entry's, so the two collapse.</summary>
    internal bool IsRepeatOf(LogLevel level, string category, string message, string? exception) =>
        Level == level
        && string.Equals(Category, category, StringComparison.Ordinal)
        && string.Equals(Message, message, StringComparison.Ordinal)
        && string.Equals(Exception, exception, StringComparison.Ordinal);

    /// <summary>The short level label used in exported text ("INFO", "WARN"…).</summary>
    public static string LevelLabel(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRIT",
        _ => level.ToString().ToUpperInvariant(),
    };

    /// <summary>
    /// Formats the entry as plain text, the way <c>logs.txt</c> and the Logs
    /// panel's Copy write it: local ISO-8601 time, level, area, category,
    /// message, a repeat count, then the exception on the following lines.
    /// </summary>
    /// <param name="redact">Applied to the message and exception text, e.g. <see cref="HomePathRedactor.Redact(string?)"/>.</param>
    public string ToText(Func<string, string>? redact = null)
    {
        var sb = new StringBuilder();
        AppendText(sb, redact);
        return sb.ToString();
    }

    internal void AppendText(StringBuilder sb, Func<string, string>? redact = null)
    {
        sb.Append(Timestamp.ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture))
            .Append(' ').Append(LevelLabel(Level).PadRight(5))
            .Append(' ').Append('[').Append(Area).Append("] ")
            .Append(Category).Append(": ")
            .Append(redact is null ? Message : redact(Message));
        if (Count > 1)
        {
            sb.Append(" (×").Append(Count.ToString(CultureInfo.InvariantCulture)).Append(", last ")
                .Append(LastTimestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(')');
        }
        if (Exception is not null)
            sb.Append(Environment.NewLine).Append(redact is null ? Exception : redact(Exception));
    }

    /// <summary>Formats <paramref name="entries"/> one after another, one per line (see <see cref="ToText"/>).</summary>
    public static string ToText(IEnumerable<LogEntry> entries, Func<string, string>? redact = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var sb = new StringBuilder();
        foreach (var entry in entries)
        {
            entry.AppendText(sb, redact);
            sb.Append(Environment.NewLine);
        }
        return sb.ToString();
    }
}
