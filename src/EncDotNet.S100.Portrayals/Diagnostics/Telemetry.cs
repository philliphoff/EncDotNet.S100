using System.Diagnostics;
using System.Diagnostics.Metrics;
using EncDotNet.S100.Diagnostics;

namespace EncDotNet.S100.Portrayals.Diagnostics;

/// <summary>Per-assembly <see cref="ActivitySource"/> and <see cref="Meter"/> for <c>EncDotNet.S100.Portrayals</c>.</summary>
internal static class Telemetry
{
    public static readonly ActivitySource ActivitySource =
        S100Telemetry.CreateActivitySource(typeof(Telemetry));

    public static readonly Meter Meter =
        S100Telemetry.CreateMeter(typeof(Telemetry));

    /// <summary>
    /// Decoded-asset cache hits across the per-<see cref="EncDotNet.S100.Core.SpecRef"/>
    /// <see cref="IPortrayalAssetCache"/> (asset-caching audit §6 PR-3 + PR-CACHE-7).
    /// Tagged with <c>s100.product</c> and <c>s100.asset.kind</c>; the latter
    /// takes one of: <c>xslt</c>, <c>svg</c>, <c>line_style</c>,
    /// <c>area_fill</c>, <c>palette</c>, <c>lua_script</c>, <c>lua_source</c>.
    /// </summary>
    public static readonly Counter<long> PortrayalCacheHit =
        Meter.CreateCounter<long>(
            name: "s100.portrayal.cache.hit.count",
            unit: "{hits}",
            description: "Portrayal asset cache hits (compiled XSLT, SVG symbols, line styles, area fills, palettes, compiled Lua scripts, Lua source strings).");

    /// <inheritdoc cref="PortrayalCacheHit"/>
    public static readonly Counter<long> PortrayalCacheMiss =
        Meter.CreateCounter<long>(
            name: "s100.portrayal.cache.miss.count",
            unit: "{misses}",
            description: "Portrayal asset cache misses (asset loaded from underlying IAssetSource for the first time).");

    /// <summary>
    /// Dedicated Lua-source cache hits for the S-101 and S-131 catalogue
    /// wrappers (asset-caching audit PR-2 / #66). Emitted alongside the
    /// <see cref="PortrayalCacheHit"/> counter with <c>s100.asset.kind = lua_source</c>
    /// so dashboards can either join on the dedicated counter or filter
    /// the portrayal counter by kind. Tagged with <c>s100.product</c>.
    /// </summary>
    public static readonly Counter<long> LuaSourceCacheHit =
        Meter.CreateCounter<long>(
            name: "s100.lua.source.cache.hit.count",
            unit: "{hits}",
            description: "Lua source-string cache hits in S-101 / S-131 portrayal catalogues.");

    /// <inheritdoc cref="LuaSourceCacheHit"/>
    public static readonly Counter<long> LuaSourceCacheMiss =
        Meter.CreateCounter<long>(
            name: "s100.lua.source.cache.miss.count",
            unit: "{misses}",
            description: "Lua source-string cache misses (file read from the asset source for the first time).");
}

/// <summary>
/// Standard portrayal-asset cache <c>kind</c> tag values. Used as the
/// value space for the <see cref="TelemetryTags.AssetKind"/> dimension
/// on <see cref="Telemetry.PortrayalCacheHit"/> and
/// <see cref="Telemetry.PortrayalCacheMiss"/>.
/// </summary>
public static class PortrayalAssetKinds
{
    /// <summary><c>xslt</c>: compiled XSLT rule transforms.</summary>
    public const string Xslt = "xslt";
    /// <summary><c>svg</c>: SVG symbol documents.</summary>
    public const string Svg = "svg";
    /// <summary><c>line_style</c>: parsed line style definitions.</summary>
    public const string LineStyle = "line_style";
    /// <summary><c>area_fill</c>: parsed area fill definitions.</summary>
    public const string AreaFill = "area_fill";
    /// <summary><c>palette</c>: colour palettes parsed from colour profiles.</summary>
    public const string Palette = "palette";
    /// <summary><c>lua_script</c>: compiled Lua rule scripts.</summary>
    public const string LuaScript = "lua_script";
    /// <summary><c>lua_source</c>: Lua rule source strings.</summary>
    public const string LuaSource = "lua_source";
}

/// <summary>
/// Helper for recording portrayal-cache hit/miss events with consistent
/// dimensions. Marked <c>public</c> so dataset catalogues in sibling
/// assemblies (S-101, S-131, S-111) can emit through it without forking
/// the dimension list.
/// </summary>
public static class PortrayalCacheMetrics
{
    /// <summary>Records a cache hit for the given product + asset kind.</summary>
    public static void RecordHit(string? product, string assetKind)
    {
        Telemetry.PortrayalCacheHit.Add(
            1,
            new KeyValuePair<string, object?>(TelemetryTags.Product, product),
            new KeyValuePair<string, object?>(TelemetryTags.AssetKind, assetKind));
    }

    /// <summary>Records a cache miss for the given product + asset kind.</summary>
    public static void RecordMiss(string? product, string assetKind)
    {
        Telemetry.PortrayalCacheMiss.Add(
            1,
            new KeyValuePair<string, object?>(TelemetryTags.Product, product),
            new KeyValuePair<string, object?>(TelemetryTags.AssetKind, assetKind));
    }

    /// <summary>
    /// Records a dedicated Lua-source cache hit (<see cref="Telemetry.LuaSourceCacheHit"/>)
    /// alongside the portrayal counter with <c>kind = lua_source</c>.
    /// </summary>
    public static void RecordLuaSourceHit(string? product)
    {
        Telemetry.LuaSourceCacheHit.Add(
            1,
            new KeyValuePair<string, object?>(TelemetryTags.Product, product));
        RecordHit(product, PortrayalAssetKinds.LuaSource);
    }

    /// <summary>
    /// Records a dedicated Lua-source cache miss (<see cref="Telemetry.LuaSourceCacheMiss"/>)
    /// alongside the portrayal counter with <c>kind = lua_source</c>.
    /// </summary>
    public static void RecordLuaSourceMiss(string? product)
    {
        Telemetry.LuaSourceCacheMiss.Add(
            1,
            new KeyValuePair<string, object?>(TelemetryTags.Product, product));
        RecordMiss(product, PortrayalAssetKinds.LuaSource);
    }
}
