using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.S129;
using EncDotNet.S100.Datasets.S129.DataModel;
using EncDotNet.S100.Datasets.S129.Validation;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// <see cref="IDatasetProcessor"/> for IHO S-129 Under Keel Clearance
/// Management GML datasets. Drives the standard S-100 Part 9 XSLT vector
/// portrayal pipeline inherited from
/// <see cref="GmlDatasetProcessorBase{TFeature}"/>, then fills any
/// under-keel-clearance areas the portrayal left unfilled (see
/// <see cref="PostProcessInstructions"/>).
/// </summary>
/// <remarks>
/// Normally created through the <see cref="S100Products.S129"/> registration
/// (used by <see cref="DatasetPipelineFactory"/>) rather than constructed
/// directly.
/// </remarks>
public sealed class S129DatasetProcessor : GmlDatasetProcessorBase<S129Feature>
{
    private readonly S129Dataset _dataset;
    private ValidationReport? _validationReport;
    private bool _validationCached;
    /// <inheritdoc />
    protected override string ProductDescription => "Under Keel Clearance Management";
    /// <inheritdoc />
    protected override IReadOnlyList<S129Feature> Features => _dataset.Features;

    /// <inheritdoc />
    public override LoadedDatasetData CreateLoadedData() => new S129DatasetData(_dataset);

    /// <summary>
    /// Initializes a new <see cref="S129DatasetProcessor"/> by reading and parsing the
    /// dataset file at <paramref name="path"/>. The file is read in full and
    /// closed before the constructor returns.
    /// </summary>
    /// <param name="path">Path to the S-129 GML dataset file.</param>
    /// <param name="catalogueManager">Supplies the S-129 portrayal catalogue.</param>
    /// <param name="authorityProvider">Resolves the default S-98 display plane for the dataset's content.</param>
    /// <param name="featureCatalogueManager">
    /// Optional source of the S-129 feature catalogue, used to decode attribute
    /// values in feature info; <see langword="null"/> leaves them undecoded.
    /// </param>
    public S129DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        IDisplayPlaneAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, authorityProvider, featureCatalogueManager)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S129DatasetProcessor"/> by reading
    /// the dataset file <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    public S129DatasetProcessor(
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

    private S129DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        IDisplayPlaneAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager)
        : base(
            new S129PortrayalCatalogue(catalogueManager.GetProvider("S-129")),
            featureCatalogueManager?.GetDecoder("S-129"),
            fileName,
            authorityProvider,
            "S-129")
    {
        using (datasetStream)
        {
            _dataset = S129Dataset.Open(datasetStream);
        }

        SetDeclaredEdition(_dataset.DeclaredEdition);
    }

    /// <inheritdoc />
    protected override IFeatureXmlSource CreateFeatureXmlSource() =>
        new GmlFeatureXmlSource<S129Feature>(_dataset.Features);

    /// <inheritdoc />
    public override ValidationReport? Validate()
    {
        if (!_validationCached)
        {
            _validationReport = ValidationRunner.Run(
                _dataset,
                static raw => S129UnderKeelClearancePlan.From(raw, out _),
                S129UkcRules.Default);
            _validationCached = true;
        }
        return _validationReport;
    }

    /// <summary>
    /// Gives every area instruction that the portrayal left without a fill
    /// colour or pattern a semi-transparent (0.7) fallback fill keyed on the
    /// feature type: <c>RED</c> for non-navigable areas, <c>GOLDN</c> for
    /// almost non-navigable areas, and <c>CHMGD</c> otherwise. All other
    /// instructions pass through unchanged.
    /// </summary>
    /// <param name="instructions">Drawing instructions produced by the portrayal pipeline.</param>
    /// <returns>A new list with the fallback fills applied.</returns>
    protected override IReadOnlyList<DrawingInstruction> PostProcessInstructions(
        IReadOnlyList<DrawingInstruction> instructions) => ApplyAreaFillFallback(instructions);

    private List<DrawingInstruction> ApplyAreaFillFallback(IReadOnlyList<DrawingInstruction> instructions)
    {
        var byId = new Dictionary<string, S129Feature>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in _dataset.Features) byId[f.Id] = f;

        var result = new List<DrawingInstruction>(instructions.Count);
        foreach (var instr in instructions)
        {
            if (instr is AreaInstruction { FillColor: null, AreaFillReference: null } area
                && byId.TryGetValue(area.FeatureReference, out var feature))
            {
                var token = feature.FeatureType switch
                {
                    var t when string.Equals(t, "UnderKeelClearanceNonNavigableArea", StringComparison.OrdinalIgnoreCase)
                        => "RED",
                    var t when string.Equals(t, "UnderKeelClearanceAlmostNonNavigableArea", StringComparison.OrdinalIgnoreCase)
                        => "GOLDN",
                    _ => "CHMGD",
                };

                result.Add(new AreaInstruction
                {
                    FeatureReference = area.FeatureReference,
                    ViewingGroup = area.ViewingGroup,
                    DrawingPriority = area.DrawingPriority,
                    Plane = area.Plane,
                    ScaleMinimum = area.ScaleMinimum,
                    ScaleMaximum = area.ScaleMaximum,
                    AreaFillReference = area.AreaFillReference,
                    FillColor = token,
                    Transparency = 0.7,
                });
            }
            else
            {
                result.Add(instr);
            }
        }
        return result;
    }
}
