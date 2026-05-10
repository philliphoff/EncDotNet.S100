using System.IO;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.Pipelines;

public sealed class S124DatasetProcessor : GmlDatasetProcessorBase<S124Feature>
{
    private readonly S124Dataset _dataset;

    public override string ProductSpec => "S-124";
    protected override string ProductDescription => "Navigational Warnings";
    protected override IReadOnlyList<S124Feature> Features => _dataset.Features;

    public S124DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, featureCatalogueManager)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S124DatasetProcessor"/> by reading the
    /// dataset file <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    public S124DatasetProcessor(
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

    private S124DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        FeatureCatalogueManager? featureCatalogueManager)
        : base(
            new S124PortrayalCatalogue(catalogueManager.GetProvider("S-124")),
            featureCatalogueManager?.GetDecoder("S-124"),
            fileName)
    {
        using (datasetStream)
        {
            _dataset = S124Dataset.Open(datasetStream);
        }
    }

    protected override IFeatureXmlSource CreateFeatureXmlSource() =>
        new GmlFeatureXmlSource<S124Feature>(_dataset.Features);
}
