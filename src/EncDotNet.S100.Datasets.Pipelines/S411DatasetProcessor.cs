using System.Collections.Immutable;
using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.S411;
using EncDotNet.S100.Datasets.S411.DataModel;
using EncDotNet.S100.Datasets.S411.Validation;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Validation;


namespace EncDotNet.S100.Datasets.Pipelines;

public sealed class S411DatasetProcessor : GmlDatasetProcessorBase<S411Feature>
{
    private readonly S411Dataset _dataset;
    private ValidationReport? _validationReport;
    private bool _validationCached;
    protected override string ProductDescription => "Sea Ice";
    protected override IReadOnlyList<S411Feature> Features => _dataset.Features;

    /// <summary>
    /// Time samples this dataset can be rendered at. S-411 datasets are
    /// snapshot-per-file; this is either a single-element list with the
    /// dataset's <see cref="S411Dataset.IssueDate"/> or empty when the
    /// source GML carried no recognised timestamp.
    /// </summary>
    public IReadOnlyList<DateTime> AvailableTimes =>
        _dataset.IssueDate is { } dt ? [dt] : Array.Empty<DateTime>();

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
        using (datasetStream)
        {
            _dataset = S411Dataset.Open(datasetStream);
        }

        SetDeclaredEdition(_dataset.DeclaredEdition);
    }

    protected override IFeatureXmlSource CreateFeatureXmlSource() =>
        new S411FeatureXmlSource(_dataset);

    /// <summary>
    /// Feature-type element names (JCOMM short codes and IHO PascalCase) whose
    /// Feature Catalogue class carries the WMO egg code (S-411 Edition 1.2.1
    /// Annex A — sea ice and lake ice).
    /// </summary>
    private static readonly HashSet<string> EggCodeFeatureTypes =
        new(StringComparer.OrdinalIgnoreCase) { "seaice", "SeaIce", "lacice", "LakeIce" };

    /// <summary>
    /// Projects an S-411 sea-ice / lake-ice feature's WMO concentration, stage
    /// of development, and form-of-ice attributes into an <see cref="IceEggCode"/>
    /// for the pick report. Other feature classes have no egg code.
    /// </summary>
    protected override IceEggCode? BuildEggCode(S411Feature feature)
    {
        if (!EggCodeFeatureTypes.Contains(feature.FeatureType))
            return null;

        var attributes = feature.Attributes;
        double? snowDepth = null;
        if (attributes.TryGetValue("snowDepth", out var snowRaw)
            && double.TryParse(snowRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var snow))
        {
            snowDepth = snow;
        }

        string? totalConcentration = null;
        var totalConcentrationSourceCode = "iceact";
        foreach (var code in new[] { "iceact", "totalConcentration" })
        {
            // Skip empty source values so canonical totalConcentration can fill
            // Ct when a producer emits an empty JCOMM iceact element.
            if (attributes.TryGetValue(code, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                totalConcentration = value;
                totalConcentrationSourceCode = code;
                break;
            }
        }

        var egg = IceEggCodeBuilder.Build(
            totalConcentration,
            attributes.GetValueOrDefault("iceapc"),
            attributes.GetValueOrDefault("icesod"),
            attributes.GetValueOrDefault("iceflz"),
            snowDepth,
            totalConcentrationSourceCode);

        return egg is null ? null : EnrichWithDefinitions(egg);
    }

    /// <summary>
    /// JCOMM short and canonical attribute codes (as carried on <see cref="IceEggValue.SourceCode"/>)
    /// mapped to their S-411 Feature Catalogue simple-attribute codes, so an
    /// egg value's numeric SIGRID-3 code can be resolved to its prose meaning.
    /// </summary>
    private static readonly Dictionary<string, string> EggAttributeCatalogueCodes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["iceact"] = "totalConcentration",
            ["totalConcentration"] = "totalConcentration",
            ["iceapc"] = "partialConcentration",
            ["icesod"] = "iceStageofDevelopment",
            ["iceflz"] = "floeSizes",
        };

    /// <summary>
    /// Resolves each egg value's enumerated definition from the Feature
    /// Catalogue (when a decoder is available) so the pick report can show the
    /// prose meaning (e.g. <c>"Grey Ice"</c>) on hover.
    /// </summary>
    private IceEggCode EnrichWithDefinitions(IceEggCode egg)
    {
        if (Decoder is not { } decoder)
            return egg;

        IceEggValue? Define(IceEggValue? value)
        {
            if (value is null || value.SourceCode is not { } source
                || !EggAttributeCatalogueCodes.TryGetValue(source, out var catalogueCode))
                return value;
            var definition = decoder.ResolveListedValueDefinition(catalogueCode, value.Text);
            return definition is null ? value : value with { Definition = definition };
        }

        ImmutableArray<IceEggValue> DefineRow(ImmutableArray<IceEggValue> row)
        {
            if (row.IsDefaultOrEmpty)
                return row;
            var builder = ImmutableArray.CreateBuilder<IceEggValue>(row.Length);
            foreach (var value in row)
                builder.Add(Define(value)!);
            return builder.ToImmutable();
        }

        return egg with
        {
            TotalConcentration = Define(egg.TotalConcentration),
            PartialConcentrations = DefineRow(egg.PartialConcentrations),
            StagesOfDevelopment = DefineRow(egg.StagesOfDevelopment),
            FormsOfIce = DefineRow(egg.FormsOfIce),
            TrailingPartialConcentrations = DefineRow(egg.TrailingPartialConcentrations),
            TrailingStagesOfDevelopment = DefineRow(egg.TrailingStagesOfDevelopment),
            TrailingFormsOfIce = DefineRow(egg.TrailingFormsOfIce),
            Annotations = DefineRow(egg.Annotations),
        };
    }

    /// <inheritdoc />
    public override ValidationReport? Validate()
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

    protected override string? GetSuppressionInfo(RenderContext? context)
    {
        if (context is S411RenderContext { TimeStep: { } t }
            && _dataset.IssueDate is { } issued
            && t < issued)
        {
            return $"S-411 Sea Ice — {FileName}\nHidden (snapshot at {issued:u} is after slider time {t:u})";
        }
        return null;
    }
}
