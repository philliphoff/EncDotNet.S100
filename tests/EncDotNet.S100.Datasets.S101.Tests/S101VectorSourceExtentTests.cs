using System.Collections.Immutable;
using EncDotNet.S100.Datasets.S101;
using Xunit;

namespace EncDotNet.S100.Datasets.S101.Tests;

/// <summary>
/// Regression tests for <see cref="S101VectorSource"/> extent computation.
/// </summary>
/// <remarks>
/// Motivated by issue #274: cells whose geometry is entirely curves/surfaces
/// carry no Point/MultiPoint records, so the extent collapsed to (0,0,0,0).
/// The MCP <c>open_dataset</c> tool then fell back to world bounds
/// (-90/-180/90/180), making single-file S-101 loads impossible to target.
/// The boundary vertices live in each curve segment's intermediate
/// coordinates, which the extent computation must fold in.
/// </remarks>
public sealed class S101VectorSourceExtentTests
{
    [Fact]
    public void Extent_CurvesOnlyCell_ReflectsCurveCoordinates()
    {
        var dataset = S101Dataset.FromDocument(BuildCurvesOnlyDocument());

        var extent = new S101VectorSource(dataset).Metadata.Extent;

        Assert.Equal(0, extent.SouthLatitude);
        Assert.Equal(0, extent.WestLongitude);
        Assert.Equal(10, extent.NorthLatitude);
        Assert.Equal(10, extent.EastLongitude);
    }

    private static S101Document BuildCurvesOnlyDocument()
    {
        // A 0..10 square boundary stored purely as curve-segment intermediate
        // coordinates — no Point or MultiPoint records.
        var curve = new S101CurveSegmentRecord
        {
            RecordId = 10,
            PointAssociations = ImmutableArray<S101PointAssociation>.Empty,
            IntermediateCoordinates = ImmutableArray.Create((0, 0), (0, 10), (10, 10), (10, 0)),
        };

        return new S101Document
        {
            Identification = new S101DatasetIdentification { DatasetName = "TEST.000" },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = 1,
                CoordinateMultiplicationFactorY = 1,
                CoordinateMultiplicationFactorZ = 1,
            },
            FeatureTypeCatalogue = ImmutableDictionary<ushort, string>.Empty,
            AttributeTypeCatalogue = ImmutableDictionary<ushort, string>.Empty,
            Points = ImmutableDictionary<uint, S101PointRecord>.Empty,
            CurveSegments = new[] { curve }.ToImmutableDictionary(c => c.RecordId),
            CompositeCurves = ImmutableDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = ImmutableDictionary<uint, S101SurfaceRecord>.Empty,
            Features = ImmutableArray<S101FeatureRecord>.Empty,
            InformationTypes = ImmutableDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ImmutableDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ImmutableDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ImmutableDictionary<ushort, string>.Empty,
            RoleCatalogue = ImmutableDictionary<ushort, string>.Empty,
        };
    }
}
