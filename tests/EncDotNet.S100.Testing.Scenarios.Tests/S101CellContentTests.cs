using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Testing.Scenarios.Writers;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>
/// The generated S-101 cells, read back through the real reader, chart what
/// the world model says: their skin of the earth tiles the cell, and every
/// depth area's range holds the depth the cell sampled inside it.
/// </summary>
public sealed class S101CellContentTests(SpitAcrossScalesFixture fixture) : IClassFixture<SpitAcrossScalesFixture>
{
    private static readonly string[] SkinOfTheEarth = ["LandArea", "DepthArea", "DredgedArea"];

    public static TheoryData<string> Cells => new() { "101ZZ00SPIT5", "101ZZ00SPIT3" };

    [Theory]
    [MemberData(nameof(Cells))]
    public void The_cell_carries_every_feature_type(string name)
    {
        var features = Features(name);

        foreach (var type in new[] { "DataCoverage", "SoundingDatum", "LandArea", "DepthArea", "DredgedArea", "Coastline", "DepthContour", "Sounding" })
        {
            Assert.Contains(features, f => f.FeatureType == type);
        }

        var coverage = Assert.Single(features, f => f.FeatureType == "DataCoverage");
        var spec = fixture.Definition.Cells.Single(c => c.DatasetName == name);
        Assert.Equal(spec.MinimumDisplayScale.ToString(CultureInfo.InvariantCulture), Text(coverage, "minimumDisplayScale"));
        Assert.Equal(spec.MaximumDisplayScale.ToString(CultureInfo.InvariantCulture), Text(coverage, "maximumDisplayScale"));
        Assert.Equal("23", Text(Assert.Single(features, f => f.FeatureType == "SoundingDatum"), "verticalDatum"));
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void The_skin_of_the_earth_tiles_the_cell_and_depth_ranges_hold_the_sampled_depth(string name)
    {
        var spec = fixture.Definition.Cells.Single(c => c.DatasetName == name);
        var world = fixture.Definition.World;
        var skin = Features(name).Where(f => SkinOfTheEarth.Contains(f.FeatureType)).ToList();
        var problems = new List<string>();

        const int probes = 80;
        for (var j = 0; j < probes; j++)
        {
            for (var i = 0; i < probes; i++)
            {
                // Probe cell centres, so no probe sits on the cell's edge.
                var x = spec.Extent.West + (i + 0.5) * spec.Extent.Width / probes;
                var y = spec.Extent.South + (j + 0.5) * spec.Extent.Height / probes;
                var p = world.Frame.ToGeo(x, y);
                var covering = skin.Where(f => Contains((IS100Feature)f, p)).ToList();
                var sampled = S101CellWriter.GeneralisedDepth(world, x, y, spec.GeneralisationRadius);

                // Near a boundary the grid's linear interpolation may put the
                // probe on either side; a grid cell's worth of slope is allowed.
                if (covering.Count != 1)
                {
                    if (!NearBoundary(world, spec, x, y))
                    {
                        problems.Add($"({x:0},{y:0}) depth {sampled:0.00}: covered by {covering.Count} [{string.Join(", ", covering.Select(Describe))}]");
                    }

                    continue;
                }

                var feature = covering[0];
                if (feature.FeatureType == "DepthArea"
                    && !NearBoundary(world, spec, x, y)
                    && !(Value(feature, "depthRangeMinimumValue") <= sampled && sampled <= Value(feature, "depthRangeMaximumValue")))
                {
                    problems.Add($"({x:0},{y:0}) depth {sampled:0.00} outside {Describe(feature)}");
                }
            }
        }

        Assert.True(problems.Count == 0, $"{problems.Count} problems:{Environment.NewLine}{string.Join(Environment.NewLine, problems.Take(25))}");
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void The_skin_of_the_earth_has_no_gaps_or_overlaps(string name)
    {
        // Gaps shrink the total area and overlaps grow it; thin slivers that
        // sparse probes miss still show up here.
        var spec = fixture.Definition.Cells.Single(c => c.DatasetName == name);
        var frame = fixture.Definition.World.Frame;
        double total = 0;
        foreach (var feature in Features(name).Where(f => SkinOfTheEarth.Contains(f.FeatureType)))
        {
            foreach (var surface in ((IS100Feature)feature).Surfaces)
            {
                total += Math.Abs(Area(frame, surface.ExteriorRing)) - surface.InteriorRings.Sum(h => Math.Abs(Area(frame, h)));
            }
        }

        var cell = spec.Extent.Width * spec.Extent.Height;
        Assert.InRange(total / cell, 0.99999, 1.00001);
    }

    private static double Area(World.WorldFrame frame, IReadOnlyList<GeoPosition> ring)
    {
        double sum = 0;
        for (var k = 0; k < ring.Count - 1; k++)
        {
            var (x0, y0) = frame.ToLocal(ring[k].Latitude, ring[k].Longitude);
            var (x1, y1) = frame.ToLocal(ring[k + 1].Latitude, ring[k + 1].Longitude);
            sum += x0 * y1 - x1 * y0;
        }

        return sum / 2;
    }

    private bool NearBoundary(World.SyntheticWorld world, S101CellSpec spec, double x, double y)
    {
        var d = S101CellWriter.GeneralisedDepth(world, x, y, spec.GeneralisationRadius);
        var limits = new List<double> { -world.HighWaterHeight };
        limits.AddRange(spec.DepthContours);
        var step = spec.GridSpacing;
        double slope = 0;
        foreach (var (dx, dy) in new[] { (step, 0.0), (0.0, step) })
        {
            slope = Math.Max(slope, Math.Abs(S101CellWriter.GeneralisedDepth(world, x + dx, y + dy, spec.GeneralisationRadius) - d));
            slope = Math.Max(slope, Math.Abs(S101CellWriter.GeneralisedDepth(world, x - dx, y - dy, spec.GeneralisationRadius) - d));
        }

        var nearChannel = fixture.Definition.DredgedChannels.Any(c => Math.Abs(c.Area.SignedDistance(x, y)) < step);
        return nearChannel || limits.Any(l => Math.Abs(d - l) <= slope);
    }

    private IReadOnlyList<Feature> Features(string name)
    {
        var path = fixture.Scenario.Datasets.Single(d => d.RelativePath.EndsWith(name + ".000", StringComparison.Ordinal)).FullPath;
        return new S101VectorSource(S101Dataset.Open(path)).GetFeatures().ToList();
    }

    private static string Describe(Feature f) =>
        f.FeatureType == "DepthArea" ? $"DepthArea {Text(f, "depthRangeMinimumValue")}–{Text(f, "depthRangeMaximumValue")}" : f.FeatureType;

    private static double Value(Feature f, string attribute) => double.Parse(Text(f, attribute)!, CultureInfo.InvariantCulture);

    private static string? Text(Feature f, string attribute) => Convert.ToString(f.Attributes[attribute], CultureInfo.InvariantCulture);

    private static bool Contains(IS100Feature feature, GeoPosition p)
    {
        foreach (var surface in feature.Surfaces)
        {
            if (InRing(surface.ExteriorRing, p) && !surface.InteriorRings.Any(h => InRing(h, p)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InRing(IReadOnlyList<GeoPosition> ring, GeoPosition p)
    {
        var inside = false;
        for (int a = 0, b = ring.Count - 1; a < ring.Count; b = a++)
        {
            if ((ring[a].Latitude > p.Latitude) != (ring[b].Latitude > p.Latitude)
                && p.Longitude < (ring[b].Longitude - ring[a].Longitude) * (p.Latitude - ring[a].Latitude) / (ring[b].Latitude - ring[a].Latitude) + ring[a].Longitude)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
