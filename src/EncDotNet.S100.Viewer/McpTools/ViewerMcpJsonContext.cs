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
[JsonSerializable(typeof(UiTreeSnapshot))]
[JsonSerializable(typeof(UiElementSnapshot))]
[JsonSerializable(typeof(RouteDetail))]
[JsonSerializable(typeof(ListRoutesResult))]
[JsonSerializable(typeof(DeleteRouteResult))]
[JsonSerializable(typeof(RouteEditModeResult))]
[JsonSerializable(typeof(PassageCardResult))]
[JsonSerializable(typeof(GetPassageResult))]
[JsonSerializable(typeof(ImportRouteResult))]
[JsonSerializable(typeof(CopyRouteResult))]
[JsonSerializable(typeof(ExportRouteResult))]
[JsonSerializable(typeof(CheckStateDto))]
[JsonSerializable(typeof(RouteFindingsDto))]
[JsonSerializable(typeof(MapLegendDto))]
[JsonSerializable(typeof(MeasureDto))]
[JsonSerializable(typeof(ClearanceStateDto))]
[JsonSerializable(typeof(NavigationalWarningsDto))]
[JsonSerializable(typeof(WarningCardsDto))]
[JsonSerializable(typeof(RecentLogsDto))]
[JsonSerializable(typeof(DisplayCombinationListDto))]
[JsonSerializable(typeof(SetDisplayCombinationDto))]
[JsonSerializable(typeof(DepthBasisDto))]
[JsonSerializable(typeof(RangeBearingDto))]
// Errors, written by their concrete type.
[JsonSerializable(typeof(MapNotReady))]
[JsonSerializable(typeof(WindowNotReady))]
[JsonSerializable(typeof(UiNotReady))]
[JsonSerializable(typeof(PanelNotFound))]
[JsonSerializable(typeof(PanelUnavailable))]
[JsonSerializable(typeof(RouteNotFound))]
[JsonSerializable(typeof(RouteReadOnly))]
[JsonSerializable(typeof(RouteImportFailed))]
[JsonSerializable(typeof(RouteHistoryEmpty))]
[JsonSerializable(typeof(ViewTimeNotApplied))]
[JsonSerializable(typeof(NotificationNotFound))]
[JsonSerializable(typeof(UiElementNotFound))]
[JsonSerializable(typeof(UiElementAmbiguous))]
[JsonSerializable(typeof(UiElementDisabled))]
[JsonSerializable(typeof(UiActionNotSupported))]
[JsonSerializable(typeof(DisplayCombinationNotFound))]
[JsonSerializable(typeof(NoActiveDisplayCombination))]
// Parameter types the adapters bind, beyond the shared tools'.
[JsonSerializable(typeof(long?))]
[JsonSerializable(typeof(float?))]
[JsonSerializable(typeof(double?))]
[JsonSerializable(typeof(bool?))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonElement?))]
internal sealed partial class ViewerMcpJsonContext : JsonSerializerContext;
