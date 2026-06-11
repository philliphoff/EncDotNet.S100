using System.Collections.Immutable;
using System.Linq;

namespace EncDotNet.S100.Datasets.S101.Tests;

/// <summary>
/// Phase 2 tests for the S-101 sequential-update applicator
/// (<see cref="S101UpdateApplicator"/> and <see cref="S101Document.ApplyChanges"/>):
/// record-level insert / delete / modify keyed by RCID, inline attribute and
/// association merges (ATIN / SAUI / FAUI / IUIN), and best-effort sequencing.
/// </summary>
public class S101UpdateApplicatorTests
{
    [Fact]
    public void ApplyChanges_InsertsNewSpatialAndFeatureRecords()
    {
        var baseDoc = Doc(updateNumber: 0, profile: "1",
            points: new[] { Point(1) },
            features: new[] { Feature(10, instruction: S101UpdateInstruction.Insert) });

        var update = Doc(updateNumber: 1, profile: "2",
            points: new[] { Point(2, instruction: S101UpdateInstruction.Insert) },
            features: new[] { Feature(11, instruction: S101UpdateInstruction.Insert) });

        var result = baseDoc.ApplyChanges(update);

        Assert.Equal(2, result.Points.Count);
        Assert.True(result.Points.ContainsKey(2));
        Assert.Equal(2, result.Features.Length);
        Assert.Contains(result.Features, f => f.RecordId == 11);
        Assert.Equal(1, result.Identification.UpdateNumber);
    }

    [Fact]
    public void ApplyChanges_DeletesRecords()
    {
        var baseDoc = Doc(updateNumber: 0, profile: "1",
            points: new[] { Point(1), Point(2) },
            features: new[] { Feature(10), Feature(11) });

        var update = Doc(updateNumber: 1, profile: "2",
            points: new[] { Point(2, instruction: S101UpdateInstruction.Delete) },
            features: new[] { Feature(11, instruction: S101UpdateInstruction.Delete) });

        var result = baseDoc.ApplyChanges(update);

        Assert.Single(result.Points);
        Assert.False(result.Points.ContainsKey(2));
        Assert.Single(result.Features);
        Assert.Equal(10u, result.Features[0].RecordId);
    }

    [Fact]
    public void ApplyChanges_ModifyFeature_MergesAttributesByInstruction()
    {
        var existing = Feature(10, attributes: new[]
        {
            new S101Attribute(1, 1, "keep"),
            new S101Attribute(2, 1, "old"),
            new S101Attribute(3, 1, "remove"),
        });

        var update = Feature(10, instruction: S101UpdateInstruction.Modify, attributes: new[]
        {
            new S101Attribute(2, 1, "new", 0, S101UpdateInstruction.Modify),
            new S101Attribute(3, 1, "remove", 0, S101UpdateInstruction.Delete),
            new S101Attribute(4, 1, "added", 0, S101UpdateInstruction.Insert),
        });

        var baseDoc = Doc(0, "1", features: new[] { existing });
        var result = baseDoc.ApplyChanges(Doc(1, "2", features: new[] { update }));

        var merged = Assert.Single(result.Features);
        var values = merged.Attributes.ToDictionary(a => a.NumericCode, a => a.Value);
        Assert.Equal("keep", values[1]);
        Assert.Equal("new", values[2]);
        Assert.False(values.ContainsKey(3));
        Assert.Equal("added", values[4]);
        Assert.Equal(S101UpdateInstruction.None, merged.UpdateInstruction);
    }

    [Fact]
    public void ApplyChanges_ModifyFeature_MergesSpatialAssociations()
    {
        var existing = Feature(10, spatials: new[]
        {
            new S101SpatialAssociation(120, 100, 1),
            new S101SpatialAssociation(120, 101, 1),
        });

        var update = Feature(10, instruction: S101UpdateInstruction.Modify, spatials: new[]
        {
            new S101SpatialAssociation(120, 101, 1, S101UpdateInstruction.Delete),
            new S101SpatialAssociation(120, 102, 2, S101UpdateInstruction.Insert),
        });

        var result = Doc(0, "1", features: new[] { existing })
            .ApplyChanges(Doc(1, "2", features: new[] { update }));

        var merged = Assert.Single(result.Features);
        Assert.Equal(2, merged.SpatialAssociations.Length);
        Assert.Contains(merged.SpatialAssociations, s => s.RecordId == 100);
        Assert.Contains(merged.SpatialAssociations, s => s.RecordId == 102);
        Assert.DoesNotContain(merged.SpatialAssociations, s => s.RecordId == 101);
    }

    [Fact]
    public void Apply_SequentialUpdates_FoldInOrder()
    {
        var baseDoc = Doc(0, "1", features: new[] { Feature(10) });
        var u1 = Doc(1, "2", features: new[] { Feature(11, instruction: S101UpdateInstruction.Insert) });
        var u2 = Doc(2, "2", features: new[] { Feature(10, instruction: S101UpdateInstruction.Delete) });

        var result = S101UpdateApplicator.Apply(baseDoc, new[] { u1, u2 }, out var report);

        Assert.Single(result.Features);
        Assert.Equal(11u, result.Features[0].RecordId);
        Assert.Equal(2, report.AppliedThroughUpdateNumber);
        Assert.Equal(0, report.BaseUpdateNumber);
        Assert.Equal(1, report.Inserted);
        Assert.Equal(1, report.Deleted);
        Assert.True(report.Success);
    }

    [Fact]
    public void Apply_NonContiguousUpdate_StopsBestEffortWithWarning()
    {
        var baseDoc = Doc(0, "1", features: new[] { Feature(10) });
        var u1 = Doc(1, "2", features: new[] { Feature(11, instruction: S101UpdateInstruction.Insert) });
        var u3 = Doc(3, "2", features: new[] { Feature(12, instruction: S101UpdateInstruction.Insert) });

        var result = S101UpdateApplicator.Apply(baseDoc, new[] { u1, u3 }, out var report);

        Assert.Equal(2, result.Features.Length); // base + u1 only
        Assert.Equal(1, report.AppliedThroughUpdateNumber);
        Assert.False(report.Success);
        Assert.Contains(report.Messages, m => m.Severity == S101UpdateSeverity.Warning && m.UpdateNumber == 3);
    }

    [Fact]
    public void Apply_EmptyUpdateList_ReturnsBaseUnchanged()
    {
        var baseDoc = Doc(0, "1", features: new[] { Feature(10) });

        var result = S101UpdateApplicator.Apply(baseDoc, Array.Empty<S101Document>(), out var report);

        Assert.Same(baseDoc, result);
        Assert.Equal(0, report.AppliedThroughUpdateNumber);
        Assert.True(report.Success);
    }

    // --- builders -----------------------------------------------------------

    private static S101PointRecord Point(uint id, S101UpdateInstruction instruction = S101UpdateInstruction.Insert) =>
        new() { RecordId = id, X = 0, Y = 0, RecordVersion = 1, UpdateInstruction = instruction };

    private static S101FeatureRecord Feature(
        uint id,
        S101UpdateInstruction instruction = S101UpdateInstruction.Insert,
        IEnumerable<S101Attribute>? attributes = null,
        IEnumerable<S101SpatialAssociation>? spatials = null) =>
        new()
        {
            RecordId = id,
            FeatureTypeCode = 1,
            Attributes = (attributes ?? Enumerable.Empty<S101Attribute>()).ToImmutableArray(),
            SpatialAssociations = (spatials ?? Enumerable.Empty<S101SpatialAssociation>()).ToImmutableArray(),
            FeatureAssociations = ImmutableArray<S101FeatureAssociation>.Empty,
            InformationAssociations = ImmutableArray<S101InformationAssociation>.Empty,
            RecordVersion = 1,
            UpdateInstruction = instruction,
        };

    private static S101Document Doc(
        int updateNumber,
        string profile,
        IEnumerable<S101PointRecord>? points = null,
        IEnumerable<S101FeatureRecord>? features = null) =>
        new()
        {
            Identification = new S101DatasetIdentification
            {
                DatasetName = $"TEST.{updateNumber:D3}",
                ApplicationProfile = profile,
                UpdateNumber = updateNumber,
            },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = 1,
                CoordinateMultiplicationFactorY = 1,
                CoordinateMultiplicationFactorZ = 1,
            },
            FeatureTypeCatalogue = ImmutableDictionary<ushort, string>.Empty,
            AttributeTypeCatalogue = ImmutableDictionary<ushort, string>.Empty,
            Points = (points ?? Enumerable.Empty<S101PointRecord>()).ToImmutableDictionary(p => p.RecordId),
            CurveSegments = ImmutableDictionary<uint, S101CurveSegmentRecord>.Empty,
            CompositeCurves = ImmutableDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = ImmutableDictionary<uint, S101SurfaceRecord>.Empty,
            Features = (features ?? Enumerable.Empty<S101FeatureRecord>()).ToImmutableArray(),
            InformationTypes = ImmutableDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ImmutableDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ImmutableDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ImmutableDictionary<ushort, string>.Empty,
            RoleCatalogue = ImmutableDictionary<ushort, string>.Empty,
        };
}
