using System.Globalization;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Weather;

namespace EncDotNet.S100.Collections.Library;

/// <summary>
/// A scope of a weather service (#1075; design note weather-sources.md §6.3):
/// for a GRIB2 model, its parameters and forecast days; for ERDDAP, its
/// datasets and forecast days; for alerts, nothing to choose. The area is the
/// map view when the source is added (the GRIB filter and ERDDAP cut to it,
/// alerts are its marine zones'); NOAA's whole-grid files on AWS take the
/// whole globe. Reading the "catalogue" finds the service's latest run (or
/// the alerts in force), so a service that can't be reached says so before
/// the source is added. The service's defaults start ticked, and only the
/// ticked choices are included.
/// </summary>
public sealed class WeatherScope : LibraryCatalogueScope
{
    private readonly KnownCatalogueSource _known;
    private readonly WeatherService _service;
    private readonly Func<WeatherFeedSource, CancellationToken, Task<WeatherServiceSummary>> _describe;
    private readonly Func<GeoBounds?>? _currentView;
    private readonly Func<GeoBounds, IReadOnlyList<string>>? _marineZones;
    private IReadOnlyList<LibraryChoiceGroup> _groups = [];
    private WeatherServiceSummary? _summary;
    private GeoBounds? _area;
    private IReadOnlyList<string> _zones = [];

    /// <summary>Creates a scope of the weather service <paramref name="known"/> describes.</summary>
    /// <param name="known">The known source; its <see cref="KnownCatalogueSource.Weather"/> must be set.</param>
    /// <param name="describe">Reads what the service has now (its latest run).</param>
    /// <param name="currentView">The map view the area is taken from; the whole globe without one.</param>
    /// <param name="marineZones">The NWS marine zone codes in an area, for alerts; every marine alert without.</param>
    public WeatherScope(
        KnownCatalogueSource known,
        Func<WeatherFeedSource, CancellationToken, Task<WeatherServiceSummary>> describe,
        Func<GeoBounds?>? currentView = null,
        Func<GeoBounds, IReadOnlyList<string>>? marineZones = null)
        : base((known ?? throw new ArgumentNullException(nameof(known))).CatalogUri)
    {
        ArgumentNullException.ThrowIfNull(describe);
        _known = known;
        _service = known.Weather ?? throw new ArgumentException($"{known.Name} is not a weather service.", nameof(known));
        _describe = describe;
        _currentView = currentView;
        _marineZones = marineZones;
        IncludeAll = false;
    }

    /// <summary>What the service offers.</summary>
    public WeatherService Service => _service;

    /// <inheritdoc />
    public override bool IsLoaded => _summary is not null;

    /// <inheritdoc />
    public override IReadOnlyList<LibraryChoiceGroup> Groups => _groups;

    /// <summary>The area the source covers (the map view when it was read), or null for the whole globe.</summary>
    public GeoBounds? Area => _area;

    /// <summary>The service's latest run, once read (a model service).</summary>
    public DateTimeOffset? LatestRun => _summary?.LatestRun;

    /// <summary>"Latest run 2026-10-28 00Z · area 32°N–40°N, 76°W–68°W" once read; null before.</summary>
    public string? RunNote => _summary is null
        ? null
        : string.Join(" · ", new[]
        {
            _summary.LatestRun is { } run
                ? string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Weather_LatestRunFormat"), WeatherRequests.FormatRun(run))
                : null,
            AreaText,
            _summary.Detail,
        }.OfType<string>());

    /// <summary>"Area 32°N–40°N, 76°W–68°W", "Whole globe", or the marine zones.</summary>
    public string AreaText => _service.Kind == WeatherServiceKind.NwsAlerts
        ? _zones.Count == 0
            ? LibraryText.Get("Weather_AllMarineZones")
            : string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Weather_ZonesFormat"), _zones.Count)
        : _area is { } area
            ? string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Weather_AreaFormat"), FormatArea(area))
            : LibraryText.Get("Weather_WholeGlobe");

    private IEnumerable<LibraryChoice> Parameters => GroupOptions(ParametersKey);

    private IEnumerable<LibraryChoice> Days => GroupOptions(DaysKey);

    private IEnumerable<LibraryChoice> DatasetChoices => GroupOptions(DatasetsKey);

    /// <inheritdoc />
    public override Task<string?> LoadAsync(CancellationToken cancellationToken = default) => TryLoadAsync(async () =>
    {
        _area = _service.Kind == WeatherServiceKind.GribIndex ? null : _currentView?.Invoke();
        _zones = _service.Kind == WeatherServiceKind.NwsAlerts && _area is { } view && _marineZones is { } zones ? zones(view) : [];
        var groups = new List<LibraryChoiceGroup>();
        if (_service.IsGrib)
        {
            groups.Add(new LibraryChoiceGroup(LibraryText.Get("Weather_ParametersTitle"),
                [.. _service.Parameters.Select(p => new LibraryChoice(p.IndexMatch, p.Name, $"{p.Id} · {p.Level}") { IsSelected = p.Default })])
            {
                Key = ParametersKey,
            });
        }

        if (_service.Kind == WeatherServiceKind.ErddapGriddap)
        {
            groups.Add(new LibraryChoiceGroup(LibraryText.Get("Weather_DatasetsTitle"),
                [.. _service.Datasets.Select(d => new LibraryChoice(d.Id, d.Name, string.Join(", ", d.Variables)) { IsSelected = d.Default || _service.Datasets.Count == 1 })])
            {
                Key = DatasetsKey,
            });
        }

        if (_service.Kind != WeatherServiceKind.NwsAlerts)
        {
            var days = (int)Math.Ceiling(_service.MaxHours / 24d);
            groups.Add(new LibraryChoiceGroup(LibraryText.Get("Weather_DaysTitle"),
                [.. Enumerable.Range(1, Math.Max(days, 1)).Select(d => new LibraryChoice(
                    d.ToString(CultureInfo.InvariantCulture),
                    string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Weather_DayFormat"), d),
                    string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Weather_DayHoursFormat"), d == 1 ? 0 : ((d - 1) * 24) + _service.StepHours, Math.Min(d * 24, _service.MaxHours)))
                {
                    IsSelected = (d - 1) * 24 < _service.DefaultHours,
                })])
            {
                Key = DaysKey,
            });
        }

        _groups = groups;
        var summary = await _describe((WeatherFeedSource)Build(Guid.Empty, null), cancellationToken).ConfigureAwait(false);
        CatalogueDate = summary.LatestRun is { } run ? DateOnly.FromDateTime(run.UtcDateTime) : null;
        _summary = summary;
    });

    /// <summary>The selection the ticks make.</summary>
    public WeatherSelection CurrentSelection
    {
        get
        {
            var parameters = IncludeAll ? _service.Parameters.Select(p => p.IndexMatch) : Parameters.Where(o => o.IsSelected).Select(o => o.Value);
            var datasets = IncludeAll ? _service.Datasets.Select(d => d.Id) : DatasetChoices.Where(o => o.IsSelected).Select(o => o.Value);
            var days = IncludeAll ? Days.Select(o => o.Value) : Days.Where(o => o.IsSelected).Select(o => o.Value);
            var hours = days
                .Select(d => int.Parse(d, CultureInfo.InvariantCulture))
                .SelectMany(d => _service.OfferedHours.Where(h => d == 1 ? h <= 24 : h > (d - 1) * 24 && h <= d * 24))
                .Distinct()
                .Order()
                .ToArray();
            return new WeatherSelection([.. parameters], hours, _area, _zones, [.. datasets]);
        }
    }

    /// <summary>"3 parameters · 17 forecast hours" (or "1 dataset · …", or the alerts in force).</summary>
    public override string SelectionSummary => _summary is null ? string.Empty : Describe(CurrentSelection);

    /// <inheritdoc />
    public override string EverythingSummary
    {
        get
        {
            if (_summary is null)
                return string.Empty;
            var all = new WeatherSelection(
                [.. _service.Parameters.Select(p => p.IndexMatch)], _service.OfferedHours, _area, _zones, [.. _service.Datasets.Select(d => d.Id)]);
            return Describe(all);
        }
    }

    /// <inheritdoc />
    /// <remarks>Alerts have nothing to choose: the one download is the alerts in force.</remarks>
    public override LibraryChoice? SingleEntry => _summary is not null && _service.Kind == WeatherServiceKind.NwsAlerts
        ? new LibraryChoice(_service.Model, _service.ModelName ?? _known.Name,
            string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Weather_AlertsInForceFormat"), _summary.Items) + " · " + AreaText)
        : null;

    /// <summary>The ticked parameters ("Significant wave height, Peak wave period"), or null when none.</summary>
    public override string? DescribeSelection() => _service.Kind switch
    {
        WeatherServiceKind.ErddapGriddap => LibrarySourceText.List([.. DatasetChoices.Where(o => o.IsSelected).Select(o => o.Label)]),
        _ when _service.IsGrib => LibrarySourceText.List([.. Parameters.Where(o => o.IsSelected).Select(o => o.Label)]),
        _ => null,
    };

    /// <summary>The name stays the service's: what is chosen is said by the selection summary.</summary>
    public override bool IsEverything => true;

    /// <inheritdoc />
    public override CollectionSource Build(Guid id, string? name) =>
        new WeatherFeedSource(id, name, CatalogUri, _service, CurrentSelection)
        {
            Licence = _known.Licence,
            Attribution = _known.Attribution,
        };

    private string Describe(WeatherSelection selection)
    {
        var c = CultureInfo.CurrentCulture;
        if (_service.Kind == WeatherServiceKind.NwsAlerts)
            return string.Format(c, LibraryText.Get("Weather_AlertsInForceFormat"), _summary?.Items ?? 0) + " · " + AreaText;
        var what = _service.IsGrib
            ? string.Format(c, LibraryText.Get("Weather_ParametersFormat"), selection.ParametersOf(_service).Count)
            : string.Format(c, LibraryText.Get("Weather_DatasetsFormat"), selection.DatasetsOf(_service).Count);
        var hours = selection.HoursOf(_service);
        return string.Join(" · ", what,
            string.Format(c, LibraryText.Get("Weather_HoursFormat"), hours.Count, hours.Count > 0 ? hours[^1] : 0),
            AreaText);
    }

    private IEnumerable<LibraryChoice> GroupOptions(string key) => _groups.Where(g => g.Key == key).SelectMany(g => g.Options);

    /// <summary>"32°N–40°N, 76°W–68°W".</summary>
    public static string FormatArea(GeoBounds area)
    {
        static string Lat(double v) => string.Create(CultureInfo.InvariantCulture, $"{Math.Abs(v):0.##}°{(v < 0 ? 'S' : 'N')}");
        static string Lon(double v) => string.Create(CultureInfo.InvariantCulture, $"{Math.Abs(v):0.##}°{(v < 0 ? 'W' : 'E')}");
        return $"{Lat(area.South)}–{Lat(area.North)}, {Lon(area.West)}–{Lon(area.East)}";
    }

    private const string ParametersKey = "parameters";
    private const string DaysKey = "days";
    private const string DatasetsKey = "datasets";
}
