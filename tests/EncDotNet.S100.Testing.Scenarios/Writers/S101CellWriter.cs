using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Testing.Scenarios.Geometry;
using EncDotNet.S100.Testing.Scenarios.Quirks;
using EncDotNet.S100.Testing.Scenarios.World;
using static EncDotNet.S100.Testing.Scenarios.Writers.S101DocumentBuilder;

namespace EncDotNet.S100.Testing.Scenarios.Writers;

/// <summary>A maintained channel, charted as an S-101 <c>DredgedArea</c>.</summary>
/// <param name="Name">A fictitious name.</param>
/// <param name="Area">The dredged rectangle.</param>
/// <param name="MaintainedDepth">The maintained depth (DRVAL1) in metres; no deeper than the true seabed anywhere in the area.</param>
public sealed record DredgedChannel(string Name, LocalRect Area, double MaintainedDepth);

/// <summary>The kind of a charted point danger.</summary>
public enum HazardKind
{
    /// <summary>An S-101 <c>Wreck</c>.</summary>
    Wreck,

    /// <summary>An S-101 <c>Obstruction</c>.</summary>
    Obstruction,

    /// <summary>An S-101 <c>UnderwaterAwashRock</c>.</summary>
    Rock,
}

/// <summary>
/// A charted point danger (#955): a wreck, obstruction or rock, charted as a
/// point feature in the cells that cover it.
/// </summary>
/// <param name="Name">The scenario's name for it, written as its <c>featureName</c> (fictitious).</param>
/// <param name="Kind">What it is.</param>
/// <param name="Position">Where it is, in local metres.</param>
/// <param name="ValueOfSounding">Its least depth in metres (<c>valueOfSounding</c>), or <c>null</c> when its depth is unknown.</param>
/// <param name="WaterLevelEffect">
/// Its <c>waterLevelEffect</c>: 1 partly submerged at high water, 2 always
/// dry, 3 always under water, 4 covers and uncovers, 5 awash.
/// </param>
/// <param name="Cells">
/// The dataset names of the cells that chart it, or <c>null</c> for every
/// cell whose extent contains it.
/// </param>
public sealed record PointHazard(
    string Name,
    HazardKind Kind,
    (double X, double Y) Position,
    double? ValueOfSounding,
    int WaterLevelEffect,
    IReadOnlyList<string>? Cells = null)
{
    /// <summary>The S-101 feature type.</summary>
    public string FeatureType => Kind switch
    {
        HazardKind.Wreck => "Wreck",
        HazardKind.Obstruction => "Obstruction",
        _ => "UnderwaterAwashRock",
    };

    /// <summary>Whether a cell charts it.</summary>
    /// <param name="cell">The cell.</param>
    /// <returns><c>true</c> when the cell charts it.</returns>
    public bool ChartedIn(S101CellSpec cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return Cells is null ? cell.Extent.Contains(Position.X, Position.Y) : Cells.Contains(cell.DatasetName);
    }
}

/// <summary>
/// A regulated area charted in the S-101 cells (#956): a restricted,
/// caution, military practice, anchorage or traffic area, with its
/// restrictions, categories and dates.
/// </summary>
/// <param name="Name">The scenario's name for it, written as its <c>featureName</c> (fictitious).</param>
/// <param name="FeatureType">The S-101 feature type, such as <c>"RestrictedArea"</c> or <c>"CautionArea"</c>.</param>
/// <param name="Area">The area's rectangle, in local metres.</param>
/// <param name="Restrictions">Its <c>restriction</c> codes, one attribute each (7 entry prohibited, 27 speed restricted, …).</param>
/// <param name="Categories">
/// Its category codes (<c>categoryOfRestrictedArea</c> for a restricted area,
/// <c>categoryOfMilitaryPracticeArea</c>, <c>categoryOfAnchorage</c>).
/// </param>
/// <param name="Cells">
/// The dataset names of the cells that chart it, or <c>null</c> for every
/// cell whose extent contains it.
/// </param>
public sealed record RegulatedAreaSpec(
    string Name,
    string FeatureType,
    LocalRect Area,
    IReadOnlyList<int> Restrictions,
    IReadOnlyList<int> Categories,
    IReadOnlyList<string>? Cells = null)
{
    /// <summary>Holes in the area, in local metres; empty for none.</summary>
    public IReadOnlyList<LocalRect> Holes { get; init; } = [];

    /// <summary>When it applies each year (<c>periodicDateRange</c>); empty for all year.</summary>
    public IReadOnlyList<Season> Seasons { get; init; } = [];

    /// <summary>Whether a cell charts it.</summary>
    /// <param name="cell">The cell.</param>
    /// <returns><c>true</c> when the cell charts it.</returns>
    public bool ChartedIn(S101CellSpec cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return Cells is null
            ? cell.Extent.Contains(Area.West, Area.South) && cell.Extent.Contains(Area.East, Area.North)
            : Cells.Contains(cell.DatasetName);
    }

    /// <summary>The category attribute's code for the feature type.</summary>
    public string CategoryAttribute => FeatureType switch
    {
        "MilitaryPracticeArea" => "categoryOfMilitaryPracticeArea",
        "AnchorageArea" => "categoryOfAnchorage",
        _ => "categoryOfRestrictedArea",
    };
}

/// <summary>
/// One S-101 cell: its extent, usage band and scale range, and how coarsely
/// it samples the world.
/// </summary>
/// <param name="DatasetName">The file name without extension, such as <c>"101ZZ00SSOUND5"</c>.</param>
/// <param name="Title">The dataset title (DSTL).</param>
/// <param name="Extent">The cell's rectangle; it is also the data coverage.</param>
/// <param name="GridSpacing">The contouring resolution in metres.</param>
/// <param name="GeneralisationRadius">
/// The shoal-biased generalisation radius in metres: each node takes the
/// shoalest depth within this distance, so a coarser cell widens shoals and
/// never deepens anything. Zero samples the world as is.
/// </param>
/// <param name="DepthContours">The depth contours in metres, ascending, starting at 0.</param>
/// <param name="SoundingSpacing">The spacing of selected soundings in metres.</param>
/// <param name="MinimumDisplayScale">The DataCoverage <c>minimumDisplayScale</c> denominator.</param>
/// <param name="OptimumDisplayScale">The DataCoverage <c>optimumDisplayScale</c> denominator.</param>
/// <param name="MaximumDisplayScale">The DataCoverage <c>maximumDisplayScale</c> denominator.</param>
/// <param name="FoidSubdivision">The FOID subdivision, unique per cell in the exchange set.</param>
public sealed record S101CellSpec(
    string DatasetName,
    string Title,
    LocalRect Extent,
    double GridSpacing,
    double GeneralisationRadius,
    IReadOnlyList<double> DepthContours,
    double SoundingSpacing,
    int MinimumDisplayScale,
    int OptimumDisplayScale,
    int MaximumDisplayScale,
    ushort FoidSubdivision)
{
    /// <summary>The file name, with the <c>.000</c> extension.</summary>
    public string FileName => DatasetName + ".000";
}

/// <summary>
/// Samples a <see cref="SyntheticWorld"/> into an S-101 cell (S-101 Ed 2.0.0):
/// the skin of the earth (<c>LandArea</c>, <c>DepthArea</c>,
/// <c>DredgedArea</c>), <c>Coastline</c>, <c>DepthContour</c>,
/// <c>Sounding</c>, point dangers (<c>Wreck</c>, <c>Obstruction</c>,
/// <c>UnderwaterAwashRock</c>), and the <c>DataCoverage</c> and
/// <c>SoundingDatum</c> meta-features.
/// </summary>
/// <remarks>
/// <para>
/// Depth areas are the bands between the cell's depth contours, traced on
/// the cell's grid; the drying band runs from minus the high-water height
/// (the coastline) to 0 m, and land is everything shallower. A band's
/// <c>depthRangeMinimumValue</c> (DRVAL1) is its lower contour, so it is
/// never deeper than the sampled seabed inside it. Dredged areas are cut
/// out of the depth areas.
/// </para>
/// <para>
/// Soundings are taken on a jittered grid (seeded per cell) and rounded
/// down to the decimetre, as charted soundings are.
/// </para>
/// </remarks>
public static class S101CellWriter
{
    /// <summary>The FOID producing agency code used for every generated feature.</summary>
    public const ushort ProducingAgency = 0;

    /// <summary>Builds the cell's document.</summary>
    /// <param name="world">The world.</param>
    /// <param name="cell">The cell.</param>
    /// <param name="dredged">Dredged channels to chart where they overlap the cell.</param>
    /// <param name="issueDate">The dataset reference date.</param>
    /// <param name="quirks">
    /// The cell's producer quirks, or <c>null</c> for none: declared display
    /// scales, a shoal bias, and spiked areas.
    /// </param>
    /// <param name="hazards">Point dangers, charted where <see cref="PointHazard.ChartedIn"/> says; <c>null</c> for none.</param>
    /// <param name="areas">Regulated areas, charted where <see cref="RegulatedAreaSpec.ChartedIn"/> says; <c>null</c> for none.</param>
    /// <returns>The document.</returns>
    public static S101Document Build(SyntheticWorld world, S101CellSpec cell, IReadOnlyList<DredgedChannel> dredged, DateOnly issueDate, DatasetQuirks? quirks = null, IReadOnlyList<PointHazard>? hazards = null, IReadOnlyList<RegulatedAreaSpec>? areas = null)
    {
        quirks ??= DatasetQuirks.None;
        var bias = quirks.ShoalBias;
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(dredged);

        var extent = cell.Extent;
        var frame = world.Frame;
        var highWater = world.HighWaterHeight;
        var channels = dredged
            .Select(d => d with { Area = Intersect(d.Area, extent) })
            .Where(d => d.Area.Width > 0 && d.Area.Height > 0)
            .ToList();

        // One node lattice for the whole cell, including the dredged areas'
        // edges, so the pieces below share their nodes along common edges.
        var depths = new SampleGrid(
            frame,
            SampleGrid.Lattice(extent.West, extent.East, cell.GridSpacing, channels.SelectMany(c => new[] { c.Area.West, c.Area.East })),
            SampleGrid.Lattice(extent.South, extent.North, cell.GridSpacing, channels.SelectMany(c => new[] { c.Area.South, c.Area.North })),
            (x, y) => GeneralisedDepth(world, x, y, cell.GeneralisationRadius) - bias);

        // Depth areas and land are traced piece by piece over the cell minus
        // its dredged areas, so the skin of the earth tiles the cell exactly.
        var pieces = Partition(extent, channels.Select(c => c.Area)).Select(depths.Within).ToList();

        var builder = new S101DocumentBuilder(ProducingAgency, cell.FoidSubdivision);
        var coverage = Rectangle(frame, extent);
        var (minimumScale, maximumScale) = quirks.DisplayScales(cell.MinimumDisplayScale, cell.MaximumDisplayScale);
        var coverageAttributes = new List<FeatureAttribute>();
        if (maximumScale is { } maximum)
        {
            coverageAttributes.Add(FeatureAttribute.Number("maximumDisplayScale", maximum));
        }

        if (minimumScale is { } minimum)
        {
            coverageAttributes.Add(FeatureAttribute.Number("minimumDisplayScale", minimum));
        }

        coverageAttributes.Add(FeatureAttribute.Number("optimumDisplayScale", cell.OptimumDisplayScale));
        builder.AddSurface("DataCoverage", coverage, [], [.. coverageAttributes]);
        builder.AddSurface("SoundingDatum", coverage, [], FeatureAttribute.Number("verticalDatum", world.Datums.ChartDatum));

        foreach (var piece in pieces)
        {
            foreach (var land in Contouring.Regions(piece, double.NegativeInfinity, -highWater))
            {
                builder.AddSurface("LandArea", land.Exterior, land.Holes, FeatureAttribute.FeatureName($"{world.Name} mainland (fictitious)"));
            }
        }

        var deepest = Math.Ceiling(MaximumDepth(depths));
        var limits = new List<double> { -highWater };
        limits.AddRange(cell.DepthContours);
        for (var band = 0; band < limits.Count; band++)
        {
            var shallow = limits[band];
            var deep = band + 1 < limits.Count ? limits[band + 1] : Math.Max(deepest, shallow + 1);
            var upper = band + 1 < limits.Count ? deep : double.PositiveInfinity;
            foreach (var piece in pieces)
            {
                foreach (var area in Contouring.Regions(piece, shallow, upper))
                {
                    builder.AddSurface(
                        "DepthArea",
                        area.Exterior,
                        area.Holes,
                        FeatureAttribute.Number("depthRangeMinimumValue", shallow),
                        FeatureAttribute.Number("depthRangeMaximumValue", deep));
                }
            }
        }

        foreach (var channel in channels)
        {
            builder.AddSurface(
                "DredgedArea",
                Rectangle(frame, channel.Area),
                [],
                FeatureAttribute.Number("depthRangeMinimumValue", channel.MaintainedDepth),
                FeatureAttribute.FeatureName(channel.Name));
        }

        // Coastline: water on the left, land on the right.
        foreach (var line in Contouring.Lines(depths, -highWater))
        {
            builder.AddCurve("Coastline", line);
        }

        foreach (var contour in cell.DepthContours)
        {
            foreach (var line in Contouring.Lines(depths, contour))
            {
                foreach (var run in OutsideAreas(frame, line, channels))
                {
                    builder.AddCurve("DepthContour", run, FeatureAttribute.Number("valueOfDepthContour", contour));
                }
            }
        }

        foreach (var spiked in quirks.SpikedAreas)
        {
            builder.AddSurface(spiked.FeatureType, DatasetQuirks.SpikedRing(frame, spiked), []);
        }

        foreach (var hazard in hazards ?? [])
        {
            if (!hazard.ChartedIn(cell))
            {
                continue;
            }

            var attributes = new List<FeatureAttribute>();
            if (hazard.ValueOfSounding is { } valueOfSounding)
            {
                attributes.Add(FeatureAttribute.Number("valueOfSounding", valueOfSounding));
            }

            attributes.Add(FeatureAttribute.Number("waterLevelEffect", hazard.WaterLevelEffect));
            attributes.Add(FeatureAttribute.FeatureName(hazard.Name));
            builder.AddPoint(hazard.FeatureType, frame.ToGeo(hazard.Position.X, hazard.Position.Y), [.. attributes]);
        }

        foreach (var area in areas ?? [])
        {
            if (!area.ChartedIn(cell))
            {
                continue;
            }

            var attributes = new List<FeatureAttribute> { FeatureAttribute.FeatureName(area.Name) };
            foreach (var season in area.Seasons)
            {
                attributes.Add(new FeatureAttribute("periodicDateRange", "",
                [
                    new FeatureAttribute("dateStart", "----" + season.Start.Text[2..].Replace("-", "", StringComparison.Ordinal)),
                    new FeatureAttribute("dateEnd", "----" + season.End.Text[2..].Replace("-", "", StringComparison.Ordinal)),
                ]));
            }

            attributes.AddRange(area.Restrictions.Select(r => FeatureAttribute.Number("restriction", r)));
            attributes.AddRange(area.Categories.Select(c => FeatureAttribute.Number(area.CategoryAttribute, c)));
            builder.AddSurface(
                area.FeatureType,
                Rectangle(frame, area.Area),
                area.Holes.Select(h => (IReadOnlyList<GeoPosition>)Rectangle(frame, h).Reverse().ToList()).ToList(),
                [.. attributes]);
        }

        var soundings = Soundings(world, cell, highWater, bias);
        if (soundings.Count > 0)
        {
            builder.AddSoundings(soundings);
        }

        return builder.Build(new S101DatasetIdentification
        {
            RecordName = 10,
            RecordId = 1,
            EncodingSpecification = "S-100 Part 10a",
            EncodingSpecificationEdition = "5.2",
            ProductSpecification = "INT.IHO.S-101.2.0",
            ProductSpecificationEdition = "2.0.0",
            ApplicationProfile = "1",
            DatasetName = cell.FileName,
            DatasetTitle = cell.Title,
            DatasetReferenceDate = issueDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            DatasetLanguage = "eng",
            DatasetAbstract = $"SYNTHETIC test data for {world.Name}, a fictitious area (seed {world.Seed.ToString(CultureInfo.InvariantCulture)}). Not for navigation.",
            DatasetEdition = "1",
        });
    }

    /// <summary>
    /// The depth a cell charts at a point: the shoalest true depth within
    /// <paramref name="radius"/> metres (on a 5 × 5 stencil), or the true
    /// depth when the radius is zero.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="x">Metres east.</param>
    /// <param name="y">Metres north.</param>
    /// <param name="radius">The generalisation radius in metres.</param>
    /// <returns>The generalised depth in metres.</returns>
    public static double GeneralisedDepth(SyntheticWorld world, double x, double y, double radius)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (radius <= 0)
        {
            return world.DepthAt(x, y);
        }

        var shoalest = double.PositiveInfinity;
        for (var dj = -2; dj <= 2; dj++)
        {
            for (var di = -2; di <= 2; di++)
            {
                shoalest = Math.Min(shoalest, world.DepthAt(x + di * radius / 2, y + dj * radius / 2));
            }
        }

        return shoalest;
    }

    private static List<(GeoPosition Position, double Depth)> Soundings(SyntheticWorld world, S101CellSpec cell, double highWater, double bias)
    {
        var random = ScenarioRandom.For(world.Seed, "soundings:" + cell.DatasetName);
        var extent = cell.Extent;
        var spacing = cell.SoundingSpacing;
        var result = new List<(GeoPosition, double)>();
        for (var y = extent.South + spacing / 2; y < extent.North; y += spacing)
        {
            for (var x = extent.West + spacing / 2; x < extent.East; x += spacing)
            {
                var sx = x + random.NextDouble(-0.3, 0.3) * spacing;
                var sy = y + random.NextDouble(-0.3, 0.3) * spacing;
                var depth = world.DepthAt(sx, sy) - bias;
                if (depth <= -highWater)
                {
                    continue;
                }

                result.Add((world.Frame.ToGeo(sx, sy), Math.Floor(depth * 10) / 10));
            }
        }

        return result;
    }

    // Splits a line into the runs whose vertices lie outside every area.
    private static IEnumerable<IReadOnlyList<GeoPosition>> OutsideAreas(WorldFrame frame, IReadOnlyList<GeoPosition> line, List<DredgedChannel> areas)
    {
        var run = new List<GeoPosition>();
        foreach (var p in line)
        {
            var (x, y) = frame.ToLocal(p.Latitude, p.Longitude);
            if (areas.Any(a => a.Area.Contains(x, y)))
            {
                if (run.Count >= 2)
                {
                    yield return run;
                }

                run = new List<GeoPosition>();
                continue;
            }

            run.Add(p);
        }

        if (run.Count >= 2)
        {
            yield return run;
        }
    }

    private static double MaximumDepth(SampleGrid grid)
    {
        var max = double.NegativeInfinity;
        for (var j = 0; j < grid.Rows; j++)
        {
            for (var i = 0; i < grid.Columns; i++)
            {
                max = Math.Max(max, grid[i, j]);
            }
        }

        return max;
    }

    // The rectangle minus the holes, as rectangles: around each hole, a full-
    // height piece west and east of it and pieces south and north of it.
    private static List<LocalRect> Partition(LocalRect extent, IEnumerable<LocalRect> holes)
    {
        var pieces = new List<LocalRect> { extent };
        foreach (var hole in holes)
        {
            var next = new List<LocalRect>();
            foreach (var piece in pieces)
            {
                var h = Intersect(piece, hole);
                if (h.Width <= 0 || h.Height <= 0)
                {
                    next.Add(piece);
                    continue;
                }

                next.AddRange(new[]
                {
                    new LocalRect(piece.West, piece.South, h.West, piece.North),
                    new LocalRect(h.East, piece.South, piece.East, piece.North),
                    new LocalRect(h.West, piece.South, h.East, h.South),
                    new LocalRect(h.West, h.North, h.East, piece.North),
                }.Where(r => r.Width > 0 && r.Height > 0));
            }

            pieces = next;
        }

        return pieces;
    }

    private static LocalRect Intersect(LocalRect a, LocalRect b) => new(
        Math.Max(a.West, b.West),
        Math.Max(a.South, b.South),
        Math.Min(a.East, b.East),
        Math.Min(a.North, b.North));

    /// <summary>A rectangle as a closed clockwise ring.</summary>
    /// <param name="frame">The world frame.</param>
    /// <param name="rect">The rectangle.</param>
    /// <returns>The ring.</returns>
    public static IReadOnlyList<GeoPosition> Rectangle(WorldFrame frame, LocalRect rect)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return
        [
            frame.ToGeo(rect.West, rect.South),
            frame.ToGeo(rect.West, rect.North),
            frame.ToGeo(rect.East, rect.North),
            frame.ToGeo(rect.East, rect.South),
            frame.ToGeo(rect.West, rect.South),
        ];
    }
}
