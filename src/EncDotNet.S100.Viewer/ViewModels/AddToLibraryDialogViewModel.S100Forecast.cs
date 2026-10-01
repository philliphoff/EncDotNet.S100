using System.Collections.ObjectModel;
using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The forecast-feed part of the "Add to Library" dialog (#685; NOAA's S-111
/// on AWS, handoff A3): one list of models — water body, code, how often it
/// runs, and the size of one run — with a note on regional models that
/// overlap smaller ones, and how runs download (tiles, or one file per model).
/// </summary>
internal sealed partial class AddToLibraryDialogViewModel
{
    private const string TilesShape = "tiles";
    private const string RegionalShape = "regional";

    private readonly Func<Uri, IReadOnlyList<ForecastModel>, CancellationToken, Task<IReadOnlyList<ForecastModelSummary>>>? _loadForecastModels;
    private IReadOnlyList<ForecastModelSummary>? _forecastModels;
    private ResolutionOptionViewModel? _selectedForecastShape;

    /// <summary>True when adding a forecast feed.</summary>
    public bool IsS100Forecast => _kind == AddToLibraryKind.S100Forecast;

    /// <summary>The forecast feed's models (the one facet).</summary>
    public ObservableCollection<FacetOptionViewModel> ForecastModels { get; } = [];

    /// <summary>How runs download: tiles (the default), or one file per model.</summary>
    public IReadOnlyList<ResolutionOptionViewModel> ForecastShapes { get; } =
    [
        new(TilesShape, Strings.Wizard_ForecastShapeTiles),
        new(RegionalShape, Strings.Wizard_ForecastShapeRegional),
    ];

    /// <summary>The chosen download shape; it applies to every model.</summary>
    public ResolutionOptionViewModel SelectedForecastShape
    {
        get => _selectedForecastShape ??= ForecastShapes[0];
        set
        {
            if (value is null || !SetProperty(ref _selectedForecastShape, value))
                return;

            OnPropertyChanged(nameof(ForecastShapeHint));
            RefreshForecastDetails();
            UpdateSelection();
            OnScopeChanged();
        }
    }

    /// <summary>What the chosen shape means.</summary>
    public string ForecastShapeHint => IsRegional ? Strings.Wizard_ForecastShapeRegionalHint : Strings.Wizard_ForecastShapeTilesHint;

    private bool IsRegional => SelectedForecastShape.Value == RegionalShape;

    private async Task LoadForecastModelsAsync(CancellationToken cancellationToken)
    {
        if (_loadForecastModels is null || _known is not { Models.Count: > 0 } known)
            return;

        IsLoading = true;
        LoadError = null;
        try
        {
            _forecastModels = await _loadForecastModels(CatalogUri, known.Models, cancellationToken).ConfigureAwait(true);
            if (_forecastModels.All(m => m.Error is not null))
            {
                LoadError = _forecastModels[0].Error;
                _forecastModels = null;
                return;
            }

            SetCatalogueDate(_forecastModels.Max(m => m.Run) is { } latest ? DateOnly.FromDateTime(latest.UtcDateTime) : null);
            foreach (var option in ForecastModels)
                option.PropertyChanged -= OnFacetChanged;
            ForecastModels.Clear();
            foreach (var summary in _forecastModels)
            {
                var option = new FacetOptionViewModel(summary.Model.Id, summary.Model.Name, string.Empty) { Note = OverlapNote(summary) };
                option.PropertyChanged += OnFacetChanged;
                ForecastModels.Add(option);
            }

            RefreshForecastDetails();
            UpdateSelection();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            LoadError = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>"Overlaps cbofs, dbofs" for a model whose tiles cover most of other models' (a regional model).</summary>
    private string? OverlapNote(ForecastModelSummary model)
    {
        var covered = (_forecastModels ?? []).Where(model.Covers).Select(m => m.Model.Id).ToArray();
        return covered.Length == 0 ? null : string.Format(CultureInfo.CurrentCulture, Strings.Wizard_OverlapsFormat, string.Join(", ", covered));
    }

    /// <summary>"cbofs · every 6 h · 58 tiles · 25 MB" (or "· one file ·" per model) for each model.</summary>
    private void RefreshForecastDetails()
    {
        if (_forecastModels is null)
            return;

        var c = CultureInfo.CurrentCulture;
        foreach (var option in ForecastModels)
        {
            var summary = _forecastModels.First(m => m.Model.Id == option.Value);
            if (summary.Error is not null)
            {
                option.Detail = string.Format(c, Strings.Wizard_ModelUnavailableFormat, summary.Model.Id);
                continue;
            }

            var cadence = summary.Model.CadenceHours >= 24
                ? Strings.Wizard_CadenceDaily
                : string.Format(c, Strings.Wizard_CadenceHoursFormat, summary.Model.CadenceHours);
            var shape = IsRegional
                ? Strings.Wizard_OneFile
                : string.Format(c, Strings.Wizard_TilesFormat, summary.TileCount);
            var bytes = IsRegional ? summary.RegionalBytes : summary.TileBytes;
            option.Detail = string.Join(" · ", new[] { summary.Model.Id, cadence, shape, bytes is { } b ? LibraryItemViewModel.FormatBytes(b) : null }
                .OfType<string>());
        }
    }

    private void UpdateForecastSelection()
    {
        if (_forecastModels is null)
            return;

        var selected = _forecastModels.Where(m => m.Error is null
            && (_includeAll || ForecastModels.Any(o => o.IsSelected && o.Value == m.Model.Id))).ToArray();
        var bytes = selected.Sum(m => (IsRegional ? m.RegionalBytes : m.TileBytes) ?? 0);
        SelectionSummary = string.Format(CultureInfo.CurrentCulture, Strings.Wizard_ModelsPerRunFormat,
            selected.Length, LibraryItemViewModel.FormatBytes(bytes));

        FollowSelectionInName(_includeAll || !HasSelection, () => DescribeForecastSelection()!);
    }

    /// <summary>"14 models · 494 MB per round of runs".</summary>
    private string ForecastEverythingSummary
    {
        get
        {
            var models = (_forecastModels ?? []).Where(m => m.Error is null).ToArray();
            return string.Format(CultureInfo.CurrentCulture, Strings.Wizard_ModelsPerRoundFormat, models.Length,
                LibraryItemViewModel.FormatBytes(models.Sum(m => (IsRegional ? m.RegionalBytes : m.TileBytes) ?? 0)));
        }
    }

    /// <summary>The ticked water bodies ("Chesapeake Bay, Tampa Bay"), or <see langword="null"/> for every model.</summary>
    private string? DescribeForecastSelection()
    {
        var names = _includeAll ? [] : ForecastModels.Where(o => o.IsSelected).Select(o => o.Label).ToArray();
        return names.Length switch
        {
            0 => null,
            <= 3 => string.Join(", ", names),
            _ => string.Format(CultureInfo.CurrentCulture, Strings.Library_AndMoreFormat, string.Join(", ", names.Take(2)), names.Length - 2),
        };
    }

    /// <summary>The source for the chosen models and shape.</summary>
    private S100ForecastFeedSource BuildForecastSource(Guid id)
    {
        var chosen = _known!.Models
            .Where(m => _includeAll || ForecastModels.Any(o => o.IsSelected && o.Value == m.Id))
            .ToArray();
        return new S100ForecastFeedSource(
            id, DescribeForecastSelection() ?? FeedName, CatalogUri, chosen, IsRegional ? ForecastShape.Regional : ForecastShape.Tiles);
    }
}
