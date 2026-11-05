namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// A surface analysis, or a forecast of one, at one valid time: its fronts and
/// troughs and its high and low pressure centres (design note
/// <c>vector-weather.md</c> §5, §8.3). Read from a WPC coded surface bulletin
/// by <see cref="CodedSurfaceBulletinReader"/>. Not for navigation.
/// </summary>
public sealed class SurfaceAnalysis
{
    /// <summary>Creates a surface analysis.</summary>
    /// <param name="validTime">The time the analysis or forecast is valid for.</param>
    /// <param name="forecastHours">For a forecast, its lead time in hours (12 for a "12HR PROG"); <see langword="null"/> for an analysis or when the bulletin doesn't say.</param>
    /// <param name="issuingCentre">Who issued it, as the bulletin names it.</param>
    /// <param name="fronts">The fronts and troughs, in the bulletin's order. The list is copied.</param>
    /// <param name="pressureCentres">The highs and lows, in the bulletin's order. The list is copied.</param>
    public SurfaceAnalysis(
        DateTimeOffset validTime,
        int? forecastHours,
        string issuingCentre,
        IEnumerable<SurfaceFront> fronts,
        IEnumerable<PressureCentre> pressureCentres)
    {
        ArgumentNullException.ThrowIfNull(issuingCentre);
        ArgumentNullException.ThrowIfNull(fronts);
        ArgumentNullException.ThrowIfNull(pressureCentres);
        ValidTime = validTime.ToUniversalTime();
        ForecastHours = forecastHours;
        IssuingCentre = issuingCentre;
        Fronts = fronts.ToArray();
        PressureCentres = pressureCentres.ToArray();
    }

    /// <summary>The time the analysis or forecast is valid for, in UTC.</summary>
    public DateTimeOffset ValidTime { get; }

    /// <summary>
    /// For a forecast, its lead time in hours; <see langword="null"/> for an
    /// analysis.
    /// </summary>
    public int? ForecastHours { get; }

    /// <summary>
    /// Who issued it, as the bulletin names it, for example
    /// <c>"NWS WEATHER PREDICTION CENTER COLLEGE PARK MD"</c>.
    /// </summary>
    public string IssuingCentre { get; }

    /// <summary>The fronts and troughs, in the bulletin's order.</summary>
    public IReadOnlyList<SurfaceFront> Fronts { get; }

    /// <summary>The highs and lows, in the bulletin's order.</summary>
    public IReadOnlyList<PressureCentre> PressureCentres { get; }

    /// <summary>The fronts and troughs of one kind.</summary>
    /// <param name="type">The kind.</param>
    public IEnumerable<SurfaceFront> FrontsOf(SurfaceFrontType type) => Fronts.Where(f => f.Type == type);

    /// <summary>The highs, or the lows.</summary>
    /// <param name="kind">High or low.</param>
    public IEnumerable<PressureCentre> CentresOf(PressureCentreKind kind) => PressureCentres.Where(c => c.Kind == kind);
}
