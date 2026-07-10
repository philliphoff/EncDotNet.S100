using System.Diagnostics;
using System.Xml;
using System.Xml.Xsl;
using EncDotNet.S100.Diagnostics;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// S-101 portrayal catalogue implementing <see cref="IVectorPortrayalCatalogue"/>.
/// Loads and compiles XSLT rules, caches Lua scripts, and resolves symbols,
/// line styles, and area fills from a <see cref="PortrayalCatalogueProvider"/>.
/// </summary>
public sealed class S101PortrayalCatalogue : IVectorPortrayalCatalogue
{
    private readonly PortrayalCatalogueProvider _provider;
    private readonly ILuaEngine? _luaEngine;

    // PR-3 (asset-caching audit §6): decoded-asset storage lives on the
    // provider's IPortrayalAssetCache, so two S101PortrayalCatalogue
    // instances sharing a provider — or two providers sharing a
    // PortrayalCatalogueManager-owned cache for SpecRef("S-101", _) —
    // pay each XSLT compile / SVG read / line style / area fill /
    // palette / Lua source decode at most once.
    //
    // Thread-safety: PortrayalAssetCache uses non-concurrent
    // dictionaries. Today the S-101 dataset processor reads and writes
    // these slots on a single pipeline thread per dataset, so the only
    // race risk is two pipelines running concurrently against
    // S-101 catalogues that share a manager-owned cache. PR-6 of the
    // audit tracks hardening to ConcurrentDictionary.
    private readonly IPortrayalAssetCache _cache;

    private IReadOnlyList<PortrayalRule>? _rules;

    public S101PortrayalCatalogue(PortrayalCatalogueProvider provider, ILuaEngine? luaEngine = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _luaEngine = luaEngine;
        _cache = provider.AssetCache;
        DisplayModeMembership.Bind(DisplayModes, ViewingGroups, _provider.Catalogue);
    }

    public SpecRef Spec => new("S-101", default);
    public string Edition => _provider.Catalogue.Version;

    // Cached product tag used for cache metrics (avoids re-allocating strings).
    private const string ProductTag = "S-101";

    /// <summary>The identity of the underlying portrayal catalogue XML, when available.</summary>
    public CatalogueRef? CatalogueRef => _provider.Catalogue.CatalogueRef;
    public ColorPalette ActivePalette { get; private set; } = ColorPalette.Default;

    /// <inheritdoc/>
    /// <remarks>
    /// When the requested palette cannot be resolved (for example, a portrayal
    /// catalogue whose colour profile is absent, malformed, or in a format the
    /// reader does not support), this method degrades gracefully rather than
    /// throwing: it falls back to the Day palette, then to any palette that did
    /// load, and finally to <see cref="ColorPalette.Default"/>. This mirrors the
    /// behaviour of <c>GmlPortrayalCatalogueBase.SwitchPaletteAsync</c> so a
    /// colour-profile problem yields a usable render with a diagnostic instead
    /// of aborting the whole dataset load. See issue #321.
    /// </remarks>
    public async ValueTask SwitchPaletteAsync(PaletteType type, CancellationToken cancellationToken = default)
    {
        await EnsurePalettesLoadedAsync(cancellationToken).ConfigureAwait(false);

        if (_cache.Palettes.TryGetValue(type, out var palette))
        {
            Portrayals.Diagnostics.PortrayalCacheMetrics.RecordHit(ProductTag, Portrayals.Diagnostics.PortrayalAssetKinds.Palette);
            ActivePalette = palette;
            return;
        }

        // Graceful fallback: prefer Day, then any loaded palette, then the
        // built-in default. The active palette never becomes null, so the
        // renderer can still resolve colour tokens (falling back to black).
        var fallback = _cache.Palettes.TryGetValue(PaletteType.Day, out var dayPalette)
            ? dayPalette
            : _cache.Palettes.Values.FirstOrDefault() ?? ColorPalette.Default;

        Console.WriteLine(
            $"[S101] Color palette '{type}' not found in the portrayal catalogue; " +
            $"falling back to '{fallback.Name}' ({fallback.Colors.Count} colors).");

        ActivePalette = fallback;
    }

    public ViewingGroupController ViewingGroups { get; } = new();

    public DisplayModeController DisplayModes { get; } = new();

    /// <summary>Controls which S-100 Part 9 §11.6 display planes are visible.</summary>
    public DisplayPlaneController DisplayPlanes { get; } = new();

    // ── Palettes ───────────────────────────────────────────────────────

    private ValueTask EnsurePalettesLoadedAsync(CancellationToken cancellationToken) =>
        PaletteLoadCoordinator.EnsureLoadedAsync(
            _cache, LoadPalettesIntoCacheAsync, ApplyDayPalette, cancellationToken);

    private void ApplyDayPalette()
    {
        if (_cache.Palettes.TryGetValue(PaletteType.Day, out var dayPalette))
        {
            ActivePalette = dayPalette;
        }
    }

    private async ValueTask LoadPalettesIntoCacheAsync(CancellationToken cancellationToken)
    {
        foreach (var item in _provider.Catalogue.ColorProfiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paletteName = item.Description.Name;
            if (string.IsNullOrEmpty(paletteName))
            {
                paletteName = Path.GetFileNameWithoutExtension(item.FileName);
            }

            var paletteType = paletteName switch
            {
                var n when n.Contains("Day", StringComparison.OrdinalIgnoreCase) => PaletteType.Day,
                var n when n.Contains("Dusk", StringComparison.OrdinalIgnoreCase) => PaletteType.Dusk,
                var n when n.Contains("Night", StringComparison.OrdinalIgnoreCase) => PaletteType.Night,
                _ => (PaletteType?)null,
            };

            if (paletteType is not null)
            {
                try
                {
                    using var stream = await _provider.FetchAssetAsync(item, "ColorProfiles", cancellationToken).ConfigureAwait(false);
                    var palette = ColorProfileReader.Read(stream, paletteName);
                    _cache.Palettes[paletteType.Value] = palette;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // If a color profile cannot be loaded, skip it gracefully.
                }
            }
            else
            {
                // The manifest entry name does not indicate a specific palette.
                // The file may contain multiple palettes (Day, Dusk, Night) —
                // try loading each one from the same file.
                foreach (var (type, name) in new[] { (PaletteType.Day, "Day"), (PaletteType.Dusk, "Dusk"), (PaletteType.Night, "Night") })
                {
                    if (_cache.Palettes.ContainsKey(type)) continue;
                    try
                    {
                        using var stream = await _provider.FetchAssetAsync(item, "ColorProfiles", cancellationToken).ConfigureAwait(false);
                        var palette = ColorProfileReader.Read(stream, name);
                        if (palette.Colors.Count > 0)
                        {
                            _cache.Palettes[type] = palette;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Skip gracefully.
                    }
                }
            }
        }
    }

    // ── Rules ──────────────────────────────────────────────────────────

    public IReadOnlyList<PortrayalRule> Rules
    {
        get
        {
            if (_rules is not null) return _rules;
            _rules = BuildRules();
            return _rules;
        }
    }

    private IReadOnlyList<PortrayalRule> BuildRules()
    {
        var rules = new List<PortrayalRule>();
        int order = 0;

        foreach (var ruleFile in _provider.Catalogue.RuleFiles)
        {
            // Determine type from file extension first; the catalogue ruleType field
            // (e.g. "TopLevelTemplate", "SubTemplate") describes the rule's role,
            // not its format.
            var ruleType = Path.GetExtension(ruleFile.FileName).Equals(".lua", StringComparison.OrdinalIgnoreCase)
                ? PortrayalRuleType.Lua
                : PortrayalRuleType.Xslt;

            // Map the rule description name to feature type codes
            // Convention: rule filename prefix corresponds to the feature type
            var featureTypes = InferFeatureTypes(ruleFile);

            rules.Add(new PortrayalRule
            {
                Name = ruleFile.Id,
                Type = ruleType,
                ExecutionOrder = order++,
                AppliesTo = featureTypes,
                AlwaysApply = featureTypes.Count == 0,
            });
        }

        return rules;
    }

    private static IReadOnlyList<string> InferFeatureTypes(RuleFile ruleFile)
    {
        // S-101 PC convention: rule files are named after their target feature type.
        // Use the Description.Name first (more reliable than filename), falling back to filename.
        var name = !string.IsNullOrEmpty(ruleFile.Description.Name)
            ? ruleFile.Description.Name
            : Path.GetFileNameWithoutExtension(ruleFile.FileName);

        // Top-level / utility rules that apply to all features
        if (name.Contains("TopLevel", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PlainBoundaries", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("TopOfChart", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("main", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("template", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Updates", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        // Strip trailing numeric suffixes (e.g. "LIGHTS05" → "LIGHTS")
        int i = name.Length - 1;
        while (i >= 0 && char.IsDigit(name[i])) i--;
        var featureType = name[..(i + 1)];

        return string.IsNullOrEmpty(featureType) ? [] : [featureType];
    }

    // ── XSLT ───────────────────────────────────────────────────────────

    public ValueTask<XslCompiledTransform> GetCompiledRuleAsync(string ruleName, CancellationToken cancellationToken = default)
    {
        if (_cache.CompiledXslt.TryGetValue(ruleName, out var cached))
        {
            Portrayals.Diagnostics.PortrayalCacheMetrics.RecordHit(ProductTag, Portrayals.Diagnostics.PortrayalAssetKinds.Xslt);
            return new ValueTask<XslCompiledTransform>(cached);
        }

        return new ValueTask<XslCompiledTransform>(LoadAndCacheXsltAsync(ruleName, cancellationToken));
    }

    private async Task<XslCompiledTransform> LoadAndCacheXsltAsync(string ruleName, CancellationToken cancellationToken)
    {
        Portrayals.Diagnostics.PortrayalCacheMetrics.RecordMiss(ProductTag, Portrayals.Diagnostics.PortrayalAssetKinds.Xslt);
        var ruleFile = FindRuleFile(ruleName);
        var transform = await LoadXsltRuleAsync(ruleFile, cancellationToken).ConfigureAwait(false);
        _cache.CompiledXslt[ruleName] = transform;
        return transform;
    }

    private async Task<XslCompiledTransform> LoadXsltRuleAsync(RuleFile ruleFile, CancellationToken cancellationToken)
    {
        using var activity = Diagnostics.Telemetry.ActivitySource.StartActivity("s100.xslt.compile");
        activity?.SetTag(TelemetryTags.XsltRule, ruleFile.Id);
        var start = Stopwatch.GetTimestamp();

        using var stream = await _provider.FetchAssetAsync(ruleFile, cancellationToken).ConfigureAwait(false);
        // Buffer to memory so the synchronous XslCompiledTransform.Load can
        // operate over a seekable stream without doing async I/O on the
        // original asset stream.
        using var buffered = new MemoryStream();
        await stream.CopyToAsync(buffered, cancellationToken).ConfigureAwait(false);
        buffered.Position = 0;
        using var reader = XmlReader.Create(buffered);

        var transform = new XslCompiledTransform();
        transform.Load(reader);

        var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        activity?.SetTag("s100.xslt.compile.duration_ms", elapsedMs);

        return transform;
    }

    // ── Lua ────────────────────────────────────────────────────────────

    /// <summary>
    /// The context parameters declared by the S-101 portrayal catalogue
    /// (S-100 Part 9A), projected onto the Core
    /// <see cref="LuaContextParameter"/> type for the generic Lua executor.
    /// </summary>
    public IReadOnlyList<LuaContextParameter> ContextParameters =>
        _contextParameters ??= [.. _provider.Catalogue.ContextParameters
            .Select(cp => new LuaContextParameter(cp.Id, cp.Type, cp.Default))];

    private IReadOnlyList<LuaContextParameter>? _contextParameters;

    /// <summary>
    /// Returns every Lua rule file declared in the portrayal catalogue
    /// manifest (filtered to <c>.lua</c> extension), e.g.
    /// <c>main.lua</c>, <c>S100Scripting.lua</c>.
    /// </summary>
    public ValueTask<IReadOnlyList<string>> GetLuaSourceNamesAsync(CancellationToken cancellationToken = default)
    {
        var names = _provider.Catalogue.RuleFiles
            .Where(rf => Path.GetExtension(rf.FileName).Equals(".lua", StringComparison.OrdinalIgnoreCase))
            .Select(rf => rf.FileName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new ValueTask<IReadOnlyList<string>>(names);
    }

    /// <summary>
    /// Returns the raw Lua source for the given bare filename inside the
    /// portrayal catalogue's <c>Rules/</c> directory (e.g. <c>"main.lua"</c>,
    /// <c>"S100Scripting.lua"</c>), caching the decoded string so subsequent
    /// reads do not re-open the underlying <see cref="Core.IAssetSource"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns <see langword="null"/> (and caches <see langword="null"/>) if
    /// the file cannot be fetched, so the MoonSharp module loader's
    /// "missing module → return null" contract is preserved without retrying
    /// failed lookups on every <c>require()</c> call.
    /// </para>
    /// <para>
    /// This caches only the immutable <see cref="string"/> source. The
    /// compiled Lua <c>Script</c> instance is intentionally constructed
    /// per execution to preserve sandbox isolation (S-100 Part 9A).
    /// </para>
    /// </remarks>
    public ValueTask<string?> GetLuaSourceAsync(string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        if (_cache.LuaSources.TryGetValue(fileName, out var cached))
        {
            Portrayals.Diagnostics.PortrayalCacheMetrics.RecordLuaSourceHit(ProductTag);
            return new ValueTask<string?>(cached);
        }

        return new ValueTask<string?>(LoadLuaSourceAsync(fileName, cancellationToken));
    }

    private async Task<string?> LoadLuaSourceAsync(string fileName, CancellationToken cancellationToken)
    {
        Portrayals.Diagnostics.PortrayalCacheMetrics.RecordLuaSourceMiss(ProductTag);
        string? source;
        try
        {
            using var stream = await _provider.FetchRuleAsync(fileName, cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            source = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            source = null;
        }

        _cache.LuaSources[fileName] = source;
        return source;
    }

    /// <summary>The underlying portrayal catalogue provider.</summary>
    internal PortrayalCatalogueProvider Provider => _provider;

    // ── Symbols ────────────────────────────────────────────────────────

    public ValueTask<SvgSymbol> GetSymbolAsync(string symbolName, CancellationToken cancellationToken = default)
    {
        if (_cache.Symbols.TryGetValue(symbolName, out var cached))
        {
            Portrayals.Diagnostics.PortrayalCacheMetrics.RecordHit(ProductTag, Portrayals.Diagnostics.PortrayalAssetKinds.Svg);
            return new ValueTask<SvgSymbol>(cached);
        }

        return new ValueTask<SvgSymbol>(LoadAndCacheSymbolAsync(symbolName, cancellationToken));
    }

    private async Task<SvgSymbol> LoadAndCacheSymbolAsync(string symbolName, CancellationToken cancellationToken)
    {
        Portrayals.Diagnostics.PortrayalCacheMetrics.RecordMiss(ProductTag, Portrayals.Diagnostics.PortrayalAssetKinds.Svg);
        var symbol = await LoadSymbolAsync(symbolName, cancellationToken).ConfigureAwait(false);
        _cache.Symbols[symbolName] = symbol;
        return symbol;
    }

    private async Task<SvgSymbol> LoadSymbolAsync(string symbolName, CancellationToken cancellationToken)
    {
        var catalogItem = _provider.Catalogue.Symbols
            .FirstOrDefault(s => s.Id.Equals(symbolName, StringComparison.OrdinalIgnoreCase))
            ?? throw new PortrayalAssetNotFoundException(PortrayalAssetKind.Symbol, symbolName);

        using var stream = await _provider.FetchAssetAsync(catalogItem, "Symbols", cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var svgContent = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        return new SvgSymbol
        {
            Name = symbolName,
            SvgContent = svgContent,
        };
    }

    // ── Line styles ────────────────────────────────────────────────────

    public ValueTask<LineStyle> GetLineStyleAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_cache.LineStyles.TryGetValue(name, out var cached))
        {
            Portrayals.Diagnostics.PortrayalCacheMetrics.RecordHit(ProductTag, Portrayals.Diagnostics.PortrayalAssetKinds.LineStyle);
            return new ValueTask<LineStyle>(cached);
        }

        return new ValueTask<LineStyle>(LoadAndCacheLineStyleAsync(name, cancellationToken));
    }

    private async Task<LineStyle> LoadAndCacheLineStyleAsync(string name, CancellationToken cancellationToken)
    {
        Portrayals.Diagnostics.PortrayalCacheMetrics.RecordMiss(ProductTag, Portrayals.Diagnostics.PortrayalAssetKinds.LineStyle);
        var style = await LoadLineStyleAsync(name, cancellationToken).ConfigureAwait(false);
        _cache.LineStyles[name] = style;
        return style;
    }

    private async Task<LineStyle> LoadLineStyleAsync(string name, CancellationToken cancellationToken)
    {
        var catalogItem = _provider.Catalogue.LineStyles
            .FirstOrDefault(s => s.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new PortrayalAssetNotFoundException(PortrayalAssetKind.LineStyle, name);

        using var stream = await _provider.FetchAssetAsync(catalogItem, "LineStyles", cancellationToken).ConfigureAwait(false);
        return LineStyleReader.Read(stream, name);
    }

    // ── Area fills ─────────────────────────────────────────────────────

    public ValueTask<AreaFill> GetAreaFillAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_cache.AreaFills.TryGetValue(name, out var cached))
        {
            Portrayals.Diagnostics.PortrayalCacheMetrics.RecordHit(ProductTag, Portrayals.Diagnostics.PortrayalAssetKinds.AreaFill);
            return new ValueTask<AreaFill>(cached);
        }

        return new ValueTask<AreaFill>(LoadAndCacheAreaFillAsync(name, cancellationToken));
    }

    private async Task<AreaFill> LoadAndCacheAreaFillAsync(string name, CancellationToken cancellationToken)
    {
        Portrayals.Diagnostics.PortrayalCacheMetrics.RecordMiss(ProductTag, Portrayals.Diagnostics.PortrayalAssetKinds.AreaFill);
        var fill = await LoadAreaFillAsync(name, cancellationToken).ConfigureAwait(false);
        _cache.AreaFills[name] = fill;
        return fill;
    }

    private async Task<AreaFill> LoadAreaFillAsync(string name, CancellationToken cancellationToken)
    {
        var catalogItem = _provider.Catalogue.AreaFills
            .FirstOrDefault(s => s.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new PortrayalAssetNotFoundException(PortrayalAssetKind.AreaFill, name);

        using var stream = await _provider.FetchAssetAsync(catalogItem, "AreaFills", cancellationToken).ConfigureAwait(false);
        return AreaFillReader.Read(stream, name);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private RuleFile FindRuleFile(string ruleName)
    {
        return _provider.Catalogue.RuleFiles
            .FirstOrDefault(r => r.Id.Equals(ruleName, StringComparison.OrdinalIgnoreCase))
            ?? throw new PortrayalAssetNotFoundException(PortrayalAssetKind.Rule, ruleName);
    }
}
