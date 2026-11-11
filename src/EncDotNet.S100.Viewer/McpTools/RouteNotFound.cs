using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// The supplied route identifier does not match any route in the viewer's
/// editable route collection. Distinguished from
/// <see cref="InvalidArgument"/> in that the value is well-formed; it simply
/// does not resolve. Also raised when a tool defaults to the active route
/// but the collection is empty.
/// </summary>
/// <param name="RouteId">The route identifier that could not be resolved,
/// or <c>"(active)"</c> when the active route was requested but none
/// exists.</param>
[Description("Raised when the requested routeId does not match any route in the viewer's editable route collection (or when the active route was requested but the collection is empty).")]
internal sealed record RouteNotFound(
    [property: Description("The route identifier that could not be resolved, or \"(active)\" when the active route was requested but none exists.")] string RouteId)
    : ToolError("route_not_found", $"No route with id '{RouteId}' exists in the viewer's route collection.");
