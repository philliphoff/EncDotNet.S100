using System.Reflection;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using PureHDF;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The S-102 processor reports its <c>issueDate</c> + <c>issueTime</c> through
/// <see cref="ITimeAwareDatasetProcessor.IssueTime"/> like S-104 and S-111
/// (#738), while staying untimed (no time steps).
/// </summary>
public class S102IssueTimeTests
{
    private struct SpecBathyRow
    {
        [H5Name("depth")] public float Depth;
        [H5Name("uncertainty")] public float Uncertainty;
    }

    [Theory]
    [InlineData("20260902", "105406+0000", "2026-09-02T10:54:06")]   // NOAA S-102 Ed 3 form
    [InlineData("2026-09-02", "05:54:06-05:00", "2026-09-02T10:54:06")]
    [InlineData("20260902", null, "2026-09-02T00:00:00")]
    [InlineData("20260902", "soon", "2026-09-02T00:00:00")]
    public void IssueTime_Combines_IssueDate_And_IssueTime_As_UTC(string issueDate, string? issueTime, string expectedUtc)
    {
        var path = WriteS102(issueDate, issueTime);
        try
        {
            using var manager = CreateCatalogueManager();
            using var processor = new S102DatasetProcessor(
                path, manager, new MoonSharpLuaEngine(), new ProjNetCrsTransformFactory());

            var timeAware = Assert.IsAssignableFrom<ITimeAwareDatasetProcessor>(processor);
            Assert.Empty(timeAware.AvailableTimes);
            Assert.Equal(
                DateTime.SpecifyKind(DateTime.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc),
                timeAware.IssueTime);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IssueTime_Is_Null_Without_An_IssueDate()
    {
        var path = WriteS102(issueDate: null, issueTime: "105406Z");
        try
        {
            using var manager = CreateCatalogueManager();
            using var processor = new S102DatasetProcessor(
                path, manager, new MoonSharpLuaEngine(), new ProjNetCrsTransformFactory());

            Assert.Null(processor.IssueTime);
        }
        finally { File.Delete(path); }
    }

    private static string WriteS102(string? issueDate, string? issueTime)
    {
        var path = Path.GetTempFileName() + ".h5";

        var values = new SpecBathyRow[4];
        for (int i = 0; i < values.Length; i++)
            values[i] = new SpecBathyRow { Depth = 5.0f, Uncertainty = 0.1f };

        var rootAttributes = new Dictionary<string, object>
        {
            ["productSpecification"] = "INT.IHO.S-102.3.0.0",
            ["horizontalCRS"] = 4326,
        };
        if (issueDate is not null)
            rootAttributes["issueDate"] = issueDate;
        if (issueTime is not null)
            rootAttributes["issueTime"] = issueTime;

        var file = new H5File
        {
            Attributes = rootAttributes,
            ["BathymetryCoverage"] = new H5Group
            {
                ["BathymetryCoverage.01"] = new H5Group
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
                },
            },
        };

        var options = new H5WriteOptions(
            FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name);
        file.Write(path, options);
        return path;
    }

    private static PortrayalCatalogueManager CreateCatalogueManager()
    {
        var manager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                manager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }
        return manager;
    }
}
