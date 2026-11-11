using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.ViewModels;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>The Display controls Bathymetry section.</summary>
[Description("The Display controls Bathymetry section: map-wide switches for S-102 bathymetry, applied to every S-102 dataset and kept after a restart.")]
internal sealed record BathymetryDisplayDto(
    [property: Description("True while the Bathymetry section shows: an S-102 dataset is loaded. The switch applies and can be set either way.")] bool SectionShown,
    [property: Description("Relief shading: a hillshade over the S-102 depth bands, lit from the north-west. Not part of the S-102 portrayal; off by default.")] bool ReliefShading);

/// <summary>Reads and sets the Bathymetry section's switches, as its toggles do.</summary>
internal sealed class BathymetryDisplayTools
{
    /// <summary>The read tool's name.</summary>
    public const string GetName = "get_bathymetry_display";

    /// <summary>The set tool's name.</summary>
    public const string SetName = "set_bathymetry_display";

    private readonly EcdisDisplayPanelViewModel _panel;
    private readonly IRouteEditInvoker _invoker;

    public BathymetryDisplayTools(EcdisDisplayPanelViewModel panel, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(invoker);
        _panel = panel;
        _invoker = invoker;
    }

    /// <summary>Returns the switches.</summary>
    public Task<ToolResult<BathymetryDisplayDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _invoker.InvokeAsync(() => ToolResult<BathymetryDisplayDto>.Ok(Dto(_panel)));
    }

    /// <summary>Sets the given switches (a null one is left as it is) and returns them all.</summary>
    public Task<ToolResult<BathymetryDisplayDto>> SetAsync(
        bool? reliefShading = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _invoker.InvokeAsync(() =>
        {
            if (reliefShading is { } relief) _panel.IsReliefShadingVisible = relief;
            return ToolResult<BathymetryDisplayDto>.Ok(Dto(_panel));
        });
    }

    internal static BathymetryDisplayDto Dto(EcdisDisplayPanelViewModel panel) =>
        new(panel.HasBathymetryData, panel.IsReliefShadingVisible);
}

/// <summary>Wraps <see cref="BathymetryDisplayTools"/> as <see cref="McpServerTool"/>s.</summary>
internal static class BathymetryDisplayMcpAdapters
{
    public static McpServerTool CreateGet(BathymetryDisplayTools inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (CancellationToken ct = default) => McpAdapterShared.DispatchAsync(() => inner.GetAsync(ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = BathymetryDisplayTools.GetName,
            Description =
                "Returns the Display controls Bathymetry section's switches: relief shading (a hillshade over the S-102 " +
                "depth bands), with whether the section shows. Change them with set_bathymetry_display.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }

    public static McpServerTool CreateSet(BathymetryDisplayTools inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Relief shading on or off; omit to leave it.")] bool? reliefShading = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.SetAsync(reliefShading, ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = BathymetryDisplayTools.SetName,
            Description =
                "Sets Display controls Bathymetry switches, as their toggles do; an omitted one is left as it is, and all are " +
                "kept after a restart. They apply to every S-102 dataset, whether or not the section shows. Returns what " +
                "get_bathymetry_display returns. Viewer-injected tool — mutates live state.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
