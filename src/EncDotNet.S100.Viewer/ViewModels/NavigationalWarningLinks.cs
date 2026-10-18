using System.Text.RegularExpressions;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// One run of a warning's message text: plain text, or a warning number that
/// opens the warning it names (#932).
/// </summary>
/// <param name="Text">The run's text, exactly as the message writes it.</param>
/// <param name="Target">The warning the number names; <c>null</c> for plain text.</param>
internal sealed record WarningTextSegment(string Text, NavigationalWarningEntry? Target)
{
    /// <summary>Whether the run is a link.</summary>
    public bool IsLink => Target is not null;
}

/// <summary>
/// Finds the warning numbers in a message ("106/26", "098/26") that name
/// another listed warning, so a "warnings in force" bulletin works as an
/// index (#932).
/// </summary>
/// <remarks>
/// A number is matched to a series in this order:
/// <list type="number">
/// <item>A series name written right before it on the same line
/// ("NAVAREA X 106/26"): that series only.</item>
/// <item>The nearest series name earlier in the text (a heading such as
/// "AUSCOAST WARNINGS IN FORCE:").</item>
/// <item>The message's own series.</item>
/// <item>The only listed warning with that number, when one series has it.</item>
/// </list>
/// Only the series of listed warnings are recognised as names. Numbers that
/// match nothing, and the message's own number, stay plain.
/// When several parts carry the matched warning, the first in the same
/// dataset wins, else the first listed.
/// </remarks>
internal static partial class NavigationalWarningLinks
{
    /// <summary>
    /// Splits <paramref name="text"/> into plain runs and links to the
    /// warnings in <paramref name="warnings"/>.
    /// </summary>
    /// <param name="text">The message text.</param>
    /// <param name="self">The warning the message belongs to.</param>
    /// <param name="selfDatasetId">The dataset that holds <paramref name="self"/>, preferred among duplicates.</param>
    /// <param name="warnings">The listed warnings that numbers may name.</param>
    /// <returns>The runs, in order; concatenated, they give back <paramref name="text"/>.</returns>
    public static IReadOnlyList<WarningTextSegment> Split(
        string? text,
        NavigationalWarningInfo self,
        string? selfDatasetId,
        IReadOnlyList<NavigationalWarningEntry> warnings)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(warnings);
        if (string.IsNullOrEmpty(text))
            return [];
        if (warnings.Count == 0)
            return [new WarningTextSegment(text, null)];

        var byKey = new Dictionary<(string Series, string Number), List<NavigationalWarningEntry>>();
        var seriesByNumber = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var entry in warnings)
        {
            if (entry.Warning.Number is not { } number)
                continue;
            var series = Normalize(entry.Warning.Series);
            var key = (series, number);
            if (!byKey.TryGetValue(key, out var list))
                byKey[key] = list = [];
            list.Add(entry);
            if (!seriesByNumber.TryGetValue(number, out var set))
                seriesByNumber[number] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(series);
        }

        if (byKey.Count == 0)
            return [new WarningTextSegment(text, null)];

        var mentions = SeriesMentions(text, byKey.Keys.Select(k => k.Series));
        var ownSeries = Normalize(self.Series);
        var segments = new List<WarningTextSegment>();
        var plainStart = 0;

        foreach (Match match in NumberPattern().Matches(text))
        {
            var target = Resolve(match, text, mentions, ownSeries, byKey, seriesByNumber);
            if (target is null)
                continue;
            if (target.Value.Series == ownSeries && target.Value.Number == self.Number)
                continue;

            var parts = byKey[target.Value];
            var entry = parts.FirstOrDefault(p => string.Equals(p.DatasetId, selfDatasetId, StringComparison.Ordinal))
                ?? parts[0];
            if (match.Index > plainStart)
                segments.Add(new WarningTextSegment(text[plainStart..match.Index], null));
            segments.Add(new WarningTextSegment(match.Value, entry));
            plainStart = match.Index + match.Length;
        }

        if (plainStart < text.Length)
            segments.Add(new WarningTextSegment(text[plainStart..], null));
        return segments;
    }

    private static (string Series, string Number)? Resolve(
        Match match,
        string text,
        IReadOnlyList<(int Start, int End, string Series)> mentions,
        string ownSeries,
        Dictionary<(string Series, string Number), List<NavigationalWarningEntry>> byKey,
        Dictionary<string, HashSet<string>> seriesByNumber)
    {
        var number = match.Value;
        var before = mentions.LastOrDefault(m => m.End <= match.Index);
        if (before.Series is not null)
        {
            // Written right before the number: that series or nothing.
            var gap = text.AsSpan(before.End, match.Index - before.End);
            if (gap.IsWhiteSpace() && !gap.ContainsAny('\n', '\r'))
                return byKey.ContainsKey((before.Series, number)) ? (before.Series, number) : null;
            if (byKey.ContainsKey((before.Series, number)))
                return (before.Series, number);
        }

        if (byKey.ContainsKey((ownSeries, number)))
            return (ownSeries, number);

        return seriesByNumber.TryGetValue(number, out var series) && series.Count == 1
            ? (series.First(), number)
            : null;
    }

    // Every place a known series name is written, in text order. Longer
    // names win where two overlap ("NAVAREA XI" over "NAVAREA X").
    private static List<(int Start, int End, string Series)> SeriesMentions(string text, IEnumerable<string> series)
    {
        var names = series.Where(s => s.Length > 0).Distinct(StringComparer.Ordinal)
            .OrderByDescending(s => s.Length).ToList();
        var found = new List<(int Start, int End, string Series)>();
        if (names.Count == 0)
            return found;

        var pattern = @"(?<![\p{L}\p{N}])(?:"
            + string.Join('|', names.Select(n => Regex.Escape(n).Replace(@"\ ", @"\s+", StringComparison.Ordinal)))
            + @")(?![\p{L}\p{N}])";
        foreach (Match m in Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            found.Add((m.Index, m.Index + m.Length, Normalize(m.Value)));
        return found;
    }

    // Series compare case-insensitively with runs of white space as one space.
    private static string Normalize(string? series) =>
        string.IsNullOrWhiteSpace(series)
            ? string.Empty
            : WhiteSpace().Replace(series.Trim(), " ").ToUpperInvariant();

    // A warning number and two-digit year, e.g. "106/26"; not part of a date.
    [GeneratedRegex(@"(?<![\d/])\d{3,4}/\d{2}(?![\d/])", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhiteSpace();
}
