using System.Globalization;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// What the bounds of a <see cref="PortrayalTimeInterval"/> are: the
/// <c>Date</c>, <c>Time</c> or <c>DateTime</c> command that set them (S-100
/// Part 9a §9a-11.2.6).
/// </summary>
public enum PortrayalTimeKind
{
    /// <summary>Calendar dates (<c>S100_Date</c> or <c>S100_TruncatedDate</c>), e.g. <c>20131130</c> or <c>----03--</c>.</summary>
    Date = 0,

    /// <summary>Times of day (<c>S100_Time</c>), e.g. <c>0800</c>, <c>080000Z</c>.</summary>
    Time = 1,

    /// <summary>Instants (<c>S100_DateTime</c>), e.g. <c>20131130T080000Z</c>.</summary>
    DateTime = 2,
}

/// <summary>
/// Which bounds of a <see cref="PortrayalTimeInterval"/> apply and whether
/// each is included: the S-100 <c>S100_IntervalType</c> named by a
/// <c>TimeValid</c> command (S-100 Part 9a §9a-11.2.6).
/// </summary>
/// <remarks>The numeric values are persisted by the portrayal cache; don't renumber them.</remarks>
public enum TimeIntervalClosure
{
    /// <summary>Begin &lt; t &lt; end.</summary>
    OpenInterval = 0,

    /// <summary>Begin ≤ t &lt; end.</summary>
    GeLtInterval = 1,

    /// <summary>Begin &lt; t ≤ end.</summary>
    GtLeInterval = 2,

    /// <summary>Begin ≤ t ≤ end.</summary>
    ClosedInterval = 3,

    /// <summary>Begin &lt; t; the end is ignored.</summary>
    GtSemiInterval = 4,

    /// <summary>Begin ≤ t; the end is ignored.</summary>
    GeSemiInterval = 5,

    /// <summary>T &lt; end; the begin is ignored.</summary>
    LtSemiInterval = 6,

    /// <summary>T ≤ end; the begin is ignored.</summary>
    LeSemiInterval = 7,
}

/// <summary>
/// The time a map shows, for drawing instructions that are only valid at some
/// times: one instant, or a range when the mariner views a period (S-98
/// §12.8.1). Dates and times without an offset are read in
/// <see cref="Zone"/>.
/// </summary>
/// <param name="Start">The view time, or the start of the viewed range.</param>
/// <param name="End">The end of the viewed range; <see cref="Start"/> for one instant.</param>
/// <param name="Zone">
/// The time zone calendar dates and times of day are read in. A host passes
/// the zone it shows times in (the machine's zone or UTC), never the local
/// time of the chart.
/// </param>
public sealed record PortrayalTime(DateTimeOffset Start, DateTimeOffset End, TimeZoneInfo Zone)
{
    /// <summary>One instant, with dates read in <paramref name="zone"/> (UTC when <see langword="null"/>).</summary>
    /// <param name="time">The view time.</param>
    /// <param name="zone">The time zone dates are read in.</param>
    /// <returns>The portrayal time.</returns>
    public static PortrayalTime At(DateTimeOffset time, TimeZoneInfo? zone = null) =>
        new(time, time, zone ?? TimeZoneInfo.Utc);

    /// <summary>A viewed range, with dates read in <paramref name="zone"/> (UTC when <see langword="null"/>).</summary>
    /// <param name="start">The start of the range.</param>
    /// <param name="end">The end of the range; swapped with <paramref name="start"/> when earlier.</param>
    /// <param name="zone">The time zone dates are read in.</param>
    /// <returns>The portrayal time.</returns>
    public static PortrayalTime Between(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo? zone = null) =>
        end < start
            ? new(end, start, zone ?? TimeZoneInfo.Utc)
            : new(start, end, zone ?? TimeZoneInfo.Utc);
}

/// <summary>
/// A time interval a drawing instruction is valid in: one entry of its
/// <see cref="DrawingInstruction.TimeValid"/> list, from a
/// <c>Date</c>/<c>Time</c>/<c>DateTime</c> command and the <c>TimeValid</c>
/// command that follows it (S-100 Part 9a §9a-11.2.6).
/// </summary>
/// <remarks>
/// <para>
/// Bounds are kept as the portrayal emitted them. A missing bound is
/// unbounded. A date bound may be truncated (S-100 Part 1
/// <c>S100_TruncatedDate</c>): a periodic range such as <c>----03--</c> to
/// <c>----10--</c> compares only the month, so it holds from March to the end
/// of October every year, and one whose begin is later than its end (November
/// to February) runs over the year end. Times of day wrap over midnight the
/// same way.
/// </para>
/// <para>
/// Intervals compare by value, so a host can collect the distinct intervals of
/// a display list cheaply.
/// </para>
/// </remarks>
/// <param name="Kind">What the bounds are.</param>
/// <param name="Begin">The begin bound as emitted, or <see langword="null"/> for none.</param>
/// <param name="End">The end bound as emitted, or <see langword="null"/> for none.</param>
/// <param name="Closure">Which bounds apply and whether they're included.</param>
public sealed record PortrayalTimeInterval(
    PortrayalTimeKind Kind,
    string? Begin,
    string? End,
    TimeIntervalClosure Closure)
{
    // Ranges longer than this are checked by their ends only (fixed dates) or
    // over one year (periodic dates).
    private const int MaxDaysScanned = 366;

    /// <summary>Whether the interval holds at <paramref name="time"/>, with dates read in <paramref name="zone"/>.</summary>
    /// <param name="time">The instant to test.</param>
    /// <param name="zone">The time zone dates and times of day are read in.</param>
    /// <returns><see langword="true"/> when the interval holds.</returns>
    public bool Contains(DateTimeOffset time, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return Kind switch
        {
            PortrayalTimeKind.Date => ContainsDate(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, zone).DateTime)),
            PortrayalTimeKind.Time => ContainsTime(time, zone),
            _ => ContainsInstant(time, zone),
        };
    }

    /// <summary>
    /// Whether the interval holds at any time in <paramref name="time"/>: at
    /// its instant, or anywhere in its range.
    /// </summary>
    /// <param name="time">The time the map shows.</param>
    /// <returns><see langword="true"/> when the interval holds then.</returns>
    public bool Overlaps(PortrayalTime time)
    {
        ArgumentNullException.ThrowIfNull(time);
        if (time.Start == time.End)
            return Contains(time.Start, time.Zone);

        return Kind switch
        {
            PortrayalTimeKind.Date => OverlapsDates(time),
            _ => Candidates(time).Any(t => Contains(t, time.Zone)),
        };
    }

    /// <summary>Parses a <c>TimeValid</c> closure name (case-insensitive).</summary>
    /// <param name="value">The name, e.g. <c>closedInterval</c>.</param>
    /// <param name="closure">The closure, when the name is known.</param>
    /// <returns><see langword="true"/> when the name is known.</returns>
    public static bool TryParseClosure(string? value, out TimeIntervalClosure closure) =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out closure)
        && Enum.IsDefined(closure)
        && !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    private bool UsesBegin => Closure is not (TimeIntervalClosure.LtSemiInterval or TimeIntervalClosure.LeSemiInterval);

    private bool UsesEnd => Closure is not (TimeIntervalClosure.GtSemiInterval or TimeIntervalClosure.GeSemiInterval);

    private bool IncludesBegin => Closure is TimeIntervalClosure.GeLtInterval
        or TimeIntervalClosure.ClosedInterval or TimeIntervalClosure.GeSemiInterval;

    private bool IncludesEnd => Closure is TimeIntervalClosure.GtLeInterval
        or TimeIntervalClosure.ClosedInterval or TimeIntervalClosure.LeSemiInterval;

    // Combines the per-bound tests; each compare is the bound against the
    // value, negative when the bound is earlier. When both bounds apply and the
    // begin is later than the end (a periodic range over the year end, or
    // times over midnight), either test is enough.
    private bool Combine(int? beginCompare, int? endCompare, bool wraps)
    {
        var afterBegin = beginCompare is not { } b || (IncludesBegin ? b <= 0 : b < 0);
        var beforeEnd = endCompare is not { } e || (IncludesEnd ? e >= 0 : e > 0);
        return wraps ? afterBegin || beforeEnd : afterBegin && beforeEnd;
    }

    // ── Dates ──

    private bool ContainsDate(DateOnly date)
    {
        var begin = UsesBegin ? DateBound.Parse(Begin) : null;
        var end = UsesEnd ? DateBound.Parse(End) : null;
        var wraps = begin is { } b && end is { } e && b.Year is null && e.Year is null && b.CompareTo(e) > 0;
        return Combine(begin?.CompareDate(date), end?.CompareDate(date), wraps);
    }

    private bool OverlapsDates(PortrayalTime time)
    {
        var first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.Start, time.Zone).DateTime);
        var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.End, time.Zone).DateTime);
        var days = last.DayNumber - first.DayNumber;
        var begin = UsesBegin ? DateBound.Parse(Begin) : null;
        var end = UsesEnd ? DateBound.Parse(End) : null;
        if (days > MaxDaysScanned
            && (begin is not null || end is not null)
            && begin?.Year is not null == begin is not null
            && end?.Year is not null == end is not null)
        {
            // Fixed dates: the valid dates are one run, so the range meets it
            // when its last day is on or after the begin and its first day on
            // or before the end.
            return Combine(begin?.CompareDate(last), null, wraps: false)
                && Combine(null, end?.CompareDate(first), wraps: false);
        }

        for (var day = 0; day <= Math.Min(days, MaxDaysScanned); day++)
        {
            if (ContainsDate(first.AddDays(day)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// A date bound with the parts it gives: <c>YYYYMMDD</c> with any part
    /// replaced by dashes (<c>----MMDD</c>, <c>----MM--</c>, <c>YYYY----</c>),
    /// or the extended <c>YYYY-MM-DD</c>.
    /// </summary>
    private readonly record struct DateBound(int? Year, int? Month, int? Day)
    {
        public static DateBound? Parse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var text = value.Trim();
            if (text.Length == 10 && text[4] == '-' && text[7] == '-' && char.IsDigit(text[0]))
                text = string.Concat(text.AsSpan(0, 4), text.AsSpan(5, 2), text.AsSpan(8, 2));
            if (text.Length != 8)
                return null;

            var year = Part(text, 0, 4);
            var month = Part(text, 4, 2);
            var day = Part(text, 6, 2);
            return year is null && month is null && day is null ? null : new DateBound(year, month, day);
        }

        private static int? Part(string text, int start, int length) =>
            int.TryParse(text.AsSpan(start, length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

        // Compares this bound with a date over the parts the bound gives:
        // negative when the bound is earlier.
        public int CompareDate(DateOnly date) => Compare(this, new DateBound(
            Year is null ? null : date.Year,
            Month is null ? null : date.Month,
            Day is null ? null : date.Day));

        public int CompareTo(DateBound other) => Compare(this, other);

        private static int Compare(DateBound a, DateBound b)
        {
            var c = Nullable.Compare(a.Year, b.Year);
            if (c != 0 && a.Year is not null && b.Year is not null)
                return c;
            c = a.Month is not null && b.Month is not null ? a.Month.Value.CompareTo(b.Month.Value) : 0;
            if (c != 0)
                return c;
            return a.Day is not null && b.Day is not null ? a.Day.Value.CompareTo(b.Day.Value) : 0;
        }
    }

    // ── Times of day ──

    private bool ContainsTime(DateTimeOffset time, TimeZoneInfo zone)
    {
        var begin = UsesBegin ? TimeBound.Parse(Begin) : null;
        var end = UsesEnd ? TimeBound.Parse(End) : null;
        var wraps = begin is { } b && end is { } e && b.TimeOfDay > e.TimeOfDay;
        return Combine(
            begin is { } bb ? bb.TimeOfDay.CompareTo(bb.TimeOfDayOf(time, zone)) : null,
            end is { } ee ? ee.TimeOfDay.CompareTo(ee.TimeOfDayOf(time, zone)) : null,
            wraps);
    }

    /// <summary>A time-of-day bound: <c>HHMM[SS]</c> or <c>HH:MM[:SS]</c>, then optionally <c>Z</c> or <c>±HH[MM]</c>.</summary>
    private readonly record struct TimeBound(TimeSpan TimeOfDay, TimeSpan? Offset)
    {
        public static TimeBound? Parse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var text = value.Trim().Replace(":", "", StringComparison.Ordinal);
            TimeSpan? offset = null;
            if (text.EndsWith('Z') || text.EndsWith('z'))
            {
                offset = TimeSpan.Zero;
                text = text[..^1];
            }
            else if (text.LastIndexOfAny(['+', '-']) is var sign and > 0)
            {
                if (!TryParseClock(text[(sign + 1)..], out var o))
                    return null;
                offset = text[sign] == '-' ? -o : o;
                text = text[..sign];
            }

            return TryParseClock(text, out var timeOfDay) ? new TimeBound(timeOfDay, offset) : null;
        }

        // HH, HHMM or HHMMSS, with optional fractional seconds.
        private static bool TryParseClock(string text, out TimeSpan value)
        {
            value = default;
            var fraction = text.IndexOf('.', StringComparison.Ordinal);
            var whole = fraction < 0 ? text : text[..fraction];
            if (whole.Length is not (2 or 4 or 6) || !whole.All(char.IsAsciiDigit))
                return false;

            var hours = int.Parse(whole.AsSpan(0, 2), CultureInfo.InvariantCulture);
            var minutes = whole.Length >= 4 ? int.Parse(whole.AsSpan(2, 2), CultureInfo.InvariantCulture) : 0;
            var seconds = whole.Length == 6 ? int.Parse(whole.AsSpan(4, 2), CultureInfo.InvariantCulture) : 0;
            if (hours > 24 || minutes > 59 || seconds > 60)
                return false;

            value = new TimeSpan(hours, minutes, seconds);
            if (fraction >= 0 && double.TryParse("0" + text[fraction..], NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                value += TimeSpan.FromSeconds(f);
            return true;
        }

        public TimeSpan TimeOfDayOf(DateTimeOffset time, TimeZoneInfo zone) =>
            (Offset is { } o ? time.ToOffset(o) : TimeZoneInfo.ConvertTime(time, zone)).TimeOfDay;

        // This bound on the calendar day of time, as an instant.
        public DateTimeOffset On(DateTimeOffset time, TimeZoneInfo zone)
        {
            if (Offset is { } o)
                return new DateTimeOffset(time.ToOffset(o).Date + TimeOfDay, o);
            var local = TimeZoneInfo.ConvertTime(time, zone).Date + TimeOfDay;
            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }
    }

    // ── Instants ──

    private bool ContainsInstant(DateTimeOffset time, TimeZoneInfo zone)
    {
        var begin = UsesBegin ? ParseInstant(Begin, zone) : null;
        var end = UsesEnd ? ParseInstant(End, zone) : null;
        return Combine(begin?.CompareTo(time), end?.CompareTo(time), wraps: false);
    }

    private static readonly string[] InstantFormats =
    [
        "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm", "yyyyMMdd'T'HH",
        "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm", "yyyyMMdd",
    ];

    /// <summary>An instant: <c>YYYYMMDDTHHMMSS</c> or ISO 8601 extended, optionally with <c>Z</c> or an offset; read in <paramref name="zone"/> without one.</summary>
    private static DateTimeOffset? ParseInstant(string? value, TimeZoneInfo zone)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();
        var t = text.IndexOf('T', StringComparison.OrdinalIgnoreCase);
        var date = t < 0 ? text : text[..t];
        TimeBound? clock = null;
        if (t >= 0)
        {
            clock = TimeBound.Parse(text[(t + 1)..]);
            if (clock is null)
                return null;
        }

        if (!System.DateTime.TryParseExact(date, ["yyyyMMdd", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return null;

        var local = day + (clock?.TimeOfDay ?? TimeSpan.Zero);
        return clock?.Offset is { } offset
            ? new DateTimeOffset(local, offset)
            : new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    // ── Ranges of times and instants ──

    // Where an interval of times or instants can first hold within a range:
    // the range's ends and each bound (and just after it, for an excluded
    // begin) that falls inside it.
    private IEnumerable<DateTimeOffset> Candidates(PortrayalTime time)
    {
        yield return time.Start;
        yield return time.End;

        var bounds = new List<DateTimeOffset>();
        foreach (var raw in new[] { UsesBegin ? Begin : null, UsesEnd ? End : null })
        {
            if (Kind == PortrayalTimeKind.Time)
            {
                if (TimeBound.Parse(raw) is not { } bound)
                    continue;
                for (var day = -1; day <= Math.Min((time.End - time.Start).Days + 1, 2); day++)
                    bounds.Add(bound.On(time.Start.AddDays(day), time.Zone));
            }
            else if (ParseInstant(raw, time.Zone) is { } instant)
            {
                bounds.Add(instant);
            }
        }

        foreach (var bound in bounds)
        {
            foreach (var t in new[] { bound, bound.AddSeconds(1) })
            {
                if (t >= time.Start && t <= time.End)
                    yield return t;
            }
        }
    }
}
