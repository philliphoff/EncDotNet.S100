using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.S412;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// <see cref="IDatasetProcessor"/> for WMO S-412 Marine Weather Warnings GML
/// datasets. Drives the standard S-100 Part 9 XSLT vector portrayal pipeline
/// inherited from <see cref="GmlDatasetProcessorBase{TFeature}"/>.
/// S-412 is a draft WMO product; there is no validation rule pack yet
/// (issue #841).
/// </summary>
/// <remarks>
/// Normally created through the <see cref="S100Products.S412"/> registration
/// (used by <see cref="DatasetPipelineFactory"/>) rather than constructed
/// directly.
/// </remarks>
public sealed class S412DatasetProcessor : GmlDatasetProcessorBase<S412Feature>
{
    private readonly S412Dataset _dataset;
    /// <inheritdoc />
    protected override string ProductDescription => "Marine Weather Warnings";
    /// <inheritdoc />
    protected override IReadOnlyList<S412Feature> Features => _dataset.Features;

    /// <inheritdoc />
    public override LoadedDatasetData CreateLoadedData() => new S412DatasetData(_dataset);

    /// <summary>
    /// Initializes a new <see cref="S412DatasetProcessor"/> by reading and parsing the
    /// dataset file at <paramref name="path"/>. The file is read in full and
    /// closed before the constructor returns.
    /// </summary>
    /// <param name="path">Path to the S-412 GML dataset file.</param>
    /// <param name="catalogueManager">Supplies the S-412 portrayal catalogue.</param>
    /// <param name="authorityProvider">Resolves the default S-98 display plane for the dataset's content.</param>
    /// <param name="featureCatalogueManager">
    /// Optional source of the S-412 feature catalogue, used to decode attribute
    /// values in feature info; <see langword="null"/> leaves them undecoded.
    /// </param>
    public S412DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        IDisplayPlaneAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, authorityProvider, featureCatalogueManager)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S412DatasetProcessor"/> by reading the
    /// dataset file <paramref name="relativePath"/> from
    /// <paramref name="source"/> (e.g. <c>FileSystemAssetSource</c> or
    /// <c>ZipAssetSource</c>). Used by exchange-set bulk loading.
    /// </summary>
    public S412DatasetProcessor(
        IAssetSource source,
        string relativePath,
        PortrayalCatalogueManager catalogueManager,
        IDisplayPlaneAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(
            AssetSourceHelpers.OpenSeekable(source, relativePath),
            AssetSourceHelpers.GetFileName(relativePath),
            catalogueManager,
            authorityProvider,
            featureCatalogueManager)
    {
    }

    private S412DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        IDisplayPlaneAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager)
        : base(
            new S412PortrayalCatalogue(catalogueManager.GetProvider("S-412")),
            featureCatalogueManager?.GetDecoder("S-412"),
            fileName,
            authorityProvider,
            "S-412")
    {
        SetContentHash(DatasetContentHash.HashAndRewind(ref datasetStream));
        using (datasetStream)
        {
            _dataset = S412Dataset.Open(datasetStream);
        }

        SetDeclaredEdition(_dataset.DeclaredEdition);

        SetAxisOrder(_dataset.AxisOrder);
    }

    /// <inheritdoc />
    protected override IFeatureXmlSource CreateFeatureXmlSource() =>
        new GmlFeatureXmlSource<S412Feature>(_dataset.Features);
}
