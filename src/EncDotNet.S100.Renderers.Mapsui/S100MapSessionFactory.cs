using EncDotNet.S100.Pipelines;
using Mapsui;
using Microsoft.Extensions.DependencyInjection;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Default <see cref="IS100MapSessionFactory"/>: resolves the CRS transform
/// factory (required) and <see cref="S100MapsuiOptions"/> (optional) from the
/// container and composes a session via <see cref="S100MapExtensions.AddS100"/>.
/// </summary>
internal sealed class S100MapSessionFactory : IS100MapSessionFactory
{
    private readonly IServiceProvider _services;

    public S100MapSessionFactory(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public IS100MapSession Create(Map map)
    {
        ArgumentNullException.ThrowIfNull(map);

        // The reusable assembly ships no CRS implementation, so the host must
        // register one (e.g. ProjNetCrsTransformFactory); a missing registration
        // surfaces as a clear DI error here.
        var crsTransformFactory = _services.GetRequiredService<ICrsTransformFactory>();
        var options = _services.GetService<S100MapsuiOptions>();
        return map.AddS100(crsTransformFactory, options);
    }
}
