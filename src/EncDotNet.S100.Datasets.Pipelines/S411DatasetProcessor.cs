using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.S411;
using EncDotNet.S100.Datasets.S411.DataModel;
using EncDotNet.S100.Datasets.S411.Validation;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Validation;


namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// <see cref="IDatasetProcessor"/> for S-411 Ice Information GML datasets,
/// accepting both the JCOMM operational encoding and the IHO sample encoding.
/// Drives the standard S-100 Part 9 XSLT vector portrayal pipeline inherited
/// from <see cref="GmlDatasetProcessorBase{TFeature}"/>. The WMO ice "egg
/// code" of sea-ice and lake-ice features comes from
/// <see cref="IceEggCodeContributor"/>, which resolves its definitions through
/// this processor's feature catalogue.
/// </summary>
/// <remarks>
/// <para>
/// Each S-411 file is a single snapshot. As an
/// <see cref="ITimeAwareDatasetProcessor"/> it reports its issue date as the
/// only available time, and renders nothing while the render context's time
/// step is earlier than that issue date.
/// </para>
/// <para>
/// Normally created through the <see cref="S100Products.S411"/> registration
/// (used by <see cref="DatasetPipelineFactory"/>) rather than constructed
/// directly.
/// </para>
/// </remarks>
public sealed class S411DatasetProcessor :
    GmlDatasetProcessorBase<S411Feature>,
    ITimeAwareDatasetProcessor
{
    private readonly S411Dataset _dataset;
    private ValidationReport? _validationReport;
    private bool _validationCached;
    /// <inheritdoc />
    protected override string ProductDescription => "Sea Ice";
    /// <inheritdoc />
    protected override IReadOnlyList<S411Feature> Features => _dataset.Features;

    /// <inheritdoc />
    public override LoadedDatasetData CreateLoadedData() => new S411DatasetData(_dataset);

    /// <summary>
    /// Time samples this dataset can be rendered at. S-411 datasets are
    /// snapshot-per-file; this is either a single-element list with the
    /// dataset's <see cref="S411Dataset.IssueDate"/> or empty when the
    /// source GML carried no recognised timestamp.
    /// </summary>
    public IReadOnlyList<DateTime> AvailableTimes =>
        _dataset.IssueDate is { } dt ? [dt] : Array.Empty<DateTime>();

    /// <summary>
    /// S-411 ice chart snapshots: observations, each valid until the next one
    /// for at most 14 days.
    /// </summary>
    public static TimeSemantics SeaIceTimeSemantics { get; } =
        new(TimeStepKind.Observation, TimeStepSelection.AtOrBefore, TimeSpan.FromDays(14));

    /// <inheritdoc />
    /// <remarks>Always <see cref="SeaIceTimeSemantics"/>.</remarks>
    public TimeSemantics TimeSemantics => SeaIceTimeSemantics;

    /// <summary>
    /// Initializes a new <see cref="S411DatasetProcessor"/> by reading and parsing the
    /// dataset file at <paramref name="path"/>. The file is read in full and
    /// closed before the constructor returns.
    /// </summary>
    /// <param name="path">Path to the S-411 GML dataset file.</param>
    /// <param name="catalogueManager">Supplies the S-411 portrayal catalogue.</param>
    /// <param name="authorityProvider">Resolves the default S-98 display plane for the dataset's content.</param>
    /// <param name="featureCatalogueManager">
    /// Optional source of the S-411 feature catalogue, used to decode attribute
    /// values in feature info; <see langword="null"/> leaves them undecoded.
    /// </param>
    public S411DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        IDisplayPlaneAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager = null)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, authorityProvider, featureCatalogueManager)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S411DatasetProcessor"/> by reading
    /// the dataset file <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    public S411DatasetProcessor(
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

    private S411DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        IDisplayPlaneAuthorityProvider authorityProvider,
        FeatureCatalogueManager? featureCatalogueManager)
        : base(
            new S411PortrayalCatalogue(catalogueManager.GetProvider("S-411")),
            featureCatalogueManager?.GetDecoder("S-411"),
            fileName,
            authorityProvider,
            "S-411")
    {
        SetContentHash(DatasetContentHash.HashAndRewind(ref datasetStream));
        using (datasetStream)
        {
            _dataset = S411Dataset.Open(datasetStream);
        }

        SetDeclaredEdition(_dataset.DeclaredEdition);

        SetAxisOrder(_dataset.AxisOrder);
    }

    /// <inheritdoc />
    protected override IFeatureXmlSource CreateFeatureXmlSource() =>
        new S411FeatureXmlSource(_dataset);

    /// <inheritdoc />
    protected override ValidationReport? ValidateDataset()
    {
        if (!_validationCached)
        {
            _validationReport = ValidationRunner.Run(
                _dataset,
                static raw => S411SeaIceInventory.From(raw, out _),
                S411SeaIceRules.Default);
            _validationCached = true;
        }
        return _validationReport;
    }

    /// <summary>
    /// Suppresses rendering when the context is an S-411 render context whose
    /// time step is earlier than the dataset's issue date (the snapshot does
    /// not exist yet at that time).
    /// </summary>
    /// <param name="context">The current render context, if any.</param>
    /// <returns>
    /// An info string explaining why the dataset is hidden, or <see langword="null"/>
    /// to render normally.
    /// </returns>
    protected override string? GetSuppressionInfo(RenderContext? context)
    {
        if (context?.TimeStep is { } t
            && _dataset.IssueDate is { } issued
            && t < issued)
        {
            return $"S-411 Sea Ice — {FileName}\nHidden (snapshot at {issued:u} is after slider time {t:u})";
        }
        return null;
    }

    /// <summary>
    /// The ice key for the active display mode: one swatch per WMO colour of
    /// the total concentration or stage of development table
    /// (<see cref="S411IceColours"/>), labelled from the feature catalogue's
    /// definitions of its codes. The provisional navigational mode has no
    /// catalogue colours, so it has no legend.
    /// </summary>
    /// <param name="instructions">The drawing instructions just emitted (unused: the key shows the whole table).</param>
    /// <param name="legend">The legend builder over the catalogue's assets.</param>
    protected override IReadOnlyList<LegendEntry>? BuildLegend(
        IReadOnlyList<DrawingInstruction> instructions, CatalogueLegendBuilder legend) =>
        BuildIceLegend(Catalogue.DisplayModes.ActiveDisplayModeId, Decoder, legend);

    /// <inheritdoc />
    protected override string? LegendCaption => IceLegendCaption(Catalogue.DisplayModes.ActiveDisplayModeId);

    /// <summary>
    /// The S-411 key for <paramref name="modeId"/> (see <see cref="BuildLegend"/>),
    /// or <see langword="null"/> for the navigational mode.
    /// </summary>
    /// <param name="modeId">The active display mode id, or <see langword="null"/> for the default (concentration).</param>
    /// <param name="decoder">The S-411 feature catalogue, for the labels; codes are shown bare without it.</param>
    /// <param name="legend">The legend builder.</param>
    internal static IReadOnlyList<LegendEntry>? BuildIceLegend(
        string? modeId, FeatureCatalogueDecoder? decoder, CatalogueLegendBuilder legend)
    {
        var (table, attribute) = Table(modeId) ?? default;
        if (table is null)
            return null;

        return [.. table.Select(c => (LegendEntry)legend.Swatch(
            c.Color,
            S411IceColours.Label(c.Codes, code => decoder?.ResolveListedValueDefinition(attribute!, code.ToString(CultureInfo.InvariantCulture))),
            S411IceColours.FillTransparency))];
    }

    /// <summary>The caption naming the ice key's WMO table, or <see langword="null"/> for the navigational mode.</summary>
    /// <param name="modeId">The active display mode id, or <see langword="null"/> for the default.</param>
    internal static string? IceLegendCaption(string? modeId) =>
        Table(modeId) is { Attribute: var attribute }
            ? attribute == StageOfDevelopmentAttribute ? "Stage of development (WMO colour code)" : "Total concentration (WMO colour code)"
            : null;

    private const string ConcentrationAttribute = "totalConcentration";
    private const string StageOfDevelopmentAttribute = "iceStageofDevelopment";

    private static (IReadOnlyList<S411IceColours.ColourClass> Table, string Attribute)? Table(string? modeId)
    {
        if (S411DisplayModes.IsProvisional(modeId))
            return null;
        return string.Equals(modeId, S411DisplayModes.StageOfDevelopmentModeId, StringComparison.OrdinalIgnoreCase)
            ? (S411IceColours.StageOfDevelopment, StageOfDevelopmentAttribute)
            : (S411IceColours.Concentration, ConcentrationAttribute);
    }
}
