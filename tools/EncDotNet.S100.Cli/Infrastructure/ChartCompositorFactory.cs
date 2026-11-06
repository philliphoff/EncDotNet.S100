using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Rendering.Engine;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>The chart engine's compositor as the CLI configures it.</summary>
internal static class ChartCompositorFactory
{
    /// <summary>
    /// A compositor over ProjNet reprojection and the S-98 authority of the
    /// bundled product registry.
    /// </summary>
    public static ChartCompositor Create() =>
        new(new ProjNetCrsTransformFactory(), new InteroperabilityAuthority(ProductRegistry.Default.DisplayPlanes));
}
