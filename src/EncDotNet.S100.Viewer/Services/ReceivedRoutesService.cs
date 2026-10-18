using System.ComponentModel;
using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Routing.Persistence;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Turns S-421 datasets the user opens into received, read-only routes, and
/// keeps each route in step with its dataset (#960, #391; handoff decisions
/// 11, 15 and 16, items A10 and F4).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>An S-421 dataset opened explicitly (Datasets, Library <em>Load</em>,
/// drag and drop, Import route…) adds a received route in Native mode.
/// Datasets the Library loads as you pan (<see cref="DatasetEntry.IsDeferred"/>)
/// never do. A file that isn't a valid route stays a dataset, and its row
/// says why (<see cref="DatasetEntry.RouteWarning"/>).</item>
/// <item>Opening the same file, or another file with the same route ID and
/// edition, selects the existing route. A newer edition replaces the older
/// edition's route.</item>
/// <item>The route's display mode is the source of truth for its dataset's
/// visibility: Native shows the dataset, Planning hides it, except while the
/// route is checked, when Check route draws it in Planning mode and the
/// dataset stays on for its action points. Toggling the dataset's eye
/// switches the mode.</item>
/// <item>Closing the dataset removes the route; if it was being checked,
/// Check route and Follow passage end and a toast says why. Closing the
/// route closes its dataset.</item>
/// <item>The route's local check settings are kept in
/// <see cref="CheckSettingsStore"/> by route ID and edition.</item>
/// </list>
/// Everything runs on the UI thread, where the loader raises its events.
/// </remarks>
internal sealed class ReceivedRoutesService
{
    private readonly IDatasetLoaderService _loader;
    private readonly RoutesService _routes;
    private readonly CheckSettingsStore _checkSettings;
    private readonly Action<DatasetEntry>? _closeDataset;
    private readonly INotificationService? _notifications;
    private readonly FollowPassageService? _follow;
    private readonly Func<DatasetEntry, S421Dataset?> _read;
    private readonly Dictionary<Route, DatasetEntry> _datasets = new();
    private readonly HashSet<Route> _fileRoutes = [];
    private bool _syncing;

    /// <summary>Creates the service and starts watching the loader and the routes.</summary>
    /// <param name="loader">The dataset loader whose loads and closes to follow.</param>
    /// <param name="routes">The route collection received routes join.</param>
    /// <param name="checkSettings">Where check settings for received routes are kept; in memory when <c>null</c>.</param>
    /// <param name="closeDataset">Closes a dataset (removes it from the Datasets panel) when its route is closed.</param>
    /// <param name="notifications">Shows the "Check route ended" toast; none when <c>null</c>.</param>
    /// <param name="follow">Follow passage, stopped when a checked route's dataset closes.</param>
    /// <param name="read">Reads a loaded entry's S-421 dataset; from its processor (else its file) when omitted.</param>
    public ReceivedRoutesService(
        IDatasetLoaderService loader,
        RoutesService routes,
        CheckSettingsStore? checkSettings = null,
        Action<DatasetEntry>? closeDataset = null,
        INotificationService? notifications = null,
        FollowPassageService? follow = null,
        Func<DatasetEntry, S421Dataset?>? read = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(routes);
        _loader = loader;
        _routes = routes;
        _checkSettings = checkSettings ?? new CheckSettingsStore();
        _closeDataset = closeDataset;
        _notifications = notifications;
        _follow = follow;
        _read = read ?? ReadFromLoader;

        _loader.DatasetLoaded += OnDatasetLoaded;
        _loader.DatasetRemoved += OnDatasetRemoved;
        _routes.Routes.Changed += (_, _) => OnRoutesChanged();
        _routes.CheckModeChanged += (_, _) => SyncAllVisibility();
    }

    /// <summary>The dataset a received route belongs to, or <c>null</c>.</summary>
    /// <param name="route">A route.</param>
    /// <returns>The route's dataset entry.</returns>
    public DatasetEntry? DatasetOf(Route route)
        => _datasets.TryGetValue(route, out var entry) ? entry : null;

    /// <summary>The received route a dataset gave, or <c>null</c>.</summary>
    /// <param name="entry">A dataset entry.</param>
    /// <returns>The route.</returns>
    public Route? RouteOf(DatasetEntry entry)
        => _datasets.FirstOrDefault(p => ReferenceEquals(p.Value, entry)).Key;

    /// <summary>The received route read from <paramref name="filePath"/>, or <c>null</c>.</summary>
    /// <param name="filePath">The file opened.</param>
    /// <returns>The route for that file, or <c>null</c>.</returns>
    public Route? FindByFile(string filePath)
        => _routes.Routes.Routes.FirstOrDefault(r => r.Source is { } s && SamePath(s.FilePath, filePath));

    /// <summary>
    /// Whether the map should draw <paramref name="route"/> with the route
    /// renderer: in Planning mode, or while Check route is on for it.
    /// </summary>
    /// <param name="routes">The route service.</param>
    /// <param name="route">The route.</param>
    /// <returns><c>true</c> when the route renderer draws it.</returns>
    public static bool DrawsWithRouteRenderer(RoutesService routes, Route route)
        => route.DisplayMode == RouteDisplayMode.Planning || IsChecked(routes, route);

    private static bool IsChecked(RoutesService routes, Route route)
        => routes.IsCheckModeActive && ReferenceEquals(routes.Routes.ActiveRoute, route);

    private void OnDatasetLoaded(DatasetEntry entry)
    {
        if (!string.Equals(entry.ProductSpec, "S-421", StringComparison.OrdinalIgnoreCase) || entry.IsDeferred)
            return;
        if (RouteOf(entry) is { } own)
        {
            // A reload of the same entry (a re-render): keep the route.
            SyncVisibility(own);
            return;
        }

        if (FindByFile(entry.FilePath) is { } sameFile)
        {
            _routes.Routes.SetActiveRoute(sameFile);
            return;
        }

        S421Route s421;
        try
        {
            if (_read(entry) is not { } dataset)
                return;
            s421 = S421RoutePlan.From(dataset, out _).Route;
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or System.Xml.XmlException or FormatException)
        {
            entry.RouteWarning = Strings.Received_NotARoute;
            return;
        }

        if (s421.Waypoints.Count < 2)
        {
            entry.RouteWarning = Strings.Received_TooFewWaypoints;
            return;
        }

        var received = RouteS421Mapping.ToReceived(s421, entry.FilePath);
        var (route, warning) = Admit(received, entry);
        entry.RouteWarning = warning;
        if (route is not null && ReferenceEquals(route, received))
            entry.PropertyChanged += OnEntryPropertyChanged;
    }

    /// <summary>
    /// Adds a received route read straight from a file with no dataset (a
    /// format without its own portrayal, such as RTZ in #961), applying the
    /// same duplicate and edition rules as a dataset's route.
    /// </summary>
    /// <param name="route">The received route.</param>
    /// <returns>The route now in the list for that file (this one, or the existing one it duplicates), or <c>null</c> with why.</returns>
    public (Route? Route, string? Warning) Receive(Route route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (!route.IsReadOnly)
            throw new ArgumentException("Only a received route can be received.", nameof(route));
        if (FindByFile(route.Source!.FilePath) is { } sameFile)
        {
            _routes.Routes.SetActiveRoute(sameFile);
            return (sameFile, null);
        }
        return Admit(route, null);
    }

    // The duplicate rules (F4): the same route ID and edition selects the
    // route already open; a newer edition replaces it; an older one is
    // refused. Otherwise the route joins the list with its kept settings.
    private (Route? Route, string? Warning) Admit(Route route, DatasetEntry? entry)
    {
        var source = route.Source!;
        if (source.RouteId is { Length: > 0 } routeId
            && _routes.Routes.Routes.FirstOrDefault(r => !ReferenceEquals(r, route) && r.Source is { } s && s.RouteId == routeId) is { Source: { } existingSource } existing)
        {
            var edition = source.Edition ?? 0;
            var existingEdition = existingSource.Edition ?? 0;
            if (edition == existingEdition)
            {
                _routes.Routes.SetActiveRoute(existing);
                return (existing, null);
            }
            if (edition < existingEdition)
                return (null, string.Format(CultureInfo.CurrentCulture, Strings.Received_OlderEditionFormat, existingEdition));

            // A newer edition replaces the older one's route; the older
            // dataset stays open as a dataset.
            if (_datasets.TryGetValue(existing, out var olderEntry))
                olderEntry.PropertyChanged -= OnEntryPropertyChanged;
            _datasets.Remove(existing);
            _fileRoutes.Remove(existing);
            EndCheckIfChecked(existing, announce: false);
            _routes.Routes.Remove(existing);
        }

        if (_checkSettings.Get(source.CheckSettingsKey) is { } kept)
            route.ApplyLocalCheckSettings(kept);
        if (entry is not null)
            _datasets[route] = entry;
        else
            _fileRoutes.Add(route);
        _routes.Routes.Add(route);
        SyncVisibility(route);
        return (route, null);
    }

    private void OnDatasetRemoved(DatasetEntry entry)
    {
        if (RouteOf(entry) is not { } route)
            return;
        entry.PropertyChanged -= OnEntryPropertyChanged;
        _datasets.Remove(route);
        EndCheckIfChecked(route, announce: true);
        _routes.Routes.Remove(route);
    }

    // A10: closing a checked route's dataset ends Check route and Follow
    // passage (the findings clear with the check) and says why.
    private void EndCheckIfChecked(Route route, bool announce)
    {
        if (!IsChecked(_routes, route))
            return;
        _follow?.SetOn(false);
        _routes.IsCheckModeActive = false;
        if (announce && _notifications is not null)
        {
            _notifications.Create(Strings.Toast_Info)
                .WithSeverity(NotificationSeverity.Info)
                .WithContent(string.Format(CultureInfo.CurrentCulture, Strings.Toast_CheckRouteEndedFormat, route.Name))
                .AutoDismiss(TimeSpan.FromSeconds(6))
                .Show();
        }
    }

    private void OnRoutesChanged()
    {
        // A received route closed from the Routes panel (or delete_route)
        // closes its dataset.
        foreach (var (route, entry) in _datasets.Where(p => !_routes.Routes.Routes.Contains(p.Key)).ToList())
        {
            entry.PropertyChanged -= OnEntryPropertyChanged;
            _datasets.Remove(route);
            _closeDataset?.Invoke(entry);
        }

        _fileRoutes.RemoveWhere(r => !_routes.Routes.Routes.Contains(r));
        var saved = false;
        foreach (var route in _datasets.Keys.Concat(_fileRoutes))
        {
            saved |= _checkSettings.Set(route.Source!.CheckSettingsKey, route.LocalCheckSettings);
            SyncVisibility(route);
        }
        if (saved)
            _checkSettings.Save();
    }

    private void SyncAllVisibility()
    {
        foreach (var route in _datasets.Keys)
            SyncVisibility(route);
    }

    private void SyncVisibility(Route route)
    {
        if (!_datasets.TryGetValue(route, out var entry))
            return;
        var visible = route.DisplayMode == RouteDisplayMode.Native || IsChecked(_routes, route);
        if (entry.IsVisible == visible)
            return;
        _syncing = true;
        try
        {
            entry.IsVisible = visible;
        }
        finally
        {
            _syncing = false;
        }
    }

    // The dataset's eye follows the route's mode; toggling the eye sets it.
    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncing || e.PropertyName != nameof(DatasetEntry.IsVisible) || sender is not DatasetEntry entry)
            return;
        if (RouteOf(entry) is not { } route || IsChecked(_routes, route))
            return;
        route.SetDisplayMode(entry.IsVisible ? RouteDisplayMode.Native : RouteDisplayMode.Planning);
    }

    private S421Dataset? ReadFromLoader(DatasetEntry entry)
    {
        using (var processors = _loader.AcquireProcessors())
        {
            if (processors.TryGetValue(entry, out var processor)
                && processor is ILoadedDatasetProjection projection
                && projection.CreateLoadedData() is S421DatasetData data)
            {
                return data.Model;
            }
        }
        return File.Exists(entry.FilePath) ? S421Dataset.Open(entry.FilePath) : null;
    }

    private static bool SamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
}
