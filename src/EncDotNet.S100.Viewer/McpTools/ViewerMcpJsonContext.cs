using System.Text.Json;
using System.Text.Json.Serialization;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Source-generated metadata for the viewer's own MCP tool results, errors
/// and parameters (issue #764). <see cref="McpAdapterShared.Options"/> chains
/// it before the shared tools' metadata.
/// </summary>
// Results.
[JsonSerializable(typeof(SetViewportResult))]
[JsonSerializable(typeof(PickFeaturesResult))]
[JsonSerializable(typeof(AwaitRenderIdleResult))]
[JsonSerializable(typeof(GetRenderStatsResult))]
[JsonSerializable(typeof(SetOwnShipResult))]
[JsonSerializable(typeof(ListPanelsResult))]
[JsonSerializable(typeof(SetPanelResult))]
[JsonSerializable(typeof(CaptureAppScreenshotResult))]
[JsonSerializable(typeof(TimelineStateDto))]
[JsonSerializable(typeof(DatasetStateDto))]
[JsonSerializable(typeof(DatasetSelectionDto))]
[JsonSerializable(typeof(NotificationListDto))]
[JsonSerializable(typeof(DismissNotificationsDto))]
[JsonSerializable(typeof(TestClockDto))]
[JsonSerializable(typeof(LibrarySourcesDto))]
[JsonSerializable(typeof(LibraryItemPage))]
[JsonSerializable(typeof(LibraryItemDetail))]
[JsonSerializable(typeof(KnownSourcesDto))]
[JsonSerializable(typeof(AddSourceResult))]
[JsonSerializable(typeof(RefreshResult))]
[JsonSerializable(typeof(LibraryActionResult))]
[JsonSerializable(typeof(RemoveSourceResult))]
[JsonSerializable(typeof(LibraryIdleResult))]
[JsonSerializable(typeof(UiTreeSnapshot))]
[JsonSerializable(typeof(UiElementSnapshot))]
[JsonSerializable(typeof(RouteDetail))]
[JsonSerializable(typeof(ListRoutesResult))]
[JsonSerializable(typeof(DeleteRouteResult))]
// Errors, written by their concrete type.
[JsonSerializable(typeof(MapNotReady))]
[JsonSerializable(typeof(WindowNotReady))]
[JsonSerializable(typeof(UiNotReady))]
[JsonSerializable(typeof(PanelNotFound))]
[JsonSerializable(typeof(PanelUnavailable))]
[JsonSerializable(typeof(RouteNotFound))]
[JsonSerializable(typeof(LibrarySourceNotFound))]
[JsonSerializable(typeof(LibraryItemNotFound))]
[JsonSerializable(typeof(LibraryChangeRejected))]
[JsonSerializable(typeof(ViewTimeNotApplied))]
[JsonSerializable(typeof(NotificationNotFound))]
[JsonSerializable(typeof(UiElementNotFound))]
[JsonSerializable(typeof(UiElementAmbiguous))]
[JsonSerializable(typeof(UiElementDisabled))]
[JsonSerializable(typeof(UiActionNotSupported))]
// Parameter types the adapters bind, beyond the shared tools'.
[JsonSerializable(typeof(long?))]
[JsonSerializable(typeof(float?))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class ViewerMcpJsonContext : JsonSerializerContext;
