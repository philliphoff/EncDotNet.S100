using System.IO;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S128;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.Pipelines;

public sealed class S128DatasetProcessor : GmlDatasetProcessorBase<S128Feature>
{
    private readonly S128Dataset _dataset;

    public override string ProductSpec => "S-128";
    protected override string ProductDescription => "Catalogue of Nautical Products";
    protected override IReadOnlyList<S128Feature> Features => _dataset.Features;

    /// <summary>The parsed S-128 dataset backing this processor.</summary>
    public S128Dataset Dataset => _dataset;

    public S128DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, featureCatalogueManager)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S128DatasetProcessor"/> by reading
    /// the dataset file <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    public S128DatasetProcessor(
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

    private S128DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        FeatureCatalogueManager? featureCatalogueManager)
        : base(
            new S128PortrayalCatalogue(catalogueManager.GetProvider("S-128")),
            featureCatalogueManager?.GetDecoder("S-128"),
            fileName)
    {
        using (datasetStream)
        {
            _dataset = S128Dataset.Open(datasetStream);
        }
    }

    protected override IFeatureXmlSource CreateFeatureXmlSource() =>
        new S128FeatureXmlSource(_dataset);
}
