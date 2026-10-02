namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>How a time-aware dataset picks the sample it draws for the clock.</summary>
public enum MapsuiTimeSelectionKind
{
    /// <summary>The nearest sample, within the tolerance either side (S-111).</summary>
    Nearest,

    /// <summary>The latest sample at or before the clock, held for the tolerance (S-104, S-411).</summary>
    AtOrBefore,
}

/// <summary>
/// The time rule shared by the session's time policy and
/// <see cref="MapsuiMapTimedDataset.SampleAt"/>: which sample a dataset draws
/// at a clock value, or none when nothing lies within its tolerance (#706).
/// </summary>
public static class MapsuiTimeSelection
{
    /// <summary>
    /// The sample of <paramref name="sorted"/> drawn at <paramref name="time"/>,
    /// or <see langword="null"/> when none is within <paramref name="tolerance"/>.
    /// </summary>
    /// <param name="sorted">The dataset's samples, ascending and distinct.</param>
    /// <param name="kind">How the sample is picked.</param>
    /// <param name="tolerance">How far from the clock a sample may be.</param>
    /// <param name="time">The clock value.</param>
    public static DateTime? Select(IReadOnlyList<DateTime> sorted, MapsuiTimeSelectionKind kind, TimeSpan tolerance, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        if (sorted.Count == 0)
            return null;

        var next = FirstAfterOrAt(sorted, time);
        if (next < sorted.Count && sorted[next] == time)
            return time;

        if (kind == MapsuiTimeSelectionKind.AtOrBefore)
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
        return nearest is { } sample && (sample - time).Duration() <= tolerance ? sample : null;
    }

    /// <summary>The latest sample before <paramref name="time"/>, or null.</summary>
    public static DateTime? Previous(IReadOnlyList<DateTime> sorted, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        var index = FirstAfterOrAt(sorted, time);
        return index > 0 ? sorted[index - 1] : null;
    }

    /// <summary>The earliest sample after <paramref name="time"/>, or null.</summary>
    public static DateTime? Next(IReadOnlyList<DateTime> sorted, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        var index = FirstAfterOrAt(sorted, time);
        if (index < sorted.Count && sorted[index] == time)
            index++;
        return index < sorted.Count ? sorted[index] : null;
    }

    /// <summary>The index of the first sample at or after <paramref name="time"/>.</summary>
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
}
