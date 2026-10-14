using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// A synthetic stand-in for the handoff's fixture route: nine waypoints due
/// east along the equator, 0,02° (about 1,2 NM) apart, run at 6 kn from
/// 07:10, with an 11 m draft and 1 m required UKC. The S-102 grid is 15 m
/// deep except a 9 m shoal on leg 6 → 7 (0,105–0,115° E), and the S-104 grid
/// holds 0,5 m every 30 minutes from 06:00 to 12:00. Both declare LAT
/// unless a test says otherwise; a test can pass its own tide
/// (<see cref="HighWaterUntil0750"/>, <see cref="TwoHighWaters"/>).
/// </summary>
internal sealed class PassageCheckFixture
{
    public const int Lat = 23;
    public static readonly DateTimeOffset Departure = new(2024, 6, 1, 7, 10, 0, TimeSpan.Zero);
    public static readonly DateTime WaterStart = new(2024, 6, 1, 6, 0, 0, DateTimeKind.Utc);

    public PassageCheckFixture(
        DateTime? waterEnd = null,
        int? depthDatum = Lat,
        TimeSpan? debounce = null,
        Action<Action>? post = null,
        double? s101FallbackUntilLon = null,
        Func<DateTime, float>? tide = null)
    {
        var datasets = new List<LoadedDataset>
        {
            Bathymetry(depthDatum, s101FallbackUntilLon),
            WaterLevel(waterEnd ?? WaterStart.AddHours(6), tide ?? (_ => 0.5f), tide is null ? TimeSpan.FromMinutes(30) : TimeSpan.FromMinutes(10)),
        };
        if (s101FallbackUntilLon is not null)
            datasets.Add(FallbackCell());
        Catalog = new FixedCatalog(datasets);
        Routes = new RoutesService();
        Route = Routes.Routes.CreateRoute("Fixture");
        for (var i = 0; i < 9; i++)
            Route.AppendWaypoint(new GeoPosition(0, i * 0.02));
        Route.SetPassageValue(RoutePassageField.Draft, 11);
        Route.SetPassageValue(RoutePassageField.Speed, 6);
        Route.SetPassageValue(RoutePassageField.RequiredUkc, 1);
        Route.SetDeparture(Departure);
        Check = new PassageCheckService(Routes, Catalog, debounce: debounce ?? TimeSpan.Zero, post: post);
    }

    public FixedCatalog Catalog { get; }

    public RoutesService Routes { get; }

    public Route Route { get; }

    public PassageCheckService Check { get; }

    /// <summary>
    /// A tide (10-minute steps) that clears the 9 m shoal by 1,5 m above the
    /// 1 m setting until 07:50, then falls to 0,5 m by 08:00: departures up
    /// to 06:40 cross the shoal in time and meet your settings, and 07:10
    /// doesn't.
    /// </summary>
    public static float HighWaterUntil0750(DateTime t) => t <= WaterStart.AddMinutes(110) ? 4f : 0.5f;

    /// <summary>
    /// <see cref="HighWaterUntil0750"/> with a second high water of 4 m from
    /// 09:30, so the departures that meet your settings form two windows.
    /// </summary>
    public static float TwoHighWaters(DateTime t) => t >= WaterStart.AddMinutes(210) ? 4f : HighWaterUntil0750(t);

    /// <summary>Turns check mode on and waits for the result.</summary>
    public async Task<PassageCheckSnapshot> CheckAsync()
    {
        Routes.IsCheckModeActive = true;
        await Check.WhenIdle();
        return Assert.IsType<PassageCheckSnapshot>(Check.Current);
    }

    /// <summary>
    /// An S-101 cell with one 13 m <c>DepthArea</c> over the whole route and
    /// a LAT <c>SoundingDatum</c>: the fallback where S-102 has no data.
    /// </summary>
    private static LoadedDataset FallbackCell()
    {
        var box = Mcp.Tools.Tests.Fakes.S101AreaSynth.Box(-0.02, -0.02, 0.02, 0.2);
        var cell = Mcp.Tools.Tests.Fakes.S101AreaSynth.Cell(
        [
            Mcp.Tools.Tests.Fakes.S101AreaSynth.DepthArea(13, box),
            Mcp.Tools.Tests.Fakes.S101AreaSynth.SoundingDatum(Lat, box),
        ]);
        return new LoadedDataset(
            new DatasetId("101FIXTURE"),
            new SpecRef("S-101", new SpecVersion(2, 0, 0)),
            new BoundingBox(-0.02, -0.02, 0.02, 0.2),
            null,
            new S101DatasetData(cell));
    }

    private static LoadedDataset Bathymetry(int? datum, double? noDataUntilLon = null)
    {
        const int rows = 22;
        const int cols = 200;
        var values = new BathymetryValue[rows * cols];
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                var lon = -0.01 + ((c + 0.5) * 0.001);
                values[(r * cols) + c] = noDataUntilLon is { } edge && lon < edge
                    ? new BathymetryValue(S102CoverageSource.FillValue, S102CoverageSource.FillValue)
                    : new BathymetryValue(lon is >= 0.105 and < 0.115 ? 9f : 15f, 0.3f);
            }
        }

        var dataset = new S102Dataset
        {
            HorizontalCRS = 4326,
            VerticalDatum = datum,
            Coverages =
            [
                new BathymetryCoverage
                {
                    OriginLatitude = -0.011,
                    OriginLongitude = -0.01,
                    SpacingLatitudinal = 0.001,
                    SpacingLongitudinal = 0.001,
                    NumPointsLatitudinal = rows,
                    NumPointsLongitudinal = cols,
                    Values = values,
                },
            ],
        };
        return new LoadedDataset(
            new DatasetId("102FIXTURE"),
            new SpecRef("S-102", new SpecVersion(2, 1, 0)),
            new BoundingBox(-0.011, -0.01, 0.011, 0.19),
            null,
            new S102CoverageData(new S102CoverageSource(dataset)));
    }

    private static LoadedDataset WaterLevel(DateTime end, Func<DateTime, float> tide, TimeSpan step)
    {
        var coverages = new List<WaterLevelCoverage>();
        for (var t = WaterStart; t <= end; t += step)
        {
            var values = new WaterLevelValue[5 * 25];
            Array.Fill(values, new WaterLevelValue(tide(t), 3));
            coverages.Add(new WaterLevelCoverage
            {
                OriginLatitude = -0.02,
                OriginLongitude = -0.02,
                SpacingLatitudinal = 0.01,
                SpacingLongitudinal = 0.01,
                NumPointsLatitudinal = 5,
                NumPointsLongitudinal = 25,
                TimePoint = t,
                Values = values,
            });
        }

        var dataset = new S104Dataset { HorizontalCRS = 4326, DataCodingFormat = 2, VerticalDatum = Lat, Coverages = coverages };
        return new LoadedDataset(
            new DatasetId("104FIXTURE"),
            new SpecRef("S-104", new SpecVersion(2, 0, 0)),
            new BoundingBox(-0.02, -0.02, 0.02, 0.22),
            null,
            new S104CoverageData(new S104CoverageSource(dataset)));
    }

    /// <summary>A fixed catalog.</summary>
    internal sealed class FixedCatalog(IReadOnlyList<LoadedDataset> datasets) : IDatasetCatalog
    {
        public IReadOnlyList<LoadedDataset> Datasets { get; } = datasets;

        public event EventHandler<DatasetCatalogChangedEventArgs>? Changed { add { } remove { } }
    }
}
