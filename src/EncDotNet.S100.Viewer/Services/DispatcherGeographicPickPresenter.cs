using EncDotNet.S100.Viewer.Services.DynamicSources;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IGeographicPickPresenter"/>: marshals an MCP-driven
/// pick onto the UI thread and forwards it to <see cref="IPickService"/>,
/// which updates the shared <see cref="ViewModels.PickReportViewModel"/>
/// (and thereby the on-screen panel and pick highlight). Dynamic-source
/// features at the point (a predicted tide station, an AIS target) are
/// hit-tested at the live map resolution, as a click is.
/// </summary>
internal sealed class DispatcherGeographicPickPresenter : IGeographicPickPresenter
{
    private readonly IPickService _pickService;
    private readonly Action<Action> _marshal;
    private readonly IDynamicSourcePickService? _dynamicPicks;
    private readonly Func<double?> _resolution;

    /// <summary>Creates the presenter.</summary>
    /// <param name="pickService">The pick service it forwards to.</param>
    /// <param name="marshal">Runs an action on the UI thread.</param>
    /// <param name="dynamicPicks">Hit-tests the dynamic sources; null for none.</param>
    /// <param name="resolution">The live map's Web Mercator resolution, or null when unknown.</param>
    public DispatcherGeographicPickPresenter(
        IPickService pickService,
        Action<Action> marshal,
        IDynamicSourcePickService? dynamicPicks,
        Func<double?> resolution)
    {
        ArgumentNullException.ThrowIfNull(pickService);
        ArgumentNullException.ThrowIfNull(marshal);
        ArgumentNullException.ThrowIfNull(resolution);
        _pickService = pickService;
        _marshal = marshal;
        _dynamicPicks = dynamicPicks;
        _resolution = resolution;
    }

    /// <inheritdoc />
    public IReadOnlyList<DynamicPickHit> Present(double latitude, double longitude, IReadOnlyList<GeographicPickFeature> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        IReadOnlyList<DynamicPickHit> dynamicHits = [];
        if (_dynamicPicks is not null && _resolution() is { } resolution and > 0)
        {
            var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(longitude, latitude);
            var point = new Mapsui.MPoint(x, y);
            var picks = _dynamicPicks;
            // Hit-test and build the cards on the UI thread, as a click does.
            dynamicHits = Avalonia.Threading.Dispatcher.UIThread.CheckAccess()
                ? picks.Pick(point, resolution)
                : Avalonia.Threading.Dispatcher.UIThread.Invoke(() => picks.Pick(point, resolution));
        }

        if (features.Count > 0 || dynamicHits.Count > 0)
        {
            _marshal(() => _pickService.PresentGeographicPick(latitude, longitude, features, dynamicHits));
        }

        return dynamicHits;
    }
}
