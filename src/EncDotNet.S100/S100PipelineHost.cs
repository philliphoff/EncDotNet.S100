using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100;

/// <summary>
/// Internal bootstrap that wires a <see cref="DatasetPipelineFactory"/> seeded
/// with the feature and portrayal catalogues this on-ramp should use. Mirrors the
/// bootstrap the in-repo viewer and <c>s100</c> CLI perform, so the facade drives
/// exactly the same pipelines.
/// </summary>
/// <remarks>
/// A host owns long-lived <see cref="PortrayalCatalogueManager"/> /
/// <see cref="FeatureCatalogueManager"/> instances whose parse caches survive for
/// the host's lifetime. The data and renderer facades keep a host alive and reuse
/// it across calls so repeated work runs against warm catalogue caches; the
/// per-dataset processors built from the host are transient and disposed after
/// use.
/// </remarks>
internal sealed class S100PipelineHost : IDisposable
{
    private readonly PortrayalCatalogueManager _portrayalManager;
    private readonly FeatureCatalogueManager _featureManager;
    private readonly DatasetPipelineFactory _factory;
    private bool _disposed;

    private S100PipelineHost(
        PortrayalCatalogueManager portrayalManager,
        FeatureCatalogueManager featureManager)
    {
        _portrayalManager = portrayalManager;
        _featureManager = featureManager;
        _factory = new DatasetPipelineFactory(
            portrayalManager,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            featureManager,
            new DisplayPlaneAuthorityProvider());
    }

    /// <summary>
    /// Creates a host whose catalogues are the official ones bundled in
    /// <c>EncDotNet.S100.Specifications</c>, optionally overriding the feature
    /// and/or portrayal catalogue for a single product specification.
    /// </summary>
    /// <param name="overrideSpec">
    /// The product specification whose catalogues the overrides apply to (e.g.
    /// <c>"S-101"</c>), or <c>null</c> when no overrides are supplied.
    /// </param>
    /// <param name="featureOverride">
    /// A caller-supplied feature catalogue for <paramref name="overrideSpec"/>, or
    /// <c>null</c> to use the bundled one.
    /// </param>
    /// <param name="portrayalOverride">
    /// A caller-supplied portrayal catalogue for <paramref name="overrideSpec"/>,
    /// or <c>null</c> to use the bundled one.
    /// </param>
    public static S100PipelineHost Create(
        string? overrideSpec = null,
        S100FeatureCatalogue? featureOverride = null,
        S100PortrayalCatalogue? portrayalOverride = null)
    {
        var portrayalManager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                portrayalManager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }

        if (overrideSpec is not null && portrayalOverride?.CustomSource is { } pcSource)
            portrayalManager.SetSource(overrideSpec, new NonDisposingAssetSource(pcSource));

        var featureManager = new FeatureCatalogueManager(spec =>
        {
            if (overrideSpec is not null
                && featureOverride is not null
                && string.Equals(spec, overrideSpec, StringComparison.OrdinalIgnoreCase)
                && featureOverride.OpenStream() is { } overrideStream)
            {
                return overrideStream;
            }

            return Specification.TryOpenFeatureCatalogue(spec);
        });

        return new S100PipelineHost(portrayalManager, featureManager);
    }

    /// <summary>Creates the dataset processor for the file at <paramref name="path"/>.</summary>
    public IDatasetProcessor CreateProcessor(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _factory.CreateProcessor(path);
    }

    /// <summary>
    /// The bundled processor factory this host wraps, for in-assembly
    /// conveniences (e.g. <see cref="BundledDatasetProcessorFactory"/>) to expose
    /// publicly. Its lifetime is bound to this host: the catalogue managers it
    /// closes over are released by <see cref="Dispose"/>, so it must not be used
    /// after the host is disposed.
    /// </summary>
    internal IDatasetProcessorFactory Factory
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _factory;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        // Both catalogue managers own asset sources and parse caches that live
        // for the host's lifetime (DatasetPipelineFactory itself is not
        // IDisposable), so release both here.
        _portrayalManager.Dispose();
        _featureManager.Dispose();
    }
}
