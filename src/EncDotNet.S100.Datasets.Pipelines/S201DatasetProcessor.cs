using System.IO;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S201;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Dataset processor that loads an S-201 Aids to Navigation Information
/// GML dataset, executes the bundled XSLT portrayal pipeline, and
/// produces a Mapsui layer ready for display in the viewer.
/// </summary>
public sealed class S201DatasetProcessor : GmlDatasetProcessorBase<S201Feature>
{
    private readonly S201Dataset _dataset;

    /// <inheritdoc />
    public override SpecRef Spec => new("S-201", default);
    /// <inheritdoc />
    protected override string ProductDescription => "Aids to Navigation Information";
    /// <inheritdoc />
    protected override IReadOnlyList<S201Feature> Features => _dataset.Features;

    /// <summary>Initializes a new <see cref="S201DatasetProcessor"/>.</summary>
    public S201DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        IInteroperabilityAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, authorityProvider, featureCatalogueManager)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S201DatasetProcessor"/> by reading
    /// the dataset file <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    public S201DatasetProcessor(
        IAssetSource source,
        string relativePath,
        PortrayalCatalogueManager catalogueManager,
        IInteroperabilityAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(
            AssetSourceHelpers.OpenSeekable(source, relativePath),
            AssetSourceHelpers.GetFileName(relativePath),
            catalogueManager,
            authorityProvider,
            featureCatalogueManager)
    {
    }

    private S201DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        IInteroperabilityAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager)
        : base(
            new S201PortrayalCatalogue(catalogueManager.GetProvider("S-201")),
            featureCatalogueManager?.GetDecoder("S-201"),
            fileName,
            authorityProvider)
    {
        using (datasetStream)
        {
            _dataset = S201Dataset.Open(datasetStream);
        }
    }

    /// <inheritdoc />
    protected override IFeatureXmlSource CreateFeatureXmlSource() =>
        new S201FeatureXmlSource(_dataset);

    /// <inheritdoc />
    protected override IReadOnlyList<FeatureReference> BuildFeatureReferences(S201Feature feature)
    {
        var references = new List<FeatureReference>();
        foreach (var infoRef in feature.InformationReferences)
        {
            if (string.IsNullOrWhiteSpace(infoRef.InformationRef))
                continue;
            references.Add(new FeatureReference
            {
                Role = infoRef.Role,
                TargetRef = infoRef.InformationRef,
            });
        }
        foreach (var featureRef in feature.FeatureReferences)
        {
            if (string.IsNullOrWhiteSpace(featureRef.TargetRef))
                continue;
            references.Add(new FeatureReference
            {
                Role = featureRef.Role,
                TargetRef = featureRef.TargetRef,
            });
        }
        return references;
    }

    /// <inheritdoc />
    protected override string BuildInfoSuffix() =>
        $"Information types: {_dataset.InformationTypes.Length}";
}
