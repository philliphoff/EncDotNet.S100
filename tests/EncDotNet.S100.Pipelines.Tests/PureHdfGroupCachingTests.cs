using System.Reflection;
using EncDotNet.S100.Hdf5.PureHdf;
using PureHDF;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// PR-F: regression tests for the attribute/group-name caches added to
/// <see cref="PureHdfFile"/>'s internal group adapter.
/// </summary>
public class PureHdfGroupCachingTests
{
    [Fact]
    public void AttributeExists_ReturnsSameAnswer_BeforeAndAfterCache()
    {
        var path = WriteFixture();
        try
        {
            using var hdf = PureHdfFile.Open(path);

            // First call populates the cache; second call hits it. Both
            // must agree.
            var first = hdf.Root.AttributeExists("horizontalCRS");
            var second = hdf.Root.AttributeExists("horizontalCRS");
            Assert.True(first);
            Assert.True(second);

            // Negative case: must also be cached as "absent".
            Assert.False(hdf.Root.AttributeExists("nonexistent"));
            Assert.False(hdf.Root.AttributeExists("nonexistent"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void GroupNames_ReturnsReferenceEqualList_OnRepeatedCalls()
    {
        var path = WriteFixture();
        try
        {
            using var hdf = PureHdfFile.Open(path);

            var first = hdf.Root.GroupNames;
            var second = hdf.Root.GroupNames;

            // Cached lazy must return the exact same list instance.
            Assert.Same(first, second);
            Assert.Contains("BathymetryCoverage", second);
        }
        finally { File.Delete(path); }
    }

    private static string WriteFixture()
    {
        var path = Path.GetTempFileName() + ".h5";

        var values = new[] { new SpecBathyRow { Depth = 12.5f, Uncertainty = 0.1f } };

        var instance = new H5Group
        {
            Attributes = new()
            {
                ["gridOriginLatitude"] = 50.0,
                ["gridOriginLongitude"] = -1.0,
                ["gridSpacingLatitudinal"] = 0.01,
                ["gridSpacingLongitudinal"] = 0.01,
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
        return path;
    }

    private struct SpecBathyRow
    {
        [H5Name("depth")] public float Depth;
        [H5Name("uncertainty")] public float Uncertainty;
    }
}
