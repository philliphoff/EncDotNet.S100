using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Threading;
using EncDotNet.S100.Features;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Spec;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Keeps a map overlay in sync with the current pick so the user (and any
/// MCP agent driving the viewer) can see <em>where</em> the active pick
/// report refers to and <em>which</em> feature it describes — even after the
/// map is panned away from the click point.
/// </summary>
/// <remarks>
/// <para>
/// The controller observes <see cref="PickReportViewModel"/> (the single
/// source of truth for the current pick, set by both user clicks and the
/// MCP <c>pick_features</c> tool). On every relevant change it rebuilds a
/// <see cref="PickHighlightOverlayLayer"/> hosted on the map's overlay tier:
/// a cursor-echo marker at <see cref="PickReportViewModel.Location"/> plus,
/// when resolvable, an outline of the selected feature's geometry.
/// </para>
/// <para>
/// Feature geometry is resolved from the <see cref="IDatasetCatalog"/> — the
/// same loaded-dataset view the MCP feature-query tools use — keyed by the
/// hit's dataset display name (which equals the catalog
/// <see cref="DatasetId"/>) and feature reference (which equals the
/// <see cref="IS100Feature.Id"/>). Coverage picks and container features
/// without geometry resolve to a marker only, which is the correct degraded
/// behaviour.
/// </para>
/// <para>
/// Appearance combines the user's accent colour (from the shared
/// <see cref="IMeasureOverlayAppearanceProvider"/>, so the highlight tracks
/// accent changes exactly like the measure overlay) with the active chart
/// palette (from <see cref="SettingsViewModel"/>): the marker's white casing
/// is dimmed against a dark Dusk/Night basemap to avoid glare.
/// </para>
/// </remarks>
internal sealed class PickHighlightController : IDisposable
{
    private readonly IMapHost _mapHost;
    private readonly PickReportViewModel _pickReport;
    private readonly IDatasetCatalog _catalog;
    private readonly IMeasureOverlayAppearanceProvider _appearance;
    private readonly SettingsViewModel _settings;
    private readonly Action<Action> _marshal;
    private readonly MemoryLayer _layer;
    private bool _disposed;

    /// <summary>
    /// Creates and attaches the controller. The map host must already be
    /// initialised (basemap added) so the overlay lands above the basemap;
    /// layers added before initialisation are silently dropped by
    /// <see cref="MapsuiMapHost"/>.
    /// </summary>
    /// <param name="mapHost">Target map host.</param>
    /// <param name="pickReport">The pick-report view model to observe.</param>
    /// <param name="catalog">Loaded-dataset catalog used to resolve feature geometry.</param>
    /// <param name="appearance">Accent/theme provider for the highlight colours.</param>
    /// <param name="settings">
    /// Settings view-model supplying the active chart palette, which drives the
    /// marker's casing colour (dimmed against a dark Dusk/Night basemap).
    /// </param>
    /// <param name="marshal">
    /// Optional UI-thread marshalling override. Defaults to
    /// <see cref="Dispatcher.UIThread"/>; tests inject a synchronous
    /// implementation.
    /// </param>
    public PickHighlightController(
        IMapHost mapHost,
        PickReportViewModel pickReport,
        IDatasetCatalog catalog,
        IMeasureOverlayAppearanceProvider appearance,
        SettingsViewModel settings,
        Action<Action>? marshal = null)
    {
        ArgumentNullException.ThrowIfNull(mapHost);
        ArgumentNullException.ThrowIfNull(pickReport);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(settings);

        _mapHost = mapHost;
        _pickReport = pickReport;
        _catalog = catalog;
        _appearance = appearance;
        _settings = settings;
        _marshal = marshal ?? DispatcherMarshal;

        _layer = PickHighlightOverlayLayer.Create();

        _pickReport.PropertyChanged += OnPickReportChanged;
        _appearance.Changed += OnAppearanceChanged;
        _settings.PaletteChanged += OnPaletteChanged;

        _marshal(() =>
        {
            _mapHost.AddOverlayLayer(_layer);
            Rebuild();
        });
    }

    private void OnPickReportChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Location and SelectedHit drive the two halves of the overlay;
        // HasPick covers the Clear() transition (which nulls both).
        if (e.PropertyName is nameof(PickReportViewModel.Location)
            or nameof(PickReportViewModel.SelectedHit)
            or nameof(PickReportViewModel.HasPick))
        {
            _marshal(Rebuild);
        }
    }

    private void OnAppearanceChanged(object? sender, EventArgs e) => _marshal(Rebuild);

    private void OnPaletteChanged(PaletteType palette) => _marshal(Rebuild);

    private void Rebuild()
    {
        if (_disposed) return;

        (double Lat, double Lon)? location =
            _pickReport.Location is { } loc ? (loc.Latitude, loc.Longitude) : null;

        PickHighlightGeometry? geometry =
            _pickReport.SelectedHit is { } hit ? TryResolveGeometry(hit) : null;

        var state = new PickHighlightState(location, geometry);

        // The marker's casing dims against a dark chart basemap. That is the
        // chart *palette* (Dusk/Night), not the application chrome theme — a
        // light-chrome app can still display a Night chart.
        var darkBasemap = _settings.SelectedPalette is PaletteType.Dusk or PaletteType.Night;
        var appearance = new PickHighlightAppearance(_appearance.Current.Accent, darkBasemap);

        PickHighlightOverlayLayer.Update(_layer, state, appearance);
    }

    /// <summary>
    /// Resolves the picked feature's geometry from the dataset catalog, or
    /// <c>null</c> when the feature carries no resolvable geometry (coverage
    /// picks, container features, or a feature that is no longer loaded).
    /// </summary>
    private PickHighlightGeometry? TryResolveGeometry(PickHit hit)
    {
        var featureRef = hit.FeatureRef;
        if (string.IsNullOrEmpty(featureRef)) return null;

        var datasets = _catalog.Datasets;
        foreach (var dataset in datasets)
        {
            // When the hit names its dataset, scope the search to it (the
            // display name equals the catalog id); otherwise search all.
            if (!string.IsNullOrEmpty(hit.DatasetFileName)
                && !string.Equals(dataset.Id.Value, hit.DatasetFileName, StringComparison.Ordinal))
            {
                continue;
            }

            IEnumerable<IS100Feature>? features;
            try
            {
                features = FeatureAccessor.GetFeatures(dataset);
            }
            catch
            {
                // A dataset can be unloaded between the snapshot and the read;
                // treat any failure as "no geometry here".
                continue;
            }

            if (features is null) continue;

            foreach (var feature in features)
            {
                if (string.Equals(feature.Id, featureRef, StringComparison.Ordinal))
                {
                    return ToGeometry(feature);
                }
            }
        }

        return null;
    }

    private static PickHighlightGeometry ToGeometry(IS100Feature feature)
    {
        return new PickHighlightGeometry(
            ExteriorRing: ToList(feature.ExteriorRing),
            InteriorRings: ToRings(feature.InteriorRings),
            Curves: ToRings(feature.Curves),
            Points: ToList(feature.Points));
    }

    private static IReadOnlyList<(double Lat, double Lon)> ToList(
        IReadOnlyList<(double Latitude, double Longitude)> source)
    {
        if (source.Count == 0) return Array.Empty<(double, double)>();
        var list = new List<(double Lat, double Lon)>(source.Count);
        foreach (var (lat, lon) in source) list.Add((lat, lon));
        return list;
    }

    private static IReadOnlyList<IReadOnlyList<(double Lat, double Lon)>> ToRings(
        IReadOnlyList<IReadOnlyList<(double Latitude, double Longitude)>> source)
    {
        if (source.Count == 0) return Array.Empty<IReadOnlyList<(double, double)>>();
        var list = new List<IReadOnlyList<(double Lat, double Lon)>>(source.Count);
        foreach (var ring in source) list.Add(ToList(ring));
        return list;
    }

    private static void DispatcherMarshal(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    /// <summary>Detaches the overlay layer and unsubscribes. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _pickReport.PropertyChanged -= OnPickReportChanged;
        _appearance.Changed -= OnAppearanceChanged;
        _settings.PaletteChanged -= OnPaletteChanged;

        _marshal(() => _mapHost.RemoveOverlayLayer(_layer));
    }
}
