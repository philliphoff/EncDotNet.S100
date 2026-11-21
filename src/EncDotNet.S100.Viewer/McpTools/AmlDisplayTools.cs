using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>One product of the Additional Military Layers section.</summary>
internal sealed record AmlProductDisplayDto(
    [property: Description("The product code: CLB, ESB, LBO, SBO, MFF, RAL, or 3.0 for the AML 3.0 integrated product.")] string Code,
    [property: Description("The product's full name, such as \"Contour Line Bathymetry\".")] string Name,
    [property: Description("The product's switch: false hides every cell of the product, whatever its Datasets row says. On by default; kept after a restart.")] bool Visible,
    [property: Description("How many cells of the product are loaded. A product with none shows \"Not loaded\" and no switch, but its switch can still be set.")] int CellCount,
    [property: Description("How many of the product's cells the map draws now, after the switch, each row's own visibility and the scale bands.")] int ShownCount);

/// <summary>The display panel's Additional Military Layers section (#1374).</summary>
[Description("The display panel's Additional Military Layers section: a switch per AML product and the Follow AML scale bands switch, kept after a restart.")]
internal sealed record AmlDisplayDto(
    [property: Description("True while the section shows: an AML cell is loaded.")] bool SectionShown,
    [property: Description("Follow AML scale bands (on by default): each AML cell shows from its scale band out to the band's coarsest display scale (AML Implementation Guidance Table 4), and the finer band of a product wins where its cells cover a coarser cell. Off shows every AML cell at every scale.")] bool FollowScaleBands,
    [property: Description("The map scale denominator the bands were applied at; null before the map has a viewport.")] double? ScaleDenominator,
    [property: Description("True while the corner chip \"AML · CLB not used for depth checks\" shows: a drawn CLB cell is shown.")] bool ClbChipShown,
    [property: Description("Every AML product, loaded ones first.")] IReadOnlyList<AmlProductDisplayDto> Products);

/// <summary>Reads and sets the Additional Military Layers section's switches, as its checkboxes do.</summary>
internal sealed class AmlDisplayTools
{
    /// <summary>The read tool's name.</summary>
    public const string GetName = "get_aml_display";

    /// <summary>The set tool's name.</summary>
    public const string SetName = "set_aml_display";

    private readonly AmlDisplayService _service;
    private readonly IRouteEditInvoker _invoker;

    public AmlDisplayTools(AmlDisplayService service, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(invoker);
        _service = service;
        _invoker = invoker;
    }

    /// <summary>Returns the section's state.</summary>
    public Task<ToolResult<AmlDisplayDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _invoker.InvokeAsync(() => ToolResult<AmlDisplayDto>.Ok(Dto(_service)));
    }

    /// <summary>
    /// Sets the given product switches and Follow AML scale bands (a null one
    /// is left as it is) and returns the section's state.
    /// </summary>
    public Task<ToolResult<AmlDisplayDto>> SetAsync(
        IReadOnlyDictionary<string, bool>? products = null,
        bool? followScaleBands = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _invoker.InvokeAsync(() =>
        {
            if (products is not null)
            {
                foreach (var code in products.Keys)
                {
                    if (AmlDisplayService.NormaliseCode(code) is null)
                        return ToolResult<AmlDisplayDto>.Err(new InvalidArgument("products", $"'{code}' isn't an AML product; use CLB, ESB, LBO, SBO, MFF, RAL or 3.0."));
                }

                foreach (var (code, visible) in products)
                    _service.SetProductVisible(code, visible);
            }

            if (followScaleBands is { } follow)
                _service.SetFollowScaleBands(follow);
            return ToolResult<AmlDisplayDto>.Ok(Dto(_service));
        });
    }

    internal static AmlDisplayDto Dto(AmlDisplayService service) => new(
        service.HasAml,
        service.FollowScaleBands,
        double.IsNaN(service.ScaleDenominator) ? null : Math.Round(service.ScaleDenominator),
        service.IsClbDrawn,
        [.. service.Products.Select(p => new AmlProductDisplayDto(p.Code, p.Name, p.IsVisible, p.CellCount, p.ShownCount))]);
}

/// <summary>Wraps <see cref="AmlDisplayTools"/> as <see cref="McpServerTool"/>s.</summary>
internal static class AmlDisplayMcpAdapters
{
    public static McpServerTool CreateGet(AmlDisplayTools inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (CancellationToken ct = default) => McpAdapterShared.DispatchAsync(() => inner.GetAsync(ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = AmlDisplayTools.GetName,
            Description =
                "Returns the display panel's Additional Military Layers section: each AML product's switch with its " +
                "loaded and shown cell counts, Follow AML scale bands, the scale the bands were applied at, and whether " +
                "the \"AML · CLB not used for depth checks\" chip shows. Change them with set_aml_display.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }

    public static McpServerTool CreateSet(AmlDisplayTools inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Product switches to set, keyed by product code (CLB, ESB, LBO, SBO, MFF, RAL, or 3.0 for the AML 3.0 integrated product), e.g. {\"CLB\": false}. False hides every cell of the product. Products left out keep their switch.")] Dictionary<string, bool>? products = null,
            [Description("Follow AML scale bands on or off; omit to leave it. Off shows every AML cell at every scale.")] bool? followScaleBands = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.SetAsync(products, followScaleBands, ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = AmlDisplayTools.SetName,
            Description =
                "Sets the Additional Military Layers section's switches, as its checkboxes do: per-product visibility " +
                "and Follow AML scale bands. An omitted one is left as it is; all are kept after a restart and apply " +
                "whether or not the section shows. A product switch hides whole cells without changing their Datasets " +
                "rows. Returns what get_aml_display returns. Viewer-injected tool — mutates live state.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
