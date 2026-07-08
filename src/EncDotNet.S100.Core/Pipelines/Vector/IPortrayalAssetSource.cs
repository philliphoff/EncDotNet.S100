namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Capability interface for catalogues that supply portrayal assets — SVG
/// symbols, line styles, and area fills (S-100 Part 9 §11). The
/// <see cref="MapsuiDisplayListRenderer"/> and equivalent renderers consume
/// these via per-feature provider callbacks.
/// </summary>
/// <remarks>
/// <para>
/// All members are asynchronous because uncached lookups perform I/O against
/// the underlying <see cref="IAssetSource"/>. Implementations are expected
/// to memoize decoded results, so the second access for the same name on a
/// given catalogue completes synchronously through the
/// <see cref="ValueTask{TResult}"/> fast path.
/// </para>
/// <para>
/// Renderers (Skia, Mapsui) are themselves synchronous, so dataset
/// processors are responsible for awaiting these methods before the
/// synchronous render phase and capturing the results in local resolver
/// closures. See <c>CataloguePreWarm</c> in
/// <c>EncDotNet.S100.Datasets.Pipelines</c> for the shared helper.
/// </para>
/// <para>
/// Implementations should throw <see cref="PortrayalAssetNotFoundException"/> when the
/// named asset is not present in the loaded catalogue.
/// </para>
/// </remarks>
public interface IPortrayalAssetSource
{
    /// <summary>Resolves an SVG symbol by name from the catalogue resources.</summary>
    ValueTask<SvgSymbol> GetSymbolAsync(string symbolName, CancellationToken cancellationToken = default);

    /// <summary>Resolves a line style by name from the catalogue resources.</summary>
    ValueTask<LineStyle> GetLineStyleAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Resolves an area fill by name from the catalogue resources.</summary>
    ValueTask<AreaFill> GetAreaFillAsync(string name, CancellationToken cancellationToken = default);
}
