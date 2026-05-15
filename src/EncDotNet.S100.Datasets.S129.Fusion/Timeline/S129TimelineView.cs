using System.Collections.Immutable;
using EncDotNet.S100.Datasets.S129.DataModel;

namespace EncDotNet.S100.Datasets.S129.Fusion.Timeline;

/// <summary>
/// A time-indexed view over an <see cref="S129UnderKeelClearancePlan"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each <see cref="S129ControlPoint"/> in the source plan carries a
/// single <see cref="S129ControlPoint.ExpectedPassingTime"/>. The
/// timeline view surfaces these as ordered, deduplicated samples and
/// lets callers query "the UKC state at time T" via
/// <see cref="GetSnapshotAt"/>.
/// </para>
/// <para>
/// Control points whose <c>ExpectedPassingTime</c> is <c>null</c> are
/// excluded from the timeline (the underlying typed projection sorts
/// them to the tail of the plan's control-point list in source-document
/// order — see S-129 Edition 2.0.0 §<c>UnderKeelClearanceControlPoint</c>).
/// </para>
/// <para>
/// This type is purely a data accessor: it returns typed values and
/// never produces drawing instructions, never depends on a renderer,
/// and never assumes a UI is present.
/// </para>
/// </remarks>
public sealed class S129TimelineView
{
    private readonly ImmutableArray<S129ControlPoint> _timedControlPoints;
    private readonly ImmutableArray<DateTimeOffset> _times;
    private readonly ImmutableArray<int> _firstIndexByTime;

    /// <summary>The source plan.</summary>
    public S129UnderKeelClearancePlan Plan { get; }

    /// <summary>
    /// Distinct sample times in strictly ascending order. Identical to
    /// the set of <see cref="S129ControlPoint.ExpectedPassingTime"/>
    /// values present in <see cref="Plan"/>, deduplicated.
    /// </summary>
    public ImmutableArray<DateTimeOffset> Times => _times;

    /// <summary>The earliest sample time, or <c>null</c> when the timeline is empty.</summary>
    public DateTimeOffset? Start => _times.IsDefaultOrEmpty ? null : _times[0];

    /// <summary>The latest sample time, or <c>null</c> when the timeline is empty.</summary>
    public DateTimeOffset? End => _times.IsDefaultOrEmpty ? null : _times[^1];

    /// <summary>
    /// <c>true</c> when the source plan has no control points with an
    /// <see cref="S129ControlPoint.ExpectedPassingTime"/>.
    /// </summary>
    public bool IsEmpty => _times.IsDefaultOrEmpty;

    /// <summary>
    /// Builds a timeline view from an
    /// <see cref="S129UnderKeelClearancePlan"/>.
    /// </summary>
    /// <param name="plan">The source plan.</param>
    public S129TimelineView(S129UnderKeelClearancePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Plan = plan;

        var timed = plan.ControlPoints
            .Where(cp => cp.ExpectedPassingTime.HasValue)
            .ToImmutableArray();
        _timedControlPoints = timed;

        if (timed.IsDefaultOrEmpty)
        {
            _times = ImmutableArray<DateTimeOffset>.Empty;
            _firstIndexByTime = ImmutableArray<int>.Empty;
            return;
        }

        // The projection already sorts CPs by ExpectedPassingTime
        // (stable); just collect distinct times and record the index of
        // the first CP at each time so overlap detection is O(1).
        var times = ImmutableArray.CreateBuilder<DateTimeOffset>();
        var firstIndex = ImmutableArray.CreateBuilder<int>();
        DateTimeOffset? previous = null;
        for (int i = 0; i < timed.Length; i++)
        {
            var t = timed[i].ExpectedPassingTime!.Value;
            if (previous.HasValue && t == previous.Value) continue;
            times.Add(t);
            firstIndex.Add(i);
            previous = t;
        }
        _times = times.ToImmutable();
        _firstIndexByTime = firstIndex.ToImmutable();
    }

    /// <summary>
    /// Enumerates one <see cref="S129TimelineSnapshot"/> per distinct
    /// sample time in chronological order.
    /// </summary>
    public IEnumerable<S129TimelineSnapshot> EnumerateTimeline()
    {
        for (int i = 0; i < _times.Length; i++)
        {
            int cpIndex = _firstIndexByTime[i];
            bool overlap = HasOverlapAt(i, cpIndex);
            yield return new S129TimelineSnapshot(
                _times[i],
                _timedControlPoints[cpIndex],
                IsExact: true,
                HasOverlappingControlPoints: overlap);
        }
    }

    /// <summary>
    /// Returns the snapshot at the given <paramref name="time"/>
    /// according to the supplied sampling <paramref name="mode"/>, or
    /// <c>null</c> when no snapshot can be returned (e.g. the timeline
    /// is empty, or <paramref name="mode"/> is
    /// <see cref="S129TimelineSamplingMode.NearestEarlier"/> and
    /// <paramref name="time"/> precedes the first sample).
    /// </summary>
    /// <param name="time">The query time.</param>
    /// <param name="mode">
    /// The sampling strategy. Defaults to
    /// <see cref="S129TimelineSamplingMode.NearestEarlier"/>, which
    /// matches the "most recent UKC observation as of <paramref name="time"/>"
    /// reading.
    /// </param>
    public S129TimelineSnapshot? GetSnapshotAt(
        DateTimeOffset time,
        S129TimelineSamplingMode mode = S129TimelineSamplingMode.NearestEarlier)
    {
        if (_times.IsDefaultOrEmpty) return null;

        // Binary search for the time. ImmutableArray<T>.BinarySearch is
        // not directly exposed on the struct; use Array.BinarySearch over
        // the underlying segment via ToArray() once is fine for the
        // small sizes typical of an S-129 plan, but we keep an in-place
        // loop to avoid allocations.
        int lo = 0, hi = _times.Length - 1;
        int exact = -1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            int cmp = _times[mid].CompareTo(time);
            if (cmp == 0) { exact = mid; break; }
            if (cmp < 0) lo = mid + 1;
            else hi = mid - 1;
        }
        // After the loop (no exact match), lo = insertion point.

        int chosen;
        bool isExact;
        switch (mode)
        {
            case S129TimelineSamplingMode.Exact:
                if (exact < 0) return null;
                chosen = exact;
                isExact = true;
                break;

            case S129TimelineSamplingMode.NearestEarlier:
                if (exact >= 0) { chosen = exact; isExact = true; }
                else
                {
                    int prev = lo - 1;
                    if (prev < 0) return null;
                    chosen = prev;
                    isExact = false;
                }
                break;

            case S129TimelineSamplingMode.NearestLater:
                if (exact >= 0) { chosen = exact; isExact = true; }
                else
                {
                    if (lo >= _times.Length) return null;
                    chosen = lo;
                    isExact = false;
                }
                break;

            case S129TimelineSamplingMode.Nearest:
                if (exact >= 0) { chosen = exact; isExact = true; }
                else
                {
                    int prev = lo - 1;
                    int next = lo;
                    if (prev < 0) { chosen = next; isExact = false; break; }
                    if (next >= _times.Length) { chosen = prev; isExact = false; break; }
                    var dPrev = time - _times[prev];
                    var dNext = _times[next] - time;
                    // Ties resolve to the earlier sample.
                    chosen = dPrev <= dNext ? prev : next;
                    isExact = false;
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }

        int firstCp = _firstIndexByTime[chosen];
        return new S129TimelineSnapshot(
            _times[chosen],
            _timedControlPoints[firstCp],
            IsExact: isExact,
            HasOverlappingControlPoints: HasOverlapAt(chosen, firstCp));
    }

    private bool HasOverlapAt(int timeIndex, int firstCpIndex)
    {
        int nextCpIndex = timeIndex + 1 < _firstIndexByTime.Length
            ? _firstIndexByTime[timeIndex + 1]
            : _timedControlPoints.Length;
        return (nextCpIndex - firstCpIndex) > 1;
    }
}
