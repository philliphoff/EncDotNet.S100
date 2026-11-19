using System.Globalization;

namespace EncDotNet.S100.Cli.Web;

/// <summary>
/// The zone the page shows times in (handoff D20): the browser's, read
/// through JS interop when the circuit starts, or UTC. Never the data area's.
/// </summary>
/// <remarks>
/// The server can't know the browser's zone rules, so the browser reports its
/// UTC offset and abbreviation at the instants the page shows (the time
/// steps, issue times and now). Another instant takes the offset of the
/// latest sample at or before it, else the first.
/// </remarks>
public sealed class ServeTimeZone
{
    private readonly ServeZoneSample[] _samples;

    private ServeTimeZone(string id, ServeZoneSample[] samples)
    {
        Id = id;
        _samples = samples;
    }

    /// <summary>UTC.</summary>
    public static ServeTimeZone Utc { get; } = new("UTC", []);

    /// <summary>The IANA id, <c>Europe/London</c>; <c>UTC</c> for UTC.</summary>
    public string Id { get; }

    /// <summary>Whether this is UTC, rather than a browser's zone.</summary>
    public bool IsUtc => _samples.Length == 0;

    /// <summary>The browser's zone, from the offsets and abbreviations it reported.</summary>
    /// <param name="id">The IANA id, from <c>Intl.DateTimeFormat().resolvedOptions().timeZone</c>.</param>
    /// <param name="samples">The offset and abbreviation at some instants.</param>
    public static ServeTimeZone FromBrowser(string id, IEnumerable<ServeZoneSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var ordered = samples.Select(s => s with { Utc = AsUtc(s.Utc) }).OrderBy(s => s.Utc).ToArray();
        return ordered.Length == 0 || string.IsNullOrWhiteSpace(id) ? Utc : new ServeTimeZone(id, ordered);
    }

    /// <summary>The offset from UTC at <paramref name="utc"/>.</summary>
    public TimeSpan OffsetAt(DateTime utc) => Sample(utc)?.Offset ?? TimeSpan.Zero;

    /// <summary>The abbreviation at <paramref name="utc"/>: <c>BST</c>, <c>GMT+9</c>, or <c>UTC</c>.</summary>
    public string AbbreviationAt(DateTime utc) => Sample(utc) is { Abbreviation.Length: > 0 } s ? s.Abbreviation : IsUtc ? "UTC" : FormatOffset(OffsetAt(utc));

    /// <summary>The wall-clock time in this zone at <paramref name="utc"/>.</summary>
    public DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(AsUtc(utc) + OffsetAt(utc), DateTimeKind.Unspecified);

    /// <summary>"Europe/London (BST)", with the abbreviation at <paramref name="utc"/>.</summary>
    public string Describe(DateTime utc) => IsUtc ? "UTC" : Id + " (" + AbbreviationAt(utc) + ")";

    internal static DateTime AsUtc(DateTime time) => time.Kind switch
    {
        DateTimeKind.Utc => time,
        DateTimeKind.Local => time.ToUniversalTime(),
        _ => DateTime.SpecifyKind(time, DateTimeKind.Utc),
    };

    private ServeZoneSample? Sample(DateTime utc)
    {
        if (_samples.Length == 0)
            return null;
        var at = AsUtc(utc);
        var chosen = _samples[0];
        foreach (var sample in _samples)
        {
            if (sample.Utc > at)
                break;
            chosen = sample;
        }

        return chosen;
    }

    private static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var a = offset.Duration();
        return "GMT" + sign + (a.Minutes == 0
            ? a.Hours.ToString(CultureInfo.InvariantCulture)
            : a.Hours.ToString(CultureInfo.InvariantCulture) + ":" + a.Minutes.ToString("00", CultureInfo.InvariantCulture));
    }
}

/// <summary>The browser's offset from UTC and its abbreviation at one instant.</summary>
/// <param name="Utc">The instant.</param>
/// <param name="Offset">The offset from UTC.</param>
/// <param name="Abbreviation">The short name, <c>BST</c>; empty when the browser gave none.</param>
public readonly record struct ServeZoneSample(DateTime Utc, TimeSpan Offset, string Abbreviation);

/// <summary>Where the time steps are against now (handoff D20, B8d).</summary>
public enum ServeTimeSpan
{
    /// <summary>Now is within the steps, or there are none.</summary>
    Current,

    /// <summary>Every step is in the past.</summary>
    AllPast,

    /// <summary>Every step is in the future.</summary>
    AllFuture,
}

/// <summary>A tick under the time bar's track where a day starts.</summary>
/// <param name="Fraction">Where along the track, 0 to 1.</param>
/// <param name="Label">"Thu 1".</param>
public readonly record struct ServeDayTick(double Fraction, string Label);

/// <summary>What the time bar says about the step shown: "S-111 · Forecast · issued … · step 41 of 288, every 15 min".</summary>
/// <param name="Products">The products with data at the step, as tags.</param>
/// <param name="Line">The rest of the line.</param>
public sealed record ServeStepContext(IReadOnlyList<string> Products, string Line);

/// <summary>The bar's note when every step is in the past or the future (B8d), with the step it offers.</summary>
/// <param name="Text">The note.</param>
/// <param name="ActionLabel">"Go to latest" or "Go to first step"; <see langword="null"/> when already there.</param>
/// <param name="ActionIndex">The step the action goes to.</param>
public sealed record ServeTimeNotice(string Text, string? ActionLabel, int ActionIndex);

/// <summary>The time bar's logic (handoff D19, D20, D36): steps, formats, and <c>t=</c> in the hash.</summary>
internal static class ServeTimes
{
    /// <summary>The hash parameter for the time step.</summary>
    public const string HashParameter = "t";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>
    /// The step to open on: the one <paramref name="hashTime"/> names (the
    /// nearest), else the latest at or before now when now is within the
    /// steps, else the first, as the tiles default (B8d).
    /// </summary>
    public static int StartIndex(IReadOnlyList<DateTime> times, DateTime now, DateTime? hashTime)
    {
        ArgumentNullException.ThrowIfNull(times);
        if (times.Count == 0)
            return 0;
        if (hashTime is { } t)
            return Nearest(times, t);
        var at = ServeTimeZone.AsUtc(now);
        if (Span(times, at) != ServeTimeSpan.Current)
            return 0;
        var index = 0;
        for (int i = 0; i < times.Count; i++)
        {
            if (ServeTimeZone.AsUtc(times[i]) <= at)
                index = i;
        }

        return index;
    }

    /// <summary>The step nearest <paramref name="time"/>.</summary>
    public static int Nearest(IReadOnlyList<DateTime> times, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(times);
        var at = ServeTimeZone.AsUtc(time);
        var best = 0;
        var bestDistance = TimeSpan.MaxValue;
        for (int i = 0; i < times.Count; i++)
        {
            var distance = (ServeTimeZone.AsUtc(times[i]) - at).Duration();
            if (distance < bestDistance)
                (best, bestDistance) = (i, distance);
        }

        return best;
    }

    /// <summary>Whether every step is in the past or the future.</summary>
    public static ServeTimeSpan Span(IReadOnlyList<DateTime> times, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(times);
        if (times.Count == 0)
            return ServeTimeSpan.Current;
        var at = ServeTimeZone.AsUtc(now);
        if (ServeTimeZone.AsUtc(times[^1]) < at)
            return ServeTimeSpan.AllPast;
        if (ServeTimeZone.AsUtc(times[0]) > at)
            return ServeTimeSpan.AllFuture;
        return ServeTimeSpan.Current;
    }

    /// <summary>
    /// The note when every step is in the past ("All 288 time steps are in the
    /// past (1–4 Apr 2021)…", offering the latest) or the future ("…Starts
    /// Thu 1 Apr 2021, 11:00 BST.", offering the first); <see langword="null"/> otherwise.
    /// </summary>
    public static ServeTimeNotice? Notice(IReadOnlyList<DateTime> times, int index, DateTime now, ServeTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(zone);
        var count = times.Count == 1 ? "The time step is" : "All " + times.Count.ToString(Invariant) + " time steps are";
        switch (Span(times, now))
        {
            case ServeTimeSpan.AllPast:
                var text = count + " in the past (" + DateRange(times[0], times[^1], zone) + ").";
                if (index == 0 && times.Count > 1)
                    text += " Showing the first one, as the tiles do.";
                return new ServeTimeNotice(text, index == times.Count - 1 ? null : "Go to latest", times.Count - 1);
            case ServeTimeSpan.AllFuture:
                return new ServeTimeNotice(
                    count + " in the future. Starts " + FormatDate(times[0], zone) + ", " + FormatClock(times[0], zone) + ".",
                    index == 0 ? null : "Go to first step",
                    0);
            default:
                return null;
        }
    }

    /// <summary>"11:00 BST", to the minute.</summary>
    public static string FormatMinute(DateTime utc, ServeTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return zone.ToLocal(utc).ToString("HH:mm", Invariant) + " " + zone.AbbreviationAt(utc);
    }

    /// <summary>"Thu 1 Apr 2021", in the zone.</summary>
    public static string FormatDate(DateTime utc, ServeTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return zone.ToLocal(utc).ToString("ddd d MMM yyyy", Invariant);
    }

    /// <summary>"11:00 BST" or "10:00 UTC"; seconds when the time has them.</summary>
    public static string FormatClock(DateTime utc, ServeTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = zone.ToLocal(utc);
        return local.ToString(local.Second == 0 ? "HH:mm" : "HH:mm:ss", Invariant) + " " + zone.AbbreviationAt(utc);
    }

    /// <summary>"1–4 Apr 2021", "30 Mar – 2 Apr 2021", "1 Apr 2021", in the zone.</summary>
    public static string DateRange(DateTime firstUtc, DateTime lastUtc, ServeTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var a = zone.ToLocal(firstUtc).Date;
        var b = zone.ToLocal(lastUtc).Date;
        if (a == b)
            return a.ToString("d MMM yyyy", Invariant);
        if (a.Year == b.Year && a.Month == b.Month)
            return a.Day.ToString(Invariant) + "–" + b.ToString("d MMM yyyy", Invariant);
        if (a.Year == b.Year)
            return a.ToString("d MMM", Invariant) + " – " + b.ToString("d MMM yyyy", Invariant);
        return a.ToString("d MMM yyyy", Invariant) + " – " + b.ToString("d MMM yyyy", Invariant);
    }

    /// <summary>
    /// "every 15 min", "every hour", "every 3 h", "every day": the interval
    /// most steps share; <see langword="null"/> with one step or no common interval.
    /// </summary>
    public static string? Interval(IReadOnlyList<DateTime> times)
    {
        ArgumentNullException.ThrowIfNull(times);
        if (times.Count < 2)
            return null;
        var (step, count) = Enumerable.Range(1, times.Count - 1)
            .Select(i => times[i] - times[i - 1])
            .GroupBy(d => d)
            .Select(g => (g.Key, g.Count()))
            .MaxBy(g => g.Item2);
        if (count * 2 < times.Count - 1 || step <= TimeSpan.Zero)
            return null;
        if (step.TotalDays >= 1 && step.TotalDays % 1 == 0)
            return step.TotalDays == 1 ? "every day" : "every " + step.TotalDays.ToString(Invariant) + " days";
        if (step.TotalHours >= 1 && step.TotalHours % 1 == 0)
            return step.TotalHours == 1 ? "every hour" : "every " + step.TotalHours.ToString(Invariant) + " h";
        if (step.TotalMinutes >= 1 && step.TotalMinutes % 1 == 0)
            return "every " + step.TotalMinutes.ToString(Invariant) + " min";
        return "every " + step.TotalSeconds.ToString(Invariant) + " s";
    }

    /// <summary>"step 41 of 288, every 15 min".</summary>
    public static string StepText(IReadOnlyList<DateTime> times, int index)
    {
        ArgumentNullException.ThrowIfNull(times);
        var text = "step " + (index + 1).ToString(Invariant) + " of " + times.Count.ToString(Invariant);
        return Interval(times) is { } interval ? text + ", " + interval : text;
    }

    /// <summary>
    /// The products, dynamicity and issue time of the datasets with data at
    /// the step (or of every time-varying dataset when none spans it), then
    /// the step: "Forecast · issued Wed 14 Apr 2021 06:00 BST · step 41 of 288, every 15 min".
    /// </summary>
    public static ServeStepContext Context(IReadOnlyList<ServedDataset> datasets, IReadOnlyList<DateTime> times, int index, ServeTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(zone);
        var timed = datasets.Where(d => d.Times.Count > 0).ToList();
        var at = times.Count > 0 ? ServeTimeZone.AsUtc(times[Math.Clamp(index, 0, times.Count - 1)]) : default;
        var spanning = timed.Where(d => ServeTimeZone.AsUtc(d.Times[0]) <= at && at <= ServeTimeZone.AsUtc(d.Times[^1])).ToList();
        if (spanning.Count == 0)
            spanning = timed;

        var products = spanning.Select(d => d.Product).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var parts = new List<string>();
        var dynamicity = string.Join(", ", spanning.Select(d => d.Dynamicity).OfType<string>().Where(s => s.Length > 0).Distinct(StringComparer.Ordinal));
        if (dynamicity.Length > 0)
            parts.Add(dynamicity);
        if (spanning.Select(d => d.IssueTime).OfType<DateTime>().Select(ServeTimeZone.AsUtc).DefaultIfEmpty().Max() is var issued && issued != default)
            parts.Add("issued " + FormatDate(issued, zone) + " " + FormatMinute(issued, zone));
        if (times.Count > 0)
            parts.Add(StepText(times, index));
        return new ServeStepContext(products, string.Join(" · ", parts));
    }

    /// <summary>
    /// A tick at the first step of each day in the zone, labelled "Thu 1";
    /// with many days, labels thin out so about <paramref name="maxLabels"/> show.
    /// </summary>
    public static IReadOnlyList<ServeDayTick> DayTicks(IReadOnlyList<DateTime> times, ServeTimeZone zone, int maxLabels = 4)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(zone);
        var ticks = new List<ServeDayTick>();
        if (times.Count == 0)
            return ticks;
        DateTime? day = null;
        for (int i = 0; i < times.Count; i++)
        {
            var date = zone.ToLocal(times[i]).Date;
            if (date == day)
                continue;
            day = date;
            ticks.Add(new ServeDayTick(times.Count == 1 ? 0 : (double)i / (times.Count - 1), date.ToString("ddd d", Invariant)));
        }

        // Labels keep at least a 1/maxLabels of the track apart, so days crowded by a gap in the steps don't overlap.
        var spacing = 1.0 / Math.Max(1, maxLabels) - 1e-9;
        double? labelled = null;
        for (int i = 0; i < ticks.Count; i++)
        {
            if (labelled is { } last && ticks[i].Fraction - last < spacing)
                ticks[i] = ticks[i] with { Label = "" };
            else
                labelled = ticks[i].Fraction;
        }

        return ticks;
    }

    /// <summary>A step in the hash: <c>2021-04-01T10:00Z</c>, with seconds only when it has them.</summary>
    public static string FormatHash(DateTime time)
    {
        var utc = ServeTimeZone.AsUtc(time);
        return utc.ToString(utc.Second == 0 ? "yyyy-MM-dd'T'HH:mm'Z'" : "yyyy-MM-dd'T'HH:mm:ss'Z'", Invariant);
    }

    /// <summary>The hash's <c>t=</c>, as UTC; <see langword="null"/> when it's missing or not a time.</summary>
    public static DateTime? FromHash(IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return parameters.TryGetValue(HashParameter, out var text)
            && DateTime.TryParse(text, Invariant, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time)
                ? DateTime.SpecifyKind(time, DateTimeKind.Utc)
                : null;
    }
}
