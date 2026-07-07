using EncDotNet.S100.Datasets.S101;
using Xunit;
using System.Collections.ObjectModel;

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
            PointAssociations = [],
            IntermediateCoordinates = [(0, 0), (0, 10), (10, 10), (10, 0)],
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
            FeatureTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            AttributeTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            Points = ReadOnlyDictionary<uint, S101PointRecord>.Empty,
            CurveSegments = new[] { curve }.ToDictionary(c => c.RecordId),
            CompositeCurves = ReadOnlyDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = ReadOnlyDictionary<uint, S101SurfaceRecord>.Empty,
            Features = [],
            InformationTypes = ReadOnlyDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            RoleCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        };
    }
}
