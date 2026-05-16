using System.Reflection;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Hdf5.PureHdf;
using PureHDF;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Reader-hardening tests for S-102: verifies tolerant numeric attribute
/// reads accept both F32 and F64 grid attributes (PR-A).
/// </summary>
public class S102DatasetReaderHardeningTests
{
    /// <summary>
    /// S-102 spec mandates F32 depth/uncertainty in the BathymetryCoverage
    /// compound (so the row schema is fixed) but the grid-georef attributes
    /// can be either F32 or F64 in practice. Verify both succeed.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_AcceptsBothF32AndF64GridAttrs(bool useF64)
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var values = new[] { new SpecBathyRow { Depth = 12.5f, Uncertainty = 0.1f } };

            var instance = new H5Group
            {
                Attributes = new()
                {
                    ["gridOriginLatitude"] = useF64 ? 50.0 : (object)50.0f,
                    ["gridOriginLongitude"] = useF64 ? -1.0 : (object)-1.0f,
                    ["gridSpacingLatitudinal"] = useF64 ? 0.01 : (object)0.01f,
                    ["gridSpacingLongitudinal"] = useF64 ? 0.01 : (object)0.01f,
                    ["numPointsLatitudinal"] = 1,
                    ["numPointsLongitudinal"] = 1,
                },
                ["Group_001"] = new H5Group { ["values"] = values },
            };

            var file = new H5File
            {
                Attributes = new()
                {
                    ["horizontalCRS"] = 4326,
                },
                ["BathymetryCoverage"] = new H5Group
                {
                    ["BathymetryCoverage.01"] = instance,
                },
            };

            var options = new H5WriteOptions(
                FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name);
            file.Write(path, options);

            using var hdf = PureHdfFile.Open(path);
            var dataset = S102DatasetReader.Read(hdf);
            var coverage = Assert.Single(dataset.Coverages);

            Assert.Equal(50.0, coverage.OriginLatitude, precision: 6);
            Assert.Equal(-1.0, coverage.OriginLongitude, precision: 6);
        }
        finally { File.Delete(path); }
    }

    private struct SpecBathyRow
    {
        [H5Name("depth")] public float Depth;
        [H5Name("uncertainty")] public float Uncertainty;
    }
}
