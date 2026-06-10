using EncDotNet.S100.Cli.Infrastructure;
using PureHDF;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Verifies that a recognised dataset using a data coding format the reader
/// does not yet implement — data coding format 1 (irregular time series at
/// fixed stations; S-100 Part 10c §10.2.1) — is reported as a graceful
/// "not supported" render with the dedicated exit code 4, rather than
/// crashing as a generic error (exit 1). Regression test for issue #253.
/// </summary>
/// <remarks>
/// The S-104/S-111 readers recognise dcf1 and throw
/// <c>S100DatasetNotSupportedException</c>, which does NOT derive from
/// <see cref="NotSupportedException"/>. The CLI render/info commands gained a
/// dedicated catch for it so it maps to exit 4 alongside the dcf8 fixed-station
/// path (which throws a plain <see cref="NotSupportedException"/>).
/// </remarks>
public sealed class UnsupportedDataCodingFormatTests
{
    [Fact]
    public void Render_dcf1_s111_returns_exit_4_and_writes_no_png()
    {
        var dataset = Path.Combine(Path.GetTempPath(), $"s111-dcf1-{Guid.NewGuid():N}.h5");
        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.png");
        try
        {
            WriteDcf1S111(dataset);

            int exit = CliApp.Build().Run(["render", dataset, output]);

            Assert.Equal(4, exit);
            Assert.False(File.Exists(output));
        }
        finally
        {
            if (File.Exists(dataset)) File.Delete(dataset);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public void Render_dcf1_s104_returns_exit_4_and_writes_no_png()
    {
        var dataset = Path.Combine(Path.GetTempPath(), $"s104-dcf1-{Guid.NewGuid():N}.h5");
        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.png");
        try
        {
            WriteDcf1S104(dataset);

            int exit = CliApp.Build().Run(["render", dataset, output]);

            Assert.Equal(4, exit);
            Assert.False(File.Exists(output));
        }
        finally
        {
            if (File.Exists(dataset)) File.Delete(dataset);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    /// <summary>
    /// Writes a minimal S-111 file whose <c>/SurfaceCurrent</c> container
    /// declares <c>dataCodingFormat = 1</c> (irregular time series at fixed
    /// stations; S-100 Part 10c §10.2.1). The reader rejects the format before
    /// reading any instance, so no <c>SurfaceCurrent.NN</c> group is required.
    /// </summary>
    private static void WriteDcf1S111(string path)
    {
        var file = new H5File
        {
            Attributes = new Dictionary<string, object>
            {
                ["productSpecification"] = "INT.IHO.S-111.2.0",
            },
            ["SurfaceCurrent"] = new H5Group
            {
                Attributes = new Dictionary<string, object> { ["dataCodingFormat"] = (byte)1 },
            },
        };

        file.Write(path);
    }

    /// <summary>
    /// Writes a minimal S-104 file whose <c>/WaterLevel</c> container declares
    /// <c>dataCodingFormat = 1</c> (irregular time series at fixed stations;
    /// S-100 Part 10c §10.2.1). The reader rejects the format before reading
    /// any instance, so no <c>WaterLevel.NN</c> group is required.
    /// </summary>
    private static void WriteDcf1S104(string path)
    {
        var file = new H5File
        {
            Attributes = new Dictionary<string, object>
            {
                ["horizontalCRS"] = 4326,
                ["productSpecification"] = "INT.IHO.S-104.2.0",
            },
            ["WaterLevel"] = new H5Group
            {
                Attributes = new Dictionary<string, object> { ["dataCodingFormat"] = (byte)1 },
            },
        };

        file.Write(path);
    }
}
