using EncDotNet.S100.Collections;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Notifications;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The Library's Open as route: opens a loaded S-421 item as a received route
/// and shows it in the Routes panel. With Import route…, the only way a
/// dataset joins the Routes panel.
/// </summary>
/// <remarks>Runs on the UI thread.</remarks>
internal sealed class LibraryRouteOpener
{
    private readonly ILibraryLoader _loader;
    private readonly ReceivedRoutesService _received;
    private readonly Action _showRoutes;
    private readonly INotificationService? _notifications;

    /// <summary>Creates the opener.</summary>
    /// <param name="loader">The Library loader, which knows the dataset each item opened as.</param>
    /// <param name="received">The received routes the dataset joins.</param>
    /// <param name="showRoutes">Shows the Routes panel.</param>
    /// <param name="notifications">Says why a dataset couldn't open as a route; none when <c>null</c>.</param>
    public LibraryRouteOpener(ILibraryLoader loader, ReceivedRoutesService received, Action showRoutes, INotificationService? notifications = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(received);
        ArgumentNullException.ThrowIfNull(showRoutes);
        _loader = loader;
        _received = received;
        _showRoutes = showRoutes;
        _notifications = notifications;
    }

    /// <summary>
    /// Opens <paramref name="item"/>'s dataset as a route, which becomes the
    /// active route, and shows the Routes panel; or says why it can't.
    /// </summary>
    /// <param name="item">A Library item, already loaded.</param>
    /// <returns>The route, or <c>null</c>.</returns>
    public Routing.Route? Open(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        // A dataset that failed to load has already said why.
        if (_loader.EntryOf(item) is not { IsLoaded: true } entry)
            return null;

        var (route, warning) = _received.Open(entry, FileOf(item));
        if (route is null)
        {
            _notifications?.Create(Strings.Library_OpenAsRoute)
                .WithSeverity(NotificationSeverity.Warning)
                .WithContent(warning ?? Strings.Import_NotARoute)
                .Show();
            return null;
        }

        _showRoutes();
        return route;
    }

    // The item's full path, which tells its route from another set's file of
    // the same name; inside a ZIP, the archive path and the entry within it.
    private static string? FileOf(CollectionItem item) => item.Location switch
    {
        LocalItemLocation { IsZip: true } zip => zip.RootPath + "/" + zip.RelativePath.Replace('\\', '/'),
        LocalItemLocation local => Path.Combine(local.RootPath, local.RelativePath),
        _ => null,
    };
}
