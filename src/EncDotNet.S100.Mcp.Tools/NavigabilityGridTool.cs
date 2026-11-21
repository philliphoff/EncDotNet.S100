using System.ComponentModel;
using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>A <c>get_navigability_grid</c> request.</summary>
/// <param name="South">The box's south latitude, decimal degrees WGS-84.</param>
/// <param name="West">The box's west longitude.</param>
/// <param name="North">The box's north latitude.</param>
/// <param name="East">The box's east longitude; less than <paramref name="West"/> crosses the antimeridian.</param>
/// <param name="CellMeters">The cell size in metres.</param>
/// <param name="DraftMeters">The draft in metres; a cell needs at least this much charted depth to be navigable.</param>
/// <param name="SafetyContourMeters">The safety contour in metres; a cell needs at least this much too.</param>
/// <param name="HazardDepthMeters">Dangers at or shallower than this mark their cells; default the minimum depth.</param>
/// <param name="IncludeHazards">Whether to read the charted dangers.</param>
/// <param name="Connect">Two positions to test for a navigable way between them, or <c>null</c>.</param>
/// <param name="Format">The grid file's format: <c>npy</c>, <c>raw</c> or <c>none</c>.</param>
/// <param name="IncludeImage">Whether to return a PNG picture of the grid.</param>
public sealed record NavigabilityGridToolRequest(
    [property: Description("South latitude of the box, decimal degrees WGS-84.")] double South,
    [property: Description("West longitude of the box.")] double West,
    [property: Description("North latitude of the box.")] double North,
    [property: Description("East longitude of the box; less than west crosses the antimeridian.")] double East,
    [property: Description("Cell size in metres.")] double CellMeters = NavigabilityGridTool.DefaultCellMeters,
    [property: Description("Draft in metres: a navigable cell has at least this much charted depth.")] double? DraftMeters = null,
    [property: Description("Safety contour in metres: a navigable cell has at least this much charted depth too.")] double? SafetyContourMeters = null,
    [property: Description("Dangers at or shallower than this mark their cells; default the minimum depth.")] double? HazardDepthMeters = null,
    [property: Description("Whether to read the charted dangers and the structures standing in the water (breakwaters, piers, pontoons, hulks).")] bool IncludeHazards = true,
    [property: Description("Two positions to test for a navigable way between them.")] IReadOnlyList<GeoPoint>? Connect = null,
    [property: Description("The grid file's format: npy, raw or none.")] string Format = NavigabilityGridTool.FormatNpy,
    [property: Description("Whether to return a PNG of the grid.")] bool IncludeImage = false);

/// <summary>The grid's geometry.</summary>
[Description("The grid's geometry: cell row 0 is the southernmost row, column 0 the westernmost; values are row-major from the south-west.")]
public sealed record NavigabilityGridGeometry(
    [property: Description("South edge of the grid, decimal degrees.")] double South,
    [property: Description("West edge of the grid.")] double West,
    [property: Description("North edge of the grid (the box's north rounded up to a whole cell).")] double North,
    [property: Description("East edge of the grid, possibly past 180 when it crosses the antimeridian.")] double East,
    [property: Description("Number of rows.")] int Rows,
    [property: Description("Number of columns.")] int Columns,
    [property: Description("Cell size in metres.")] double CellMeters,
    [property: Description("Degrees of latitude per row.")] double LatitudeStep,
    [property: Description("Degrees of longitude per column.")] double LongitudeStep);

/// <summary>Cell counts by class.</summary>
public sealed record NavigabilityGridCounts(
    [property: Description("Charted water at least the minimum depth with no danger in the cell.")] int Navigable,
    [property: Description("Charted water shallower than the minimum depth, drying ground included.")] int Shoal,
    [property: Description("Charted water with a danger at or shallower than the hazard depth (or of unknown depth) in the cell, or a structure standing in it (breakwater, pier, pontoon, hulk).")] int Hazard,
    [property: Description("Charted land.")] int Land,
    [property: Description("Not covered by any loaded chart.")] int Uncharted,
    [property: Description("Charted water with no charted depth.")] int NoDepth);

/// <summary>A cell, located.</summary>
public sealed record NavigabilityGridCellReport(
    [property: Description("Row, from the south.")] int Row,
    [property: Description("Column, from the west.")] int Column,
    [property: Description("Latitude of the cell's centre.")] double Latitude,
    [property: Description("Longitude of the cell's centre.")] double Longitude,
    [property: Description("The cell's least charted depth in metres, when it has one.")] double? DepthMeters = null,
    [property: Description("For the shoalest cell: what the depth is (bathymetry, dredged_area, depth_area, sounding), sampled at its centre.")] string? DepthKind = null,
    [property: Description("For the shoalest cell: the dataset the centre's depth came from.")] string? DatasetId = null,
    [property: Description("Distance from the cell's centre to the nearest cell that isn't navigable, in metres.")] double? ClearanceMeters = null);

/// <summary>The largest connected navigable area.</summary>
public sealed record NavigabilityGridArea(
    [property: Description("Cells in it.")] int Cells,
    [property: Description("Its area in square kilometres.")] double AreaKm2,
    [property: Description("The number of separate navigable areas in the grid.")] int AreaCount,
    [property: Description("Its cell farthest from anything not navigable.")] NavigabilityGridCellReport MostOpenCell);

/// <summary>Whether two positions connect.</summary>
public sealed record NavigabilityGridConnection(
    [property: Description("Whether a way through navigable cells (joined through their sides) joins them.")] bool Connected,
    [property: Description("Why not: from_off_grid, to_off_grid, from_not_navigable, to_not_navigable or no_navigable_way.")] string? Reason,
    [property: Description("Where the widest way between them is narrowest, with the clearance there (half the passage's width, roughly).")] NavigabilityGridCellReport? Narrowest);

/// <summary>The written grid file.</summary>
public sealed record NavigabilityGridFile(
    [property: Description("Absolute path on the server's file system.")] string Path,
    [property: Description("npy or raw.")] string Format,
    [property: Description("File size in bytes.")] long Bytes,
    [property: Description("How to read it.")] string Layout);

/// <summary>A <c>get_navigability_grid</c> result.</summary>
public sealed record NavigabilityGridResult(
    [property: Description("The grid's geometry.")] NavigabilityGridGeometry Grid,
    [property: Description("The least charted depth a navigable cell has, in metres: the larger of the draft and the safety contour.")] double MinimumDepthMeters,
    [property: Description("Dangers at or shallower than this marked their cells, in metres.")] double? HazardDepthMeters,
    [property: Description("Cells by class.")] NavigabilityGridCounts Counts,
    [property: Description("Navigable area in square kilometres.")] double NavigableAreaKm2,
    [property: Description("The navigable cell with the least charted depth.")] NavigabilityGridCellReport? ShoalestNavigable,
    [property: Description("The largest connected navigable area.")] NavigabilityGridArea? LargestNavigableArea,
    [property: Description("The connect test, when asked.")] NavigabilityGridConnection? Connection,
    [property: Description("The chart and S-102 datasets overlapping the box.")] IReadOnlyList<string> Datasets,
    [property: Description("The grid itself, when a format was asked for.")] NavigabilityGridFile? File,
    [property: Description("What the grid is and isn't.")] string Caveat);

/// <summary>
/// <c>get_navigability_grid</c> (#1558): water, land and charted depth over a
/// box on a regular grid, with overlapping cells resolved as the route check
/// resolves them. The reply is a summary; the grid itself goes to a file.
/// </summary>
public sealed class NavigabilityGridTool
{
    /// <summary>The tool's name.</summary>
    public const string Name = "get_navigability_grid";

    /// <summary>The default cell size in metres.</summary>
    public const double DefaultCellMeters = 50;

    /// <summary>NumPy <c>.npy</c>: a structured array of <c>depth_dm</c> (int16) and <c>class</c> (uint8).</summary>
    public const string FormatNpy = "npy";

    /// <summary>Raw little-endian int16 depths in decimetres, then one class byte per cell.</summary>
    public const string FormatRaw = "raw";

    /// <summary>No file.</summary>
    public const string FormatNone = "none";

    /// <summary>The depth written for a cell with none.</summary>
    public const short NoDepthValue = short.MinValue;

    private const string Caveat =
        "Charted depths (S-102, else the finest S-101/S-57 cell), not reduced to a common datum and with no water level applied; " +
        "a cell's depth is the least at its centre and corners. Not for navigation.";

    private readonly IDatasetCatalog _catalog;
    private readonly RouteCheckSourceCache _sources;
    private readonly string _outputDirectory;

    /// <summary>Creates the tool.</summary>
    /// <param name="catalog">The loaded datasets.</param>
    /// <param name="sources">The route-check sources, shared with <c>check_route</c>.</param>
    /// <param name="outputDirectory">Where grid files are written; created when needed.</param>
    public NavigabilityGridTool(IDatasetCatalog catalog, RouteCheckSourceCache sources, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        _catalog = catalog;
        _sources = sources;
        _outputDirectory = outputDirectory;
    }

    /// <summary>Builds the grid, writes its file and summarises it.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the build.</param>
    /// <returns>The summary, and the PNG when asked for; or the error.</returns>
    public async Task<(ToolResult<NavigabilityGridResult> Result, byte[]? Png)> InvokeAsync(NavigabilityGridToolRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Validate(request) is { } invalid)
        {
            return (ToolResult<NavigabilityGridResult>.Err(invalid), null);
        }

        var minimum = Math.Max(request.DraftMeters ?? double.NegativeInfinity, request.SafetyContourMeters ?? double.NegativeInfinity);
        var box = new BoundingBox(request.South, request.West, request.North, request.East);
        var gridRequest = new NavigabilityGridRequest(box, request.CellMeters, minimum, request.HazardDepthMeters, request.IncludeHazards);
        if (gridRequest.CellCount() > NavigabilityGridRequest.MaxCells)
        {
            var suggested = gridRequest.CellMetersFor(NavigabilityGridRequest.MaxCells);
            return (ToolResult<NavigabilityGridResult>.Err(new GridTooLarge(gridRequest.CellCount(), NavigabilityGridRequest.MaxCells, suggested)), null);
        }

        var datasets = _catalog.Datasets;
        var sources = _sources.Get(datasets);
        var grid = await Task.Run(() => NavigabilityGrid.Build(sources, gridRequest, cancellationToken), cancellationToken).ConfigureAwait(false);

        NavigabilityGridFile? file = null;
        if (!string.Equals(request.Format, FormatNone, StringComparison.OrdinalIgnoreCase))
        {
            file = await WriteFileAsync(grid, request.Format.ToLowerInvariant(), cancellationToken).ConfigureAwait(false);
        }

        var result = Summarise(grid, sources, datasets, request, file);
        var png = request.IncludeImage ? NavigabilityGridImage.Render(grid) : null;
        return (ToolResult<NavigabilityGridResult>.Ok(result), png);
    }

    private static InvalidArgument? Validate(NavigabilityGridToolRequest request)
    {
        static bool Latitude(double v) => double.IsFinite(v) && v >= -90 && v <= 90;
        static bool Longitude(double v) => double.IsFinite(v) && v >= -180 && v <= 180;
        if (!Latitude(request.South) || !Latitude(request.North))
        {
            return new InvalidArgument("south", "south and north must be latitudes in [-90, 90]");
        }

        if (!(request.North > request.South))
        {
            return new InvalidArgument("north", "north must be greater than south");
        }

        if (!Longitude(request.West) || !Longitude(request.East) || request.West == request.East)
        {
            return new InvalidArgument("west", "west and east must be different longitudes in [-180, 180]");
        }

        if (!double.IsFinite(request.CellMeters) || request.CellMeters < 1)
        {
            return new InvalidArgument("cellMeters", "the cell size must be at least 1 m");
        }

        if (request.DraftMeters is null && request.SafetyContourMeters is null)
        {
            return new InvalidArgument("draftMeters", "give draftMeters or safetyContourMeters: a navigable cell needs at least that much charted depth");
        }

        if (request.DraftMeters is { } draft && (!double.IsFinite(draft) || draft < 0))
        {
            return new InvalidArgument("draftMeters", "the draft must be zero or more");
        }

        if (request.SafetyContourMeters is { } contour && !double.IsFinite(contour))
        {
            return new InvalidArgument("safetyContourMeters", "the safety contour must be a number");
        }

        if (request.HazardDepthMeters is { } hazard && !double.IsFinite(hazard))
        {
            return new InvalidArgument("hazardDepthMeters", "the hazard depth must be a number");
        }

        if (request.Format is null || !(request.Format.Equals(FormatNpy, StringComparison.OrdinalIgnoreCase)
            || request.Format.Equals(FormatRaw, StringComparison.OrdinalIgnoreCase)
            || request.Format.Equals(FormatNone, StringComparison.OrdinalIgnoreCase)))
        {
            return new InvalidArgument("format", "the format must be npy, raw or none");
        }

        if (request.Connect is { } connect)
        {
            if (connect.Count != 2)
            {
                return new InvalidArgument("connect", "connect takes exactly two positions, [[lat,lon],[lat,lon]]");
            }

            if (connect.Any(p => !Latitude(p.Latitude) || !Longitude(p.Longitude)))
            {
                return new InvalidArgument("connect", "positions must be latitudes in [-90, 90] and longitudes in [-180, 180]");
            }
        }

        return null;
    }

    private static NavigabilityGridResult Summarise(
        NavigabilityGrid grid,
        RouteCheckSources sources,
        IReadOnlyList<LoadedDataset> datasets,
        NavigabilityGridToolRequest request,
        NavigabilityGridFile? file)
    {
        var classes = grid.Classes;
        var depths = grid.LeastDepths;
        int navigable = 0, shoal = 0, hazard = 0, land = 0, uncharted = 0, noDepth = 0;
        var shoalest = -1;
        for (var i = 0; i < classes.Length; i++)
        {
            switch (classes[i])
            {
                case NavigabilityCellClass.Navigable:
                    navigable++;
                    if (shoalest < 0 || depths[i] < depths[shoalest])
                    {
                        shoalest = i;
                    }

                    break;
                case NavigabilityCellClass.Shoal: shoal++; break;
                case NavigabilityCellClass.Hazard: hazard++; break;
                case NavigabilityCellClass.Land: land++; break;
                case NavigabilityCellClass.Uncharted: uncharted++; break;
                default: noDepth++; break;
            }
        }

        var cellKm2 = grid.CellMeters * grid.CellMeters / 1e6;
        var centres = grid.Centres;
        var geometry = new NavigabilityGridGeometry(
            Round(centres.South - (centres.LatitudeStep / 2)),
            Round(centres.West - (centres.LongitudeStep / 2)),
            Round(centres.South + ((grid.Rows - 0.5) * centres.LatitudeStep)),
            Round(centres.West + ((grid.Columns - 0.5) * centres.LongitudeStep)),
            grid.Rows,
            grid.Columns,
            grid.CellMeters,
            centres.LatitudeStep,
            centres.LongitudeStep);

        NavigabilityGridCellReport? shoalestReport = null;
        if (shoalest >= 0)
        {
            var cell = new NavigabilityCell(shoalest / grid.Columns, shoalest % grid.Columns);
            var at = sources.Depth?.Sample(grid.CenterOf(cell));
            shoalestReport = Report(grid, cell) with
            {
                DepthKind = at is null ? null : KindName(at.Kind),
                DatasetId = at?.DatasetId,
            };
        }

        NavigabilityGridArea? largest = null;
        var sizes = grid.ComponentSizes;
        if (sizes.Count > 0)
        {
            var label = 0;
            for (var k = 1; k < sizes.Count; k++)
            {
                if (sizes[k] > sizes[label])
                {
                    label = k;
                }
            }

            // Its most open cell: farthest from anything not navigable.
            var best = -1;
            var bestClearance = -1.0;
            for (var row = 0; row < grid.Rows; row++)
            {
                for (var col = 0; col < grid.Columns; col++)
                {
                    if (grid.ComponentAt(row, col) == label && grid.ClearanceMeters(row, col) is var c && c > bestClearance)
                    {
                        bestClearance = c;
                        best = (row * grid.Columns) + col;
                    }
                }
            }

            largest = new NavigabilityGridArea(sizes[label], Round(sizes[label] * cellKm2, 3), sizes.Count, Report(grid, new NavigabilityCell(best / grid.Columns, best % grid.Columns)));
        }

        NavigabilityGridConnection? connection = null;
        if (request.Connect is [var from, var to])
        {
            var c = grid.Connect(new GeoPosition(from.Latitude, from.Longitude), new GeoPosition(to.Latitude, to.Longitude));
            connection = new NavigabilityGridConnection(c.Connected, c.Reason, c.Narrowest is { } n ? Report(grid, n) : null);
        }

        var box = new BoundingBox(request.South, request.West, request.North, request.East);
        var used = new SortedSet<string>(StringComparer.Ordinal);
        if (sources.Charts is { } charts)
        {
            foreach (var cell in charts.CellsNear(box))
            {
                used.Add(cell.DatasetId);
            }
        }

        foreach (var dataset in datasets)
        {
            if (dataset.Spec.Name == "S-102" && Overlaps(dataset.Bounds, box))
            {
                used.Add(dataset.Id.Value);
            }
        }

        return new NavigabilityGridResult(
            geometry,
            grid.MinimumDepthMeters,
            request.IncludeHazards ? request.HazardDepthMeters ?? grid.MinimumDepthMeters : null,
            new NavigabilityGridCounts(navigable, shoal, hazard, land, uncharted, noDepth),
            Round(navigable * cellKm2, 3),
            shoalestReport,
            largest,
            connection,
            [.. used],
            file,
            Caveat);
    }

    private static NavigabilityGridCellReport Report(NavigabilityGrid grid, NavigabilityCell cell)
    {
        var centre = grid.CenterOf(cell);
        return new NavigabilityGridCellReport(
            cell.Row,
            cell.Column,
            Round(centre.Latitude),
            Round(centre.Longitude),
            grid.LeastDepthAt(cell.Row, cell.Column) is { } d ? Math.Round(d, 2) : null,
            ClearanceMeters: grid.IsNavigable(cell.Row, cell.Column) ? Math.Round(grid.ClearanceMeters(cell.Row, cell.Column), 1) : null);
    }

    private static string KindName(ChartedDepthKind kind) => kind switch
    {
        ChartedDepthKind.Bathymetry => "bathymetry",
        ChartedDepthKind.DredgedArea => "dredged_area",
        ChartedDepthKind.DepthArea => "depth_area",
        _ => "sounding",
    };

    private static double Round(double degrees, int digits = 7) => Math.Round(degrees, digits);

    private static bool Overlaps(BoundingBox a, BoundingBox b) =>
        a.SouthLatitude <= b.NorthLatitude && a.NorthLatitude >= b.SouthLatitude
        && (b.WestLongitude > b.EastLongitude
            || (a.WestLongitude <= b.EastLongitude && a.EastLongitude >= b.WestLongitude));

    private async Task<NavigabilityGridFile> WriteFileAsync(NavigabilityGrid grid, string format, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_outputDirectory);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(_outputDirectory, $"navigability-{stamp}-{Guid.NewGuid().ToString("N")[..8]}.{(format == FormatNpy ? "npy" : "bin")}");
        var bytes = format == FormatNpy ? NavigabilityGridFiles.Npy(grid) : NavigabilityGridFiles.Raw(grid);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        var layout = format == FormatNpy
            ? $"NumPy structured array, shape ({grid.Rows}, {grid.Columns}), row 0 south, column 0 west; fields depth_dm (int16, least charted depth in decimetres, {NoDepthValue} for none) and class (uint8: {ClassCodes})."
            : $"{grid.Rows * grid.Columns} little-endian int16 depths in decimetres ({NoDepthValue} for none), then {grid.Rows * grid.Columns} class bytes ({ClassCodes}); both row-major from the south-west, {grid.Columns} columns per row.";
        return new NavigabilityGridFile(Path.GetFullPath(path), format, bytes.LongLength, layout);
    }

    private static string ClassCodes =>
        string.Join(", ", Enum.GetValues<NavigabilityCellClass>().Select(c => $"{(byte)c} {NavigabilityGridFiles.ClassName(c)}"));
}

/// <summary>A grid larger than the cap, with a cell size that fits.</summary>
[Description("Raised when the box and cell size give more cells than the server builds; suggestedCellMeters fits.")]
public sealed record GridTooLarge(
    [property: Description("The cells the request would give.")] long Cells,
    [property: Description("The most cells a grid may have.")] int MaxCells,
    [property: Description("A cell size in metres that keeps the box within the cap.")] double SuggestedCellMeters) : ToolError(
    "grid_too_large",
    $"The grid would have {Cells} cells, more than {MaxCells}; use cellMeters of at least {SuggestedCellMeters} or a smaller box.");
