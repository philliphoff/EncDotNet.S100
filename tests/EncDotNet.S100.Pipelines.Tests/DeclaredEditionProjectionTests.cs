using System.Reflection;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S104.Tests.Fixtures;
using EncDotNet.S100.Datasets.S111.Tests.Fixtures;
using PureHDF;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #791: a loaded S-102 / S-104 / S-111 HDF5 dataset must report the
/// edition declared by its root <c>productSpecification</c> attribute
/// (S-100 Part 10c §10.2.1), not a default <c>0.0.0</c>. Covers the gridded
/// coverage payloads (and their <see cref="EncDotNet.S100.Pipelines.Coverage.CoverageMetadata.Spec"/>)
/// and the dcf8 station-series payloads.
/// </summary>
public sealed class DeclaredEditionProjectionTests
{
    public static TheoryData<string?, int, int, int> S102Editions => new()
    {
        { "INT.IHO.S-102.3.0.0", 3, 0, 0 },
        { "INT.IHO.S-102.2.2", 2, 2, 0 },
        { null, 0, 0, 0 },
    };

    public static TheoryData<string?, int, int, int> S104Editions => new()
    {
        { "INT.IHO.S-104.2.0.0", 2, 0, 0 },
        { "INT.IHO.S-104.1.1", 1, 1, 0 },
        { null, 0, 0, 0 },
    };

    public static TheoryData<string?, int, int, int> S111Editions => new()
    {
        { "INT.IHO.S-111.2.0.0", 2, 0, 0 },
        // The NOAA OFS tiles that surfaced #791 declare a two-part edition.
        { "INT.IHO.S-111.1.0", 1, 0, 0 },
        { null, 0, 0, 0 },
    };

    [Theory]
    [MemberData(nameof(S102Editions))]
    public void S102_coverage_reports_declared_edition(string? declared, int major, int minor, int clarification)
    {
        var projected = ProjectFile("S-102", path => WriteS102(path, declared));

        var data = Assert.IsType<S102CoverageData>(projected.Data);
        AssertSpec(projected.Spec, "S-102", major, minor, clarification);
        AssertSpec(data.Source.Metadata.Spec, "S-102", major, minor, clarification);
    }

    [Theory]
    [MemberData(nameof(S104Editions))]
    public void S104_coverage_reports_declared_edition(string? declared, int major, int minor, int clarification)
    {
        var projected = ProjectFile("S-104", path =>
        {
            var values = Enumerable.Range(0, 4)
                .Select(i => new S104FixtureBuilder.SpecRow { WaterLevelHeight = 0.5f + i * 0.1f, WaterLevelTrend = 1 })
                .ToArray();
            S104FixtureBuilder.WriteFile(
                path, values, 2, 2, useF64GridAttrs: true, useUnsignedCounts: false,
                productSpecification: declared);
        });

        var data = Assert.IsType<S104CoverageData>(projected.Data);
        AssertSpec(projected.Spec, "S-104", major, minor, clarification);
        AssertSpec(data.Source.Metadata.Spec, "S-104", major, minor, clarification);
    }

    [Theory]
    [MemberData(nameof(S104Editions))]
    public void S104_station_series_reports_declared_edition(string? declared, int major, int minor, int clarification)
    {
        var projected = ProjectFile("S-104", path => S104Dcf8FixtureBuilder.WriteFile(
            path,
            [
                new S104Dcf8FixtureBuilder.Station<S104Dcf8FixtureBuilder.SpecValueRow>
                {
                    Identifier = "Alpha",
                    Latitude = 51.5f,
                    Longitude = -0.1f,
                    StartDateTime = "20240101T000000Z",
                    EndDateTime = "20240101T010000Z",
                    TimeRecordInterval = 3600,
                    Values =
                    [
                        new() { WaterLevelHeight = 1.0f, WaterLevelTrend = 1 },
                        new() { WaterLevelHeight = 1.5f, WaterLevelTrend = 2 },
                    ],
                },
            ],
            productSpecification: declared));

        var data = Assert.IsType<S104StationSeriesData>(projected.Data);
        Assert.Equal(declared, data.Dataset.DeclaredProductSpecification);
        AssertSpec(projected.Spec, "S-104", major, minor, clarification);
    }

    [Theory]
    [MemberData(nameof(S111Editions))]
    public void S111_coverage_reports_declared_edition(string? declared, int major, int minor, int clarification)
    {
        var projected = ProjectFile("S-111", path =>
        {
            var values = Enumerable.Range(0, 4)
                .Select(i => new S111FixtureBuilder.SpecRow { SurfaceCurrentSpeed = 1.0f + i * 0.1f, SurfaceCurrentDirection = 45f })
                .ToArray();
            S111FixtureBuilder.WriteFile(
                path, values, 2, 2, useF64GridAttrs: true, useUnsignedCounts: false,
                productSpecification: declared);
        });

        var data = Assert.IsType<S111CoverageData>(projected.Data);
        AssertSpec(projected.Spec, "S-111", major, minor, clarification);
        AssertSpec(data.Source.Metadata.Spec, "S-111", major, minor, clarification);
    }

    [Theory]
    [MemberData(nameof(S111Editions))]
    public void S111_station_series_reports_declared_edition(string? declared, int major, int minor, int clarification)
    {
        var projected = ProjectFile("S-111", path => S111Dcf8FixtureBuilder.WriteFile(
            path,
            [
                new S111Dcf8FixtureBuilder.Station<S111Dcf8FixtureBuilder.SpecValueRow>
                {
                    Identifier = "S1",
                    Latitude = 47.6f,
                    Longitude = -122.3f,
                    StartDateTime = "20240101T000000Z",
                    EndDateTime = "20240101T010000Z",
                    TimeRecordInterval = 3600,
                    Values =
                    [
                        new() { SurfaceCurrentSpeed = 0.3f, SurfaceCurrentDirection = 45f },
                        new() { SurfaceCurrentSpeed = 0.6f, SurfaceCurrentDirection = 50f },
                    ],
                },
            ],
            productSpecification: declared));

        var data = Assert.IsType<S111StationSeriesData>(projected.Data);
        Assert.Equal(declared, data.Dataset.DeclaredProductSpecification);
        AssertSpec(projected.Spec, "S-111", major, minor, clarification);
    }

    private static LoadedDataset ProjectFile(string specName, Action<string> write)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".h5");
        try
        {
            write(path);
            using var stream = File.OpenRead(path);
            var projected = LoadedDatasetProjector.Project(new DatasetId("ds"), specName, stream);
            Assert.NotNull(projected);
            return projected!;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertSpec(SpecRef spec, string name, int major, int minor, int clarification)
    {
        Assert.Equal(name, spec.Name);
        Assert.Equal(new SpecVersion(major, minor, clarification), spec.Edition);
    }

    private struct SpecBathyRow
    {
        [H5Name("depth")] public float Depth;
        [H5Name("uncertainty")] public float Uncertainty;
    }

    private static void WriteS102(string path, string? productSpecification)
    {
        var values = new SpecBathyRow[4];
        for (int i = 0; i < values.Length; i++)
            values[i] = new SpecBathyRow { Depth = 12.5f, Uncertainty = 0.25f };

        var instance = new H5Group
        {
            Attributes = new()
            {
                ["gridOriginLatitude"] = 50.0,
                ["gridOriginLongitude"] = -1.0,
                ["gridSpacingLatitudinal"] = 0.01,
                ["gridSpacingLongitudinal"] = 0.01,
                ["numPointsLatitudinal"] = 2,
                ["numPointsLongitudinal"] = 2,
            },
            ["Group_001"] = new H5Group { ["values"] = values },
        };

        var rootAttributes = new Dictionary<string, object> { ["horizontalCRS"] = 4326 };
        if (productSpecification is not null)
            rootAttributes["productSpecification"] = productSpecification;

        var file = new H5File
        {
            Attributes = rootAttributes,
            ["BathymetryCoverage"] = new H5Group { ["BathymetryCoverage.01"] = instance },
        };

        file.Write(path, new H5WriteOptions(
            FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name));
    }
}
