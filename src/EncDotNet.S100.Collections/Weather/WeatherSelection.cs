namespace EncDotNet.S100.Collections.Weather;

/// <summary>
/// What a <see cref="WeatherFeedSource"/> includes (#1075): which of its
/// service's parameters or datasets, which forecast hours, and where.
/// </summary>
/// <param name="Parameters">
/// For a GRIB2 service, the chosen parameters' <see cref="WeatherServiceParameter.IndexMatch"/>
/// keys (<c>:HTSGW:surface:</c>); empty for none chosen, which means the service's defaults.
/// </param>
/// <param name="Hours">For a model service, the forecast hours (0, 3, 6 …); empty means the service's defaults.</param>
/// <param name="Area">
/// The area: the GRIB filter and ERDDAP cut to it, alerts are those of its
/// marine zones; <see langword="null"/> for the whole grid (AWS) or every
/// marine zone (alerts).
/// </param>
/// <param name="Zones">For alerts, the NWS marine zone codes in the area (<c>ANZ535</c>); empty to ask by area.</param>
/// <param name="Datasets">For ERDDAP, the chosen dataset ids; empty means the service's defaults.</param>
public sealed record WeatherSelection(
    IReadOnlyList<string>? Parameters = null,
    IReadOnlyList<int>? Hours = null,
    GeoBounds? Area = null,
    IReadOnlyList<string>? Zones = null,
    IReadOnlyList<string>? Datasets = null)
{
    /// <summary>The chosen parameters' <c>.idx</c> keys.</summary>
    public IReadOnlyList<string> Parameters { get; init; } = Parameters ?? [];

    /// <summary>The chosen forecast hours.</summary>
    public IReadOnlyList<int> Hours { get; init; } = Hours ?? [];

    /// <summary>For alerts, the marine zones of the area.</summary>
    public IReadOnlyList<string> Zones { get; init; } = Zones ?? [];

    /// <summary>For ERDDAP, the chosen dataset ids.</summary>
    public IReadOnlyList<string> Datasets { get; init; } = Datasets ?? [];

    /// <summary>The parameters this selection includes from <paramref name="service"/>: the chosen ones, else its defaults (else all).</summary>
    public IReadOnlyList<WeatherServiceParameter> ParametersOf(WeatherService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        var chosen = service.Parameters.Where(p => Parameters.Contains(p.IndexMatch, StringComparer.Ordinal)).ToArray();
        if (chosen.Length > 0)
            return chosen;
        var defaults = service.Parameters.Where(p => p.Default).ToArray();
        return defaults.Length > 0 ? defaults : service.Parameters;
    }

    /// <summary>The forecast hours this selection includes: the chosen ones the service offers, else up to its default hour.</summary>
    public IReadOnlyList<int> HoursOf(WeatherService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        var offered = service.OfferedHours;
        var chosen = Hours.Where(offered.Contains).Distinct().Order().ToArray();
        return chosen.Length > 0 ? chosen : [.. offered.Where(h => h <= service.DefaultHours)];
    }

    /// <summary>The datasets this selection includes from <paramref name="service"/>: the chosen ones, else its defaults (else all).</summary>
    public IReadOnlyList<WeatherServiceDataset> DatasetsOf(WeatherService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        var chosen = service.Datasets.Where(d => Datasets.Contains(d.Id, StringComparer.Ordinal)).ToArray();
        if (chosen.Length > 0)
            return chosen;
        var defaults = service.Datasets.Where(d => d.Default).ToArray();
        return defaults.Length > 0 ? defaults : service.Datasets;
    }
}
