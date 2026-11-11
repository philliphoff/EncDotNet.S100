using System.Reflection;
using PureHDF;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>Writes small S-102 Ed 3.0.0 grids to temporary files for processor tests.</summary>
internal static class S102Grids
{
    private struct BathyRow
    {
        [H5Name("depth")] public float Depth;
        [H5Name("uncertainty")] public float Uncertainty;
    }

    public static string Write(int rows, int cols, Func<int, int, float> depth, int verticalDatum = 12)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".h5");
        var values = new BathyRow[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
                values[(r * cols) + c] = new BathyRow { Depth = depth(r, c), Uncertainty = 0.1f };
        }

        var instance = new H5Group
        {
            Attributes = new()
            {
                ["gridOriginLatitude"] = 50.0,
                ["gridOriginLongitude"] = -1.0,
                ["gridSpacingLatitudinal"] = 0.01,
                ["gridSpacingLongitudinal"] = 0.01,
                ["numPointsLatitudinal"] = rows,
                ["numPointsLongitudinal"] = cols,
            },
            ["Group_001"] = new H5Group { ["values"] = values },
        };
        var file = new H5File
        {
            Attributes = new Dictionary<string, object>
            {
                ["productSpecification"] = "INT.IHO.S-102.3.0.0",
                ["horizontalCRS"] = 4326,
                ["verticalDatum"] = verticalDatum,
            },
            ["BathymetryCoverage"] = new H5Group { ["BathymetryCoverage.01"] = instance },
        };
        file.Write(path, new H5WriteOptions(FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name));
        return path;
    }
}
