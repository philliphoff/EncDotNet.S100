using EncDotNet.S100.DataModel;
using EncDotNet.S100.Testing.Scenarios.Quirks;
using EncDotNet.S100.Testing.Scenarios.World;
using EncDotNet.S100.Testing.Scenarios.Writers;

namespace EncDotNet.S100.Testing.Scenarios;

/// <summary>
/// A named, seeded set of products sampled from one world: what to write,
/// and the places and route the scenario's tests talk about.
/// </summary>
/// <param name="Name">The scenario's name, such as <c>"spit-across-scales"</c>.</param>
/// <param name="World">The world every product samples.</param>
/// <param name="Issued">The issue time written to every dataset and the catalogue (UTC).</param>
/// <param name="WaterLevelStart">The first water-level record (UTC).</param>
/// <param name="WaterLevelEnd">The last water-level record (UTC).</param>
/// <param name="Cells">The S-101 cells.</param>
/// <param name="DredgedChannels">Dredged channels, charted in every cell they overlap.</param>
/// <param name="Surfaces">The S-102 surfaces.</param>
/// <param name="Stations">The S-104 station series, or <c>null</c>.</param>
/// <param name="WaterLevelGrids">The S-104 grids.</param>
/// <param name="Places">Named local points, in metres.</param>
/// <param name="Route">The scenario's route as named local waypoints, in metres (the S-421 routes are in <see cref="Routes"/>).</param>
public sealed record ScenarioDefinition(
    string Name,
    SyntheticWorld World,
    DateTime Issued,
    DateTime WaterLevelStart,
    DateTime WaterLevelEnd,
    IReadOnlyList<S101CellSpec> Cells,
    IReadOnlyList<DredgedChannel> DredgedChannels,
    IReadOnlyList<S102SurfaceSpec> Surfaces,
    S104StationsSpec? Stations,
    IReadOnlyList<S104GridSpec> WaterLevelGrids,
    IReadOnlyDictionary<string, (double X, double Y)> Places,
    IReadOnlyList<string> Route)
{
    /// <summary>The S-111 current grids.</summary>
    public IReadOnlyList<S111GridSpec> CurrentGrids { get; init; } = [];

    /// <summary>The S-111 current station series, or <c>null</c>.</summary>
    public S111StationsSpec? CurrentStations { get; init; }

    /// <summary>The S-421 routes.</summary>
    public IReadOnlyList<S421RouteSpec> Routes { get; init; } = [];

    /// <summary>The S-124 navigational warnings, one dataset each.</summary>
    public IReadOnlyList<S124WarningSpec> Warnings { get; init; } = [];

    /// <summary>The S-124 edition to write: 2 (Edition 2.0.0, the default) or 1 (Edition 1.0.0).</summary>
    public int S124Edition { get; init; } = 2;

    /// <summary>The S-129 under-keel clearance plans.</summary>
    public IReadOnlyList<S129PlanSpec> UkcPlans { get; init; } = [];

    /// <summary>The S-122 marine protected areas, one dataset each.</summary>
    public IReadOnlyList<S122ProtectedAreaSpec> ProtectedAreas { get; init; } = [];

    /// <summary>Charted point dangers (#955), written into the S-101 cells that chart them.</summary>
    public IReadOnlyList<PointHazard> Hazards { get; init; } = [];

    /// <summary>Regulated areas (#956), written into the S-101 cells that chart them.</summary>
    public IReadOnlyList<RegulatedAreaSpec> RegulatedAreas { get; init; } = [];

    /// <summary>The S-125 aids to navigation, or <c>null</c> for none.</summary>
    public S125AidsSpec? Aids { get; init; }

    /// <summary>
    /// Producer quirks by dataset name (the file name without extension):
    /// how each dataset departs from a clean encoding. None by default.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ProducerQuirk>> Quirks { get; init; } =
        new Dictionary<string, IReadOnlyList<ProducerQuirk>>();

    /// <summary>The quirks of one dataset.</summary>
    /// <param name="datasetName">The dataset's file name without extension.</param>
    /// <returns>Its quirks, or <see cref="DatasetQuirks.None"/>.</returns>
    public DatasetQuirks QuirksFor(string datasetName) =>
        Quirks.TryGetValue(datasetName, out var quirks) ? new DatasetQuirks(quirks) : DatasetQuirks.None;

    /// <summary>The geographic position of a named place.</summary>
    /// <param name="name">The place's name.</param>
    /// <returns>The position.</returns>
    public GeoPosition Place(string name) => World.Frame.ToGeo(Places[name].X, Places[name].Y);

    /// <summary>The route's waypoints as geographic positions.</summary>
    public IReadOnlyList<GeoPosition> RoutePositions => Route.Select(Place).ToList();
}
