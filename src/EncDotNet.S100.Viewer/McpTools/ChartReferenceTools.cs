using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Services.ChartReference;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

// Viewer tools that read the chart reference service (#1635, docs/design/symbol-dictionary.md R2).

/// <summary>A product the chart reference can describe.</summary>
[Description("A product with a portrayal catalogue that the chart reference describes.")]
internal sealed record ChartReferenceProductDto(
    [property: Description("The product spec, e.g. \"S-101\".")] string Spec,
    [property: Description("Whether datasets of the product are open; loaded products are listed first.")] bool Loaded);

/// <summary>One search hit.</summary>
[Description("One chart reference entry that matches the query.")]
internal sealed record ChartReferenceHitDto(
    [property: Description("The entry's key, \"<spec>/<kind>/<code>\", e.g. \"S-101/FeatureType/LateralBeacon\"; pass it to get_chart_reference_entry.")] string Key,
    [property: Description("The product spec.")] string Spec,
    [property: Description("feature_type, symbol, line_style, area_fill, attribute or display_setting.")] string Kind,
    [property: Description("The catalogue code: a feature type or attribute code, a symbol, line style or area fill id, a family prefix or a context parameter id.")] string Code,
    [property: Description("The catalogue name.")] string Name,
    [property: Description("The S-57 acronym of a feature type or attribute, e.g. \"BCNLAT\"; null for none.")] string? Alias,
    [property: Description("How it matched: exact_code (code or S-57 acronym), name_prefix, or text (name, definition, description or a value label in a rule condition).")] string Match,
    [property: Description("One line on the entry, e.g. \"Draws BCNLAT21, BCNLAT15 and 4 more · Display base\" or \"Used by LateralBeacon\".")] string Summary);

/// <summary>The result of <c>search_chart_reference</c>.</summary>
[Description("The chart reference entries that match a query, best first.")]
internal sealed record ChartReferenceSearchResult(
    [property: Description("The products searched, in order.")] IReadOnlyList<string> Searched,
    [property: Description("Every product the chart reference describes: loaded ones first, then every other catalogue.")] IReadOnlyList<ChartReferenceProductDto> Products,
    [property: Description("The hits: exact code or acronym first, then name prefix, then text; within a rank by product, then name.")] IReadOnlyList<ChartReferenceHitDto> Hits,
    [property: Description("True when more entries matched than limit allowed.")] bool Truncated);

/// <summary>A condition under which a rule draws something.</summary>
[Description("One condition of a portrayal rule, as data.")]
internal sealed record ChartReferenceConditionDto(
    [property: Description("attribute, context_parameter, feature_type, primitive, expression (text the reader couldn't break down) or otherwise (the else after several cases).")] string Kind,
    [property: Description("equal, not_equal, less_than, less_than_or_equal, greater_than, greater_than_or_equal, present, absent, is_true, is_false, holds or does_not_hold.")] string Operator,
    [property: Description("The attribute code, context parameter id, feature type code or primitive; null for an expression.")] string? Subject,
    [property: Description("The 1-based index into a multi-valued attribute, e.g. 1 for colour[1]; null for none.")] int? Index,
    [property: Description("The value compared against, e.g. a listed value code \"3\" or \"true\"; null for none.")] string? Value,
    [property: Description("The rule source text of the condition.")] string? Text,
    [property: Description("The line of the condition in its rule file.")] int Line);

/// <summary>Something a feature type's rule draws, or a viewing group it sets.</summary>
[Description("One thing a feature type's rule draws (a portrayal item, colour fill or line) or a viewing group it sets, with when.")]
internal sealed record ChartReferenceUseDto(
    [property: Description("symbol, line_style, area_fill, family, color_fill, pen or viewing_group.")] string What,
    [property: Description("The item id, family prefix, colour token or viewing group id.")] string Item,
    [property: Description("The item's chart reference key, when it has an entry; null otherwise.")] string? Key,
    [property: Description("For a pen, \"<dash> <width> mm\"; for a viewing group, its name and display category; null otherwise.")] string? Detail,
    [property: Description("The conditions that must hold, outermost first; empty for always.")] IReadOnlyList<ChartReferenceConditionDto> Conditions,
    [property: Description("The rule file that draws it, without its extension.")] string RuleFile,
    [property: Description("The line in that rule file.")] int Line,
    [property: Description("The shared rule the use comes from, when not the feature type's own; null otherwise.")] string? ViaRule,
    [property: Description("The chain of rules from the feature type's rule to ViaRule, e.g. [\"DEPARE03\", \"SEABED01\"].")] IReadOnlyList<string> RulePath);

/// <summary>The feature-type part of an entry.</summary>
[Description("What a feature type is and what its rule draws.")]
internal sealed record ChartReferenceFeatureTypeDto(
    [property: Description("Whether the feature catalogue marks it abstract.")] bool IsAbstract,
    [property: Description("The permitted primitives, e.g. point and surface.")] IReadOnlyList<string> Primitives,
    [property: Description("Its own rule file; null when no rule names it.")] string? RuleFile,
    [property: Description("Whether its rule draws anything.")] bool Portrayed,
    [property: Description("The display category of its most visible viewing group, e.g. \"Display base\"; null when the rule sets none.")] string? DisplayCategory,
    [property: Description("The Chart 1 section: the most visible viewing group's description; null when none.")] string? Chart1Section,
    [property: Description("What the rule draws: items, colour fills and lines.")] IReadOnlyList<ChartReferenceUseDto> Draws,
    [property: Description("Every viewing group the rule sets, by geometry or case.")] IReadOnlyList<ChartReferenceUseDto> ViewingGroups,
    [property: Description("The codes of the attributes bound to it.")] IReadOnlyList<string> Attributes);

/// <summary>The portrayal-item part of an entry.</summary>
[Description("A symbol, line style, area fill or symbol family.")]
internal sealed record ChartReferenceItemDto(
    [property: Description("The catalogue file, e.g. \"BCNLAT21.svg\"; null for a family.")] string? FileName,
    [property: Description("A symbol's iho:source, e.g. \"S52Preslib4.0\" or \"new\"; null when not given.")] string? Source,
    [property: Description("Whether this is a family a rule assembles from numbers, e.g. SOUNDG.")] bool IsFamily,
    [property: Description("A family's member ids; empty otherwise.")] IReadOnlyList<string> Members,
    [property: Description("The family the item belongs to; null for none.")] string? Family,
    [property: Description("The feature types whose rules draw it.")] IReadOnlyList<string> UsedBy,
    [property: Description("The rule files that name it or its family.")] IReadOnlyList<string> NamedByRules,
    [property: Description("True when no rule names it, so it is never drawn.")] bool Unreached);

/// <summary>One listed value of an attribute.</summary>
[Description("One listed value of an enumeration attribute.")]
internal sealed record ChartReferenceListedValueDto(
    [property: Description("The numeric code, e.g. \"3\".")] string Code,
    [property: Description("The label, e.g. \"red\".")] string Label,
    [property: Description("The definition; null when none.")] string? Definition);

/// <summary>The attribute part of an entry.</summary>
[Description("A simple attribute and the rules that test it.")]
internal sealed record ChartReferenceAttributeDto(
    [property: Description("The value type, e.g. enumeration or real.")] string ValueType,
    [property: Description("The listed values.")] IReadOnlyList<ChartReferenceListedValueDto> ListedValues,
    [property: Description("The feature types whose rules test it.")] IReadOnlyList<string> ConditionedFeatureTypes);

/// <summary>The display-setting part of an entry.</summary>
[Description("A portrayal context parameter, seen as a display setting.")]
internal sealed record ChartReferenceDisplaySettingDto(
    [property: Description("The parameter type, e.g. boolean or real.")] string Type,
    [property: Description("The catalogue default.")] string Default,
    [property: Description("The feature types whose rules branch on it.")] IReadOnlyList<string> ConditionedFeatureTypes);

/// <summary>A link from an entry to another.</summary>
[Description("A link to a related chart reference entry.")]
internal sealed record ChartReferenceLinkDto(
    [property: Description("How it relates: draws, used_by, member, family, attribute, setting, conditioned_feature_type.")] string Relation,
    [property: Description("The linked entry's key.")] string Key,
    [property: Description("The linked entry's name.")] string Name);

/// <summary>The result of <c>get_chart_reference_entry</c>.</summary>
[Description("One chart reference entry with its links. Exactly one of featureType, item, attribute and displaySetting is set.")]
internal sealed record ChartReferenceEntryDto(
    [property: Description("The entry's key.")] string Key,
    [property: Description("The product spec.")] string Spec,
    [property: Description("feature_type, symbol, line_style, area_fill, attribute or display_setting.")] string Kind,
    [property: Description("The catalogue code.")] string Code,
    [property: Description("The catalogue name.")] string Name,
    [property: Description("The S-57 acronym; null for none.")] string? Alias,
    [property: Description("The catalogue's definition or description; null for none.")] string? Definition,
    [property: Description("The one-line summary search_chart_reference gives.")] string Summary,
    [property: Description("Set for a feature type.")] ChartReferenceFeatureTypeDto? FeatureType,
    [property: Description("Set for a symbol, line style, area fill or family.")] ChartReferenceItemDto? Item,
    [property: Description("Set for an attribute.")] ChartReferenceAttributeDto? Attribute,
    [property: Description("Set for a display setting.")] ChartReferenceDisplaySettingDto? DisplaySetting,
    [property: Description("Links to the related entries, each once.")] IReadOnlyList<ChartReferenceLinkDto> Links);

/// <summary>The product has no portrayal catalogue in the viewer.</summary>
[Description("Raised when the spec has no portrayal catalogue in the viewer, so the chart reference can't describe it (search_chart_reference lists the products it can).")]
internal sealed record ChartReferenceProductNotFound(
    [property: Description("The spec that was asked for.")] string Spec)
    : ToolError("chart_reference_product_not_found", $"The chart reference has no product '{Spec}'.");

/// <summary>The key names no entry.</summary>
[Description("Raised when a chart reference key is well formed but names no entry.")]
internal sealed record ChartReferenceEntryNotFound(
    [property: Description("The key that was asked for.")] string Key)
    : ToolError("chart_reference_entry_not_found", $"The chart reference has no entry '{Key}'.");

/// <summary>Wire names and summaries shared by the chart reference tools.</summary>
internal static class ChartReferenceWire
{
    /// <summary>The wire name of a kind, e.g. <c>feature_type</c>.</summary>
    public static string Kind(ReferenceKind kind) => Snake(kind.ToString());

    /// <summary>Converts a PascalCase enum name to snake_case.</summary>
    public static string Snake(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    /// <summary>Parses a kind given as <c>feature_type</c> or <c>FeatureType</c>.</summary>
    public static bool TryParseKind(string text, out ReferenceKind kind) =>
        Enum.TryParse(text.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal), ignoreCase: true, out kind)
        && Enum.IsDefined(kind)
        && !int.TryParse(text, out _);

    /// <summary>Parses a key <c>&lt;spec&gt;/&lt;kind&gt;/&lt;code&gt;</c>.</summary>
    public static bool TryParseKey(string text, out ReferenceKey key)
    {
        key = default;
        var parts = text.Trim().Split('/', 3);
        if (parts.Length != 3 || parts[2].Length == 0)
            return false;
        if (!SpecName.TryNormalize(parts[0], out var spec) || !TryParseKind(parts[1], out var kind))
            return false;
        key = new ReferenceKey(new SpecRef(spec, default), kind, parts[2]);
        return true;
    }

    /// <summary>One line on the entry.</summary>
    public static string Summary(ReferenceEntry entry) => entry switch
    {
        FeatureTypeEntry f => FeatureTypeSummary(f),
        PortrayalItemEntry { IsFamily: true } i => $"Family of {i.Members.Count} symbols{UsedBy(i.UsedBy)}",
        PortrayalItemEntry { IsUnreached: true } => "Not used by a rule",
        PortrayalItemEntry i => i.UsedBy.Count > 0 ? $"Used by {List(i.UsedBy)}" : $"Named by {List(i.NamedByRules)}",
        AttributeEntry a => a.ListedValues.Count > 0 ? $"{a.ValueType}, {a.ListedValues.Count} values" : a.ValueType,
        DisplaySettingEntry d => $"{d.Type}, default {d.Default}",
        _ => entry.Name,
    };

    private static string FeatureTypeSummary(FeatureTypeEntry f)
    {
        if (!f.IsPortrayed)
            return f.IsAbstract ? "Abstract; not drawn" : "Not drawn by a rule";
        var draws = f.Uses.Select(u => u.ItemId)
            .Concat(f.ColorFills.Select(c => c.Token))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var text = draws.Count > 0 ? $"Draws {List(draws)}" : "Draws lines";
        return f.PrimaryViewingGroup?.DisplayModeName is { } category ? $"{text} · {category}" : text;
    }

    private static string UsedBy(IReadOnlyList<string> usedBy) =>
        usedBy.Count > 0 ? $", used by {List(usedBy)}" : "";

    private static string List(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        <= 3 => string.Join(", ", items),
        _ => $"{string.Join(", ", items.Take(2))} and {items.Count - 2} more",
    };
}

/// <summary>Searches the chart reference (MCP <c>search_chart_reference</c>); headless, it reads the service.</summary>
internal sealed class SearchChartReferenceTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "search_chart_reference";

    /// <summary>The default and largest result counts.</summary>
    public const int DefaultLimit = 50, MaxLimit = 500;

    private readonly ChartReferenceService _service;
    private readonly IRouteEditInvoker _invoker;

    public SearchChartReferenceTool(ChartReferenceService service, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(invoker);
        _service = service;
        _invoker = invoker;
    }

    /// <summary>Searches one product, or every product when <paramref name="spec"/> is null.</summary>
    /// <param name="query">The text to look for.</param>
    /// <param name="spec">The product, e.g. <c>S-101</c>; null for every product.</param>
    /// <param name="kind">The kind of entry, e.g. <c>symbol</c>; null for all.</param>
    /// <param name="limit">The most hits; <see cref="DefaultLimit"/> when null.</param>
    /// <param name="ct">Cancels the call.</param>
    public async Task<ToolResult<ChartReferenceSearchResult>> InvokeAsync(
        string query, string? spec = null, string? kind = null, int? limit = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(query))
            return ToolResult<ChartReferenceSearchResult>.Err(new InvalidArgument(nameof(query), "query must not be blank"));
        ReferenceKind? kindFilter = null;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!ChartReferenceWire.TryParseKind(kind, out var k))
                return ToolResult<ChartReferenceSearchResult>.Err(new InvalidArgument(nameof(kind),
                    "kind must be feature_type, symbol, line_style, area_fill, attribute or display_setting"));
            kindFilter = k;
        }

        var max = limit ?? DefaultLimit;
        if (max is < 1 or > MaxLimit)
            return ToolResult<ChartReferenceSearchResult>.Err(new InvalidArgument(nameof(limit), $"limit must be 1 to {MaxLimit}"));

        var products = await _invoker.InvokeAsync(() => _service.Products).ConfigureAwait(false);
        IReadOnlyList<SpecRef> searched;
        if (string.IsNullOrWhiteSpace(spec))
        {
            searched = [.. products.Select(p => p.Spec)];
        }
        else
        {
            var match = SpecName.TryNormalize(spec, out var name)
                ? products.FirstOrDefault(p => p.Spec.Name == name)
                : null;
            if (match is null)
                return ToolResult<ChartReferenceSearchResult>.Err(new ChartReferenceProductNotFound(spec));
            searched = [match.Spec];
        }

        // Build (or reuse) every index at once; each build runs on a worker.
        var indexes = await Task.WhenAll(searched.Select(s => _service.GetIndexAsync(s, ct))).ConfigureAwait(false);
        var hits = indexes
            .OfType<ChartReferenceIndex>()
            .SelectMany((index, order) => index.Search(query, kindFilter, max + 1).Select(h => (Hit: h, Order: order)))
            .OrderBy(h => h.Hit.Match)
            .ThenBy(h => h.Order)
            .Select(h => h.Hit)
            .Take(max + 1)
            .ToList();

        return ToolResult<ChartReferenceSearchResult>.Ok(new ChartReferenceSearchResult(
            [.. searched.Select(s => s.Name)],
            [.. products.Select(p => new ChartReferenceProductDto(p.Spec.Name, p.IsLoaded))],
            [.. hits.Take(max).Select(Hit)],
            hits.Count > max));
    }

    private static ChartReferenceHitDto Hit(ReferenceHit hit)
    {
        var e = hit.Entry;
        return new ChartReferenceHitDto(
            e.Key.ToString(),
            e.Key.Spec.Name,
            ChartReferenceWire.Kind(e.Key.Kind),
            e.Code,
            e.Name,
            Alias(e),
            ChartReferenceWire.Snake(hit.Match.ToString()),
            ChartReferenceWire.Summary(e));
    }

    internal static string? Alias(ReferenceEntry entry) => entry switch
    {
        FeatureTypeEntry f => f.Alias,
        AttributeEntry a => a.Alias,
        _ => null,
    };
}

/// <summary>Reads one chart reference entry (MCP <c>get_chart_reference_entry</c>); headless, it reads the service.</summary>
internal sealed class GetChartReferenceEntryTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "get_chart_reference_entry";

    private readonly ChartReferenceService _service;

    public GetChartReferenceEntryTool(ChartReferenceService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>Returns the entry <paramref name="key"/> names, with its links.</summary>
    /// <param name="key">A key from <c>search_chart_reference</c>, e.g. <c>S-101/FeatureType/LateralBeacon</c>.</param>
    /// <param name="ct">Cancels the call.</param>
    public async Task<ToolResult<ChartReferenceEntryDto>> InvokeAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(key) || !ChartReferenceWire.TryParseKey(key, out var refKey))
            return ToolResult<ChartReferenceEntryDto>.Err(new InvalidArgument(nameof(key),
                "key must be <spec>/<kind>/<code>, e.g. S-101/FeatureType/LateralBeacon"));

        var index = await _service.GetIndexAsync(refKey.Spec, ct).ConfigureAwait(false);
        if (index is null)
            return ToolResult<ChartReferenceEntryDto>.Err(new ChartReferenceProductNotFound(refKey.Spec.Name));
        if (!index.TryGet(refKey, out var entry))
            return ToolResult<ChartReferenceEntryDto>.Err(new ChartReferenceEntryNotFound(key.Trim()));

        return ToolResult<ChartReferenceEntryDto>.Ok(ToDto(index, entry));
    }

    /// <summary>The wire shape of an entry.</summary>
    internal static ChartReferenceEntryDto ToDto(ChartReferenceIndex index, ReferenceEntry entry)
    {
        var links = new Links(index);
        ChartReferenceFeatureTypeDto? featureType = null;
        ChartReferenceItemDto? item = null;
        ChartReferenceAttributeDto? attribute = null;
        ChartReferenceDisplaySettingDto? setting = null;

        switch (entry)
        {
            case FeatureTypeEntry f:
                featureType = FeatureType(f, links);
                break;
            case PortrayalItemEntry i:
                item = new ChartReferenceItemDto(
                    i.FileName, i.Source, i.IsFamily, i.Members, i.Family, i.UsedBy, i.NamedByRules, i.IsUnreached);
                foreach (var member in i.Members)
                    links.Add("member", ReferenceKind.Symbol, member);
                if (i.Family is { } family)
                    links.Add("family", ReferenceKind.Symbol, family);
                foreach (var code in i.UsedBy)
                    links.Add("used_by", ReferenceKind.FeatureType, code);
                break;
            case AttributeEntry a:
                attribute = new ChartReferenceAttributeDto(
                    a.ValueType,
                    [.. a.ListedValues.Select(v => new ChartReferenceListedValueDto(v.Code, v.Label, v.Definition))],
                    a.ConditionedFeatureTypes);
                foreach (var code in a.ConditionedFeatureTypes)
                    links.Add("conditioned_feature_type", ReferenceKind.FeatureType, code);
                break;
            case DisplaySettingEntry d:
                setting = new ChartReferenceDisplaySettingDto(d.Type, d.Default, d.ConditionedFeatureTypes);
                foreach (var code in d.ConditionedFeatureTypes)
                    links.Add("conditioned_feature_type", ReferenceKind.FeatureType, code);
                break;
        }

        return new ChartReferenceEntryDto(
            entry.Key.ToString(),
            entry.Key.Spec.Name,
            ChartReferenceWire.Kind(entry.Key.Kind),
            entry.Code,
            entry.Name,
            SearchChartReferenceTool.Alias(entry),
            entry.Definition,
            ChartReferenceWire.Summary(entry),
            featureType,
            item,
            attribute,
            setting,
            links.ToList());
    }

    private static ChartReferenceFeatureTypeDto FeatureType(FeatureTypeEntry f, Links links)
    {
        var draws = new List<ChartReferenceUseDto>();
        foreach (var use in f.Uses)
        {
            var what = use.IsFamily ? "family" : ChartReferenceWire.Kind(use.ItemKind);
            var key = links.Add("draws", use.ItemKind, use.ItemId);
            draws.Add(Use(what, use.ItemId, key, null, use));
        }

        foreach (var fill in f.ColorFills)
            draws.Add(Use("color_fill", fill.Token, null, null, fill));
        foreach (var pen in f.Pens)
            draws.Add(Use("pen", pen.Token, null, FormattableString.Invariant($"{pen.Dash} {pen.Width} mm"), pen));

        var groups = f.ViewingGroups
            .Select(g => Use(
                "viewing_group",
                g.ViewingGroup.Id,
                null,
                string.Join(" · ", new[] { g.ViewingGroup.Name, g.ViewingGroup.DisplayModeName }.OfType<string>()) is { Length: > 0 } d ? d : null,
                g))
            .ToList();

        foreach (var code in f.AttributeCodes)
            links.Add("attribute", ReferenceKind.Attribute, code);
        foreach (var condition in f.Uses.Cast<RuleUse>().Concat(f.ColorFills).Concat(f.Pens).Concat(f.ViewingGroups).SelectMany(u => u.Conditions))
        {
            if (condition.Kind == RuleConditionKind.ContextParameter && condition.Subject is { } parameter)
                links.Add("setting", ReferenceKind.DisplaySetting, parameter);
        }

        return new ChartReferenceFeatureTypeDto(
            f.IsAbstract,
            f.PermittedPrimitives,
            f.RuleFile,
            f.IsPortrayed,
            f.PrimaryViewingGroup?.DisplayModeName ?? f.PrimaryViewingGroup?.DisplayModeId,
            f.PrimaryViewingGroup?.Description,
            draws,
            groups,
            f.AttributeCodes);
    }

    private static ChartReferenceUseDto Use(string what, string item, string? key, string? detail, RuleUse use) =>
        new(what, item, key, detail, [.. use.Conditions.Select(Condition)], use.RuleFile, use.Line, use.ViaRule, use.RulePath);

    private static ChartReferenceConditionDto Condition(RuleCondition c) => new(
        ChartReferenceWire.Snake(c.Kind.ToString()),
        ChartReferenceWire.Snake(c.Operator.ToString()),
        c.Subject,
        c.Index,
        c.Value,
        c.Text,
        c.Line);

    /// <summary>Collects an entry's links, each once, to entries the index has.</summary>
    private sealed class Links(ChartReferenceIndex index)
    {
        private readonly List<ChartReferenceLinkDto> _links = [];
        private readonly HashSet<(string Relation, string Key)> _seen = [];

        /// <summary>Adds the link when the index has the entry, and returns the entry's key, or null.</summary>
        public string? Add(string relation, ReferenceKind kind, string code)
        {
            if (!index.TryGet(new ReferenceKey(index.Spec, kind, code), out var target))
                return null;
            var key = target.Key.ToString();
            if (_seen.Add((relation, key)))
                _links.Add(new ChartReferenceLinkDto(relation, key, target.Name));
            return key;
        }

        public IReadOnlyList<ChartReferenceLinkDto> ToList() => _links;
    }
}

/// <summary>The MCP adapters for the chart reference tools.</summary>
internal static class ChartReferenceMcpAdapter
{
    /// <summary>Creates <c>search_chart_reference</c>.</summary>
    public static McpServerTool Create(SearchChartReferenceTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("The text to look for, without regard to case: a code or S-57 acronym (\"BCNLAT\", \"BCNLAT21\"), the start of a name (\"lateral\"), or words in a definition, symbol description or value label (\"red\").")] string query,
            [Description("The product to search, e.g. \"S-101\"; omit to search every product.")] string? spec = null,
            [Description("Only entries of this kind: feature_type, symbol, line_style, area_fill, attribute or display_setting; omit for all.")] string? kind = null,
            [Description("The most hits to return, 1 to 500; default 50.")] int? limit = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(query, spec, kind, limit, ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SearchChartReferenceTool.Name,
            Description =
                "Searches the chart reference: the feature types, symbols, line styles, area fills, attributes and display "
                + "settings of a product's feature and portrayal catalogues, as the viewer has them loaded, with what each "
                + "portrayal rule draws. Ranks an exact code or S-57 acronym first, then a name or code prefix, then other "
                + "text (names, definitions, symbol descriptions and the value labels rules test, so \"red\" finds Lateral "
                + "Beacon). Each hit gives its key, spec, kind, code, name, alias, how it matched and a one-line summary; "
                + "pass the key to get_chart_reference_entry. Also lists the products (loaded ones first, then every other "
                + "catalogue, S-401 included). The first search of a product builds its index (a few hundred ms). "
                + "Read-only; viewer-injected tool.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }

    /// <summary>Creates <c>get_chart_reference_entry</c>.</summary>
    public static McpServerTool Create(GetChartReferenceEntryTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("The entry's key from search_chart_reference, \"<spec>/<kind>/<code>\", e.g. \"S-101/FeatureType/LateralBeacon\" or \"S-101/Symbol/BCNLAT21\". The code matches without regard to case.")] string key,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(key, ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = GetChartReferenceEntryTool.Name,
            Description =
                "Reads one chart reference entry. A feature type gives its primitives, display category, Chart 1 section, "
                + "what its rule draws (symbols, line styles, area fills, symbol families, colour fills and lines) and the "
                + "viewing groups it sets, each with its conditions as data (attribute, context parameter, primitive...), "
                + "rule file and line, and the shared rule it comes through. A symbol, line style or area fill gives its file, "
                + "iho:source, family and the feature types that use it; an attribute its listed values; a display setting its "
                + "type and default. Links name the related entries by key. Read-only; viewer-injected tool.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
