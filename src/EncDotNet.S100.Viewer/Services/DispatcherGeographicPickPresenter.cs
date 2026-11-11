namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IGeographicPickPresenter"/>: marshals an MCP-driven
/// pick onto the UI thread and forwards it to <see cref="IPickService"/>,
/// which updates the shared <see cref="ViewModels.PickReportViewModel"/>
/// (and thereby the on-screen panel and pick highlight).
/// </summary>
internal sealed class DispatcherGeographicPickPresenter : IGeographicPickPresenter
{
    private readonly IPickService _pickService;
    private readonly Action<Action> _marshal;

    /// <summary>Creates a new presenter.</summary>
    /// <param name="pickService">Pick service that owns pick-report updates.</param>
    /// <param name="marshal">Runs a pick on the UI thread (the app passes <see cref="UiThread.RunOrPost"/>).</param>
    public DispatcherGeographicPickPresenter(IPickService pickService, Action<Action> marshal)
    {
        ArgumentNullException.ThrowIfNull(pickService);
        ArgumentNullException.ThrowIfNull(marshal);
        _pickService = pickService;
        _marshal = marshal;
    }

    /// <inheritdoc />
    public void Present(double latitude, double longitude, IReadOnlyList<GeographicPickFeature> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        _marshal(() => _pickService.PresentGeographicPick(latitude, longitude, features));
    }
}
