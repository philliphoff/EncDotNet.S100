using System.Text.Json.Serialization;

namespace EncDotNet.S100.Collections.Weather;

/// <summary>How a weather service in the Library is read (#1075; design note weather-sources.md §6.3).</summary>
public enum WeatherServiceKind
{
    /// <summary>
    /// A NOMADS GRIB filter (<c>filter_gfswave.pl</c>, <c>filter_gfs_0p25.pl</c>):
    /// one request per forecast hour, cut to the area and the chosen
    /// parameters, appended into one GRIB2 file.
    /// </summary>
    GribFilter,

    /// <summary>
    /// Whole-grid GRIB2 files with a <c>.idx</c> index beside each (NOAA's
    /// <c>noaa-gfs-bdp-pds</c> bucket on AWS): only the chosen parameters'
    /// messages are read, with HTTP byte ranges, and appended into one file.
    /// No area cut, so it suits larger areas.
    /// </summary>
    GribIndex,

    /// <summary>Active alerts from <c>api.weather.gov</c> for the marine zones of an area, as NWS GeoJSON (CAP).</summary>
    NwsAlerts,

    /// <summary>An ERDDAP <c>griddap</c> dataset, chosen by dataset id, fetched as a NetCDF subset of the area and hours.</summary>
    ErddapGriddap,

    /// <summary>
    /// WPC's coded surface bulletins (design note <c>vector-weather.md</c>
    /// §6.4, §8.3): each chosen bulletin (the latest analysis, the forecast
    /// fronts) is one small text file at a fixed address below the service's
    /// data root, whole-domain, re-read on every index.
    /// </summary>
    WpcBulletins,

    /// <summary>
    /// NHC's active tropical cyclones (design note <c>vector-weather.md</c>
    /// §6.4, §8.1): the storm list <c>CurrentStorms.json</c>, the service's
    /// <see cref="WeatherService.File"/> below its data root, is read on every
    /// index, one item per storm; an item downloads its latest advisory's GIS
    /// zips (forecast, wind radii and best track). A new advisory replaces the
    /// downloaded one as a newer model run does.
    /// </summary>
    NhcStorms,
}

/// <summary>A parameter a GRIB2 service offers: its NCEP short name and level as the <c>.idx</c> files spell them.</summary>
/// <param name="Id">The short name, e.g. <c>HTSGW</c> or <c>UGRD</c>.</param>
/// <param name="Name">The display name, e.g. "Significant wave height".</param>
/// <param name="Level">The level as <c>.idx</c> lines spell it, e.g. <c>surface</c> or <c>10 m above ground</c>.</param>
/// <param name="Default">True when the parameter is chosen by default.</param>
public sealed record WeatherServiceParameter(string Id, string Name, string Level, bool Default = false)
{
    /// <summary>The <c>.idx</c> match for the parameter: <c>:UGRD:10 m above ground:</c>.</summary>
    public string IndexMatch => $":{Id}:{Level}:";
}

/// <summary>An ERDDAP <c>griddap</c> dataset, or a WPC bulletin, a service offers.</summary>
/// <param name="Id">The ERDDAP dataset id, e.g. <c>NWW3_Global_Best</c>; for a bulletin, a short id (<c>codsus</c>) that names its item.</param>
/// <param name="Name">The display name.</param>
/// <param name="Variables">The variables to fetch (CF names the NetCDF reader maps, such as <c>Thgt</c>); empty for a bulletin.</param>
/// <param name="Default">True when the dataset is chosen by default.</param>
/// <param name="File">For a bulletin, its path below the service's data root (<c>as/asus02.kwbc.cod.sus.txt</c>).</param>
public sealed record WeatherServiceDataset(string Id, string Name, IReadOnlyList<string> Variables, bool Default = false, string? File = null);

/// <summary>
/// What a weather service in the Library offers and how its requests are
/// made (#1075): the <c>weather</c> block of a <c>known-sources.json</c>
/// entry. A source made from it keeps a copy, so it never depends on the list.
/// </summary>
/// <param name="Kind">How the service is read.</param>
/// <param name="Model">
/// For a GRIB2 service, the model's id (<c>gfswave</c>, <c>gfs</c>); it names
/// the download. For other services, a short id of the product (<c>alerts</c>, <c>erddap</c>).
/// </param>
/// <param name="ModelName">The model's display name ("GFS-Wave"), if any.</param>
/// <param name="DataRoot">
/// For a GRIB2 service, the folder runs are published under, ending in <c>/</c>
/// (NOMADS' <c>…/com/gfs/prod/</c>, or the AWS bucket root). Runs are found
/// by asking for the <c>.idx</c> of their last chosen hour there.
/// </param>
/// <param name="File">
/// For NHC, the storm list's path below <paramref name="DataRoot"/>
/// (<c>CurrentStorms.json</c>). For a GRIB2 service, the path of one forecast hour's file below
/// <paramref name="DataRoot"/>, with <c>{date}</c> (<c>yyyyMMdd</c>),
/// <c>{cycle}</c> (<c>HH</c>) and <c>{hour}</c> (three digits) in it, e.g.
/// <c>gfs.{date}/{cycle}/wave/gridded/gfswave.t{cycle}z.global.0p25.f{hour}.grib2</c>.
/// </param>
/// <param name="CadenceHours">How often the model runs (6 for GFS); 0 for a service without runs.</param>
/// <param name="StepHours">The forecast hours offered are every <paramref name="StepHours"/> hours.</param>
/// <param name="MaxHours">The last forecast hour offered.</param>
/// <param name="DefaultHours">The forecast hours chosen by default: up to this hour.</param>
/// <param name="GridDegrees">The grid spacing, for rounding an area out to whole cells (0.25 for GFS 0.25°); 0 for none.</param>
/// <param name="Parameters">For a GRIB2 service, the parameters offered.</param>
/// <param name="Datasets">For ERDDAP, the datasets offered; for WPC, the bulletins.</param>
/// <param name="StaleAfterHours">
/// How old an item's run may be before it is stale, in hours, or 0 for
/// <see cref="WeatherRuns.StaleAfter"/> (12 hours). WPC's analyses are stale
/// after 6, NHC's storms 7 hours after their advisory (V28).
/// </param>
/// <param name="RefreshHours">
/// How often a host re-reads the source while its latest item is loaded, and
/// loads a newer one in its place, in hours; 0 (the default) for only when
/// asked. WPC's is 3, its analysis cadence. For NHC, a newer advisory of a
/// loaded storm is announced rather than loaded (decision Q4).
/// </param>
public sealed record WeatherService(
    WeatherServiceKind Kind,
    string Model,
    string? ModelName = null,
    Uri? DataRoot = null,
    string? File = null,
    int CadenceHours = 0,
    int StepHours = 3,
    int MaxHours = 120,
    int DefaultHours = 48,
    double GridDegrees = 0,
    IReadOnlyList<WeatherServiceParameter>? Parameters = null,
    IReadOnlyList<WeatherServiceDataset>? Datasets = null,
    double StaleAfterHours = 0,
    double RefreshHours = 0)
{
    /// <summary>For a GRIB2 service, the parameters offered.</summary>
    public IReadOnlyList<WeatherServiceParameter> Parameters { get; init; } = Parameters ?? [];

    /// <summary>For ERDDAP, the datasets offered.</summary>
    public IReadOnlyList<WeatherServiceDataset> Datasets { get; init; } = Datasets ?? [];

    /// <summary>True for a GRIB2 model service (<see cref="WeatherServiceKind.GribFilter"/> or <see cref="WeatherServiceKind.GribIndex"/>).</summary>
    [JsonIgnore]
    public bool IsGrib => Kind is WeatherServiceKind.GribFilter or WeatherServiceKind.GribIndex;

    /// <summary>How old an item's run may be before it is stale: <see cref="StaleAfterHours"/>, else <see cref="WeatherRuns.StaleAfter"/>.</summary>
    [JsonIgnore]
    public TimeSpan StaleAfter => StaleAfterHours > 0 ? TimeSpan.FromHours(StaleAfterHours) : WeatherRuns.StaleAfter;

    /// <summary>The address of a bulletin: <see cref="DataRoot"/> joined with its <see cref="WeatherServiceDataset.File"/>.</summary>
    /// <param name="dataset">One of <see cref="Datasets"/>, with a file.</param>
    public Uri BulletinUri(WeatherServiceDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        if (DataRoot is not { IsAbsoluteUri: true } root || string.IsNullOrWhiteSpace(dataset.File))
            throw new InvalidOperationException($"{dataset.Id} has no address.");
        return new Uri(root, dataset.File.TrimStart('/'));
    }

    /// <summary>The address of an NHC storm list: <see cref="DataRoot"/> joined with <see cref="File"/> (<c>CurrentStorms.json</c>).</summary>
    [JsonIgnore]
    public Uri? StormListUri =>
        Kind == WeatherServiceKind.NhcStorms && DataRoot is { IsAbsoluteUri: true } root && !string.IsNullOrWhiteSpace(File)
            ? new Uri(root, File.TrimStart('/'))
            : null;

    /// <summary>The forecast hours offered: 0, <see cref="StepHours"/>, … up to <see cref="MaxHours"/>.</summary>
    [JsonIgnore]
    public IReadOnlyList<int> OfferedHours =>
        [.. Enumerable.Range(0, (Math.Max(MaxHours, 0) / Math.Max(StepHours, 1)) + 1).Select(i => i * Math.Max(StepHours, 1))];

    /// <summary>True when the description can be used: its kind's required fields are set.</summary>
    [JsonIgnore]
    public bool IsUsable => Kind switch
    {
        WeatherServiceKind.GribFilter or WeatherServiceKind.GribIndex =>
            DataRoot is { IsAbsoluteUri: true } && !string.IsNullOrWhiteSpace(File) && Parameters.Count > 0
            && CadenceHours > 0 && StepHours > 0 && MaxHours >= 0,
        WeatherServiceKind.ErddapGriddap => Datasets.Count > 0 && Datasets.All(d => d.Variables.Count > 0),
        WeatherServiceKind.NwsAlerts => true,
        WeatherServiceKind.WpcBulletins => DataRoot is { IsAbsoluteUri: true } && Datasets.Count > 0
            && Datasets.All(d => !string.IsNullOrWhiteSpace(d.File)),
        WeatherServiceKind.NhcStorms => StormListUri is not null,
        _ => false,
    };
}
