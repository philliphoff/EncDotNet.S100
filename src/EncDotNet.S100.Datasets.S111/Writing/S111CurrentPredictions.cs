using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Tides;

namespace EncDotNet.S100.Datasets.S111.Writing;

/// <summary>
/// Turns a tidal current prediction run at one station into an S-111
/// station-series dataset (data coding format 8), ready for
/// <see cref="S111StationSeriesWriter"/>.
/// </summary>
/// <remarks>
/// <para>
/// A prediction isn't an official product, and the dataset says so: its
/// producer code is <see cref="ProducerCode"/> (never a real producer's,
/// the same as predicted S-104), its <c>typeOfCurrentData</c> is
/// astronomical prediction, and its <c>methodCurrentsProduct</c> starts
/// with <see cref="MethodPrefix"/>, names the station and says it's not for
/// navigation. <see cref="IsPrediction"/> recognises such a dataset.
/// </para>
/// <para>
/// Speeds are knots and directions degrees true, toward which the current
/// sets, from <see cref="ICurrentPredictor.CurrentAt"/>: a rectilinear
/// station's flood or ebb direction, or a rotary station's predicted
/// direction.
/// </para>
/// </remarks>
public static class S111CurrentPredictions
{
    /// <summary>
    /// The producer code of predicted datasets, used in their file names;
    /// the same as predicted S-104's. <c>ZZ</c> codes are never assigned to
    /// a real producer.
    /// </summary>
    public const string ProducerCode = "ZZ01";

    /// <summary>The start of a predicted dataset's <c>methodCurrentsProduct</c>.</summary>
    public const string MethodPrefix = "Harmonic tidal current prediction";

    /// <summary>
    /// Predicts the current at a station and returns it as a station-series
    /// dataset. Synchronous and CPU-bound.
    /// </summary>
    /// <param name="predictor">The station's predictor: harmonic or subordinate.</param>
    /// <param name="stationId">The station identifier, such as NOAA's <c>PUG1701_33</c>.</param>
    /// <param name="stationName">The station's name.</param>
    /// <param name="position">The station's position (WGS 84).</param>
    /// <param name="depthMeters">
    /// The depth below the surface the current is predicted at, in metres,
    /// or <c>null</c> when not known. Written as <c>surfaceCurrentDepth</c>,
    /// negative below the sea surface.
    /// </param>
    /// <param name="startUtc">The first time (UTC).</param>
    /// <param name="endUtc">
    /// The last time (UTC); the series runs from <paramref name="startUtc"/>
    /// at <paramref name="step"/> up to and including it.
    /// </param>
    /// <param name="step">The time between values: whole seconds, at most 65,535.</param>
    /// <param name="issuedUtc">The dataset's issue time (UTC).</param>
    /// <returns>The dataset.</returns>
    /// <exception cref="ArgumentException">
    /// A time isn't UTC, <paramref name="endUtc"/> is before
    /// <paramref name="startUtc"/>, the step isn't a positive whole number
    /// of seconds up to 65,535, or the depth is negative or not finite.
    /// </exception>
    public static S111StationSeriesDataset CreateDataset(
        ICurrentPredictor predictor,
        string stationId,
        string stationName,
        GeoPosition position,
        double? depthMeters,
        DateTime startUtc,
        DateTime endUtc,
        TimeSpan step,
        DateTime issuedUtc)
    {
        ArgumentNullException.ThrowIfNull(predictor);
        ArgumentException.ThrowIfNullOrEmpty(stationId);
        ArgumentNullException.ThrowIfNull(stationName);
        RequireUtc(startUtc, nameof(startUtc));
        RequireUtc(endUtc, nameof(endUtc));
        RequireUtc(issuedUtc, nameof(issuedUtc));
        if (endUtc < startUtc)
        {
            throw new ArgumentException("The end is before the start.", nameof(endUtc));
        }

        if (step <= TimeSpan.Zero || step.Ticks % TimeSpan.TicksPerSecond != 0 || step.TotalSeconds > ushort.MaxValue)
        {
            throw new ArgumentException(
                $"The step must be a positive whole number of seconds, at most {ushort.MaxValue}.", nameof(step));
        }

        if (depthMeters is { } depth && (!double.IsFinite(depth) || depth < 0))
        {
            throw new ArgumentException("The depth must be zero or more metres below the surface.", nameof(depthMeters));
        }

        int count = checked((int)((endUtc - startUtc).Ticks / step.Ticks) + 1);
        var start = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        var speeds = new float[count];
        var directions = new float[count];
        for (int index = 0; index < count; index++)
        {
            var current = predictor.CurrentAt(start + (step * index));
            speeds[index] = (float)current.SpeedKnots;

            // Round to the float first, so 359.99999 doesn't become 360.
            var direction = (float)current.DirectionDegrees;
            directions[index] = direction >= 360f ? 0f : direction;
        }

        var issued = DateTime.SpecifyKind(issuedUtc, DateTimeKind.Utc);
        return new S111StationSeriesDataset
        {
            HorizontalCRS = 4326,
            GeographicIdentifier = $"{stationName} (predicted)",
            IssueDate = issued.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            IssueTime = issued.ToString("HHmmss'Z'", CultureInfo.InvariantCulture),
            DataCodingFormat = 8,
            TypeOfCurrentData = 3,
            SurfaceCurrentDepth = depthMeters is { } d ? -(float)d : null,
            MethodCurrentsProduct =
                $"{MethodPrefix} at station {stationId} {stationName}, from harmonic constituents " +
                "(EncDotNet.S100.Tides). Astronomical current only; wind and river flow change real currents. " +
                "Not an official product; not for navigation.",
            Stations =
            [
                new SurfaceCurrentStation
                {
                    Identifier = stationId,
                    Name = stationName,
                    Latitude = position.Latitude,
                    Longitude = position.Longitude,
                    StartTime = start,
                    EndTime = start + (step * (count - 1)),
                    TimeRecordInterval = step,
                    NumberOfTimes = count,
                    SpeedsKnots = speeds,
                    DirectionsDegreesTrue = directions,
                },
            ],
        };
    }

    /// <summary>
    /// The S-100 file name for a station's predictions: <c>111</c>, the
    /// producer code, and the station identifier's letters and digits, such
    /// as <c>111ZZ01PUG170133.h5</c>.
    /// </summary>
    /// <param name="stationId">The station identifier.</param>
    /// <returns>The file name.</returns>
    public static string FileName(string stationId)
    {
        ArgumentException.ThrowIfNullOrEmpty(stationId);
        var id = new string([.. stationId.Where(char.IsAsciiLetterOrDigit)]);
        return $"111{ProducerCode}{id}.h5";
    }

    /// <summary>
    /// Whether a dataset is a harmonic prediction written here, by the start
    /// of its <c>methodCurrentsProduct</c>. Such a dataset is not
    /// authoritative.
    /// </summary>
    /// <param name="dataset">The dataset.</param>
    /// <returns><see langword="true"/> for a predicted dataset.</returns>
    public static bool IsPrediction(S111StationSeriesDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return dataset.MethodCurrentsProduct?.StartsWith(MethodPrefix, StringComparison.Ordinal) == true;
    }

    private static void RequireUtc(DateTime time, string name)
    {
        if (time.Kind == DateTimeKind.Local)
        {
            throw new ArgumentException("The time must be UTC.", name);
        }
    }
}
