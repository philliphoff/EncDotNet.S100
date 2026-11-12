using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Legends;

namespace EncDotNet.S100.Viewer.ViewModels.Legends;

/// <summary>
/// The Legend tab's fixed first row (#1407, legends D17): the S-98 position
/// legend at the view centre. Lives in <see cref="LegendViewModel.PinnedRows"/>,
/// so it doesn't scroll or collapse. Call on the UI thread.
/// </summary>
internal sealed class PositionLegendRowViewModel : ViewModelBase, IDisposable
{
    private readonly PositionLegendService _service;

    /// <summary>Creates the row over the service.</summary>
    /// <param name="service">The position legend.</param>
    public PositionLegendRowViewModel(PositionLegendService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _service.Changed += OnChanged;
    }

    /// <summary>"Depths in metres · Mean Lower Low Water · Safety contour 10 m".</summary>
    public string SettingsLine => PositionLegendText.SettingsLine(_service.Current);

    /// <summary>The cell's name, drawn in the mono face; empty with no cell.</summary>
    public string CellName => _service.Current.Cell?.Name ?? string.Empty;

    /// <summary>Whether a chart cell covers the view centre.</summary>
    public bool HasCell => _service.Current.Cell is not null;

    /// <summary>
    /// What follows the name, " ed. 4 · compiled 1:12 000 · shown 1:22 000"
    /// (with its leading space), or "No chart at the view centre".
    /// </summary>
    public string CellDetail => HasCell
        ? " " + PositionLegendText.CellDetail(_service.Current)
        : PositionLegendText.CellDetail(_service.Current);

    /// <summary>Whether the display is finer than the cell's compilation scale.</summary>
    public bool IsOverscaled => _service.Current.OverscaleFactor is not null;

    /// <summary>The accessible name: the heading and both lines.</summary>
    public string AccessibleName =>
        Strings.PositionLegend_Name + ": " + SettingsLine + ". " + PositionLegendText.CellLine(_service.Current);

    /// <inheritdoc />
    public void Dispose() => _service.Changed -= OnChanged;

    private void OnChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
