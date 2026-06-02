namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Publishes lat/lon bounding boxes of the map's current viewport.
/// Implemented as a singleton in DI so feature-source code can
/// subscribe to viewport changes without taking a dependency on
/// Mapsui types or on the construction order of <c>MapControl</c>.
/// </summary>
/// <remarks>
/// <para>
/// The viewer's concrete implementation
/// (<see cref="MapViewportNotifier"/>) is constructed eagerly at app
/// startup and starts publishing once <c>MainWindow</c> binds it to
/// the live <c>Mapsui.Navigator</c>. Subscribers registered before
/// <c>Bind</c> simply see no events until then; this matches the
/// "gate is closed by default" semantics of
/// <see cref="DynamicSources.Ais.DeferredAisFeatureSource"/>.
/// </para>
/// <para>
/// Threading: <see cref="ViewportChanged"/> fires on whatever thread
/// raised the underlying Mapsui event — typically the UI thread.
/// Subscribers that need to do heavy work should marshal off the UI
/// thread themselves.
/// </para>
/// </remarks>
internal interface IMapViewportNotifier
{
    /// <summary>
    /// Most recent viewport snapshot, or <see langword="null"/> if
    /// the notifier hasn't been bound to a navigator yet.
    /// </summary>
    MapViewportSnapshot? Current { get; }

    /// <summary>
    /// Raised whenever the viewport changes. The <see cref="EventArgs"/>
    /// payload is the new snapshot in EPSG:4326.
    /// </summary>
    event EventHandler<MapViewportSnapshot>? ViewportChanged;
}
