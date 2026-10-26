namespace EncDotNet.S100.Datasets.Grib;

/// <summary>
/// The product definition section (Section 4) of a GRIB2 field: the
/// parameter, its level and its times.
/// </summary>
/// <remarks>
/// <para>
/// Templates 4.0 (analysis or forecast at a point in time) and 4.8 (a
/// statistic over a time interval, such as a maximum gust) are read in full.
/// For any other template only <see cref="ParameterCategory"/> and
/// <see cref="ParameterNumber"/> are read (they're in the same place in every
/// template), the rest are <see langword="null"/>, and the field's
/// <see cref="GribField.UnsupportedReason"/> names the template.
/// </para>
/// <para>
/// For template 4.8, <see cref="ForecastTime"/> is the start of the overall
/// time interval and <see cref="IntervalEnd"/> its end; the field is valid at
/// the end, as <c>wgrib2</c> and ecCodes report it ("0-3 hour max fcst").
/// </para>
/// </remarks>
public sealed class GribProduct
{
    internal GribProduct(
        int templateNumber,
        byte parameterCategory,
        byte parameterNumber,
        byte? generatingProcessType,
        byte? generatingProcessIdentifier,
        GribTimeRange? forecastTime,
        GribFixedSurface? firstSurface,
        GribFixedSurface? secondSurface,
        DateTime? intervalEnd,
        IReadOnlyList<GribStatisticalProcess> statisticalProcesses,
        long? missingInStatisticalProcess)
    {
        TemplateNumber = templateNumber;
        ParameterCategory = parameterCategory;
        ParameterNumber = parameterNumber;
        GeneratingProcessType = generatingProcessType;
        GeneratingProcessIdentifier = generatingProcessIdentifier;
        ForecastTime = forecastTime;
        FirstSurface = firstSurface;
        SecondSurface = secondSurface;
        IntervalEnd = intervalEnd;
        StatisticalProcesses = statisticalProcesses;
        MissingInStatisticalProcess = missingInStatisticalProcess;
    }

    /// <summary>The product definition template number (code table 4.0).</summary>
    public int TemplateNumber { get; }

    /// <summary>The parameter category (code table 4.1), for example <c>2</c> for momentum.</summary>
    public byte ParameterCategory { get; }

    /// <summary>The parameter number (code table 4.2), for example <c>2</c> for the u-component of wind.</summary>
    public byte ParameterNumber { get; }

    /// <summary>The type of generating process (code table 4.3), for example <c>0</c> analysis or <c>2</c> forecast.</summary>
    public byte? GeneratingProcessType { get; }

    /// <summary>The centre's identifier of the generating process (model), for example NCEP's <c>96</c> for GFS.</summary>
    public byte? GeneratingProcessIdentifier { get; }

    /// <summary>
    /// The forecast time from the reference time; for template 4.8, the
    /// start of the overall time interval.
    /// </summary>
    public GribTimeRange? ForecastTime { get; }

    /// <summary>The first fixed surface, or <see langword="null"/> when the template has none or its type is 255.</summary>
    public GribFixedSurface? FirstSurface { get; }

    /// <summary>The second fixed surface of a layer, or <see langword="null"/>.</summary>
    public GribFixedSurface? SecondSurface { get; }

    /// <summary>For template 4.8, the end of the overall time interval, in UTC; otherwise <see langword="null"/>.</summary>
    public DateTime? IntervalEnd { get; }

    /// <summary>For template 4.8, the time range specifications, outermost first; otherwise empty.</summary>
    public IReadOnlyList<GribStatisticalProcess> StatisticalProcesses { get; }

    /// <summary>For template 4.8, the total number of data values missing in the statistical process.</summary>
    public long? MissingInStatisticalProcess { get; }

    /// <summary>
    /// The time the field is valid for: <see cref="IntervalEnd"/> for a
    /// statistic, otherwise the reference time plus <see cref="ForecastTime"/>.
    /// </summary>
    /// <param name="referenceTime">The message's reference time (Section 1).</param>
    /// <returns>The valid time, or <see langword="null"/> when the template isn't read.</returns>
    public DateTime? GetValidTime(DateTime referenceTime)
    {
        if (IntervalEnd is { } end)
            return end;
        return ForecastTime is { } forecast ? forecast.AddTo(referenceTime) : null;
    }
}
