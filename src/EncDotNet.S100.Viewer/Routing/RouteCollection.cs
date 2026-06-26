using System;
using System.Collections.Generic;
using System.Linq;

namespace EncDotNet.S100.Viewer.Routing;

/// <summary>
/// An ordered set of named <see cref="Route"/>s with at most one designated
/// <em>active</em> route — the one the interactive editor and agent MCP
/// tools target by default. Mirrors how loaded datasets are managed as a
/// list with a current selection.
/// </summary>
/// <remarks>
/// The collection raises <see cref="Changed"/> when routes are added or
/// removed and when the active route changes, and re-raises each member
/// route's own <see cref="Route.Changed"/> so a single subscriber can drive
/// rendering and UI updates for the whole set.
/// </remarks>
public sealed class RouteCollection
{
    private readonly List<Route> _routes = new();
    private Route? _activeRoute;

    /// <summary>All routes, in insertion order.</summary>
    public IReadOnlyList<Route> Routes => _routes;

    /// <summary>
    /// The active route, or <c>null</c> when the collection is empty. Adding
    /// the first route makes it active; removing the active route promotes
    /// the previous route (or the new first route) to active.
    /// </summary>
    public Route? ActiveRoute => _activeRoute;

    /// <summary>
    /// Raised when the set of routes or the active route changes, or when
    /// any member route raises its own <see cref="Route.Changed"/>.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Creates a new route, adds it to the collection, and makes it active.
    /// </summary>
    /// <param name="name">Optional route name.</param>
    /// <param name="id">Optional stable id; generated when omitted. Must be
    /// unique within the collection.</param>
    /// <returns>The created route.</returns>
    /// <exception cref="ArgumentException">A route with the same
    /// <see cref="Route.Id"/> already exists.</exception>
    public Route CreateRoute(string? name = null, string? id = null)
    {
        var route = new Route(id, name);
        Add(route);
        return route;
    }

    /// <summary>
    /// Adds an existing route to the collection, making it active.
    /// </summary>
    /// <param name="route">The route to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="route"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">A route with the same
    /// <see cref="Route.Id"/> already exists.</exception>
    public void Add(Route route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (_routes.Any(r => r.Id == route.Id))
            throw new ArgumentException($"A route with id '{route.Id}' already exists.", nameof(route));

        _routes.Add(route);
        route.Changed += OnRouteChanged;
        _activeRoute = route;
        OnChanged();
    }

    /// <summary>
    /// Removes <paramref name="route"/> from the collection.
    /// </summary>
    /// <param name="route">The route to remove.</param>
    /// <returns><c>true</c> if the route was present and removed.</returns>
    public bool Remove(Route route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var position = _routes.IndexOf(route);
        if (position < 0)
            return false;

        _routes.RemoveAt(position);
        route.Changed -= OnRouteChanged;

        if (ReferenceEquals(_activeRoute, route))
        {
            // Promote the nearest remaining route: the one now at the removed
            // slot, else the new last route, else none.
            _activeRoute = _routes.Count == 0
                ? null
                : _routes[Math.Min(position, _routes.Count - 1)];
        }

        OnChanged();
        return true;
    }

    /// <summary>
    /// Sets the active route. Pass <c>null</c> to clear the selection.
    /// </summary>
    /// <param name="route">A route already in the collection, or <c>null</c>.</param>
    /// <returns><c>true</c> when the active route changed.</returns>
    /// <exception cref="ArgumentException"><paramref name="route"/> is not in
    /// this collection.</exception>
    public bool SetActiveRoute(Route? route)
    {
        if (route is not null && !_routes.Contains(route))
            throw new ArgumentException("Route is not part of this collection.", nameof(route));

        if (ReferenceEquals(_activeRoute, route))
            return false;

        _activeRoute = route;
        OnChanged();
        return true;
    }

    /// <summary>
    /// Finds a route by its <see cref="Route.Id"/>.
    /// </summary>
    /// <param name="id">The id to look up.</param>
    /// <returns>The matching route, or <c>null</c> when none matches.</returns>
    public Route? FindById(string id)
        => _routes.FirstOrDefault(r => r.Id == id);

    private void OnRouteChanged(object? sender, EventArgs e) => OnChanged();

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
