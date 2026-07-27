namespace EncDotNet.S100.Pipelines.Coverage.Pyramid;

/// <summary>
/// Shoal-biased minimum reducer for depth fields (S-102
/// <c>BathymetryCoverage.depth</c>; S-100 Part 10c §11).
/// </summary>
/// <remarks>
/// <para>
/// <b>Safety invariant:</b> the reduced value is always ≤ the minimum
/// valid source cell in the window (i.e. the shoalest / shallowest
/// depth). Downsampling a coverage with <see cref="MinReducer"/>
/// therefore never makes a region appear <em>safer</em> (deeper) than
/// the raw survey; a pyramid cell reports at least the shoalest
/// hazard it pools. This matches GDAL's <c>-r min</c> resampler and is
/// the safety story called out in issue #486.
/// </para>
/// <para>
/// NODATA cells are excluded from the reduction. A window whose cells
/// are entirely NODATA reduces to NODATA (the sentinel is propagated
/// through coarser levels so genuinely unsurveyed regions stay
/// distinguishable from surveyed shoal water).
/// </para>
/// </remarks>
public sealed class MinReducer : IPyramidReducer
{
    /// <summary>Shared instance; the reducer holds no state.</summary>
    public static readonly MinReducer Instance = new();

    /// <inheritdoc/>
    public float Reduce(ReadOnlySpan<float> window, float noDataValue)
    {
        float min = float.PositiveInfinity;
        bool anyValid = false;
        for (int i = 0; i < window.Length; i++)
        {
            float v = window[i];
            if (v == noDataValue) continue;
            if (v < min) min = v;
            anyValid = true;
        }
        return anyValid ? min : noDataValue;
    }
}

/// <summary>
/// Worst-case maximum reducer for pooled uncertainty fields (S-102
/// <c>BathymetryCoverage.uncertainty</c>).
/// </summary>
/// <remarks>
/// A pyramid cell's uncertainty is at least the maximum of the pooled
/// source uncertainties: coarsening cannot legitimately claim tighter
/// confidence than the loosest input. NODATA cells are excluded; an
/// entirely-NODATA window returns NODATA.
/// </remarks>
public sealed class MaxReducer : IPyramidReducer
{
    /// <summary>Shared instance; the reducer holds no state.</summary>
    public static readonly MaxReducer Instance = new();

    /// <inheritdoc/>
    public float Reduce(ReadOnlySpan<float> window, float noDataValue)
    {
        float max = float.NegativeInfinity;
        bool anyValid = false;
        for (int i = 0; i < window.Length; i++)
        {
            float v = window[i];
            if (v == noDataValue) continue;
            if (v > max) max = v;
            anyValid = true;
        }
        return anyValid ? max : noDataValue;
    }
}

/// <summary>
/// Arithmetic-mean reducer for scalar fields where averaging is the
/// natural downsample (S-104 <c>waterLevelHeight</c>, generic
/// non-safety scalars).
/// </summary>
/// <remarks>
/// NODATA cells are excluded from the average. An entirely-NODATA
/// window returns NODATA.
/// </remarks>
public sealed class MeanReducer : IPyramidReducer
{
    /// <summary>Shared instance; the reducer holds no state.</summary>
    public static readonly MeanReducer Instance = new();

    /// <inheritdoc/>
    public float Reduce(ReadOnlySpan<float> window, float noDataValue)
    {
        double sum = 0.0;
        int n = 0;
        for (int i = 0; i < window.Length; i++)
        {
            float v = window[i];
            if (v == noDataValue) continue;
            sum += v;
            n++;
        }
        return n == 0 ? noDataValue : (float)(sum / n);
    }
}

/// <summary>
/// Vector-mean reducer for a paired (speed, direction) field (S-111
/// surface currents; S-111 §10.5.1).
/// </summary>
/// <remarks>
/// <para>
/// Directly averaging directions (angles in degrees) produces the
/// wrong answer at the 0°/360° branch cut — e.g. mean of (10°, 350°)
/// is 0° (net "north"), not 180° (which a naive average would give).
/// This reducer projects each (speed, direction) sample into Cartesian
/// (u, v) components, means <em>them</em>, then recomposes the
/// resultant into (‖r‖, atan2). The resultant magnitude is not the
/// mean speed: it is the magnitude of the mean current vector, which
/// is what a portrayal wants (net flow through the pooled cell).
/// </para>
/// <para>
/// NODATA sentinels (either paired field) exclude the cell from the
/// reduction. An entirely-NODATA window returns
/// (<paramref name="noDataSpeed"/>, <paramref name="noDataDirection"/>).
/// </para>
/// </remarks>
public sealed class VectorMeanReducer : IVectorPyramidReducer
{
    /// <summary>Shared instance; the reducer holds no state.</summary>
    public static readonly VectorMeanReducer Instance = new();

    /// <inheritdoc/>
    public (float Speed, float Direction) Reduce(
        ReadOnlySpan<float> speeds,
        ReadOnlySpan<float> directions,
        float noDataSpeed,
        float noDataDirection)
    {
        if (speeds.Length != directions.Length)
            throw new ArgumentException(
                "Vector reducer requires paired speed/direction spans of equal length.",
                nameof(directions));

        double uSum = 0.0;
        double vSum = 0.0;
        int n = 0;
        for (int i = 0; i < speeds.Length; i++)
        {
            float s = speeds[i];
            float d = directions[i];
            if (s == noDataSpeed || d == noDataDirection) continue;

            // S-111 §10.5.1: direction is degrees clockwise from north.
            // Standard convention east = +u, north = +v.
            double rad = d * (Math.PI / 180.0);
            uSum += s * Math.Sin(rad);
            vSum += s * Math.Cos(rad);
            n++;
        }
        if (n == 0)
            return (noDataSpeed, noDataDirection);

        double uMean = uSum / n;
        double vMean = vSum / n;
        double speed = Math.Sqrt(uMean * uMean + vMean * vMean);

        // atan2(u, v): east-y, north-x → returns radians clockwise from
        // north, matching S-111's direction convention.
        double dir = Math.Atan2(uMean, vMean) * (180.0 / Math.PI);
        if (dir < 0.0) dir += 360.0;
        return ((float)speed, (float)dir);
    }
}
