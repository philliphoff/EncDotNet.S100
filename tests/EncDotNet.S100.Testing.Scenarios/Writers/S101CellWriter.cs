using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Testing.Scenarios.Geometry;
using EncDotNet.S100.Testing.Scenarios.World;
using static EncDotNet.S100.Testing.Scenarios.Writers.S101DocumentBuilder;

namespace EncDotNet.S100.Testing.Scenarios.Writers;

/// <summary>A maintained channel, charted as an S-101 <c>DredgedArea</c>.</summary>
/// <param name="Name">A fictitious name.</param>
/// <param name="Area">The dredged rectangle.</param>
/// <param name="MaintainedDepth">The maintained depth (DRVAL1) in metres; no deeper than the true seabed anywhere in the area.</param>
public sealed record DredgedChannel(string Name, LocalRect Area, double MaintainedDepth);

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
/// <c>Sounding</c>, and the <c>DataCoverage</c> and <c>SoundingDatum</c>
/// meta-features.
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
    /// <returns>The document.</returns>
    public static S101Document Build(SyntheticWorld world, S101CellSpec cell, IReadOnlyList<DredgedChannel> dredged, DateOnly issueDate)
    {
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
            (x, y) => GeneralisedDepth(world, x, y, cell.GeneralisationRadius));

        // Depth areas and land are traced piece by piece over the cell minus
        // its dredged areas, so the skin of the earth tiles the cell exactly.
        var pieces = Partition(extent, channels.Select(c => c.Area)).Select(depths.Within).ToList();

        var builder = new S101DocumentBuilder(ProducingAgency, cell.FoidSubdivision);
        var coverage = Rectangle(frame, extent);
        builder.AddSurface(
            "DataCoverage",
            coverage,
            [],
            FeatureAttribute.Number("maximumDisplayScale", cell.MaximumDisplayScale),
            FeatureAttribute.Number("minimumDisplayScale", cell.MinimumDisplayScale),
            FeatureAttribute.Number("optimumDisplayScale", cell.OptimumDisplayScale));
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

        var soundings = Soundings(world, cell, highWater);
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

    private static List<(GeoPosition Position, double Depth)> Soundings(SyntheticWorld world, S101CellSpec cell, double highWater)
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
                var depth = world.DepthAt(sx, sy);
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
