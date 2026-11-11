using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// The route has no edit to undo (or redo). The route exists; its history is
/// simply empty in that direction.
/// </summary>
/// <param name="RouteId">The route whose history was stepped.</param>
/// <param name="Direction"><c>"undo"</c> or <c>"redo"</c>.</param>
[Description("Raised by undo_route_edit / redo_route_edit when the route has no edit to undo or redo.")]
internal sealed record RouteHistoryEmpty(
    [property: Description("The route whose history was stepped.")] string RouteId,
    [property: Description("\"undo\" or \"redo\".")] string Direction)
    : ToolError("route_history_empty", $"Route '{RouteId}' has nothing to {Direction}.");
