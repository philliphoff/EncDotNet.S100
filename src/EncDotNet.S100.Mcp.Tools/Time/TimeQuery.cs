using System.Collections.Immutable;

namespace EncDotNet.S100.Mcp.Tools.Time;

/// <summary>
/// Discriminated union over the supported temporal input shapes.
/// </summary>
/// <remarks>
/// <para>
/// Tools that accept a time parameter take a <see cref="TimeQuery"/>
/// rather than a bare <c>DateTimeOffset?</c> so the same wire shape
/// handles single-instant, range, and stepped-series questions.
/// </para>
/// <para>
/// <see cref="Instant"/> selects a single point in time (the existing
/// nearest-step semantics in coverage samplers). <see cref="Range"/>
/// expresses "anywhere in [from, to]" — used both as a coverage-series
/// window and as a feature-validity filter. <see cref="Series"/>
/// expresses "every <c>step</c> from <c>from</c> through <c>to</c>",
/// useful for "every 30 min for 6 hours" agent queries.
/// </para>
/// <para>
/// Construct instances via the static factory methods; the underlying
/// records validate their bounds and throw <see cref="ArgumentException"/>
/// on degenerate inputs. Use <see cref="TimeQueryJsonReader.Parse"/>
/// to construct from the wire format.
/// </para>
/// </remarks>
public abstract record TimeQuery
{
    /// <summary>Hard cap on the number of instants a <see cref="Series"/> may enumerate.</summary>
    /// <remarks>
    /// Mirrors the page-size cap on <c>query_features</c> — server-side
    /// protection against agents asking for a 1-second cadence over a
    /// week. Tools should surface a structured error when the requested
    /// cadence × window exceeds this.
    /// </remarks>
    public const int MaxSeriesCount = 4096;

    private TimeQuery() { }

    /// <summary>Single-instant query (nearest-step in coverage samplers).</summary>
    /// <param name="Value">The instant of interest, normalised to UTC.</param>
    public sealed record Instant(DateTimeOffset Value) : TimeQuery;

    /// <summary>
    /// Closed time window <c>[From, To]</c>. For coverage samplers this
    /// returns every dataset time-step whose <c>TimePoint</c> falls
    /// within the window. For feature-validity filters this matches any
    /// feature whose validity interval overlaps the window.
    /// </summary>
    /// <param name="From">Window start (inclusive), normalised to UTC.</param>
    /// <param name="To">Window end (inclusive), normalised to UTC; must be &gt;= <paramref name="From"/>.</param>
    public sealed record Range(DateTimeOffset From, DateTimeOffset To) : TimeQuery
    {
        /// <summary>Span of the window, always &gt;= <see cref="TimeSpan.Zero"/>.</summary>
        public TimeSpan Duration => To - From;
    }

    /// <summary>
    /// Stepped enumeration from <see cref="From"/> through <see cref="To"/>
    /// inclusive, separated by <see cref="Step"/>.
    /// </summary>
    /// <param name="From">First instant in the series.</param>
    /// <param name="To">Last instant the series may not exceed.</param>
    /// <param name="Step">Cadence between successive instants; must be &gt; <see cref="TimeSpan.Zero"/>.</param>
    public sealed record Series(DateTimeOffset From, DateTimeOffset To, TimeSpan Step) : TimeQuery
    {
        /// <summary>
        /// Materialises the series as an immutable array. Throws
        /// <see cref="InvalidOperationException"/> if the count would
        /// exceed <see cref="MaxSeriesCount"/>.
        /// </summary>
        public ImmutableArray<DateTimeOffset> Enumerate()
        {
            var count = EstimatedCount;
            if (count > MaxSeriesCount)
            {
                throw new InvalidOperationException(
                    $"Series would yield {count} instants which exceeds the cap of {MaxSeriesCount}.");
            }
            var builder = ImmutableArray.CreateBuilder<DateTimeOffset>(count);
            for (int i = 0; i < count; i++)
            {
                builder.Add(From + TimeSpan.FromTicks(Step.Ticks * i));
            }
            return builder.MoveToImmutable();
        }

        /// <summary>
        /// Number of instants that <see cref="Enumerate"/> would produce,
        /// without materialising them. Always at least 1 when <c>From == To</c>.
        /// </summary>
        public int EstimatedCount
        {
            get
            {
                var span = To - From;
                if (span <= TimeSpan.Zero) return 1;
                var n = (long)(span.Ticks / Step.Ticks) + 1;
                return n > int.MaxValue ? int.MaxValue : (int)n;
            }
        }
    }

    /// <summary>
    /// Creates a single-instant query. The supplied <see cref="DateTimeOffset"/>
    /// is normalised to UTC.
    /// </summary>
    public static Instant At(DateTimeOffset value) =>
        new(value.ToUniversalTime());

    /// <summary>
    /// Creates a range query. Throws <see cref="ArgumentException"/> when
    /// <paramref name="to"/> precedes <paramref name="from"/>.
    /// </summary>
    public static Range Between(DateTimeOffset from, DateTimeOffset to)
    {
        from = from.ToUniversalTime();
        to = to.ToUniversalTime();
        if (to < from)
        {
            throw new ArgumentException("'to' must be greater than or equal to 'from'.", nameof(to));
        }
        return new Range(from, to);
    }

    /// <summary>
    /// Creates a stepped-series query. Throws <see cref="ArgumentException"/>
    /// on degenerate ordering or non-positive step.
    /// </summary>
    public static Series Every(DateTimeOffset from, DateTimeOffset to, TimeSpan step)
    {
        from = from.ToUniversalTime();
        to = to.ToUniversalTime();
        if (to < from)
        {
            throw new ArgumentException("'to' must be greater than or equal to 'from'.", nameof(to));
        }
        if (step <= TimeSpan.Zero)
        {
            throw new ArgumentException("'step' must be strictly positive.", nameof(step));
        }
        return new Series(from, to, step);
    }

    /// <summary>
    /// Window covered by this query. For <see cref="Instant"/> the
    /// window is a degenerate (zero-length) range at the requested
    /// instant; for <see cref="Series"/> it's the range from
    /// <c>From</c> to <c>To</c> (the actual enumeration may end earlier
    /// when <c>(To - From) % Step != 0</c>).
    /// </summary>
    public (DateTimeOffset From, DateTimeOffset To) GetWindow() => this switch
    {
        Instant i => (i.Value, i.Value),
        Range r => (r.From, r.To),
        Series s => (s.From, s.To),
        _ => throw new InvalidOperationException("Unknown TimeQuery variant."),
    };

    /// <summary>
    /// Returns <c>true</c> when this query's window overlaps the supplied
    /// validity interval. Useful for feature-validity filtering.
    /// </summary>
    /// <param name="validityStart">Inclusive validity start; <c>null</c> means "open-ended in the past".</param>
    /// <param name="validityEnd">Inclusive validity end; <c>null</c> means "open-ended in the future".</param>
    public bool Overlaps(DateTimeOffset? validityStart, DateTimeOffset? validityEnd)
    {
        var (from, to) = GetWindow();
        if (validityEnd is { } end && end < from) return false;
        if (validityStart is { } start && start > to) return false;
        return true;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="instant"/> falls inside
    /// this query's window. For <see cref="Instant"/> requires equality.
    /// </summary>
    public bool Contains(DateTimeOffset instant)
    {
        var (from, to) = GetWindow();
        return instant >= from && instant <= to;
    }
}
