using System.IO;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.Pipelines;

public sealed class S122DatasetProcessor : GmlDatasetProcessorBase<S122Feature>
{
    private readonly S122Dataset _dataset;

    public override SpecRef Spec => new("S-122", default);
    protected override string ProductDescription => "Marine Protected Areas";
    protected override IReadOnlyList<S122Feature> Features => _dataset.Features;

    public S122DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, featureCatalogueManager)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S122DatasetProcessor"/> by reading the
    /// dataset file <paramref name="relativePath"/> from
    /// <paramref name="source"/> (e.g. <c>FileSystemAssetSource</c> or
    /// <c>ZipAssetSource</c>). Used by exchange-set bulk loading.
    /// </summary>
    public S122DatasetProcessor(
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

    private S122DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        FeatureCatalogueManager? featureCatalogueManager)
        : base(
            new S122PortrayalCatalogue(catalogueManager.GetProvider("S-122")),
            featureCatalogueManager?.GetDecoder("S-122"),
            fileName)
    {
        using (datasetStream)
        {
            _dataset = S122Dataset.Open(datasetStream);
        }
    }

    protected override IFeatureXmlSource CreateFeatureXmlSource() =>
        new GmlFeatureXmlSource<S122Feature>(_dataset.Features);
}
