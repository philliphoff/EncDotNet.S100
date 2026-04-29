namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Capability interface for catalogues that supply portrayal assets — SVG
/// symbols, line styles, and area fills (S-100 Part 9 §11). The
/// <see cref="MapsuiDisplayListRenderer"/> and equivalent renderers consume
/// these via per-feature provider callbacks.
/// </summary>
/// <remarks>
/// Implementations should throw <see cref="KeyNotFoundException"/> when the
/// named asset is not present in the loaded catalogue.
/// </remarks>
public interface IPortrayalAssetSource
{
    /// <summary>Resolves an SVG symbol by name from the catalogue resources.</summary>
    SvgSymbol GetSymbol(string symbolName);

    /// <summary>Resolves a line style by name from the catalogue resources.</summary>
    LineStyle GetLineStyle(string name);

    /// <summary>Resolves an area fill by name from the catalogue resources.</summary>
    AreaFill GetAreaFill(string name);
}
