using EncDotNet.S100.Core.Metadata;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Core.Tests;

public class DatasetMetadataSerializerTests
{
    private static DatasetMetadata Full() => new()
    {
        Spec = new SpecRef("S-104", new SpecVersion(2, 0, 0)),
        Extent = new BoundingBox(10.5, -20.25, 11.75, -19.0),
        HorizontalCrsEpsg = 32610,
        DisplayScale = new DisplayScaleRange(45000, 12000),
        TimeCoverage = new TimeCoverage(
            new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc)),
    };

    [Fact]
    public void RoundTrips_all_fields_populated()
    {
        var original = Full();

        var restored = DatasetMetadataSerializer.TryDeserialize(
            DatasetMetadataSerializer.Serialize(original));

        Assert.Equal(original, restored);
    }

    [Fact]
    public void RoundTrips_with_only_required_spec()
    {
        var original = new DatasetMetadata { Spec = new SpecRef("S-101", new SpecVersion(1, 2, 0)) };

        var restored = DatasetMetadataSerializer.TryDeserialize(
            DatasetMetadataSerializer.Serialize(original));

        Assert.NotNull(restored);
        Assert.Equal(original.Spec, restored.Spec);
        Assert.Null(restored.Extent);
        Assert.Null(restored.HorizontalCrsEpsg);
        Assert.Null(restored.DisplayScale);
        Assert.Null(restored.TimeCoverage);
    }

    [Fact]
    public void RoundTrips_display_scale_with_one_open_bound()
    {
        var original = new DatasetMetadata
        {
            Spec = new SpecRef("S-101", new SpecVersion(1, 0, 0)),
            DisplayScale = new DisplayScaleRange(null, 5000),
        };

        var restored = DatasetMetadataSerializer.TryDeserialize(
            DatasetMetadataSerializer.Serialize(original));

        Assert.NotNull(restored);
        Assert.Equal(new DisplayScaleRange(null, 5000), restored.DisplayScale);
    }

    [Fact]
    public void TimeCoverage_survives_as_utc()
    {
        var original = new DatasetMetadata
        {
            Spec = new SpecRef("S-111", new SpecVersion(1, 2, 0)),
            TimeCoverage = new TimeCoverage(
                new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 1, 18, 0, 0, DateTimeKind.Utc)),
        };

        var restored = DatasetMetadataSerializer.TryDeserialize(
            DatasetMetadataSerializer.Serialize(original));

        Assert.NotNull(restored);
        Assert.Equal(original.TimeCoverage, restored.TimeCoverage);
        Assert.Equal(DateTimeKind.Utc, restored.TimeCoverage!.Value.Start.Kind);
    }

    [Fact]
    public void Version_mismatch_yields_null()
    {
        var bytes = DatasetMetadataSerializer.Serialize(Full());
        // The first four bytes are the little-endian FormatVersion; corrupt it.
        bytes[0] = (byte)(bytes[0] + 1);

        Assert.Null(DatasetMetadataSerializer.TryDeserialize(bytes));
    }

    [Fact]
    public void Truncated_frame_yields_null()
    {
        var bytes = DatasetMetadataSerializer.Serialize(Full());

        Assert.Null(DatasetMetadataSerializer.TryDeserialize(bytes[..8]));
    }

    [Fact]
    public void Empty_bytes_yield_null()
    {
        Assert.Null(DatasetMetadataSerializer.TryDeserialize([]));
    }
}
