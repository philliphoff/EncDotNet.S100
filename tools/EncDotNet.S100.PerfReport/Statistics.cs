namespace EncDotNet.S100.PerfReport;

/// <summary>
/// Static helpers for the noise-robust statistics used by the
/// performance gate. Median + median absolute deviation (MAD) are
/// preferred over mean + standard deviation because CI runner noise
/// produces heavy-tailed iteration distributions where a single
/// outlier can swing the mean by tens of percent.
/// </summary>
internal static class Statistics
{
    /// <summary>
    /// Computes the linearly-interpolated <paramref name="p"/>-quantile
    /// of <paramref name="sortedValues"/>. <paramref name="p"/> must be
    /// in <c>[0, 1]</c>. Returns <c>0</c> when the list is empty.
    /// </summary>
    public static double Percentile(IReadOnlyList<double> sortedValues, double p)
    {
        if (sortedValues.Count == 0) return 0;
        if (sortedValues.Count == 1) return sortedValues[0];
        var index = p * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper) return sortedValues[lower];
        return sortedValues[lower] + (index - lower) * (sortedValues[upper] - sortedValues[lower]);
    }

    /// <summary>
    /// Returns the median of <paramref name="values"/>. Allocates a
    /// sorted copy so the caller's collection is unchanged.
    /// </summary>
    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        return Percentile(sorted, 0.5);
    }

    /// <summary>
    /// Computes the median absolute deviation (MAD) of
    /// <paramref name="values"/> from a precomputed
    /// <paramref name="median"/>. Optionally scaled by the standard
    /// constant <c>1.4826</c> to estimate a Gaussian-equivalent sigma
    /// (set <paramref name="scaleToSigma"/> to <c>true</c>).
    /// </summary>
    public static double MedianAbsoluteDeviation(
        IReadOnlyList<double> values,
        double median,
        bool scaleToSigma = false)
    {
        if (values.Count == 0) return 0;
        var deviations = new double[values.Count];
        for (int i = 0; i < values.Count; i++)
            deviations[i] = Math.Abs(values[i] - median);
        Array.Sort(deviations);
        var mad = Percentile(deviations, 0.5);
        return scaleToSigma ? mad * 1.4826 : mad;
    }
}
