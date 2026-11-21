using System.Runtime.CompilerServices;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Every symbol a portrayal catalogue defines, not only those a dataset
/// uses. A renderer that builds its own symbol style (GIS symbology, vector
/// PDF) can build it once from this set and reuse it across datasets.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Names"/> comes from the catalogue's symbol list and costs no
/// I/O. Symbols load only when asked for, through the catalogue's own
/// memoized <see cref="IPortrayalAssetSource.GetSymbolAsync"/>, so the
/// records are the same ones the renderers draw from: a symbol already
/// loaded for drawing is not loaded again, and its
/// <see cref="SvgSymbol.Geometry"/> parses once whichever path asks first.
/// </para>
/// <para>
/// Symbols carry palette tokens, not colours, so the set does not depend on
/// the active palette or any other display setting.
/// </para>
/// </remarks>
public sealed class CatalogueSymbolSet
{
    private readonly IPortrayalAssetSource _source;

    /// <summary>Creates the set of every symbol <paramref name="source"/> defines.</summary>
    public CatalogueSymbolSet(IPortrayalAssetSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        Names = source.SymbolNames;
    }

    /// <summary>
    /// The name of every symbol in the catalogue, as drawing instructions
    /// reference it, in catalogue order.
    /// </summary>
    public IReadOnlyCollection<string> Names { get; }

    /// <summary>
    /// Loads the named symbol, or returns <see langword="null"/> when the
    /// catalogue has no such symbol or it cannot be read.
    /// </summary>
    public async ValueTask<SvgSymbol?> GetSymbolAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        try
        {
            return await _source.GetSymbolAsync(name, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        // Same leniency as the render path's pre-warm: a missing or
        // unreadable asset is "not in catalogue", never a failure.
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Loads the named symbol as parsed vector geometry
    /// (<see cref="SvgSymbol.Geometry"/>), or returns <see langword="null"/>
    /// when the symbol is missing or its SVG cannot be parsed.
    /// </summary>
    public async ValueTask<SymbolGeometry?> GetGeometryAsync(string name, CancellationToken cancellationToken = default) =>
        (await GetSymbolAsync(name, cancellationToken).ConfigureAwait(false))?.Geometry;

    /// <summary>
    /// Loads every symbol in <see cref="Names"/> order, one at a time,
    /// skipping any that cannot be read.
    /// </summary>
    public async IAsyncEnumerable<SvgSymbol> GetSymbolsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var name in Names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await GetSymbolAsync(name, cancellationToken).ConfigureAwait(false) is { } symbol)
                yield return symbol;
        }
    }
}
