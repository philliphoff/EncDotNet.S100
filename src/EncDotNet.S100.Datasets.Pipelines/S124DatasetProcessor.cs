using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Datasets.S124.DataModel;
using EncDotNet.S100.Datasets.S124.Validation;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// <see cref="IDatasetProcessor"/> for IHO S-124 Navigational Warnings GML
/// datasets. Drives the standard S-100 Part 9 XSLT vector portrayal pipeline
/// inherited from <see cref="GmlDatasetProcessorBase{TFeature}"/>.
/// </summary>
/// <remarks>
/// Normally created through the <see cref="S100Products.S124"/> registration
/// (used by <see cref="DatasetPipelineFactory"/>) rather than constructed
/// directly.
/// </remarks>
public sealed class S124DatasetProcessor : GmlDatasetProcessorBase<S124Feature>
{
    private readonly S124Dataset _dataset;
    private ValidationReport? _validationReport;
    private bool _validationCached;
    /// <inheritdoc />
    protected override string ProductDescription => "Navigational Warnings";
    /// <inheritdoc />
    protected override IReadOnlyList<S124Feature> Features => _dataset.Features;

    /// <inheritdoc />
    public override LoadedDatasetData CreateLoadedData() => new S124DatasetData(_dataset);

    /// <summary>
    /// Initializes a new <see cref="S124DatasetProcessor"/> by reading and parsing the
    /// dataset file at <paramref name="path"/>. The file is read in full and
    /// closed before the constructor returns.
    /// </summary>
    /// <param name="path">Path to the S-124 GML dataset file.</param>
    /// <param name="catalogueManager">Supplies the S-124 portrayal catalogue.</param>
    /// <param name="authorityProvider">Resolves the default S-98 display plane for the dataset's content.</param>
    /// <param name="featureCatalogueManager">
    /// Optional source of the S-124 feature catalogue, used to decode attribute
    /// values in feature info; <see langword="null"/> leaves them undecoded.
    /// </param>
    public S124DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        IDisplayPlaneAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, authorityProvider, featureCatalogueManager)
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

    private S124DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        IDisplayPlaneAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager)
        : base(
            new S124PortrayalCatalogue(catalogueManager.GetProvider("S-124")),
            featureCatalogueManager?.GetDecoder("S-124"),
            fileName,
            authorityProvider,
            "S-124")
    {
        using (datasetStream)
        {
            _dataset = S124Dataset.Open(datasetStream);
        }

        SetDeclaredEdition(_dataset.DeclaredEdition);

        SetAxisOrder(_dataset.AxisOrder);
    }

    /// <inheritdoc />
    protected override IFeatureXmlSource CreateFeatureXmlSource() =>
        new GmlFeatureXmlSource<S124Feature>(_dataset.Features);

    /// <summary>
    /// Leaves out the parts in <see cref="RenderContext.HiddenFeatures"/>,
    /// with the <c>NavwarnAreaAffected</c> and <c>TextPlacement</c> features
    /// that only hidden parts reference (S-98 Edition 2.0.0 §12.12). Areas
    /// and texts no part references, and those a drawn part also references,
    /// stay. Filtering the input rather than the drawing instructions keeps
    /// the rules that look across parts right, such as the count label the
    /// point rule puts on parts that share a point.
    /// </summary>
    /// <param name="context">The render context, if any.</param>
    protected override IFeatureXmlSource CreateFeatureXmlSource(RenderContext? context)
    {
        var undrawn = UndrawnFeatures(context?.HiddenFeatures);
        _undrawn = undrawn;
        return undrawn.Count == 0
            ? CreateFeatureXmlSource()
            : new GmlFeatureXmlSource<S124Feature>([.. _dataset.Features.Where(f => !undrawn.Contains(f.Id))]);
    }

    /// <summary>
    /// Hides the whole warning when every part is in
    /// <see cref="RenderContext.HiddenFeatures"/>, so its unattached areas
    /// and texts aren't drawn alone.
    /// </summary>
    /// <param name="context">The render context, if any.</param>
    /// <returns>The status line, or <c>null</c> to render.</returns>
    protected override string? GetSuppressionInfo(RenderContext? context)
    {
        if (context?.HiddenFeatures is not { Count: > 0 } hidden)
        {
            _undrawn = Empty;
            return null;
        }

        var parts = _dataset.Features.Where(IsPart).ToList();
        if (parts.Count == 0 || !parts.All(p => hidden.Contains(p.Id)))
            return null;

        _undrawn = _dataset.Features.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        return $"S-124 Navigational Warnings — {FileName}\nHidden (no warning drawn)";
    }

    /// <inheritdoc />
    protected override bool IsPickable(S124Feature feature) => !_undrawn.Contains(feature.Id);

    /// <summary>
    /// The features a render with <paramref name="hidden"/> leaves out: the
    /// hidden parts and what only they reference.
    /// </summary>
    internal IReadOnlySet<string> UndrawnFeatures(IReadOnlySet<string>? hidden)
    {
        if (hidden is not { Count: > 0 })
            return Empty;

        var undrawn = new HashSet<string>(StringComparer.Ordinal);
        // Areas and texts referenced by at least one drawn part.
        var kept = new HashSet<string>(StringComparer.Ordinal);
        var referencedByHidden = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in _dataset.Features)
        {
            if (!IsPart(feature))
                continue;
            var isHidden = hidden.Contains(feature.Id);
            if (isHidden)
                undrawn.Add(feature.Id);
            foreach (var reference in feature.References)
            {
                var target = reference.Href.TrimStart('#');
                (isHidden ? referencedByHidden : kept).Add(target);
            }
        }

        foreach (var feature in _dataset.Features)
        {
            if (feature.FeatureType is "NavwarnAreaAffected" or "TextPlacement"
                && referencedByHidden.Contains(feature.Id)
                && !kept.Contains(feature.Id))
            {
                undrawn.Add(feature.Id);
            }
        }

        return undrawn;
    }

    private static bool IsPart(S124Feature feature) =>
        string.Equals(feature.FeatureType, "NavwarnPart", StringComparison.Ordinal);

    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();

    // What the last portrayal left out; read by picking on other threads.
    private volatile IReadOnlySet<string> _undrawn = Empty;

    /// <inheritdoc />
    protected override NavigationalWarningInfo? BuildNavigationalWarning(S124Feature feature)
    {
        if (!string.Equals(feature.FeatureType, "NavwarnPart", StringComparison.Ordinal))
            return null;
        var warning = TypedWarning;
        var part = warning?.Parts.FirstOrDefault(p => string.Equals(p.Id, feature.Id, StringComparison.Ordinal));
        return part is null ? null : S124WarningInfo.From(warning!, part);
    }

    /// <inheritdoc />
    public override NavigationalWarningInfo? GetNavigationalWarningMessage() =>
        TypedWarning is { Preamble: not null } warning ? S124WarningInfo.Message(warning) : null;

    /// <summary>
    /// The typed warning, projected once; <c>null</c> when the dataset is
    /// empty and cannot be projected.
    /// </summary>
    private S124NavigationalWarning? TypedWarning
    {
        get
        {
            if (!_typedWarningProjected)
            {
                try
                {
                    _typedWarning = S124NavigationalWarning.From(_dataset, out _);
                }
                catch (InvalidOperationException)
                {
                    _typedWarning = null;
                }
                _typedWarningProjected = true;
            }
            return _typedWarning;
        }
    }

    private S124NavigationalWarning? _typedWarning;
    private bool _typedWarningProjected;

    /// <inheritdoc />
    protected override ValidationReport? ValidateDataset()
    {
        if (!_validationCached)
        {
            _validationReport = ValidationRunner.Run(
                _dataset,
                static raw => S124NavigationalWarning.From(raw, out _),
                S124NavigationalWarningRules.Default);
            _validationCached = true;
        }
        return _validationReport;
    }
}
