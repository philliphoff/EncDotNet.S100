using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Datasets.S421.Validation;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// <see cref="IDatasetProcessor"/> for IHO S-421 Route Plan GML datasets.
/// Drives the standard S-100 Part 9 XSLT vector portrayal pipeline inherited
/// from <see cref="GmlDatasetProcessorBase{TFeature}"/> and exposes route
/// xlink references as navigable links in pick reports.
/// </summary>
/// <remarks>
/// Normally created through the <see cref="S100Products.S421"/> registration
/// (used by <see cref="DatasetPipelineFactory"/>) rather than constructed
/// directly.
/// </remarks>
public sealed class S421DatasetProcessor : GmlDatasetProcessorBase<S421Feature>
{
    private readonly S421Dataset _dataset;
    private ValidationReport? _validationReport;
    private bool _validationCached;
    /// <inheritdoc />
    protected override string ProductDescription => "Route Plan";
    /// <inheritdoc />
    protected override IReadOnlyList<S421Feature> Features => _dataset.Features;

    /// <inheritdoc />
    public override LoadedDatasetData CreateLoadedData() => new S421DatasetData(_dataset);
    /// <inheritdoc />
    protected override double MinExtentPadding => 0.05;

    /// <summary>
    /// Initializes a new <see cref="S421DatasetProcessor"/> by reading and parsing the
    /// dataset file at <paramref name="path"/>. The file is read in full and
    /// closed before the constructor returns.
    /// </summary>
    /// <param name="path">Path to the S-421 GML dataset file.</param>
    /// <param name="catalogueManager">Supplies the S-421 portrayal catalogue.</param>
    /// <param name="authorityProvider">Resolves the default S-98 display plane for the dataset's content.</param>
    /// <param name="featureCatalogueManager">
    /// Optional source of the S-421 feature catalogue, used to decode attribute
    /// values in feature info; <see langword="null"/> leaves them undecoded.
    /// </param>
    public S421DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        IDisplayPlaneAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, authorityProvider, featureCatalogueManager)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S421DatasetProcessor"/> by reading
    /// the dataset file <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    public S421DatasetProcessor(
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

    private S421DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        IDisplayPlaneAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager)
        : base(
            new S421PortrayalCatalogue(catalogueManager.GetProvider("S-421")),
            featureCatalogueManager?.GetDecoder("S-421"),
            fileName,
            authorityProvider,
            "S-421")
    {
        using (datasetStream)
        {
            _dataset = S421Dataset.Open(datasetStream);
        }

        SetDeclaredEdition(_dataset.DeclaredEdition);
    }

    /// <inheritdoc />
    protected override IFeatureXmlSource CreateFeatureXmlSource() =>
        new S421FeatureXmlSource(_dataset);

    /// <inheritdoc />
    protected override IReadOnlyList<FeatureReference> BuildFeatureReferences(S421Feature feature)
    {
        var references = new List<FeatureReference>();
        foreach (var reference in feature.References)
        {
            if (string.IsNullOrWhiteSpace(reference.Href))
                continue;
            references.Add(new FeatureReference
            {
                Role = reference.Role,
                TargetRef = reference.Href.TrimStart('#'),
                ArcRole = reference.ArcRole,
            });
        }
        return references;
    }

    /// <inheritdoc />
    protected override string BuildInfoSuffix() =>
        $"Information types: {_dataset.InformationTypes.Count}";

    /// <inheritdoc />
    public override ValidationReport? Validate()
    {
        if (!_validationCached)
        {
            _validationReport = ValidationRunner.Run(
                _dataset,
                static raw => S421RoutePlan.From(raw, out _),
                S421RoutePlanRules.Default);
            _validationCached = true;
        }
        return _validationReport;
    }
}
