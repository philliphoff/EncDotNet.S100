using System.Reflection;

using EncDotNet.S100.Cli.Infrastructure;
using PureHDF;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Verifies that S-104 and S-111 data coding format 1 station/node datasets
/// render successfully through the CLI's Mapsui-free point-glyph path.
/// </summary>
public sealed class UnsupportedDataCodingFormatTests
{
    private struct GeometryRow
    {
        [H5Name("longitude")]
        public double Longitude;

        [H5Name("latitude")]
        public double Latitude;
    }

    private struct S111ValueRow
    {
        [H5Name("surfaceCurrentSpeed")]
        public float Speed;

        [H5Name("surfaceCurrentDirection")]
        public float Direction;
    }

    private struct S104ValueRow
    {
        [H5Name("waterLevelHeight")]
        public float Height;

        [H5Name("waterLevelTrend")]
        public short Trend;
    }

    [Fact]
    public void Render_dcf1_s111_returns_success_and_writes_png()
    {
        var dataset = Path.Combine(Path.GetTempPath(), $"s111-dcf1-{Guid.NewGuid():N}.h5");
        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.png");
        try
        {
            WriteDcf1S111(dataset);

            int exit = CliApp.Build().Run(["render", dataset, output]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(output));
            Assert.True(new FileInfo(output).Length > 8);
        }
        finally
        {
            if (File.Exists(dataset)) File.Delete(dataset);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public void Render_dcf1_s104_returns_success_and_writes_png()
    {
        var dataset = Path.Combine(Path.GetTempPath(), $"s104-dcf1-{Guid.NewGuid():N}.h5");
        var output = Path.Combine(Path.GetTempPath(), $"s100-cli-{Guid.NewGuid():N}.png");
        try
        {
            WriteDcf1S104(dataset);

            int exit = CliApp.Build().Run(["render", dataset, output]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(output));
            Assert.True(new FileInfo(output).Length > 8);
        }
        finally
        {
            if (File.Exists(dataset)) File.Delete(dataset);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    /// <summary>
    /// Writes a minimal S-111 DCF1 positioned-node dataset.
    /// </summary>
    private static void WriteDcf1S111(string path)
    {
        var file = new H5File
        {
            Attributes = new Dictionary<string, object>
            {
                ["horizontalCRS"] = 4326,
                ["productSpecification"] = "INT.IHO.S-111.2.0",
            },
            ["SurfaceCurrent"] = new H5Group
            {
                Attributes = new Dictionary<string, object>
                {
                    ["dataCodingFormat"] = (byte)1,
                    ["typeOfCurrentData"] = (long)6,
                },
                ["SurfaceCurrent.01"] = new H5Group
                {
                    Attributes = new Dictionary<string, object>
                    {
                        ["numberOfStations"] = 1L,
                        ["numberOfTimes"] = 1L,
                        ["timeRecordInterval"] = 3600L,
                    },
                    ["Positioning"] = new H5Group
                    {
                        ["geometryValues"] = new[]
                        {
                            new GeometryRow { Longitude = 4.61, Latitude = 52.88 },
                        },
                    },
                    ["Group_001"] = new H5Group
                    {
                        Attributes = new Dictionary<string, object>
                        {
                            ["timePoint"] = "20240101T000000Z",
                        },
                        ["values"] = new[]
                        {
                            new S111ValueRow { Speed = 0.5f, Direction = 45f },
                        },
                    },
                },
            },
        };

        file.Write(path, WriteOptions());
    }

    /// <summary>
    /// Writes a minimal S-104 DCF1 positioned-station dataset.
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
                Attributes = new Dictionary<string, object>
                {
                    ["dataCodingFormat"] = (byte)1,
                    ["methodWaterLevelProduct"] = "forecast",
                },
                ["WaterLevel.01"] = new H5Group
                {
                    Attributes = new Dictionary<string, object>
                    {
                        ["numberOfStations"] = 1L,
                        ["numberOfTimes"] = 1L,
                        ["timeRecordInterval"] = 3600L,
                    },
                    ["Positioning"] = new H5Group
                    {
                        ["geometryValues"] = new[]
                        {
                            new GeometryRow { Longitude = 3.2, Latitude = 51.5 },
                        },
                    },
                    ["Group_001"] = new H5Group
                    {
                        Attributes = new Dictionary<string, object>
                        {
                            ["timePoint"] = "20240101T000000Z",
                        },
                        ["values"] = new[]
                        {
                            new S104ValueRow { Height = 1.2f, Trend = 2 },
                        },
                    },
                },
            },
        };

        file.Write(path, WriteOptions());
    }

    private static H5WriteOptions WriteOptions() =>
        new(FieldNameMapper: field => field.GetCustomAttribute<H5NameAttribute>()?.Name);
}
