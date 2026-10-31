using System.ComponentModel;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Services.Depth;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

// Viewer-only tool that reports what the depths on the chart are relative to
// (#1191, docs/design/depth-basis-indicator.md §2.6).

/// <summary>A dataset that supplies depths or water level in the view.</summary>
[Description("A dataset supplying depths or water level in the map view.")]
internal sealed record DepthBasisSourceDto(
    [property: Description("The dataset's name, as the Datasets panel shows it.")] string Id,
    [property: Description("The product: S-101, S-401, S-57, S-102 or S-104.")] string Spec,
    [property: Description("The vertical datum as an S-100 vertical datum code; null when the dataset doesn't declare one.")] int? VerticalDatum,
    [property: Description("The vertical datum's name, e.g. \"Mean Lower Low Water\"; null when not declared.")] string? VerticalDatumLabel,
    [property: Description("S-104 only: 'grid' or 'station'.")] string? Kind = null,
    [property: Description("S-104 only: the first time step (UTC).")] DateTime? Start = null,
    [property: Description("S-104 only: the last time step (UTC).")] DateTime? End = null,
    [property: Description("S-104 only: the run (issue time, UTC).")] DateTime? Run = null);

/// <summary>What the depths on the chart are relative to.</summary>
[Description("What the depths and safety contour drawn on the chart are relative to: the status bar's DEPTHS indicator.")]
internal sealed record DepthBasisDto(
    [property: Description("'none' (no depth-bearing dataset loaded and active: the indicator is hidden), 'charted' (charted depths, relative to each dataset's vertical datum) or 'adjusted' (charted depth + S-104 water level).")] string Kind,
    [property: Description("Why the chart shows charted depths when water level exists: 'not_applied' (S-104 meets the view, chart adjustment is off) or 'water_level' (adjustment is on but unavailable: see waterLevelStatus); null otherwise.")] string? Reason,
    [property: Description("Set when reason is water_level or kind is adjusted: applied, no_coverage, before_start, after_end, no_data or datum_mismatch, as check_route reports them.")] string? WaterLevelStatus,
    [property: Description("The Timeline's view time the basis was evaluated at (UTC).")] DateTimeOffset? ViewTime,
    [property: Description("The S-104 time step the adjusted chart uses; null unless adjusted.")] DateTime? WaterLevelStepTime,
    [property: Description("False when the adjusted area doesn't cover every charted depth in view.")] bool CoversWholeView,
    [property: Description("Whether the depth and water-level sources in view share a vertical datum: same, different or unknown; null unless both are in view.")] string? Datum,
    [property: Description("The indicator's value exactly as the user reads it at full width, e.g. \"Charted · water level not applied\"; empty when hidden.")] string Display,
    [property: Description("The depth-bearing datasets in the view (S-102 surfaces first).")] IReadOnlyList<DepthBasisSourceDto> DepthSources,
    [property: Description("The active S-104 datasets meeting the view (a station series counts within the route check's station distance).")] IReadOnlyList<DepthBasisSourceDto> WaterLevelSources);

/// <summary>Reads the depth basis indicator (MCP <c>get_depth_basis</c>).</summary>
internal sealed class GetDepthBasisTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "get_depth_basis";

    private readonly DepthBasisService _service;
    private readonly IRouteEditInvoker _invoker;

    public GetDepthBasisTool(DepthBasisService service, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(invoker);
        _service = service;
        _invoker = invoker;
    }

    /// <summary>Recomputes the basis on the UI thread and returns it.</summary>
    public async Task<ToolResult<DepthBasisDto>> InvokeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var dto = await _invoker.InvokeAsync(() =>
        {
            _service.Refresh();
            return ToDto(_service.Current);
        }).ConfigureAwait(false);
        return ToolResult<DepthBasisDto>.Ok(dto);
    }

    /// <summary>The wire shape of a basis.</summary>
    internal static DepthBasisDto ToDto(DepthBasis basis)
    {
        ArgumentNullException.ThrowIfNull(basis);
        return new DepthBasisDto(
            Kind(basis.Kind),
            basis.Reason switch
            {
                DepthBasisReason.NotApplied => "not_applied",
                DepthBasisReason.WaterLevel => "water_level",
                _ => null,
            },
            basis.WaterLevelStatus is { } status ? Status(status) : null,
            basis.ViewTime?.ToUniversalTime(),
            basis.WaterLevelStepTime,
            basis.CoversWholeView,
            basis.Datum is { } datum ? Datum(datum) : null,
            DepthBasisText.Display(basis),
            [.. basis.DepthSources.Select(Source)],
            [.. basis.WaterLevelSources.Select(Source)]);
    }

    internal static string Kind(DepthBasisKind kind) => kind switch
    {
        DepthBasisKind.Charted => "charted",
        DepthBasisKind.Adjusted => "adjusted",
        _ => "none",
    };

    internal static string Status(WaterLevelStatus status) => status switch
    {
        WaterLevelStatus.Applied => "applied",
        WaterLevelStatus.NoCoverage => "no_coverage",
        WaterLevelStatus.BeforeStart => "before_start",
        WaterLevelStatus.AfterEnd => "after_end",
        WaterLevelStatus.NoData => "no_data",
        WaterLevelStatus.DatumMismatch => "datum_mismatch",
        _ => status.ToString().ToLowerInvariant(),
    };

    internal static string Datum(DatumAgreement datum) => datum switch
    {
        DatumAgreement.Same => "same",
        DatumAgreement.Different => "different",
        _ => "unknown",
    };

    private static DepthBasisSourceDto Source(DepthBasisSource source) => new(
        source.DatasetId,
        source.Spec,
        source.VerticalDatumCode,
        source.VerticalDatumCode is { } code && VerticalDatums.TryGetLabel(code, out var label) ? label : null,
        source.Kind switch
        {
            WaterLevelSourceKind.Grid => "grid",
            WaterLevelSourceKind.Station => "station",
            _ => null,
        },
        source.Start,
        source.End,
        source.Run);
}

/// <summary>Wraps <see cref="GetDepthBasisTool"/> as an MCP server tool.</summary>
internal static class DepthBasisMcpAdapter
{
    /// <summary>Creates <c>get_depth_basis</c>.</summary>
    public static McpServerTool Create(GetDepthBasisTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = GetDepthBasisTool.Name,
            Description =
                "Reads what the depths and safety contour drawn on the live viewer's chart are relative to, as the status "
                + "bar's DEPTHS indicator shows it: kind ('charted' today; 'adjusted' once chart water level adjustment "
                + "exists; 'none' when no S-101, S-57 or S-102 dataset is loaded and active), why charted depths are shown "
                + "when S-104 water level is in view (reason 'not_applied'), the depth sources and S-104 water-level sources "
                + "in view with their vertical datums, whether those datums agree, and display, the indicator's text exactly "
                + "as the user reads it. Use it to report whether a picture or a clearance the user sees is on charted or "
                + "adjusted depths. Read-only; viewer-injected tool.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
