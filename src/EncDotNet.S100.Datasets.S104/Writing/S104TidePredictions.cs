using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Tides;

namespace EncDotNet.S100.Datasets.S104.Writing;

/// <summary>
/// Turns a harmonic tide prediction run at one station into an S-104
/// station-series dataset (data coding format 8), ready for
/// <see cref="S104StationSeriesWriter"/>.
/// </summary>
/// <remarks>
/// <para>
/// A prediction isn't an official product, and the dataset says so: its
/// producer code is <see cref="ProducerCode"/> (never a real producer's),
/// its <c>dataDynamicity</c> is astronomical prediction, and its
/// <c>methodWaterLevelProduct</c> starts with <see cref="MethodPrefix"/>,
/// names the station and says it's not for navigation.
/// <see cref="IsPrediction"/> recognises such a dataset.
/// </para>
/// <para>
/// Heights are on the station's datum, written as an S-100 vertical datum
/// code; a datum with no code (station datum, NAVD88, mean tide level) is
/// refused. Trends compare the rise over <see cref="TrendIntervalMinutes"/>
/// with <see cref="TrendThresholdMeters"/>, as NOAA's S-104 files do.
/// </para>
/// </remarks>
public static class S104TidePredictions
{
    /// <summary>
    /// The producer code of predicted datasets, used in their file names.
    /// <c>ZZ</c> codes are never assigned to a real producer; the scenario
    /// generator's synthetic data uses <c>ZZ00</c>.
    /// </summary>
    public const string ProducerCode = "ZZ01";

    /// <summary>The start of a predicted dataset's <c>methodWaterLevelProduct</c>.</summary>
    public const string MethodPrefix = "Harmonic tide prediction";

    /// <summary>The rise or fall, in metres, below which the trend is steady.</summary>
    public const double TrendThresholdMeters = 0.2;

    /// <summary>The interval, in minutes, over which the trend's rise or fall is judged.</summary>
    public const int TrendIntervalMinutes = 60;

    /// <summary>
    /// Predicts the heights at a station and returns them as a station-series
    /// dataset. Synchronous and CPU-bound.
    /// </summary>
    /// <param name="station">The station, with its datum.</param>
    /// <param name="position">The station's position (WGS 84).</param>
    /// <param name="startUtc">The first time (UTC).</param>
    /// <param name="endUtc">
    /// The last time (UTC); the series runs from <paramref name="startUtc"/>
    /// at <paramref name="step"/> up to and including it.
    /// </param>
    /// <param name="step">The time between heights: whole seconds, at most 65,535.</param>
    /// <param name="issuedUtc">The dataset's issue time (UTC).</param>
    /// <returns>The dataset.</returns>
    /// <exception cref="ArgumentException">
    /// The station's datum has no S-100 vertical datum code, a time isn't UTC,
    /// <paramref name="endUtc"/> is before <paramref name="startUtc"/>, or the step
    /// isn't a positive whole number of seconds up to 65,535.
    /// </exception>
    public static S104StationSeriesDataset CreateDataset(
        TideStation station,
        GeoPosition position,
        DateTime startUtc,
        DateTime endUtc,
        TimeSpan step,
        DateTime issuedUtc)
    {
        ArgumentNullException.ThrowIfNull(station);
        ValidateRun(startUtc, endUtc, step, issuedUtc);

        if (!TryGetVerticalDatum(station.Datum, out var datum))
        {
            throw new ArgumentException(
                $"Datum '{station.Datum.Name}' has no S-100 vertical datum code, so S-104 can't carry it. " +
                "Predict on a chart datum such as MLLW or LAT.",
                nameof(station));
        }

        return CreateDataset(
            new TidePredictor(station),
            station.Id,
            station.Name,
            datum,
            "from its harmonic constituents",
            position,
            startUtc,
            endUtc,
            step,
            issuedUtc);
    }

    /// <summary>
    /// Predicts the heights from any predictor, such as a subordinate
    /// station's, and returns them as a station-series dataset. Synchronous
    /// and CPU-bound.
    /// </summary>
    /// <param name="predictor">The predictor, giving heights on <paramref name="datum"/>.</param>
    /// <param name="stationId">The station's identifier.</param>
    /// <param name="stationName">The station's name.</param>
    /// <param name="datum">The S-100 vertical datum the predictor's heights are on.</param>
    /// <param name="basis">
    /// How the heights are derived, for the method text, such as
    /// <c>from its harmonic constituents</c> or
    /// <c>from reference station 9447130, with offsets</c>.
    /// </param>
    /// <param name="position">The station's position (WGS 84).</param>
    /// <param name="startUtc">The first time (UTC).</param>
    /// <param name="endUtc">
    /// The last time (UTC); the series runs from <paramref name="startUtc"/>
    /// at <paramref name="step"/> up to and including it.
    /// </param>
    /// <param name="step">The time between heights: whole seconds, at most 65,535.</param>
    /// <param name="issuedUtc">The dataset's issue time (UTC).</param>
    /// <returns>The dataset.</returns>
    /// <exception cref="ArgumentException">
    /// A time isn't UTC, <paramref name="endUtc"/> is before <paramref name="startUtc"/>,
    /// or the step isn't a positive whole number of seconds up to 65,535.
    /// </exception>
    public static S104StationSeriesDataset CreateDataset(
        ITidePredictor predictor,
        string stationId,
        string stationName,
        VerticalDatum datum,
        string basis,
        GeoPosition position,
        DateTime startUtc,
        DateTime endUtc,
        TimeSpan step,
        DateTime issuedUtc)
    {
        ArgumentNullException.ThrowIfNull(predictor);
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(basis);
        ValidateRun(startUtc, endUtc, step, issuedUtc);

        int count = checked((int)((endUtc - startUtc).Ticks / step.Ticks) + 1);
        var heights = new double[count];
        predictor.Predict(startUtc, step, heights);

        var trends = new byte[count];
        for (int index = 0; index < count; index++)
        {
            var rise = predictor.RateAt(startUtc + (step * index)) * (TrendIntervalMinutes / 60.0);
            trends[index] = Math.Abs(rise) < TrendThresholdMeters ? (byte)3 : rise > 0 ? (byte)2 : (byte)1;
        }

        var start = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        var issued = DateTime.SpecifyKind(issuedUtc, DateTimeKind.Utc);
        return new S104StationSeriesDataset
        {
            HorizontalCRS = 4326,
            VerticalDatum = (int)datum,
            GeographicIdentifier = $"{stationName} (predicted)",
            IssueDate = issued.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            IssueTime = issued.ToString("HHmmss'Z'", CultureInfo.InvariantCulture),
            DataCodingFormat = 8,
            DataDynamicity = 2,
            MethodWaterLevelProduct =
                $"{MethodPrefix} at station {stationId} {stationName}, {basis} " +
                "(EncDotNet.S100.Tides). Astronomical tide only, no weather. Not an official product; not for navigation.",
            WaterLevelTrendThreshold = TrendThresholdMeters,
            TrendInterval = TrendIntervalMinutes,
            Stations =
            [
                new WaterLevelStation
                {
                    Identifier = stationId,
                    Name = stationName,
                    Latitude = position.Latitude,
                    Longitude = position.Longitude,
                    StartTime = start,
                    EndTime = start + (step * (count - 1)),
                    TimeRecordInterval = step,
                    NumberOfTimes = count,
                    Heights = Array.ConvertAll(heights, height => (float)height),
                    Trends = trends,
                },
            ],
        };
    }

    /// <summary>
    /// The S-100 file name for a station's predictions: <c>104</c>, the
    /// producer code, and the station identifier's letters and digits, such
    /// as <c>104ZZ019447130.h5</c>.
    /// </summary>
    /// <param name="station">The station.</param>
    /// <returns>The file name.</returns>
    public static string FileName(TideStation station)
    {
        ArgumentNullException.ThrowIfNull(station);
        return FileName(station.Id);
    }

    /// <summary>
    /// The S-100 file name for a station's predictions, by its identifier:
    /// <c>104</c>, the producer code, and the identifier's letters and
    /// digits, such as <c>104ZZ019447130.h5</c>.
    /// </summary>
    /// <param name="stationId">The station's identifier.</param>
    /// <returns>The file name.</returns>
    public static string FileName(string stationId)
    {
        ArgumentNullException.ThrowIfNull(stationId);
        var id = new string([.. stationId.Where(char.IsAsciiLetterOrDigit)]);
        return $"104{ProducerCode}{id}.h5";
    }

    /// <summary>
    /// Whether a dataset is a harmonic prediction written here, by the start
    /// of its <c>methodWaterLevelProduct</c>. Such a dataset is not
    /// authoritative.
    /// </summary>
    /// <param name="dataset">The dataset.</param>
    /// <returns><see langword="true"/> for a predicted dataset.</returns>
    public static bool IsPrediction(S104StationSeriesDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return dataset.MethodWaterLevelProduct?.StartsWith(MethodPrefix, StringComparison.Ordinal) == true;
    }

    /// <summary>
    /// Finds the S-100 vertical datum for a tide datum, by its NOAA
    /// abbreviation (<c>MLLW</c>, <c>MLW</c>, <c>MSL</c>, <c>MHW</c>,
    /// <c>MHHW</c>, <c>LAT</c>, <c>HAT</c>, …) or the vertical datum's name
    /// (<c>MeanLowerLowWater</c>), ignoring case.
    /// </summary>
    /// <param name="datum">The tide datum.</param>
    /// <param name="verticalDatum">The S-100 vertical datum, when there is one.</param>
    /// <returns>
    /// <see langword="true"/> when the datum has an S-100 code; <see langword="false"/>
    /// for one that hasn't, such as station datum (<c>STND</c>), mean tide level
    /// (<c>MTL</c>) or a geodetic datum (<c>NAVD88</c>).
    /// </returns>
    public static bool TryGetVerticalDatum(TideDatum datum, out VerticalDatum verticalDatum)
    {
        ArgumentNullException.ThrowIfNull(datum);
        var name = datum.Name.Trim();
        if (Abbreviations.TryGetValue(name, out verticalDatum))
        {
            return true;
        }

        return Enum.TryParse(name, ignoreCase: true, out verticalDatum) &&
            !int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
            Enum.IsDefined(verticalDatum);
    }

    private static readonly Dictionary<string, VerticalDatum> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MLLW"] = VerticalDatum.MeanLowerLowWater,
        ["MLW"] = VerticalDatum.MeanLowWater,
        ["MSL"] = VerticalDatum.MeanSeaLevel,
        ["MHW"] = VerticalDatum.MeanHighWater,
        ["MHHW"] = VerticalDatum.MeanHigherHighWater,
        ["MLWS"] = VerticalDatum.MeanLowWaterSprings,
        ["MLLWS"] = VerticalDatum.MeanLowerLowWaterSprings,
        ["MHWS"] = VerticalDatum.MeanHighWaterSprings,
        ["LAT"] = VerticalDatum.LowestAstronomicalTide,
        ["HAT"] = VerticalDatum.HighestAstronomicalTide,
        ["LLW"] = VerticalDatum.LowestLowWater,
        ["ISLW"] = VerticalDatum.IndianSpringLowWater,
        ["MWL"] = VerticalDatum.MeanWaterLevel,
        ["IGLD85"] = VerticalDatum.InternationalGreatLakesDatum1985,
        ["IGLD 1985"] = VerticalDatum.InternationalGreatLakesDatum1985,
    };

    private static void ValidateRun(DateTime startUtc, DateTime endUtc, TimeSpan step, DateTime issuedUtc)
    {
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
    }

    private static void RequireUtc(DateTime time, string name)
    {
        if (time.Kind == DateTimeKind.Local)
        {
            throw new ArgumentException("The time must be UTC.", name);
        }
    }
}
