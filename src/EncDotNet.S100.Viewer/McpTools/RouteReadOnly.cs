using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// The route is a received route (from an opened S-421 dataset) and is
/// read-only (#960). Its check settings can still be set with
/// <c>set_passage</c>; to edit anything else, make an editable copy with
/// <c>copy_route</c>.
/// </summary>
/// <param name="RouteId">The read-only route's id.</param>
[Description("Raised when a tool would edit a received, read-only route. Only its check settings (draftMeters, squatMeters, requiredUkcMeters, departure) can be set, with set_passage; make an editable copy with copy_route to change anything else.")]
internal sealed record RouteReadOnly(
    [property: Description("The read-only route's id.")] string RouteId)
    : ToolError("route_read_only", $"Route '{RouteId}' is a received route and is read-only. Make an editable copy with copy_route.");
