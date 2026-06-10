using EncDotNet.S100.Cli.Infrastructure;
using PureHDF;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Verifies that a recognised-but-non-conforming dataset (a required
/// HDF5 attribute is missing) is reported as a non-conforming dataset
/// with the dedicated exit code 5, rather than crashing as a generic
/// error (exit 1). Regression test for issue #242.
/// </summary>
public sealed class NonConformingDatasetTests
{
    [Fact]
    public void Render_non_conforming_s104_returns_exit_5_and_writes_no_png()
    {
        var dataset = Path.Combine(Path.GetTempPath(), $"s104-nonconforming-{Guid.NewGuid():N}.h5");
        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.png");
        try
        {
            // dcf2 (regular grid) instance missing the required
            // gridOriginLatitude attribute — and declaring a draft edition.
            WriteNonConformingS104(dataset, productSpecification: "INT.IHO.S-104.0.8");

            int exit = CliApp.Build().Run(["render", dataset, output]);

            Assert.Equal(5, exit);
            Assert.False(File.Exists(output));
        }
        finally
        {
            if (File.Exists(dataset)) File.Delete(dataset);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    /// <summary>
    /// Writes a minimal S-104 dcf2 file whose <c>WaterLevel.01</c> instance
    /// group omits the mandatory <c>gridOriginLatitude</c> attribute
    /// (S-100 Part 10c §10.2.1.2). The reader rejects it before reading any
    /// time-step values, so no <c>Group_NNN</c> is required.
    /// </summary>
    private static void WriteNonConformingS104(string path, string productSpecification)
    {
        var instance = new H5Group
        {
            Attributes = new Dictionary<string, object>
            {
                // gridOriginLatitude intentionally omitted.
                ["gridOriginLongitude"] = -1.0,
                ["gridSpacingLatitudinal"] = 0.01,
                ["gridSpacingLongitudinal"] = 0.01,
                ["numPointsLatitudinal"] = 1,
                ["numPointsLongitudinal"] = 1,
            },
        };

        var file = new H5File
        {
            Attributes = new Dictionary<string, object>
            {
                ["horizontalCRS"] = 4326,
                ["productSpecification"] = productSpecification,
            },
            ["WaterLevel"] = new H5Group
            {
                Attributes = new Dictionary<string, object> { ["dataCodingFormat"] = (byte)2 },
                ["WaterLevel.01"] = instance,
            },
        };

        file.Write(path);
    }
}
