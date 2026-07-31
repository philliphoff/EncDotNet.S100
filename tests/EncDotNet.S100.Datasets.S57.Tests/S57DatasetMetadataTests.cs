using EncDotNet.S100.Datasets.S101;

namespace EncDotNet.S100.Datasets.S57.Tests;

/// <summary>
/// Tests for <see cref="S57Dataset.ReadMetadata(EncDotNet.S57.S57Document)"/> —
/// the cheap "peek" path added for phased / deferred loading (issue #460). The
/// extent must fold the raw spatial coordinates (2-D vertices and sounding
/// nodes) through the coordinate multiplication factor (COMF), and the
/// display-scale window must carry the compilation scale (CSCL) as its coarsest
/// bound, all without the S-57 → S-101 translation or any portrayal.
/// </summary>
public class S57DatasetMetadataTests
{
    private const uint Comf = 10_000_000;

    private static int Raw(double degrees) => (int)Math.Round(degrees * Comf);

    private static EncDotNet.S57.S57RecordName Name(byte rcnm, uint id)
        => new() { RecordNameCode = rcnm, RecordId = (int)id };

    private static EncDotNet.S57.S57VectorRecord Node(uint id, double lat, double lon)
        => new()
        {
            RecordName = Name(120, id), // ConnectedNode
            VectorPointers = [],
            Coordinates2D = [new EncDotNet.S57.S57Coordinate2D { X = Raw(lon), Y = Raw(lat) }],
            Soundings = [],
            Attributes = [],
        };

    private static EncDotNet.S57.S57VectorRecord SoundingNode(uint id, double lat, double lon, int depth)
        => new()
        {
            RecordName = Name(110, id), // IsolatedNode
            VectorPointers = [],
            Coordinates2D = [],
            Soundings = [new EncDotNet.S57.S57Sounding { X = Raw(lon), Y = Raw(lat), Depth = depth }],
            Attributes = [],
        };

    private static EncDotNet.S57.S57Document Document(
        IEnumerable<EncDotNet.S57.S57VectorRecord>? vectorRecords = null,
        int compilationScale = 50_000,
        uint comf = Comf)
        => new()
        {
            DataSetIdentification = new EncDotNet.S57.S57DataSetIdentification
            {
                DataSetName = "TEST.000",
                EditionNumber = "1",
                UpdateNumber = "0",
                IssueDate = "20240101",
            },
            DataSetParameters = new EncDotNet.S57.S57DataSetParameters
            {
                CompilationScale = compilationScale,
                CoordinateMultiplicationFactor = (int)comf,
                SoundingMultiplicationFactor = 10,
            },
            VectorRecords = (vectorRecords ?? Array.Empty<EncDotNet.S57.S57VectorRecord>()).ToArray(),
            FeatureRecords = Array.Empty<EncDotNet.S57.S57FeatureRecord>(),
        };

    [Fact]
    public void ReadMetadata_declares_canonical_S57_spec()
    {
        var metadata = S57Dataset.ReadMetadata(Document());

        Assert.Equal("S-57", metadata.Spec.Name);
        Assert.Equal(default, metadata.Spec.Edition);
        Assert.Null(metadata.HorizontalCrsEpsg);
    }

    [Fact]
    public void ReadMetadata_folds_extent_over_vertices()
    {
        var document = Document(
        [
            Node(1, lat: 42.1, lon: -71.5),
            Node(2, lat: 42.9, lon: -70.5),
        ]);

        var extent = S57Dataset.ReadMetadata(document).Extent;

        Assert.NotNull(extent);
        Assert.Equal(-71.5, extent!.WestLongitude, 6);
        Assert.Equal(-70.5, extent.EastLongitude, 6);
        Assert.Equal(42.1, extent.SouthLatitude, 6);
        Assert.Equal(42.9, extent.NorthLatitude, 6);
    }

    [Fact]
    public void ReadMetadata_includes_sounding_nodes_in_extent()
    {
        var document = Document(
        [
            Node(1, lat: 42.5, lon: -71.0),
            SoundingNode(2, lat: 43.2, lon: -69.8, depth: 120),
        ]);

        var extent = S57Dataset.ReadMetadata(document).Extent;

        Assert.NotNull(extent);
        Assert.Equal(-71.0, extent!.WestLongitude, 6);
        Assert.Equal(-69.8, extent.EastLongitude, 6);
        Assert.Equal(42.5, extent.SouthLatitude, 6);
        Assert.Equal(43.2, extent.NorthLatitude, 6);
    }

    [Fact]
    public void ReadMetadata_returns_null_extent_when_no_coordinates()
    {
        var metadata = S57Dataset.ReadMetadata(Document());

        Assert.Null(metadata.Extent);
    }

    [Fact]
    public void ReadMetadata_returns_null_extent_when_comf_is_zero()
    {
        var document = Document([Node(1, lat: 42.0, lon: -71.0)], comf: 0);

        Assert.Null(S57Dataset.ReadMetadata(document).Extent);
    }

    [Fact]
    public void ReadMetadata_surfaces_compilation_scale_as_coarsest_bound()
    {
        var metadata = S57Dataset.ReadMetadata(Document(compilationScale: 45_000));

        Assert.NotNull(metadata.DisplayScale);
        Assert.Equal(45_000, metadata.DisplayScale!.Value.Minimum);
        Assert.Null(metadata.DisplayScale.Value.Maximum);
    }

    [Fact]
    public void ReadMetadata_returns_null_display_scale_when_compilation_scale_absent()
    {
        var metadata = S57Dataset.ReadMetadata(Document(compilationScale: 0));

        Assert.Null(metadata.DisplayScale);
    }

    [Fact]
    public void ReadMetadata_rejects_null_document()
    {
        Assert.Throws<ArgumentNullException>(
            () => S57Dataset.ReadMetadata((EncDotNet.S57.S57Document)null!));
    }

    private static string ResolveFixturePath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "datasets", "S57", "US5MA1BO", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return Path.Combine("tests", "datasets", "S57", "US5MA1BO", fileName);
    }

    [SkippableFact]
    public void ReadMetadata_real_cell_overloads_agree_and_extent_matches_full_translation()
    {
        var path = ResolveFixturePath("US5MA1BO.000");
        Skip.IfNot(File.Exists(path), $"S-57 test cell not present: {path}");

        var pathExtent = S57Dataset.ReadMetadata(path).Extent;
        Assert.NotNull(pathExtent);

        // The static path overload and the instance overload must agree.
        var dataset = S57Dataset.Open(path);
        Assert.Equal(pathExtent, dataset.ReadMetadata().Extent);

        // Plausible WGS-84 window (US5MA1BO is a US east-coast harbour cell).
        var extent = pathExtent!;
        Assert.True(extent.WestLongitude < extent.EastLongitude);
        Assert.True(extent.SouthLatitude < extent.NorthLatitude);
        Assert.InRange(extent.WestLongitude, -180.0, 180.0);
        Assert.InRange(extent.NorthLatitude, -90.0, 90.0);

        // The cheap raw-coordinate scan covers every vertex; the full S-57 →
        // S-101 translation only materialises the coordinates referenced by
        // features, so the authoritative translated extent must sit inside the
        // probe estimate (never larger). Both derive from the same integer
        // coordinates and COMF, so they should also be close.
        var translated = S101Dataset.FromDocument(new S57ToS101Translator().Translate(dataset));
        var full = translated.ReadMetadata().Extent;
        Assert.NotNull(full);

        const double tolerance = 1e-6;
        Assert.True(extent.WestLongitude <= full!.WestLongitude + tolerance);
        Assert.True(extent.EastLongitude >= full.EastLongitude - tolerance);
        Assert.True(extent.SouthLatitude <= full.SouthLatitude + tolerance);
        Assert.True(extent.NorthLatitude >= full.NorthLatitude - tolerance);
    }
}
