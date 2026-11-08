using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.S412;
using EncDotNet.S100.Datasets.S412.DataModel;
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
/// <para>
/// <b>Time.</b> A warning is drawn and picked only while it is in force at the
/// render context's <see cref="RenderContext.TimeStep"/>, by the rule S-124
/// navigational warnings and CAP weather warnings share
/// (<see cref="WarningValidity"/>): within its <c>fixedDateRange</c> and
/// before its <c>cancellationDate</c>. With no time step every warning is
/// drawn. As an <see cref="ITimeAwareDatasetProcessor"/> its steps are the
/// instants the set in force changes
/// (<see cref="S412MarineWeatherWarnings.Changes"/>), each held until the next
/// (<see cref="TimeStepSelection.AtOrBefore"/>), so a host's timeline redraws
/// it exactly when a warning starts or ends.
/// </para>
/// <para>
/// Normally created through the <see cref="S100Products.S412"/> registration
/// (used by <see cref="DatasetPipelineFactory"/>) rather than constructed
/// directly.
/// </para>
/// </remarks>
public sealed class S412DatasetProcessor : GmlDatasetProcessorBase<S412Feature>, ITimeAwareDatasetProcessor
{
    private static readonly IReadOnlySet<string> NoneHidden = new HashSet<string>(StringComparer.Ordinal);

    private readonly S412Dataset _dataset;
    private readonly S412MarineWeatherWarnings _warnings;
    private readonly TimeSemantics _timeSemantics;
    private IReadOnlySet<string> _undrawn = NoneHidden;
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

        _warnings = S412MarineWeatherWarnings.From(_dataset, out _);
        _timeSemantics = new TimeSemantics(TimeStepKind.Forecast, TimeStepSelection.AtOrBefore, LongestGap(_warnings));
    }

    /// <summary>The dataset's warnings, typed and timed.</summary>
    public S412MarineWeatherWarnings Warnings => _warnings;

    /// <inheritdoc />
    /// <remarks>The instants the set of warnings in force changes (<see cref="S412MarineWeatherWarnings.Changes"/>).</remarks>
    public IReadOnlyList<DateTime> AvailableTimes => _warnings.Changes;

    /// <inheritdoc />
    /// <remarks>
    /// Forecast steps, each held until the next; the last is held for the
    /// longest gap between steps, or ten years when a warning has no end.
    /// </remarks>
    public TimeSemantics TimeSemantics => _timeSemantics;

    /// <inheritdoc />
    /// <remarks>The latest issue date and time of the warnings.</remarks>
    public DateTime? IssueTime => _warnings.IssuedAt?.UtcDateTime;

    /// <inheritdoc />
    /// <remarks>The earliest <c>nextUpdateDateTime</c> of the warnings.</remarks>
    public DateTime? NextIssueTime => _warnings.NextUpdateDateTime?.UtcDateTime;

    /// <inheritdoc />
    protected override TimeCoverage? TimeCoverage => _warnings.TimeCoverage;

    /// <inheritdoc />
    protected override IFeatureXmlSource CreateFeatureXmlSource() =>
        new GmlFeatureXmlSource<S412Feature>(_dataset.Features);

    /// <summary>
    /// Leaves out the warnings not in force at the context's time step, so the
    /// portrayal rules see only those drawn.
    /// </summary>
    /// <param name="context">The render context, if any.</param>
    protected override IFeatureXmlSource CreateFeatureXmlSource(RenderContext? context)
    {
        var undrawn = UndrawnAt(context?.TimeStep);
        _undrawn = undrawn;
        return undrawn.Count == 0
            ? CreateFeatureXmlSource()
            : new GmlFeatureXmlSource<S412Feature>([.. _dataset.Features.Where(f => !undrawn.Contains(f.Id))]);
    }

    /// <summary>Draws nothing, with a status line, when no warning is in force at the time step.</summary>
    /// <param name="context">The render context, if any.</param>
    /// <returns>The status line, or <c>null</c> to render.</returns>
    protected override string? GetSuppressionInfo(RenderContext? context)
    {
        if (context?.TimeStep is not { } step || _dataset.Features.Count == 0)
            return null;

        var undrawn = UndrawnAt(step);
        if (undrawn.Count < _dataset.Features.Count)
            return null;

        _undrawn = undrawn;
        return $"S-412 Marine Weather Warnings — {FileName}\nHidden (no warning in force at {step:u})";
    }

    /// <inheritdoc />
    protected override bool IsPickable(S412Feature feature) => !_undrawn.Contains(feature.Id);

    /// <summary>The ids of the features a render at <paramref name="timeStep"/> leaves out.</summary>
    /// <param name="timeStep">The time step, or <c>null</c> to draw every warning.</param>
    internal IReadOnlySet<string> UndrawnAt(DateTime? timeStep)
    {
        if (timeStep is not { } step)
            return NoneHidden;

        var time = new DateTimeOffset(DateTime.SpecifyKind(step, DateTimeKind.Utc));
        var drawn = _warnings.DrawnAt(time).Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
        var undrawn = _dataset.Features.Where(f => !drawn.Contains(f.Id)).Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        return undrawn.Count == 0 ? NoneHidden : undrawn;
    }

    // How long the last step stays valid: the longest gap between steps, or
    // ten years while a warning has no end (as CAP warnings do).
    private static TimeSpan LongestGap(S412MarineWeatherWarnings warnings)
    {
        if (warnings.Warnings.Any(w => w.InForceUntil is null))
            return TimeSpan.FromDays(3650);
        var times = warnings.Changes;
        var gap = TimeSpan.FromDays(1);
        if (times.Count > 1)
        {
            gap = TimeSpan.FromMinutes(1);
            for (var i = 1; i < times.Count; i++)
            {
                var step = times[i] - times[i - 1];
                if (step > gap)
                    gap = step;
            }
        }

        return gap;
    }
}
