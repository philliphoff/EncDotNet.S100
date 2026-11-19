using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Pipelines.Time;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>Builds <see cref="SampleTool"/> for tests and reads its results.</summary>
internal static class TestSampleTool
{
    /// <summary>A tool over the catalog with no tide predictions.</summary>
    public static SampleTool Create(IDatasetCatalog catalog)
    {
        var transforms = new ProjNetCrsTransformFactory();
        return new SampleTool(catalog, transforms, new RouteCheckSourceCache(transforms), TimeProvider.System);
    }

    /// <summary>A polyline request for one product, as the old along tool took it.</summary>
    public static SampleRequest Along(
        SpecRef spec,
        GeoPolyline polyline,
        DateTimeOffset? Time = null,
        TimeQuery? Times = null,
        TimeOutOfRangePolicy OutOfRange = TimeOutOfRangePolicy.Error) =>
        new(null, spec, Polyline: polyline, Time: Time, Times: Times, OutOfRange: OutOfRange);

    /// <summary>Runs a polyline request and returns its profile.</summary>
    public static async Task<ToolResult<SampleProfileResult>> ProfileAsync(this SampleTool tool, SampleRequest request, CancellationToken cancellationToken)
    {
        var result = await tool.InvokeAsync(request, cancellationToken);
        if (result.TryGetError(out var error))
        {
            return ToolResult<SampleProfileResult>.Err(error);
        }

        result.TryGetValue(out var output);
        return ToolResult<SampleProfileResult>.Ok(output!.Profile ?? throw new InvalidOperationException("Not a profile."));
    }
}
