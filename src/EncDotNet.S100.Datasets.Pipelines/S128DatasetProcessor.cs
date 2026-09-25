using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.S128;
using EncDotNet.S100.Datasets.S128.DataModel;
using EncDotNet.S100.Datasets.S128.Validation;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// <see cref="IDatasetProcessor"/> for IHO S-128 Catalogue of Nautical
/// Products GML datasets. Drives the standard S-100 Part 9 XSLT vector
/// portrayal pipeline inherited from
/// <see cref="GmlDatasetProcessorBase{TFeature}"/> and exposes the parsed
/// catalogue through <see cref="Dataset"/>.
/// </summary>
/// <remarks>
/// Normally created through the <see cref="S100Products.S128"/> registration
/// (used by <see cref="DatasetPipelineFactory"/>) rather than constructed
/// directly.
/// </remarks>
public sealed class S128DatasetProcessor : GmlDatasetProcessorBase<S128Feature>
{
    private readonly S128Dataset _dataset;
    private ValidationReport? _validationReport;
    private bool _validationCached;
    /// <inheritdoc />
    protected override string ProductDescription => "Catalogue of Nautical Products";
    /// <inheritdoc />
    protected override IReadOnlyList<S128Feature> Features => _dataset.Features;

    /// <inheritdoc />
    public override LoadedDatasetData CreateLoadedData() => new S128DatasetData(_dataset);

    /// <summary>The parsed S-128 dataset backing this processor.</summary>
    public S128Dataset Dataset => _dataset;

    /// <summary>
    /// Initializes a new <see cref="S128DatasetProcessor"/> by reading and parsing the
    /// dataset file at <paramref name="path"/>. The file is read in full and
    /// closed before the constructor returns.
    /// </summary>
    /// <param name="path">Path to the S-128 GML dataset file.</param>
    /// <param name="catalogueManager">Supplies the S-128 portrayal catalogue.</param>
    /// <param name="authorityProvider">Resolves the default S-98 display plane for the dataset's content.</param>
    /// <param name="featureCatalogueManager">
    /// Optional source of the S-128 feature catalogue, used to decode attribute
    /// values in feature info; <see langword="null"/> leaves them undecoded.
    /// </param>
    public S128DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        IDisplayPlaneAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, authorityProvider, featureCatalogueManager)
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

    private S128DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        IDisplayPlaneAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager)
        : base(
            new S128PortrayalCatalogue(catalogueManager.GetProvider("S-128")),
            featureCatalogueManager?.GetDecoder("S-128"),
            fileName,
            authorityProvider,
            "S-128")
    {
        using (datasetStream)
        {
            _dataset = S128Dataset.Open(datasetStream);
        }

        SetDeclaredEdition(_dataset.DeclaredEdition);
    }

    /// <inheritdoc />
    protected override IFeatureXmlSource CreateFeatureXmlSource() =>
        new S128FeatureXmlSource(_dataset);

    /// <inheritdoc />
    public override ValidationReport? Validate()
    {
        if (!_validationCached)
        {
            _validationReport = ValidationRunner.Run(
                _dataset,
                static raw => S128ProductCatalogue.From(raw, out _),
                S128CatalogueRules.Default);
            _validationCached = true;
        }
        return _validationReport;
    }
}
