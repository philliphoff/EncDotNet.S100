using System.Xml.Xsl;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Process-scoped, spec-segmented storage for decoded portrayal assets
/// (compiled XSLT transforms, parsed SVG symbols, line styles, area
/// fills, colour palettes, compiled Lua scripts, and raw Lua source
/// strings).
/// </summary>
/// <remarks>
/// <para>
/// Introduced by PR-3 of the asset-caching audit
/// (<c>docs/internal/asset-caching-audit.md</c> §6 PR-3) to lift the
/// per-catalogue-instance caches up to the spec level. A single
/// <see cref="IPortrayalAssetCache"/> instance is owned by
/// <see cref="PortrayalCatalogueManager"/> per <c>SpecRef</c> and shared
/// by every <c>S101PortrayalCatalogue</c> / <c>S131PortrayalCatalogue</c>
/// / <c>GmlPortrayalCatalogueBase</c> wrapper that the manager hands
/// out for that spec. This means two open datasets of the same product
/// pay the underlying <see cref="Core.IAssetSource.OpenAsync"/> cost
/// for each asset only once, not once per dataset.
/// </para>
/// <para>
/// The catalogues continue to own the loading logic; they only
/// delegate <em>storage</em> to the injected cache.
/// </para>
/// </remarks>
public interface IPortrayalAssetCache
{
    /// <summary>
    /// Cache of compiled XSLT transforms keyed by rule name. Populated
    /// on first call to <c>GetCompiledRule</c>.
    /// </summary>
    IDictionary<string, XslCompiledTransform> CompiledXslt { get; }

    /// <summary>
    /// Cache of parsed SVG symbols keyed by symbol id. Populated on
    /// first call to <c>GetSymbol</c>.
    /// </summary>
    IDictionary<string, SvgSymbol> Symbols { get; }

    /// <summary>
    /// Cache of parsed line styles keyed by line-style id. Populated
    /// on first call to <c>GetLineStyle</c>.
    /// </summary>
    IDictionary<string, LineStyle> LineStyles { get; }

    /// <summary>
    /// Cache of parsed area fills keyed by area-fill id. Populated on
    /// first call to <c>GetAreaFill</c>.
    /// </summary>
    IDictionary<string, AreaFill> AreaFills { get; }

    /// <summary>
    /// Cache of loaded colour palettes keyed by palette type
    /// (Day/Dusk/Night). Populated together by the catalogue's palette
    /// scan; the scan happens once per cache (see
    /// <see cref="PalettesLoaded"/>).
    /// </summary>
    IDictionary<PaletteType, ColorPalette> Palettes { get; }

    /// <summary>
    /// Cache of compiled Lua <see cref="Script"/> instances keyed by
    /// script name. Populated on first call to <c>GetLuaScript</c>.
    /// </summary>
    IDictionary<string, Script> LuaScripts { get; }

    /// <summary>
    /// Cache of raw Lua source strings keyed (case-insensitively) by
    /// bare file name. A cached <see langword="null"/> records a
    /// negative lookup so missing-module <c>require()</c> calls do
    /// not re-open the asset source on every miss (see PR-2 of the
    /// asset-caching audit).
    /// </summary>
    IDictionary<string, string?> LuaSources { get; }

    /// <summary>
    /// Sticky flag set by the catalogue once it has performed the
    /// one-shot scan of <c>Catalogue.ColorProfiles</c> that populates
    /// <see cref="Palettes"/>. When two catalogues share a cache this
    /// flag prevents the second catalogue from re-running the scan
    /// (and re-opening the underlying colour-profile assets).
    /// </summary>
    bool PalettesLoaded { get; set; }
}

/// <summary>
/// Default <see cref="IPortrayalAssetCache"/> implementation backed by
/// plain <see cref="Dictionary{TKey, TValue}"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread-safety:</b> this implementation is <em>not</em>
/// thread-safe. All slots are non-concurrent dictionaries. Today the
/// dataset processors that consume catalogues read and write the cache
/// on a single pipeline thread per dataset, which makes this safe in
/// practice — but two pipelines running in parallel against catalogues
/// for the same spec would race. Hardening to
/// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>
/// is tracked separately by PR-6 of the asset-caching audit
/// (<c>docs/internal/asset-caching-audit.md</c> §6 PR-6).
/// </para>
/// <para>
/// The <see cref="LuaSources"/> dictionary uses
/// <see cref="StringComparer.OrdinalIgnoreCase"/> to preserve PR-2's
/// case-insensitive lookup semantics (Lua <c>require</c> name lookups
/// from MoonSharp are case-insensitive on Windows-style fs).
/// </para>
/// </remarks>
public sealed class PortrayalAssetCache : IPortrayalAssetCache
{
    /// <inheritdoc/>
    public IDictionary<string, XslCompiledTransform> CompiledXslt { get; } =
        new Dictionary<string, XslCompiledTransform>(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public IDictionary<string, SvgSymbol> Symbols { get; } =
        new Dictionary<string, SvgSymbol>(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public IDictionary<string, LineStyle> LineStyles { get; } =
        new Dictionary<string, LineStyle>(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public IDictionary<string, AreaFill> AreaFills { get; } =
        new Dictionary<string, AreaFill>(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public IDictionary<PaletteType, ColorPalette> Palettes => PalettesDictionary;

    /// <summary>
    /// Concrete-typed view of the palette dictionary, used by
    /// <c>GmlPortrayalCatalogueBase.LoadPalettes</c> whose signature is
    /// part of the protected API and remains <c>Dictionary&lt;,&gt;</c>.
    /// </summary>
    internal Dictionary<PaletteType, ColorPalette> PalettesDictionary { get; } =
        new Dictionary<PaletteType, ColorPalette>();

    /// <inheritdoc/>
    public IDictionary<string, Script> LuaScripts { get; } =
        new Dictionary<string, Script>(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public IDictionary<string, string?> LuaSources { get; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public bool PalettesLoaded { get; set; }
}
