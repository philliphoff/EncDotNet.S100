using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// The Library's Open as route (#1394): opens an S-421 item, then opens its
/// dataset as the active route and shows the Routes panel. With Import
/// route…, the only way a dataset becomes a route.
/// </summary>
/// <remarks>Runs on the UI thread.</remarks>
internal sealed class OpenLibraryItemAsRouteWorkflow
{
    private readonly OpenLibraryItemsWorkflow _open;
    private readonly LibraryRouteOpener _routes;

    /// <summary>Creates the workflow.</summary>
    /// <param name="open">Opens the item.</param>
    /// <param name="routes">Opens the loaded dataset as a route.</param>
    public OpenLibraryItemAsRouteWorkflow(OpenLibraryItemsWorkflow open, LibraryRouteOpener routes)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(routes);
        _open = open;
        _routes = routes;
    }

    /// <summary>
    /// Opens <paramref name="item"/> and then its dataset as a route; or says
    /// why it can't.
    /// </summary>
    /// <param name="item">The item, as the Library shows it.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The route, or <see langword="null"/>.</returns>
    public async Task<Routing.Route?> RunAsync(LibraryItemState item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        // An explicit load, also for a dataset loaded as you pan, so the
        // route's dataset stays loaded wherever the map goes.
        await _open.RunAsync([item], defer: false, cancellationToken).ConfigureAwait(true);
        return _routes.Open(item.EffectiveItem);
    }
}
