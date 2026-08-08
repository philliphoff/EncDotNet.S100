using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Samples.MapHost;

/// <summary>
/// Bootstraps the <see cref="DatasetPipelineFactory"/> that
/// <c>IS100MapSession.Datasets.LoadAsync</c> needs, seeded with the official
/// feature and portrayal catalogues bundled in
/// <c>EncDotNet.S100.Specifications</c>.
/// </summary>
/// <remarks>
/// This is the same bootstrap the in-repo Viewer and <c>s100</c> CLI perform,
/// reproduced by hand here so the sample stays honest about what a non-Viewer
/// host must wire today. That a host needs to reference Portrayals /
/// Specifications / Features / Scripting.MoonSharp - and that
/// <see cref="DatasetPipelineFactory"/> transitively pulls in every S-1xx
/// product - is precisely the coupling issue #512 step 9 aims to reduce. A
/// future public "bundled factory" convenience would collapse this file to a
/// single call.
/// </remarks>
internal sealed class SampleS100Host : IDisposable
{
    private readonly PortrayalCatalogueManager _portrayalManager;

    private SampleS100Host(
        PortrayalCatalogueManager portrayalManager,
        DatasetPipelineFactory pipelineFactory)
    {
        _portrayalManager = portrayalManager;
        PipelineFactory = pipelineFactory;
    }

    /// <summary>The factory to hand to <c>S100MapsuiOptions.DatasetPipelineFactory</c>.</summary>
    public DatasetPipelineFactory PipelineFactory { get; }

    /// <summary>Builds a host with the bundled catalogues for every available spec.</summary>
    public static SampleS100Host Create()
    {
        var portrayalManager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
            {
                portrayalManager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
            }
        }

        // The FC parse cache lives for the manager's lifetime, so keep the
        // manager alive as long as the factory is used.
        var featureManager = new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue);

        var pipelineFactory = new DatasetPipelineFactory(
            portrayalManager,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            featureManager,
            new DisplayPlaneAuthorityProvider());

        return new SampleS100Host(portrayalManager, pipelineFactory);
    }

    public void Dispose() => _portrayalManager.Dispose();
}
