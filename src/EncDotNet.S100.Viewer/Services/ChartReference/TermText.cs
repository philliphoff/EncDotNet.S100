using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EncDotNet.S100.Datasets.Pipelines.Reference;

namespace EncDotNet.S100.Viewer.Services.ChartReference;

/// <summary>
/// One run of a <see cref="TermText"/>: plain words, or a term that names a
/// chart reference entry (symbol dictionary design note §4.6, R8, #1651).
/// </summary>
/// <param name="Text">The words.</param>
/// <param name="Key">The entry the words name, or <see langword="null"/> for plain words.</param>
/// <param name="Value">
/// For a listed value ("red" in "Colour is red"), its code in the attribute
/// <paramref name="Key"/> names; otherwise <see langword="null"/>.
/// </param>
internal readonly record struct TermRun(string Text, ReferenceKey? Key = null, string? Value = null)
{
    /// <summary>Whether the run is a term.</summary>
    public bool IsTerm => Key is not null;
}

/// <summary>
/// Text in which some words are terms, as the condition reader says a case:
/// "Colour is red", where "Colour" names the attribute and "red" its listed
/// value (R8, #1651). <see cref="Text"/> is the words alone.
/// </summary>
/// <remarks>
/// Two texts are equal when their words are, so cases are said once however
/// they were read.
/// </remarks>
internal sealed partial class TermText : IEquatable<TermText>
{
    private TermText(IReadOnlyList<TermRun> runs, string text)
    {
        Runs = runs;
        Text = text;
    }

    /// <summary>No words.</summary>
    public static TermText Empty { get; } = new([], string.Empty);

    /// <summary>The runs, in order; neighbouring plain runs are one run, and none is empty.</summary>
    public IReadOnlyList<TermRun> Runs { get; }

    /// <summary>The words.</summary>
    public string Text { get; }

    /// <summary>Whether there are no words.</summary>
    public bool IsEmpty => Text.Length == 0;

    /// <summary>Whether any run is a term.</summary>
    public bool HasTerms => Runs.Any(r => r.IsTerm);

    /// <summary>Text from runs.</summary>
    /// <param name="runs">The runs.</param>
    /// <returns>The text, with neighbouring plain runs joined and empty runs left out.</returns>
    public static TermText Of(IEnumerable<TermRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var list = new List<TermRun>();
        var text = new StringBuilder();
        foreach (var run in runs)
        {
            if (string.IsNullOrEmpty(run.Text))
                continue;
            text.Append(run.Text);
            if (!run.IsTerm && list.Count > 0 && !list[^1].IsTerm)
                list[^1] = new TermRun(list[^1].Text + run.Text);
            else
                list.Add(run);
        }

        return list.Count == 0 ? Empty : new TermText(list, text.ToString());
    }

    /// <summary>Plain words.</summary>
    /// <param name="text">The words.</param>
    public static TermText Plain(string? text) =>
        string.IsNullOrEmpty(text) ? Empty : new TermText([new TermRun(text)], text);

    /// <summary>A term.</summary>
    /// <param name="text">Its words.</param>
    /// <param name="key">The entry it names.</param>
    /// <param name="value">A listed value's code, for a term that names one.</param>
    public static TermText Term(string text, ReferenceKey key, string? value = null) =>
        string.IsNullOrEmpty(text) ? Empty : new TermText([new TermRun(text, key, value)], text);

    /// <summary>Plain words.</summary>
    /// <param name="text">The words.</param>
    public static implicit operator TermText(string? text) => Plain(text);

    /// <summary>
    /// <see cref="string.Format(IFormatProvider, string, object[])"/> with
    /// <see cref="TermText"/> arguments: their terms stay terms. Other
    /// arguments are formatted in the current culture.
    /// </summary>
    /// <param name="format">A format with <c>{0}</c>, <c>{1}</c>… and no format strings.</param>
    /// <param name="args">The arguments.</param>
    public static TermText Format(string format, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(format);
        var runs = new List<TermRun>();
        int at = 0;
        foreach (Match match in Placeholder().Matches(format))
        {
            runs.Add(new TermRun(Unescape(format[at..match.Index])));
            var arg = args[int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)];
            if (arg is TermText text)
                runs.AddRange(text.Runs);
            else
                runs.Add(new TermRun(Convert.ToString(arg, CultureInfo.CurrentCulture) ?? string.Empty));
            at = match.Index + match.Length;
        }

        runs.Add(new TermRun(Unescape(format[at..])));
        return Of(runs);
    }

    /// <summary>The texts joined by <paramref name="separator"/>, empty ones left out.</summary>
    /// <param name="separator">Plain words between them.</param>
    /// <param name="parts">The texts.</param>
    public static TermText Join(string separator, IEnumerable<TermText> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var runs = new List<TermRun>();
        bool first = true;
        foreach (var part in parts)
        {
            if (part.IsEmpty)
                continue;
            if (!first)
                runs.Add(new TermRun(separator));
            runs.AddRange(part.Runs);
            first = false;
        }

        return Of(runs);
    }

    /// <summary>The text cut at each <paramref name="separator"/> in its plain words.</summary>
    /// <param name="separator">The separator.</param>
    /// <returns>The parts, empty ones left out.</returns>
    public IReadOnlyList<TermText> Split(string separator)
    {
        var parts = new List<TermText>();
        var current = new List<TermRun>();
        foreach (var run in Runs)
        {
            if (run.IsTerm)
            {
                current.Add(run);
                continue;
            }

            var pieces = run.Text.Split(separator);
            for (int i = 0; i < pieces.Length; i++)
            {
                if (i > 0)
                {
                    parts.Add(Of(current));
                    current = [];
                }

                current.Add(new TermRun(pieces[i]));
            }
        }

        parts.Add(Of(current));
        return [.. parts.Select(Trim).Where(p => !p.IsEmpty)];
    }

    /// <summary>
    /// The text with its first letter in lower case, for the middle of a
    /// sentence: "Simplified symbols is on" after "When". An acronym keeps its capitals.
    /// </summary>
    public TermText LowerFirst()
    {
        if (Runs.Count == 0)
            return this;
        var first = Runs[0];
        var t = first.Text;
        if (t.Length < 2 || !char.IsUpper(t[0]) || char.IsUpper(t[1]))
            return this;
        return Of([first with { Text = char.ToLower(t[0], CultureInfo.CurrentCulture) + t[1..] }, .. Runs.Skip(1)]);
    }

    /// <inheritdoc />
    public bool Equals(TermText? other) => other is not null && string.Equals(Text, other.Text, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TermText other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Text);

    /// <inheritdoc />
    public override string ToString() => Text;

    // Spaces at either end of a part that a separator cut.
    private static TermText Trim(TermText text)
    {
        if (text.Runs.Count == 0)
            return text;
        var runs = text.Runs.ToList();
        if (!runs[0].IsTerm)
            runs[0] = new TermRun(runs[0].Text.TrimStart());
        if (!runs[^1].IsTerm)
            runs[^1] = new TermRun(runs[^1].Text.TrimEnd());
        return Of(runs);
    }

    private static string Unescape(string text) =>
        text.Replace("{{", "{", StringComparison.Ordinal).Replace("}}", "}", StringComparison.Ordinal);

    [GeneratedRegex(@"(?<!\{)\{(\d+)\}(?!\})")]
    private static partial Regex Placeholder();
}
