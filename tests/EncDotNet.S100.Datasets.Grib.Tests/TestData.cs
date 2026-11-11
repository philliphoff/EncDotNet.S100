namespace EncDotNet.S100.Datasets.Grib.Tests;

/// <summary>Paths of the fixtures in tests/datasets/GRIB2, copied to TestData/.</summary>
internal static class TestData
{
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", name);
}
