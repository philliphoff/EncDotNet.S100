namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>Why a navigational warning has the status <see cref="NavigationalWarningSet.StatusAt"/> gives.</summary>
public enum NavigationalWarningStatusReason
{
    /// <summary>The warning is in force.</summary>
    None,

    /// <summary>Its validity ranges: none has started yet, or every one has ended.</summary>
    Validity,

    /// <summary>Its own cancellation date has passed.</summary>
    CancellationDate,

    /// <summary>Another warning cancels it (S-124 <c>References</c> with <c>referenceCategory</c> 1).</summary>
    Cancelled,

    /// <summary>
    /// The latest in-force bulletin for its series, published after it,
    /// doesn't list it (S-124 Edition 2.0.0 §9.2.1).
    /// </summary>
    NotInBulletin,

    /// <summary>It is an in-force bulletin, and a later bulletin for its series replaces it (§8.1.3 Table 8-1).</summary>
    BulletinReplaced,
}

/// <summary>A navigational warning's status at a time, and why.</summary>
/// <param name="Status">Whether the warning applies.</param>
/// <param name="Reason">Why; <see cref="NavigationalWarningStatusReason.None"/> while in force.</param>
/// <param name="By">
/// The warning that ended it: the cancelling warning, or the in-force
/// bulletin that leaves it out or replaces it. <c>null</c> for the other reasons.
/// </param>
public sealed record NavigationalWarningState(
    NavigationalWarningStatus Status,
    NavigationalWarningStatusReason Reason,
    NavigationalWarningInfo? By = null)
{
    /// <summary>In force.</summary>
    public static NavigationalWarningState InForce { get; } = new(NavigationalWarningStatus.InForce, NavigationalWarningStatusReason.None);

    /// <summary>Whether another warning ended it: a cancellation, or an in-force bulletin.</summary>
    public bool IsWithdrawn => Reason is NavigationalWarningStatusReason.CancellationDate
        or NavigationalWarningStatusReason.Cancelled
        or NavigationalWarningStatusReason.NotInBulletin
        or NavigationalWarningStatusReason.BulletinReplaced;
}

/// <summary>
/// The latest in-force bulletin of a series, and what it lists that isn't
/// loaded (S-124 Edition 2.0.0 §9.2.2: "an indication should be given").
/// </summary>
/// <param name="Series">The bulletin's series, as it writes it.</param>
/// <param name="Bulletin">The bulletin.</param>
/// <param name="NotReceived">
/// The warnings it lists as in force with no loaded warning to match, in its
/// order; the bulletin's reference to itself is never one of them.
/// </param>
public sealed record NavigationalWarningBulletinReport(
    string Series,
    NavigationalWarningInfo Bulletin,
    IReadOnlyList<NavigationalWarningReference> NotReceived);

/// <summary>
/// Every loaded navigational warning, judged together: a warning's status
/// depends on the others, which may cancel it or, as an in-force bulletin,
/// leave it out (S-124 Edition 2.0.0 §9.2.1, §9.3).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StatusAt"/> applies, in order: the warning's own cancellation
/// date; a warning that cancels it and was published at or before the time;
/// for an in-force bulletin, a later bulletin for the same series; the latest
/// in-force bulletin for its series published at or before the time, when the
/// warning was published before that bulletin and the bulletin doesn't list
/// it; and last the warning's validity ranges
/// (<see cref="NavigationalWarningInfo.StatusAt"/>).
/// </para>
/// <para>
/// Only the latest bulletin counts. A warning without a series, a number or a
/// publication time, and a bulletin without a publication time, are never
/// judged by bulletins: without them "prior to and not found in" can't be
/// decided, and keeping a warning in force is the safe way to be wrong.
/// Edition 1.0.0 gives no publication time, so it has no bulletin rule.
/// </para>
/// <para>
/// A warning left out of the bulletin is reported as cancelled, as S-98
/// Edition 2.0.0 §12.12 has ECDIS do: it stays listed, marked cancelled, and
/// isn't drawn on the navigation screen. (S-124 §9.3 forbids producers from
/// using the bulletin to cancel; that is about encoding, not this status.)
/// <see cref="IsInForceAt"/> is the one test a renderer needs.
/// </para>
/// </remarks>
public sealed class NavigationalWarningSet
{
    private readonly List<NavigationalWarningInfo> _cancellers = [];
    private readonly List<NavigationalWarningInfo> _bulletins = [];
    private readonly List<NavigationalWarningReference> _loaded = [];

    /// <summary>An empty set, in which each warning is judged on its own.</summary>
    public static NavigationalWarningSet Empty { get; } = new([]);

    /// <summary>Builds the set.</summary>
    /// <param name="warnings">
    /// Every loaded warning, including cancellations and in-force bulletins
    /// without parts. A warning may appear more than once (once per part).
    /// </param>
    public NavigationalWarningSet(IEnumerable<NavigationalWarningInfo> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        foreach (var warning in warnings)
        {
            ArgumentNullException.ThrowIfNull(warning, nameof(warnings));
            Count++;
            if (warning.Reference is { } reference)
                _loaded.Add(reference);
            if (warning.Cancels.Count > 0)
                _cancellers.Add(warning);
            if (warning.InForceBulletin is not null && warning.PublicationTime is not null && SeriesOf(warning) is not null)
                _bulletins.Add(warning);
        }

        // Latest first, so the first match is the latest bulletin.
        _bulletins.Sort((a, b) => b.PublicationTime!.Value.CompareTo(a.PublicationTime!.Value));
    }

    /// <summary>How many warnings (or parts) the set was built from.</summary>
    public int Count { get; }

    /// <summary>
    /// The warning's status at <paramref name="time"/>, with the reason. The
    /// warning need not be in the set; it is judged against the set's warnings.
    /// </summary>
    /// <param name="warning">The warning (or one of its parts).</param>
    /// <param name="time">The time to test, usually the view time.</param>
    /// <returns>The status and why.</returns>
    public NavigationalWarningState StatusAt(NavigationalWarningInfo warning, DateTimeOffset time)
    {
        ArgumentNullException.ThrowIfNull(warning);
        if (WithdrawnAt(warning, time) is { } withdrawn)
            return withdrawn;
        return warning.StatusAt(time) switch
        {
            NavigationalWarningStatus.InForce => NavigationalWarningState.InForce,
            var status => new NavigationalWarningState(status, NavigationalWarningStatusReason.Validity),
        };
    }

    /// <summary>Whether the warning is in force at <paramref name="time"/>, judged with the set.</summary>
    /// <param name="warning">The warning (or one of its parts).</param>
    /// <param name="time">The time to test.</param>
    /// <returns><c>true</c> when <see cref="StatusAt"/> gives <see cref="NavigationalWarningStatus.InForce"/>.</returns>
    public bool IsInForceAt(NavigationalWarningInfo warning, DateTimeOffset time) =>
        StatusAt(warning, time).Status == NavigationalWarningStatus.InForce;

    /// <summary>
    /// The latest in-force bulletin of each series published at or before
    /// <paramref name="time"/>, with the listed warnings that aren't loaded
    /// (§9.2.2), latest first.
    /// </summary>
    /// <param name="time">The time.</param>
    /// <returns>One report per series that has a bulletin by then.</returns>
    public IReadOnlyList<NavigationalWarningBulletinReport> LatestBulletins(DateTimeOffset time)
    {
        var reports = new List<NavigationalWarningBulletinReport>();
        foreach (var bulletin in _bulletins)
        {
            var series = SeriesOf(bulletin)!;
            if (bulletin.PublicationTime > time || reports.Any(r => NavigationalWarningReference.SameSeries(r.Series, series)))
                continue;
            var notReceived = bulletin.InForceBulletin!.NoMessageOnHand
                ? []
                : bulletin.InForceBulletin.InForce
                    .Where(listed => !(bulletin.Reference?.Matches(listed) ?? false) && !_loaded.Any(l => l.Matches(listed)))
                    .ToList();
            reports.Add(new NavigationalWarningBulletinReport(series, bulletin, notReceived));
        }

        return reports;
    }

    /// <summary>
    /// Why the warning has ended at <paramref name="time"/> whatever its
    /// validity ranges say: its cancellation date, a cancellation, or an
    /// in-force bulletin. <c>null</c> when nothing withdraws it.
    /// </summary>
    /// <param name="warning">The warning (or one of its parts).</param>
    /// <param name="time">The time to test.</param>
    /// <returns>An <see cref="NavigationalWarningStatus.Ended"/> state, or <c>null</c>.</returns>
    public NavigationalWarningState? WithdrawnAt(NavigationalWarningInfo warning, DateTimeOffset time)
    {
        ArgumentNullException.ThrowIfNull(warning);
        if (warning.CancellationDate is { } cancelled && time >= cancelled)
            return Ended(NavigationalWarningStatusReason.CancellationDate);
        if (warning.Reference is not { } reference)
            return null;

        foreach (var other in _cancellers)
        {
            if (other.Reference is { } own && own.Matches(reference))
                continue;
            if ((other.PublicationTime is not { } published || published <= time) && other.Cancels.Any(c => c.Matches(reference)))
                return Ended(NavigationalWarningStatusReason.Cancelled, other);
        }

        if (warning.PublicationTime is not { } issued || SeriesOf(warning) is not { } series)
            return null;
        var latest = LatestBulletin(series, time);
        if (latest is null || latest.PublicationTime <= issued || latest.Reference is { } latestRef && latestRef.Matches(reference))
            return null;
        if (warning.InForceBulletin is not null)
            return Ended(NavigationalWarningStatusReason.BulletinReplaced, latest);
        return latest.InForceBulletin!.Lists(reference) ? null : Ended(NavigationalWarningStatusReason.NotInBulletin, latest);
    }

    /// <summary>The latest in-force bulletin for a series published at or before <paramref name="time"/>, or <c>null</c>.</summary>
    /// <param name="series">The series name (e.g. <c>"NAVAREA X"</c>), compared ignoring case and spacing.</param>
    /// <param name="time">The time.</param>
    /// <returns>The bulletin, or <c>null</c> when none is loaded.</returns>
    public NavigationalWarningInfo? LatestBulletin(string series, DateTimeOffset time)
    {
        ArgumentNullException.ThrowIfNull(series);
        return _bulletins.FirstOrDefault(b => b.PublicationTime <= time && NavigationalWarningReference.SameSeries(SeriesOf(b), series));
    }

    private static NavigationalWarningState Ended(NavigationalWarningStatusReason reason, NavigationalWarningInfo? by = null) =>
        new(NavigationalWarningStatus.Ended, reason, by);

    private static string? SeriesOf(NavigationalWarningInfo warning) =>
        warning.Reference?.Series is { Length: > 0 } series && !string.IsNullOrWhiteSpace(series)
            ? series
            : string.IsNullOrWhiteSpace(warning.Series) ? null : warning.Series;
}
