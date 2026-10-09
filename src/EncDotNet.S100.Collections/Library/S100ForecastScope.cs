using System.Globalization;
using EncDotNet.S100.Collections.Indexing;

namespace EncDotNet.S100.Collections.Library;

/// <summary>
/// A scope of a forecast feed (#685; NOAA's S-111 on AWS, handoff A3): one
/// list of models — water body, code, how often it runs, and the size of one
/// run — with a note on regional models that overlap smaller ones, and how
/// runs download (<see cref="Shapes"/>: tiles, or one file per model).
/// </summary>
public sealed class S100ForecastScope : LibraryCatalogueScope
{
    private const string TilesShape = "tiles";
    private const string RegionalShape = "regional";

    private readonly IReadOnlyList<ForecastModel> _models;
    private readonly Func<Uri, IReadOnlyList<ForecastModel>, CancellationToken, Task<IReadOnlyList<ForecastModelSummary>>> _load;
    private readonly TimeProvider _time;
    private IReadOnlyList<ForecastModelSummary>? _summaries;
    private IReadOnlyList<LibraryChoiceGroup> _groups = [];
    private LibraryResolution _selectedShape;

    /// <summary>Creates a scope of the feed at <paramref name="catalogUri"/>.</summary>
    /// <param name="catalogUri">The feed's URL.</param>
    /// <param name="models">The feed's models, as its known source lists them.</param>
    /// <param name="load">Reads each model's latest run.</param>
    /// <param name="time">The clock (whether the forecasts have ended); the system clock when null.</param>
    public S100ForecastScope(
        Uri catalogUri,
        IReadOnlyList<ForecastModel> models,
        Func<Uri, IReadOnlyList<ForecastModel>, CancellationToken, Task<IReadOnlyList<ForecastModelSummary>>> load,
        TimeProvider? time = null)
        : base(catalogUri)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(load);
        _models = models;
        _load = load;
        _time = time ?? TimeProvider.System;
        Shapes =
        [
            new(TilesShape, LibraryText.Get("Wizard_ForecastShapeTiles")),
            new(RegionalShape, LibraryText.Get("Wizard_ForecastShapeRegional")),
        ];
        _selectedShape = Shapes[0];
    }

    /// <inheritdoc />
    public override bool IsLoaded => _summaries is not null;

    /// <summary>One group: the models.</summary>
    public override IReadOnlyList<LibraryChoiceGroup> Groups => _groups;

    /// <summary>The feed's models.</summary>
    public IReadOnlyList<LibraryChoice> Models => _groups.Count > 0 ? _groups[0].Options : [];

    /// <summary>How runs download: tiles (the default), or one file per model.</summary>
    public IReadOnlyList<LibraryResolution> Shapes { get; }

    /// <summary>The chosen download shape; it applies to every model.</summary>
    public LibraryResolution SelectedShape
    {
        get => _selectedShape;
        set
        {
            if (value is null || value == _selectedShape)
                return;
            _selectedShape = value;
            RefreshDetails();
        }
    }

    /// <summary>True when there is a download shape to choose (more than one model; S-104's pilot has one, as tiles).</summary>
    public bool HasShapes => _summaries is { Count: > 1 };

    /// <summary>What the chosen shape means.</summary>
    public string ShapeHint => LibraryText.Get(IsRegional ? "Wizard_ForecastShapeRegionalHint" : "Wizard_ForecastShapeTilesHint");

    /// <summary>
    /// "Forecast ended 25.12.2025; no newer run published …" when every model's
    /// latest run has ended (handoff A4, the S-104 pilot); otherwise null.
    /// </summary>
    public string? ForecastEndedNote
    {
        get
        {
            if (_summaries is not { Count: > 0 } models || models.Any(m => m.Run is null))
                return null;

            var last = models.Max(m => m.Run!.Value.AddHours(m.Model.HorizonHours));
            return last > _time.GetUtcNow()
                ? null
                : string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Wizard_ForecastEndedFormat"),
                    last.UtcDateTime.ToString("d", CultureInfo.CurrentCulture));
        }
    }

    private bool IsRegional => _selectedShape.Value == RegionalShape;

    /// <inheritdoc />
    public override Task<string?> LoadAsync(CancellationToken cancellationToken = default) => LoadModelsAsync(cancellationToken);

    private async Task<string?> LoadModelsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ForecastModelSummary>? read = null;
        var error = await TryLoadAsync(async () => read = await _load(CatalogUri, _models, cancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
        if (error is not null || read is not { } summaries)
            return error;
        if (summaries is [{ Error: { } first }, ..] && summaries.All(m => m.Error is not null))
            return first;

        _groups =
        [
            new(LibraryText.Get("Wizard_ForecastModelsTitle"),
                [.. summaries.Select(s => new LibraryChoice(s.Model.Id, s.Model.Name, string.Empty) { Note = OverlapNote(s, summaries) })]),
        ];
        CatalogueDate = summaries.Max(m => m.Run) is { } latest ? DateOnly.FromDateTime(latest.UtcDateTime) : null;
        _summaries = summaries;
        RefreshDetails();
        return null;
    }

    /// <summary>"Overlaps cbofs, dbofs" for a model whose tiles cover most of other models' (a regional model).</summary>
    private static string? OverlapNote(ForecastModelSummary model, IReadOnlyList<ForecastModelSummary> all)
    {
        var covered = all.Where(model.Covers).Select(m => m.Model.Id).ToArray();
        return covered.Length == 0
            ? null
            : string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Wizard_OverlapsFormat"), string.Join(", ", covered));
    }

    /// <summary>"cbofs · every 6 h · 58 tiles · 25 MB" (or "· one file ·" per model) for each model.</summary>
    private void RefreshDetails()
    {
        if (_summaries is null)
            return;

        var c = CultureInfo.CurrentCulture;
        foreach (var option in Models)
        {
            var summary = _summaries.First(m => m.Model.Id == option.Value);
            if (summary.Error is not null)
            {
                option.Detail = string.Format(c, LibraryText.Get("Wizard_ModelUnavailableFormat"), summary.Model.Id);
                continue;
            }

            var cadence = summary.Model.CadenceHours >= 24
                ? LibraryText.Get("Wizard_CadenceDaily")
                : string.Format(c, LibraryText.Get("Wizard_CadenceHoursFormat"), summary.Model.CadenceHours);
            var shape = IsRegional
                ? LibraryText.Get("Wizard_OneFile")
                : string.Format(c, LibraryText.Get("Wizard_TilesFormat"), summary.TileCount);
            option.Detail = string.Join(" · ", new[] { summary.Model.Id, cadence, shape, BytesOf(summary) is { } b ? LibraryTextFormat.Bytes(b) : null }
                .OfType<string>());
        }
    }

    private long? BytesOf(ForecastModelSummary model) => IsRegional ? model.RegionalBytes : model.TileBytes;

    /// <summary>The models included: every readable one, or the ticked ones.</summary>
    private ForecastModelSummary[] Included() => [.. (_summaries ?? []).Where(m => m.Error is null
        && (IncludeAll || Models.Any(o => o.IsSelected && o.Value == m.Model.Id)))];

    /// <inheritdoc />
    public override string SelectionSummary
    {
        get
        {
            if (_summaries is null)
                return string.Empty;
            var selected = Included();
            return string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Wizard_ModelsPerRunFormat"),
                selected.Length, LibraryTextFormat.Bytes(selected.Sum(m => BytesOf(m) ?? 0)));
        }
    }

    /// <summary>"14 models · 494 MB per round of runs".</summary>
    public override string EverythingSummary
    {
        get
        {
            if (_summaries is null)
                return string.Empty;
            var models = _summaries.Where(m => m.Error is null).ToArray();
            return string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Wizard_ModelsPerRoundFormat"), models.Length,
                LibraryTextFormat.Bytes(models.Sum(m => BytesOf(m) ?? 0)));
        }
    }

    /// <summary>Runs are listed, not downloaded: the summary says so.</summary>
    public override string ScopeSummary => IncludeAll || SelectedCount > 0
        ? LibrarySourceText.NothingDownloads(SelectionSummary)
        : base.ScopeSummary;

    /// <inheritdoc />
    public override LibraryChoice? SingleEntry => _summaries is [var model]
        ? new LibraryChoice(
            model.Model.Id,
            model.Model.Name,
            string.Join(" · ", new[]
            {
                string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Wizard_TilesFormat"), model.TileCount),
                model.TileBytes is { } bytes ? LibraryTextFormat.Bytes(bytes) : null,
            }.OfType<string>()))
        : null;

    /// <summary>The ticked water bodies ("Chesapeake Bay, Tampa Bay"), or null for every model.</summary>
    public override string? DescribeSelection() =>
        IncludeAll ? null : LibrarySourceText.List([.. Models.Where(o => o.IsSelected).Select(o => o.Label)]);

    /// <summary>
    /// Ticks the models whose tiles reach into <paramref name="area"/> (an S-102
    /// area, handoff B8), leaving out a regional model when a smaller ticked one
    /// already covers the area's tiles.
    /// </summary>
    /// <param name="area">The area.</param>
    public void PreselectModelsCovering(GeoBounds area)
    {
        if (_summaries is null)
            return;

        var reaching = _summaries.Where(m => m.TileBounds.Any(t => t.Intersects(area))).ToArray();
        var chosen = reaching.Where(m => !reaching.Any(other => other != m && m.Covers(other))).ToArray();
        foreach (var option in Models)
            option.IsSelected = chosen.Any(m => m.Model.Id == option.Value);
    }

    /// <inheritdoc />
    public override CollectionSource Build(Guid id, string? name)
    {
        var chosen = _models.Where(m => IncludeAll || Models.Any(o => o.IsSelected && o.Value == m.Id)).ToArray();
        return new S100ForecastFeedSource(
            id, DescribeSelection() ?? name, CatalogUri, chosen, IsRegional ? ForecastShape.Regional : ForecastShape.Tiles);
    }
}
