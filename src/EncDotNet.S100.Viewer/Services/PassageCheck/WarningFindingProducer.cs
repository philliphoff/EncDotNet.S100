using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;

namespace EncDotNet.S100.Viewer.Services.PassageCheck;

/// <summary>
/// The S-124 findings (#957): the navigational warnings the Navigational
/// warnings panel lists (<see cref="NavigationalWarningsService"/>) checked
/// against the route's corridor with <see cref="RouteWarningCheck"/>, judged
/// at the ETA where the vessel meets each one, or at the view time where the
/// legs have no speed. Status is judged with <see cref="NavigationalWarningsService.Set"/>,
/// so a part the panel shows as ended by a cancellation or an in-force
/// bulletin is cancelled here too (#992).
/// </summary>
/// <remarks>
/// It re-runs with the route, its departure and speeds (they are in the
/// request), and when the listed warnings change (<see cref="IRouteFindingInputs"/>).
/// Each finding refers to the warning part's feature, so the #976 Warning
/// card can open it, and carries the warning in <see cref="RouteFinding.Warning"/>.
/// <para>
/// A warning the mariner classified Off chart or Information is still
/// checked, since the classification only declutters the chart (S-98
/// §12.12.2; #996); its finding's meta line ends with the classification.
/// </para>
/// </remarks>
internal sealed class WarningFindingProducer : IRouteFindingProducer, IRouteFindingInputs
{
    private readonly NavigationalWarningsService _warnings;
    private readonly Func<RouteWarningOptions> _options;
    private IReadOnlyList<NavigationalWarningEntry> _seen;
    private string _classified;

    /// <param name="warnings">The listed navigational warnings.</param>
    /// <param name="options">The check's options (near distance, default corridor), read on each run; the defaults when <see langword="null"/>.</param>
    public WarningFindingProducer(NavigationalWarningsService warnings, Func<RouteWarningOptions>? options = null)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        _warnings = warnings;
        _options = options ?? (() => RouteWarningOptions.Default);
        _seen = warnings.Warnings;
        _classified = Classified();
        _warnings.Changed += OnWarningsChanged;
    }

    /// <inheritdoc/>
    public event EventHandler? InputsChanged;

    /// <inheritdoc/>
    public Task<IReadOnlyList<RouteFinding>> ProduceAsync(RouteFindingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var entries = _warnings.Warnings;
        if (entries.Count == 0 || request.Legs.Count == 0)
            return Task.FromResult<IReadOnlyList<RouteFinding>>([]);

        var warnings = entries
            .Where(e => e.Shape is not null || e.Warning.AffectedAreas.Count > 0)
            .Select(e => new RouteWarning(
                e.Warning,
                RouteWarningSources.Shapes(e.Warning, e.Shape),
                new RouteFindingFeature(e.DatasetId, e.FeatureRef)))
            .ToList();
        if (warnings.Count == 0)
            return Task.FromResult<IReadOnlyList<RouteFinding>>([]);

        var options = _options() with { Text = Text(request) };
        var result = new RouteWarningCheck(options).Check(
            request.Plan,
            request.Settings,
            new RouteWarningList(warnings, _warnings.Set),
            cancellationToken);
        return Task.FromResult<IReadOnlyList<RouteFinding>>([.. result.Findings.Select(Tag)]);
    }

    // "… · Off chart": the classification declutters the chart, not the check (#996).
    private RouteFinding Tag(RouteFinding finding)
    {
        if (finding.Feature is not { } feature || _warnings.Find(feature.DatasetId, feature.FeatureId) is not { } entry)
            return finding;
        var classification = _warnings.ClassificationOf(entry);
        return classification == NavigationalWarningClassification.OnChart
            ? finding
            : finding with { Meta = $"{finding.Meta} · {ViewModels.NavigationalWarningRowItem.NameOf(classification)}" };
    }

    // The warnings classified off the chart, so a change re-runs the check.
    private string Classified() => string.Join(
        '\u001f',
        _warnings.Warnings
            .Where(w => _warnings.ClassificationOf(w) != NavigationalWarningClassification.OnChart)
            .Select(w => $"{w.Key}={_warnings.ClassificationOf(w)}")
            .Distinct(StringComparer.Ordinal));

    /// <summary>The finding wording from <c>Strings.resx</c>, times in the Local/UTC preference.</summary>
    internal static RouteWarningText Text(RouteFindingRequest request) => new()
    {
        InsideArea = Strings.Findings_WarningInsideFormat,
        CrossesLine = Strings.Findings_WarningCrossesFormat,
        Near = Strings.Findings_WarningNearFormat,
        NotInForce = Strings.Findings_WarningNotInForceFormat,
        Cancelled = Strings.Findings_WarningCancelledFormat,
        InForceAt = Strings.Findings_WarningInForceAtFormat,
        NotInForceAt = Strings.Findings_WarningNotInForceAtFormat,
        CancelledStatus = Strings.Findings_WarningCancelled,
        NotInBulletin = Strings.Findings_WarningNotInBulletinFormat,
        NotInBulletinStatus = Strings.Findings_WarningNotInBulletin,
        ValidityRange = Strings.Findings_WarningRangeFormat,
        ValidityFrom = Strings.Pick_Warning_ValidFrom,
        ValidityUntil = Strings.Pick_Warning_ValidUntil,
        ValidityOpen = Strings.Pick_Warning_ValidUntilCancelled,
        FormatTime = time => TimeFormatting.Format(time, request.TimeFormat),
        FormatAt = time => PassageFormatting.FormatEta(time, request.Departure, request.TimeFormat),
        Culture = CultureInfo.CurrentCulture,
    };

    // Changed also fires for loading state and seen marks; re-check only when the list does.
    private void OnWarningsChanged(object? sender, EventArgs e)
    {
        var current = _warnings.Warnings;
        var classified = Classified();
        if (ReferenceEquals(current, _seen) && classified == _classified)
            return;
        _seen = current;
        _classified = classified;
        InputsChanged?.Invoke(this, EventArgs.Empty);
    }
}
