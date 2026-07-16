using System.Collections.ObjectModel;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.S101.Tests;

/// <summary>
/// Tests for <see cref="S101Dataset.ReadMetadata()"/> — the phased "peek"
/// path (issue #460). Verifies the metadata-only read surfaces the declared
/// specification, the geometry-derived extent, and the <c>DataCoverage</c>
/// display-scale window (S-101 FC §3.1.1).
/// </summary>
public sealed class S101DatasetMetadataTests
{
    private const ushort DataCoverageCode = 1;
    private const ushort MinScaleCode = 100;
    private const ushort MaxScaleCode = 101;

    [Fact]
    public void ReadMetadata_SurfacesSpecExtentAndDisplayScale()
    {
        var dataset = S101Dataset.FromDocument(BuildDocument());

        var meta = dataset.ReadMetadata();

        Assert.Equal("S-101", meta.Spec.Name);
        Assert.Equal(new SpecVersion(1, 0, 0), meta.Spec.Edition);

        // Extent matches the full vector-source computation.
        var fullExtent = new S101VectorSource(dataset).Metadata.Extent;
        Assert.NotNull(meta.Extent);
        Assert.Equal(fullExtent.SouthLatitude, meta.Extent!.SouthLatitude);
        Assert.Equal(fullExtent.WestLongitude, meta.Extent.WestLongitude);
        Assert.Equal(fullExtent.NorthLatitude, meta.Extent.NorthLatitude);
        Assert.Equal(fullExtent.EastLongitude, meta.Extent.EastLongitude);

        // Geographic vector product — no projected CRS.
        Assert.Null(meta.HorizontalCrsEpsg);

        Assert.NotNull(meta.DisplayScale);
        Assert.Equal(90000, meta.DisplayScale!.Value.Minimum); // coarsest = max minimumDisplayScale
        Assert.Equal(45000, meta.DisplayScale.Value.Maximum);  // finest = min maximumDisplayScale

        Assert.Null(meta.TimeCoverage);
    }

    [Fact]
    public void ReadMetadata_NoDataCoverage_YieldsNullDisplayScale()
    {
        var dataset = S101Dataset.FromDocument(BuildDocument(includeDataCoverage: false));

        var meta = dataset.ReadMetadata();

        Assert.Null(meta.DisplayScale);
        Assert.NotNull(meta.Extent);
    }

    [Fact]
    public void ReadMetadata_MultipleDataCoverage_UsesMostPermissiveBounds()
    {
        var dataset = S101Dataset.FromDocument(BuildDocument(extraCoverageMin: 180000, extraCoverageMax: 20000));

        var meta = dataset.ReadMetadata();

        Assert.NotNull(meta.DisplayScale);
        Assert.Equal(180000, meta.DisplayScale!.Value.Minimum); // largest minimumDisplayScale
        Assert.Equal(20000, meta.DisplayScale.Value.Maximum);   // smallest maximumDisplayScale
    }

    private static S101Document BuildDocument(
        bool includeDataCoverage = true,
        int? extraCoverageMin = null,
        int? extraCoverageMax = null)
    {
        var curve = new S101CurveSegmentRecord
        {
            RecordId = 10,
            PointAssociations = [],
            IntermediateCoordinates = [(0, 0), (0, 10), (10, 10), (10, 0)],
        };

        var features = new List<S101FeatureRecord>();
        if (includeDataCoverage)
        {
            features.Add(new S101FeatureRecord
            {
                RecordId = 1,
                FeatureTypeCode = DataCoverageCode,
                Attributes =
                [
                    new S101Attribute(MinScaleCode, 1, "90000"),
                    new S101Attribute(MaxScaleCode, 1, "45000"),
                ],
            });

            if (extraCoverageMin is not null || extraCoverageMax is not null)
            {
                features.Add(new S101FeatureRecord
                {
                    RecordId = 2,
                    FeatureTypeCode = DataCoverageCode,
                    Attributes =
                    [
                        new S101Attribute(MinScaleCode, 1, (extraCoverageMin ?? 90000).ToString()),
                        new S101Attribute(MaxScaleCode, 1, (extraCoverageMax ?? 45000).ToString()),
                    ],
                });
            }
        }

        return new S101Document
        {
            Identification = new S101DatasetIdentification
            {
                DatasetName = "TEST.000",
                ProductSpecificationEdition = "1.0.0",
            },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = 1,
                CoordinateMultiplicationFactorY = 1,
                CoordinateMultiplicationFactorZ = 1,
            },
            FeatureTypeCatalogue = new Dictionary<ushort, string> { [DataCoverageCode] = "DataCoverage" },
            AttributeTypeCatalogue = new Dictionary<ushort, string>
            {
                [MinScaleCode] = "minimumDisplayScale",
                [MaxScaleCode] = "maximumDisplayScale",
            },
            Points = ReadOnlyDictionary<uint, S101PointRecord>.Empty,
            CurveSegments = new[] { curve }.ToDictionary(c => c.RecordId),
            CompositeCurves = ReadOnlyDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = ReadOnlyDictionary<uint, S101SurfaceRecord>.Empty,
            Features = features,
            InformationTypes = ReadOnlyDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            RoleCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        };
    }
}
