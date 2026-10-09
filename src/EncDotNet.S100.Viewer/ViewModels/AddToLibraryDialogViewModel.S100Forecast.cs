using System.Collections.ObjectModel;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The forecast-feed part of the "Add to Library" dialog (#685; NOAA's S-111
/// on AWS, handoff A3), over the core's <see cref="S100ForecastScope"/>: one
/// list of models, and how runs download (tiles, or one file per model).
/// </summary>
internal sealed partial class AddToLibraryDialogViewModel
{
    private readonly Func<Uri, IReadOnlyList<ForecastModel>, CancellationToken, Task<IReadOnlyList<ForecastModelSummary>>>? _loadForecastModels;

    private S100ForecastScope? ForecastScope => _scope as S100ForecastScope;

    /// <summary>True when adding a forecast feed.</summary>
    public bool IsS100Forecast => _kind == LibrarySourceKind.S100Forecast;

    /// <summary>The forecast feed's models (the one facet).</summary>
    public ObservableCollection<LibraryChoice> ForecastModels { get; } = [];

    /// <summary>How runs download: tiles (the default), or one file per model.</summary>
    public IReadOnlyList<LibraryResolution> ForecastShapes => ForecastScope?.Shapes ?? [];

    /// <summary>The chosen download shape; it applies to every model.</summary>
    public LibraryResolution? SelectedForecastShape
    {
        get => ForecastScope?.SelectedShape;
        set
        {
            if (value is null || ForecastScope is not { } scope || value == scope.SelectedShape)
                return;

            scope.SelectedShape = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ForecastShapeHint));
            UpdateSelection();
            OnScopeChanged();
        }
    }

    /// <summary>True when there is a download shape to choose (more than one model; S-104's pilot has one, as tiles).</summary>
    public bool HasForecastShapes => ForecastScope?.HasShapes == true;

    /// <summary>"Forecast ended 25.12.2025; no newer run published …" when every model's latest run has ended (handoff A4).</summary>
    public string? ForecastEndedNote => ForecastScope?.ForecastEndedNote;

    /// <summary>True when <see cref="ForecastEndedNote"/> is shown.</summary>
    public bool HasForecastEndedNote => ForecastEndedNote is not null;

    /// <summary>What the chosen shape means.</summary>
    public string ForecastShapeHint => ForecastScope?.ShapeHint ?? string.Empty;

    /// <summary>Ticks the models whose tiles reach into <paramref name="area"/> (an S-102 area, handoff B8).</summary>
    public void PreselectModelsCovering(GeoBounds area) => ForecastScope?.PreselectModelsCovering(area);
}
