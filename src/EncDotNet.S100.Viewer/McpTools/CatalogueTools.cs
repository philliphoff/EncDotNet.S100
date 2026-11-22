using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

// Viewer tools that read the catalogue registry (docs/design/catalogue-panels.md CR1, D7).

/// <summary>One source of a catalogue.</summary>
[Description("Where a catalogue comes from and what it says about itself.")]
internal sealed record CatalogueSourceDto(
    [property: Description("built_in, custom or command_line.")] string Origin,
    [property: Description("The file, folder or ZIP package; null for built_in.")] string? Path,
    [property: Description("The catalogue's declared name (feature catalogues only); null when absent.")] string? Name,
    [property: Description("The catalogue's declared version, e.g. \"2.0.0\"; null when unreadable.")] string? Version,
    [property: Description("The declared version date (feature catalogues only); null when absent.")] string? VersionDate);

/// <summary>A product's catalogue of one kind.</summary>
[Description("A product's feature or portrayal catalogue, as in effect.")]
internal sealed record CatalogueStateDto(
    [property: Description("in_use (the configured catalogue loaded), error (it failed; drawing is what's used instead), missing (no catalogue: datasets of the product won't open) or not_required (the viewer draws the product itself, e.g. S-104).")] string Status,
    [property: Description("The highest-precedence catalogue configured (command line > custom > built-in), whether or not it loaded; null when none.")] CatalogueSourceDto? Source,
    [property: Description("The catalogue actually in use; differs from source after an error; null when none.")] CatalogueSourceDto? Drawing,
    [property: Description("The catalogue that source overrides, e.g. the built-in; null when none.")] CatalogueSourceDto? Overrides,
    [property: Description("Why source failed to load; null unless status is error.")] string? Error,
    [property: Description("True when source is custom and kept after restart; false for a session-only one.")] bool KeptAfterRestart);

/// <summary>A product's two catalogues.</summary>
[Description("One product with its feature and portrayal catalogues.")]
internal sealed record CatalogueProductDto(
    [property: Description("The product spec, e.g. \"S-101\".")] string Spec,
    [property: Description("The feature catalogue's declared name; null when none.")] string? Name,
    [property: Description("False for a product the viewer doesn't bundle (a custom product).")] bool BuiltInProduct,
    [property: Description("True when anything isn't the default: a custom or command-line catalogue, or a custom product.")] bool Changed,
    [property: Description("True when either catalogue is in error or missing.")] bool Problem,
    [property: Description("How many loaded datasets the product's catalogues draw (S-57 cells count for S-101).")] int LoadedDatasets,
    [property: Description("The feature catalogue.")] CatalogueStateDto Feature,
    [property: Description("The portrayal catalogue.")] CatalogueStateDto Portrayal);

/// <summary>The result of <c>list_catalogues</c>.</summary>
[Description("Every product the viewer knows, with the catalogues in effect.")]
internal sealed record ListCataloguesResult(
    [property: Description("The products by spec number: bundled, custom and from the command line.")] IReadOnlyList<CatalogueProductDto> Products);

/// <summary>The viewer knows no product <paramref name="Spec"/>.</summary>
[Description("Raised when the spec names no product the viewer has catalogues for (list_catalogues without a spec lists them).")]
internal sealed record CatalogueProductNotFound(
    [property: Description("The spec that was asked for.")] string Spec)
    : ToolError("catalogue_product_not_found", $"The viewer has no catalogues for '{Spec}'.");

/// <summary>Lists the catalogues in effect (MCP <c>list_catalogues</c>); headless, it reads the registry.</summary>
internal sealed class ListCataloguesTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "list_catalogues";

    private readonly CatalogueRegistry _registry;
    private readonly Func<string, int> _loadedDatasets;
    private readonly IRouteEditInvoker _invoker;

    /// <summary>Creates the tool.</summary>
    /// <param name="registry">The catalogues in effect.</param>
    /// <param name="loadedDatasets">How many loaded datasets a product's catalogues draw; read on the UI thread.</param>
    /// <param name="invoker">Runs the dataset count on the UI thread.</param>
    public ListCataloguesTool(CatalogueRegistry registry, Func<string, int> loadedDatasets, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(loadedDatasets);
        ArgumentNullException.ThrowIfNull(invoker);
        _registry = registry;
        _loadedDatasets = loadedDatasets;
        _invoker = invoker;
    }

    /// <summary>Lists one product, or every product when <paramref name="spec"/> is null.</summary>
    public async Task<ToolResult<ListCataloguesResult>> InvokeAsync(string? spec = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<CatalogueProductState> products = _registry.Products;
        if (!string.IsNullOrWhiteSpace(spec))
        {
            if (_registry.Find(spec) is not { } product)
                return ToolResult<ListCataloguesResult>.Err(new CatalogueProductNotFound(spec));
            products = [product];
        }

        var counts = await _invoker.InvokeAsync(() => products.Select(p => _loadedDatasets(p.Spec)).ToList()).ConfigureAwait(false);
        return ToolResult<ListCataloguesResult>.Ok(new ListCataloguesResult(
            [.. products.Select((p, i) => new CatalogueProductDto(
                p.Spec,
                p.DeclaredName,
                p.IsBuiltInProduct,
                p.IsChanged,
                p.HasProblem,
                counts[i],
                State(p.Feature),
                State(p.Portrayal)))]));
    }

    private static CatalogueStateDto State(CatalogueState state) => new(
        state.Status switch
        {
            CatalogueStatus.InUse => "in_use",
            CatalogueStatus.Error => "error",
            CatalogueStatus.Missing => "missing",
            _ => "not_required",
        },
        Source(state.Source),
        Source(state.Drawing),
        Source(state.Overrides),
        state.Error,
        state.KeptAfterRestart);

    private static CatalogueSourceDto? Source(CatalogueSourceInfo? source) => source is null
        ? null
        : new CatalogueSourceDto(
            source.Origin switch
            {
                CatalogueOrigin.BuiltIn => "built_in",
                CatalogueOrigin.Custom => "custom",
                _ => "command_line",
            },
            source.Path,
            source.Metadata?.Name,
            source.Metadata?.Version,
            source.Metadata?.VersionDate);
}

/// <summary>The MCP adapter for <c>list_catalogues</c>.</summary>
internal static class CatalogueMcpAdapter
{
    /// <summary>Creates <c>list_catalogues</c>.</summary>
    public static McpServerTool Create(ListCataloguesTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("A product, e.g. \"S-101\"; omit for every product.")] string? spec = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(spec, ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = ListCataloguesTool.Name,
            Description =
                "Lists the feature and portrayal catalogues the viewer draws each product with: every product it knows "
                + "(bundled, custom and from the command line), and for each catalogue its status (in_use, error, missing, "
                + "not_required), the source configured (built_in, custom or command_line, with path, name, version and "
                + "date), the one actually drawing (the built-in after an error), what it overrides and the error. "
                + "Precedence is command line > custom > built-in. Also counts each product's loaded datasets. "
                + "Read-only; viewer-injected tool.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
