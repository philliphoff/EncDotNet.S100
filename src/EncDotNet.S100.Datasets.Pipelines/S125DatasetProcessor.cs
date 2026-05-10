using System.IO;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S125;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Dataset processor that loads an S-125 Marine Aids to Navigation GML
/// dataset, executes the bundled XSLT portrayal pipeline, and produces a
/// Mapsui layer ready for display in the viewer.
/// </summary>
public sealed class S125DatasetProcessor : GmlDatasetProcessorBase<S125Feature>
{
    private readonly S125Dataset _dataset;

    /// <inheritdoc />
    public override SpecRef Spec => new("S-125", default);
    protected override string ProductDescription => "Marine Aids to Navigation";
    protected override IReadOnlyList<S125Feature> Features => _dataset.Features;

    /// <summary>Initializes a new <see cref="S125DatasetProcessor"/>.</summary>
    public S125DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, featureCatalogueManager)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S125DatasetProcessor"/> by reading
    /// the dataset file <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    public S125DatasetProcessor(
        IAssetSource source,
        string relativePath,
        PortrayalCatalogueManager catalogueManager,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(
            AssetSourceHelpers.OpenSeekable(source, relativePath),
            AssetSourceHelpers.GetFileName(relativePath),
            catalogueManager,
            featureCatalogueManager)
    {
    }

    private S125DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        FeatureCatalogueManager? featureCatalogueManager)
        : base(
            new S125PortrayalCatalogue(catalogueManager.GetProvider("S-125")),
            featureCatalogueManager?.GetDecoder("S-125"),
            fileName)
    {
        using (datasetStream)
        {
            _dataset = S125Dataset.Open(datasetStream);
        }
    }

    protected override IFeatureXmlSource CreateFeatureXmlSource() =>
        new S125FeatureXmlSource(_dataset);

    protected override IReadOnlyList<FeatureReference> BuildFeatureReferences(S125Feature feature)
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
        return references;
    }

    protected override string BuildInfoSuffix() =>
        $"Information types: {_dataset.InformationTypes.Length}";
}
