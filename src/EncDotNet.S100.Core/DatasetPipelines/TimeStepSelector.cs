namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// The time rule of <see cref="TimeSemantics"/>, shared by every host: which
/// step a time-varying dataset draws at a map time, or none when no step lies
/// within its tolerance (#706, #1676). The viewer's map and <c>s100 tiles
/// serve</c> both pick steps with it, so they draw the same thing at the same
/// time.
/// </summary>
public static class TimeStepSelector
{
    /// <summary>The tolerance of a dataset with a single step, which has no typical step: one hour.</summary>
    public static TimeSpan SingleStepTolerance { get; } = TimeSpan.FromHours(1);

    /// <summary>
    /// The step of <paramref name="sorted"/> drawn at <paramref name="time"/>,
    /// or <see langword="null"/> when none is within <paramref name="tolerance"/>.
    /// For <see cref="TimeStepSelection.Continuous"/>, the time itself to the
    /// minute (<see cref="ToMinute"/>), whatever the tolerance: outside its
    /// range the dataset still draws what doesn't move with time.
    /// </summary>
    /// <param name="sorted">The dataset's steps, ascending and distinct.</param>
    /// <param name="selection">How the step is picked.</param>
    /// <param name="tolerance">
    /// How far from <paramref name="time"/> a step may be (either side for
    /// <see cref="TimeStepSelection.Nearest"/>, after the step for
    /// <see cref="TimeStepSelection.AtOrBefore"/>); <see cref="TimeSpan.MaxValue"/>
    /// for any distance.
    /// </param>
    /// <param name="time">The map time.</param>
    /// <returns>The step drawn, or <see langword="null"/> when the dataset has none then.</returns>
    public static DateTime? Select(IReadOnlyList<DateTime> sorted, TimeStepSelection selection, TimeSpan tolerance, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        if (sorted.Count == 0)
            return null;

        // The time to the minute, so a moving clock redraws once a minute at
        // most and nearby requests share tiles.
        if (selection == TimeStepSelection.Continuous)
            return ToMinute(time);

        var next = FirstAfterOrAt(sorted, time);
        if (next < sorted.Count && sorted[next] == time)
            return time;

        if (selection == TimeStepSelection.AtOrBefore)
        {
            if (next == 0)
                return null;
            var previous = sorted[next - 1];
            return time - previous <= tolerance ? previous : null;
        }

        DateTime? nearest = null;
        if (next > 0)
            nearest = sorted[next - 1];
        if (next < sorted.Count && (nearest is not { } before || sorted[next] - time < time - before))
            nearest = sorted[next];
        return nearest is { } step && (step - time).Duration() <= tolerance ? step : null;
    }

    /// <summary>
    /// The step of <paramref name="sorted"/> drawn at <paramref name="time"/>
    /// by <paramref name="semantics"/>, within its tolerance
    /// (<see cref="ToleranceOf"/>), or <see langword="null"/> when none is.
    /// </summary>
    /// <param name="sorted">The dataset's steps, ascending and distinct.</param>
    /// <param name="semantics">The dataset's time semantics.</param>
    /// <param name="time">The map time.</param>
    public static DateTime? Select(IReadOnlyList<DateTime> sorted, TimeSemantics semantics, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(semantics);
        return Select(sorted, semantics.Selection, ToleranceOf(semantics, sorted), time);
    }

    /// <summary>
    /// How far from the map time a step of <paramref name="sorted"/> may be
    /// and still draw: <see cref="TimeSemantics.Validity"/>, or the typical
    /// step (<see cref="TypicalStep"/>) when it is <see langword="null"/>. For
    /// <see cref="TimeStepSelection.Continuous"/>, how long before the first
    /// step the dataset already draws: the validity, or zero.
    /// </summary>
    /// <param name="semantics">The dataset's time semantics.</param>
    /// <param name="sorted">The dataset's steps, ascending and distinct.</param>
    public static TimeSpan ToleranceOf(TimeSemantics semantics, IReadOnlyList<DateTime> sorted)
    {
        ArgumentNullException.ThrowIfNull(semantics);
        ArgumentNullException.ThrowIfNull(sorted);
        if (semantics.Selection == TimeStepSelection.Continuous)
            return semantics.Validity is { } lead && lead > TimeSpan.Zero ? lead : TimeSpan.Zero;

        return semantics.Validity ?? TypicalStep(sorted);
    }

    /// <summary>
    /// The span in which a dataset with steps <paramref name="sorted"/> draws
    /// something tied to a time, from its first time to its last, or
    /// <see langword="null"/> when it has no steps. Gaps inside it, between
    /// steps further apart than the tolerance, draw nothing.
    /// </summary>
    /// <param name="semantics">The dataset's time semantics.</param>
    /// <param name="sorted">The dataset's steps, ascending and distinct.</param>
    /// <returns>
    /// The tolerance before the first step for <see cref="TimeStepSelection.Nearest"/>
    /// and <see cref="TimeStepSelection.Continuous"/>, the first step for
    /// <see cref="TimeStepSelection.AtOrBefore"/>; the tolerance after the last
    /// step, or the last step itself for <see cref="TimeStepSelection.Continuous"/>.
    /// </returns>
    public static (DateTime Start, DateTime End)? RangeOf(TimeSemantics semantics, IReadOnlyList<DateTime> sorted)
    {
        ArgumentNullException.ThrowIfNull(semantics);
        ArgumentNullException.ThrowIfNull(sorted);
        if (sorted.Count == 0)
            return null;

        var tolerance = ToleranceOf(semantics, sorted);
        return semantics.Selection switch
        {
            TimeStepSelection.Continuous => (AddClamped(sorted[0], -tolerance), sorted[^1]),
            TimeStepSelection.AtOrBefore => (sorted[0], AddClamped(sorted[^1], tolerance)),
            _ => (AddClamped(sorted[0], -tolerance), AddClamped(sorted[^1], tolerance)),
        };
    }

    /// <summary>
    /// The median interval between steps: robust to the long gaps of
    /// multi-window exchange sets, which an average would turn into a
    /// tolerance of weeks. <see cref="SingleStepTolerance"/> for fewer than two steps.
    /// </summary>
    /// <param name="sorted">The dataset's steps, ascending and distinct.</param>
    public static TimeSpan TypicalStep(IReadOnlyList<DateTime> sorted)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        if (sorted.Count < 2)
            return SingleStepTolerance;

        var steps = new TimeSpan[sorted.Count - 1];
        for (var index = 1; index < sorted.Count; index++)
            steps[index - 1] = sorted[index] - sorted[index - 1];
        Array.Sort(steps);
        return steps[steps.Length / 2];
    }

    /// <summary><paramref name="time"/> rounded down to the minute, keeping its kind.</summary>
    /// <param name="time">The time.</param>
    public static DateTime ToMinute(DateTime time) =>
        new(time.Ticks - (time.Ticks % TimeSpan.TicksPerMinute), time.Kind);

    /// <summary>The latest step before <paramref name="time"/>, or <see langword="null"/>.</summary>
    /// <param name="sorted">The dataset's steps, ascending and distinct.</param>
    /// <param name="time">The map time.</param>
    public static DateTime? Previous(IReadOnlyList<DateTime> sorted, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        var index = FirstAfterOrAt(sorted, time);
        return index > 0 ? sorted[index - 1] : null;
    }

    /// <summary>The earliest step after <paramref name="time"/>, or <see langword="null"/>.</summary>
    /// <param name="sorted">The dataset's steps, ascending and distinct.</param>
    /// <param name="time">The map time.</param>
    public static DateTime? Next(IReadOnlyList<DateTime> sorted, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        var index = FirstAfterOrAt(sorted, time);
        if (index < sorted.Count && sorted[index] == time)
            index++;
        return index < sorted.Count ? sorted[index] : null;
    }

    /// <summary>The index of the first step at or after <paramref name="time"/>.</summary>
    private static int FirstAfterOrAt(IReadOnlyList<DateTime> sorted, DateTime time)
    {
        int low = 0, high = sorted.Count;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (sorted[mid] < time)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }

    private static DateTime AddClamped(DateTime value, TimeSpan delta)
    {
        if (delta > TimeSpan.Zero && DateTime.MaxValue - value < delta)
            return DateTime.MaxValue;
        if (delta < TimeSpan.Zero && value - DateTime.MinValue < -delta)
            return DateTime.MinValue;
        return value + delta;
    }
}
