namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// The in-force rule shared by every kind of warning the map draws: whether a
/// warning applies at a time, judged on its validity ranges and cancellation
/// date alone. S-124 navigational warnings
/// (<see cref="NavigationalWarningInfo.StatusAt"/>) and CAP weather warnings
/// both use it, so a warning starts and ends at the same instant whatever its
/// format.
/// </summary>
/// <remarks>
/// <para>
/// A warning is ended once its cancellation date has passed or every validity
/// range has ended, and not yet in force while it waits for a range to start.
/// With no validity range it is in force until cancelled. A range includes
/// both its start and its end: a warning valid until 12:00 is still in force
/// at 12:00 and ended a tick later.
/// </para>
/// <para>
/// Rules that look across warnings, such as cancellations by other warnings and
/// S-124 in-force bulletins, live in <see cref="NavigationalWarningSet"/>.
/// </para>
/// </remarks>
public static class WarningValidity
{
    /// <summary>
    /// Whether a warning with <paramref name="ranges"/> and
    /// <paramref name="cancellationDate"/> applies at <paramref name="time"/>.
    /// </summary>
    /// <param name="ranges">The warning's validity ranges, in any order; empty for none.</param>
    /// <param name="cancellationDate">When the warning stops applying whatever its ranges say, or <see langword="null"/>.</param>
    /// <param name="time">The time to test, usually the view time.</param>
    /// <returns>The warning's status at that time.</returns>
    public static NavigationalWarningStatus StatusAt(
        IReadOnlyList<NavigationalWarningValidity> ranges,
        DateTimeOffset? cancellationDate,
        DateTimeOffset time)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        if (cancellationDate is { } cancelled && time >= cancelled)
            return NavigationalWarningStatus.Ended;
        if (ranges.Count == 0)
            return NavigationalWarningStatus.InForce;

        var anyLater = false;
        foreach (var range in ranges)
        {
            var started = range.Start is null || time >= range.Start;
            var ended = range.End is { } end && time > end;
            if (started && !ended)
                return NavigationalWarningStatus.InForce;
            anyLater |= !started;
        }

        // Outside every range: waiting for a later one, or past them all.
        return anyLater ? NavigationalWarningStatus.NotYetInForce : NavigationalWarningStatus.Ended;
    }

    /// <summary>
    /// The instants at which <see cref="StatusAt"/> can change for a warning
    /// with <paramref name="ranges"/> and <paramref name="cancellationDate"/>:
    /// each range's start, the tick after each range's end, and the
    /// cancellation date, in no particular order. Between two consecutive
    /// instants the status is constant, so a host that draws the warning only
    /// needs to judge it again at these times.
    /// </summary>
    /// <param name="ranges">The warning's validity ranges.</param>
    /// <param name="cancellationDate">The cancellation date, or <see langword="null"/>.</param>
    /// <returns>The instants; open ends add none.</returns>
    public static IEnumerable<DateTimeOffset> Changes(
        IReadOnlyList<NavigationalWarningValidity> ranges,
        DateTimeOffset? cancellationDate)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        foreach (var range in ranges)
        {
            if (range.Start is { } start)
                yield return start;
            if (range.End is { } end && end < DateTimeOffset.MaxValue)
                yield return end.AddTicks(1);
        }

        if (cancellationDate is { } cancelled)
            yield return cancelled;
    }
}
