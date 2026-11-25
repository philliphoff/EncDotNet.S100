using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EncDotNet.S100.Core;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// A searchable reference of one product specification's catalogues: each
/// feature type with the symbols, fills and lines its portrayal rule draws
/// and when, each portrayal item with the feature types that use it, each
/// attribute with its listed values, and each display setting.
/// </summary>
/// <remarks>
/// <para>
/// The index is built from the feature and portrayal catalogues a viewer
/// has loaded, so a catalogue the user adds or replaces is described as it
/// draws. Rules are read statically: Lua rules (S-101, S-131, S-401) with
/// their <c>if</c>/<c>elseif</c>/<c>else</c> conditions and <c>require</c>d
/// shared rules, XSLT rules (the GML products) by template. See
/// <c>docs/design/symbol-dictionary.md</c> §2–3.
/// </para>
/// <para>
/// An index is immutable and safe to read from any thread.
/// </para>
/// </remarks>
public sealed partial class ChartReferenceIndex
{
    private const int SourceHeadLength = 2048;

    private readonly Dictionary<(ReferenceKind Kind, string Code), ReferenceEntry> _entries;
    private readonly List<SearchRow> _searchRows;

    private ChartReferenceIndex(ChartReferenceIndex other, RuleProbeSet? probes)
    {
        Spec = other.Spec;
        FeatureTypes = other.FeatureTypes;
        Items = other.Items;
        Attributes = other.Attributes;
        DisplaySettings = other.DisplaySettings;
        S57Aliases = other.S57Aliases;
        _entries = other._entries;
        _searchRows = other._searchRows;
        Probes = probes;
    }

    private ChartReferenceIndex(
        SpecRef spec,
        IReadOnlyList<FeatureTypeEntry> featureTypes,
        IReadOnlyList<PortrayalItemEntry> items,
        IReadOnlyList<AttributeEntry> attributes,
        IReadOnlyList<DisplaySettingEntry> displaySettings,
        IReadOnlyDictionary<string, string> conditionText)
    {
        Spec = spec;
        FeatureTypes = featureTypes;
        Items = items;
        Attributes = attributes;
        DisplaySettings = displaySettings;

        _entries = new Dictionary<(ReferenceKind, string), ReferenceEntry>(KeyComparer.Instance);
        _searchRows = [];
        foreach (var entry in featureTypes.Cast<ReferenceEntry>().Concat(items).Concat(attributes).Concat(displaySettings))
        {
            _entries.TryAdd((entry.Key.Kind, entry.Code), entry);
            _searchRows.Add(SearchRow.For(entry, conditionText.GetValueOrDefault(entry.Code)));
        }

        // Built once, with the index, for picks of S-57 cells (R9, #1678).
        S57Aliases = new S57Aliases(this);
    }

    /// <summary>The product specification the index describes.</summary>
    public SpecRef Spec { get; }

    /// <summary>The feature types, ordered by name.</summary>
    public IReadOnlyList<FeatureTypeEntry> FeatureTypes { get; }

    /// <summary>
    /// The symbols, line styles and area fills, ordered by id, followed by
    /// the symbol families the rules assemble from numbers.
    /// </summary>
    public IReadOnlyList<PortrayalItemEntry> Items { get; }

    /// <summary>The simple attributes, ordered by name.</summary>
    public IReadOnlyList<AttributeEntry> Attributes { get; }

    /// <summary>The display settings (context parameters), in catalogue order.</summary>
    public IReadOnlyList<DisplaySettingEntry> DisplaySettings { get; }

    /// <summary>
    /// What each feature type's rule draws for the values it reads, when
    /// the rules have been probed for these catalogues (design note §7,
    /// R13a); <see langword="null"/> otherwise. The reference works
    /// without them, from <see cref="FeatureTypeEntry.Uses"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="BuildAsync"/> doesn't probe. A caller attaches results with
    /// <see cref="WithProbes"/>: those committed with a bundled catalogue
    /// (<see cref="RuleProbeStore.TryLoadBundledAsync"/>), cached on disk
    /// (<see cref="RuleProbeStore.TryLoadCached"/>), or from
    /// <see cref="RuleProber.ProbeAsync"/>.
    /// </remarks>
    public RuleProbeSet? Probes { get; }

    /// <summary>
    /// Returns this index with <paramref name="probes"/> attached. The
    /// entries are shared, not copied.
    /// </summary>
    /// <param name="probes">The probe results for this index's catalogues, or <see langword="null"/> to drop them.</param>
    /// <returns>The index with the probes.</returns>
    /// <exception cref="ArgumentException"><paramref name="probes"/> are for another product.</exception>
    public ChartReferenceIndex WithProbes(RuleProbeSet? probes)
    {
        if (probes is not null && !string.Equals(probes.Spec, Spec.Name, StringComparison.Ordinal))
            throw new ArgumentException($"The probes are for {probes.Spec}, not {Spec.Name}.", nameof(probes));
        return ReferenceEquals(probes, Probes) ? this : new ChartReferenceIndex(this, probes);
    }

    /// <summary>
    /// The S-57 acronyms of the feature types and attributes, for S-57 cells
    /// drawn with this product's catalogue (design note §4.3, R9).
    /// </summary>
    public S57Aliases S57Aliases { get; }

    /// <summary>
    /// The feature type an S-57 object class acronym names through the
    /// feature catalogue's alias: <c>BCNLAT</c> gives Lateral Beacon.
    /// </summary>
    /// <param name="acronym">The acronym; ordinal, without regard to case.</param>
    /// <returns>The entry, or <see langword="null"/> when no entry, or more than one, has the alias.</returns>
    public FeatureTypeEntry? FindFeatureTypeByS57Alias(string acronym) => S57Aliases.FeatureType(acronym);

    /// <summary>
    /// The attribute an S-57 attribute acronym names through the feature
    /// catalogue's alias: <c>COLOUR</c> gives Colour. Its listed values
    /// share the S-57 codes (<see cref="AttributeEntry.ListedValuesOf"/>).
    /// </summary>
    /// <param name="acronym">The acronym; ordinal, without regard to case.</param>
    /// <returns>The entry, or <see langword="null"/> when no entry, or more than one, has the alias.</returns>
    public AttributeEntry? FindAttributeByS57Alias(string acronym) => S57Aliases.Attribute(acronym);

    /// <summary>
    /// Builds the index for <paramref name="spec"/> from its catalogues.
    /// </summary>
    /// <param name="spec">The product specification the catalogues are for.</param>
    /// <param name="features">
    /// The feature catalogue, or <see langword="null"/> when the product has
    /// none. Without it, feature types are taken from the rule files and
    /// have no names, definitions or attributes.
    /// </param>
    /// <param name="portrayal">The portrayal catalogue, whose rule files and symbols are read.</param>
    /// <param name="cancellationToken">Cancels the build.</param>
    /// <returns>The index.</returns>
    /// <remarks>
    /// <para>
    /// Line style and area fill files are read for the symbols they place,
    /// so a symbol only a line style or fill places is reached through it
    /// (<see cref="PortrayalItemEntry.UsedIn"/>).
    /// </para>
    /// <para>
    /// Reading a rule file, colour profile, symbol, line style or area fill
    /// that the catalogue lists but its source lacks isn't an error: the
    /// file is skipped.
    /// </para>
    /// </remarks>
    public static async ValueTask<ChartReferenceIndex> BuildAsync(
        SpecRef spec,
        FeatureCatalogue? features,
        PortrayalCatalogueProvider portrayal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(portrayal);
        var catalogue = portrayal.Catalogue;

        var catalogueItems = CatalogueItems(catalogue);
        var vocabulary = new RuleVocabulary(
            catalogueItems
                .GroupBy(i => i.Item.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<ReferenceKind>)g.Select(i => i.Kind).Distinct().ToList(), StringComparer.Ordinal),
            catalogueItems
                .GroupBy(i => Path.GetFileNameWithoutExtension(i.Item.FileName), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => (g.First().Item.Id, g.First().Kind), StringComparer.Ordinal),
            FamilyPrefixes(catalogueItems),
            await ReadColourTokensAsync(portrayal, cancellationToken).ConfigureAwait(false));

        var featureCodes = features?.FeatureTypes.Select(f => f.Code).ToHashSet(StringComparer.Ordinal);
        var rules = await ReadRulesAsync(portrayal, vocabulary, featureCodes, cancellationToken).ConfigureAwait(false);

        var viewingGroups = new ViewingGroupCatalogue(catalogue);
        var featureFacts = LinkFeatureFacts(rules, featureCodes);

        var featureTypes = BuildFeatureTypes(spec, features, featureFacts, viewingGroups);
        var items = await BuildItemsAsync(spec, portrayal, catalogueItems, rules, featureTypes, cancellationToken)
            .ConfigureAwait(false);
        var attributes = BuildAttributes(spec, features, featureTypes);
        var displaySettings = BuildDisplaySettings(spec, catalogue, featureTypes);

        return new ChartReferenceIndex(
            spec, featureTypes, items, attributes, displaySettings, ConditionValueText(features, featureTypes));
    }

    /// <summary>
    /// Looks up the entry <paramref name="key"/> addresses.
    /// </summary>
    /// <param name="key">The key. Codes match without regard to case.</param>
    /// <param name="entry">The entry, when found.</param>
    /// <returns><see langword="true"/> when the index has the entry.</returns>
    public bool TryGet(ReferenceKey key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ReferenceEntry? entry)
    {
        entry = null;
        if (key.Spec != Spec || key.Code is null) return false;
        return _entries.TryGetValue((key.Kind, key.Code), out entry);
    }

    /// <summary>
    /// Finds the entries that match <paramref name="query"/>, best first: an
    /// exact code or S-57 acronym, then a name or code prefix, then other
    /// text (names, definitions, symbol descriptions and the value labels of
    /// rule conditions). Within a rank, entries are ordered by name.
    /// </summary>
    /// <param name="query">The text to look for, without regard to case.</param>
    /// <param name="kind">Limits the results to one kind of entry, or <see langword="null"/> for all.</param>
    /// <param name="limit">The most results to return.</param>
    /// <returns>The matches; empty when <paramref name="query"/> is blank.</returns>
    public IReadOnlyList<ReferenceHit> Search(string query, ReferenceKind? kind = null, int limit = 200)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        var q = query.Trim();
        if (q.Length == 0 || limit == 0) return [];

        var hits = new List<(ReferenceMatch Match, SearchRow Row)>();
        foreach (var row in _searchRows)
        {
            if (kind is { } k && row.Entry.Key.Kind != k) continue;
            if (row.Match(q) is { } match) hits.Add((match, row));
        }

        return hits
            .OrderBy(h => h.Match)
            .ThenBy(h => h.Row.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(h => h.Row.Entry.Key.Kind)
            .Take(limit)
            .Select(h => new ReferenceHit(h.Row.Entry, h.Match))
            .ToList();
    }

    private static List<(CatalogItem Item, ReferenceKind Kind, string Folder)> CatalogueItems(PortrayalCatalogue catalogue) =>
    [
        .. catalogue.Symbols.Select(i => (i, ReferenceKind.Symbol, "Symbols")),
        .. catalogue.LineStyles.Select(i => (i, ReferenceKind.LineStyle, "LineStyles")),
        .. catalogue.AreaFills.Select(i => (i, ReferenceKind.AreaFill, "AreaFills")),
    ];

    /// <summary>
    /// The prefixes rules may assemble symbol ids from: an id less its last
    /// two characters, shared by at least two symbols and not itself an id
    /// (<c>SOUNDG</c> for <c>SOUNDG00</c>…<c>SOUNDGC2</c>).
    /// </summary>
    private static HashSet<string> FamilyPrefixes(List<(CatalogItem Item, ReferenceKind Kind, string Folder)> items)
    {
        var ids = items.Select(i => i.Item.Id).ToHashSet(StringComparer.Ordinal);
        return items
            .Where(i => i.Kind == ReferenceKind.Symbol && i.Item.Id.Length > 4)
            .GroupBy(i => i.Item.Id[..^2], StringComparer.Ordinal)
            .Where(g => g.Count() >= 2 && !ids.Contains(g.Key) && g.Key.Any(char.IsLetter))
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static async Task<HashSet<string>> ReadColourTokensAsync(
        PortrayalCatalogueProvider portrayal, CancellationToken cancellationToken)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in portrayal.Catalogue.ColorProfiles)
        {
            try
            {
                await using var stream = await portrayal.FetchAssetAsync(profile, "ColorProfiles", cancellationToken)
                    .ConfigureAwait(false);
                tokens.UnionWith(ColorProfileReader.Read(stream, "Day").Colors.Keys);
            }
            catch (Exception ex) when (IsMissingAsset(ex) || ex is System.Xml.XmlException)
            {
                // A profile the source lacks or can't parse adds no tokens.
            }
        }

        return tokens;
    }

    private static bool IsMissingAsset(Exception ex) =>
        ex is IOException or PortrayalAssetNotFoundException or UnauthorizedAccessException;

    private static async Task<Dictionary<string, RuleFacts>> ReadRulesAsync(
        PortrayalCatalogueProvider portrayal,
        RuleVocabulary vocabulary,
        IReadOnlySet<string>? featureCodes,
        CancellationToken cancellationToken)
    {
        var rules = new Dictionary<string, RuleFacts>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in portrayal.Catalogue.RuleFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(file.FileName)) continue;

            var isLua = string.Equals(file.FileFormat, "LUA", StringComparison.OrdinalIgnoreCase)
                || file.FileName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase);
            var isXslt = string.Equals(file.FileFormat, "XSLT", StringComparison.OrdinalIgnoreCase)
                || file.FileName.EndsWith(".xsl", StringComparison.OrdinalIgnoreCase)
                || file.FileName.EndsWith(".xslt", StringComparison.OrdinalIgnoreCase);
            if (!isLua && !isXslt) continue;

            string source;
            try
            {
                await using var stream = await portrayal.FetchAssetAsync(file, cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                source = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsMissingAsset(ex))
            {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(file.FileName);
            rules[name] = isLua
                ? LuaRuleReader.Read(name, source, vocabulary)
                : XsltRuleReader.Read(name, source, vocabulary, featureCodes);
        }

        // The XSLT engine reaches stylesheets through xsl:include and
        // xsl:import, including ones the catalogue lists under another name
        // (S-421 lists RouteWaypoint.xml and ships RouteWaypoint.xsl).
        var includes = new Queue<string>(rules.Values.SelectMany(r => r.Includes));
        while (includes.Count > 0)
        {
            var href = includes.Dequeue();
            var name = Path.GetFileNameWithoutExtension(href);
            if (!seen.Add(Path.GetFileName(href)) || rules.ContainsKey(name)) continue;

            string source;
            try
            {
                await using var stream = await portrayal.FetchRuleAsync(href, cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                source = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsMissingAsset(ex))
            {
                continue;
            }

            var included = XsltRuleReader.Read(name, source, vocabulary, featureCodes);
            rules[name] = included;
            foreach (var next in included.Includes) includes.Enqueue(next);
        }

        return rules;
    }

    /// <summary>A rule fact as it reaches a feature type.</summary>
    private readonly record struct LinkedFact(RuleFact Fact, string RuleFile, IReadOnlyList<string> Path);

    /// <summary>
    /// Gathers, for each feature type, the facts of its own rule and of the
    /// shared rules it requires, transitively.
    /// </summary>
    private static Dictionary<string, (string? RuleFile, List<LinkedFact> Facts)> LinkFeatureFacts(
        Dictionary<string, RuleFacts> rules, IReadOnlySet<string>? featureCodes)
    {
        var result = new Dictionary<string, (string? RuleFile, List<LinkedFact> Facts)>(StringComparer.Ordinal);

        (string? RuleFile, List<LinkedFact> Facts) For(string code)
        {
            if (!result.TryGetValue(code, out var linked))
            {
                linked = (null, []);
                result[code] = linked;
            }

            return linked;
        }

        var namedTemplates = new Dictionary<string, (string RuleFile, NamedTemplate Template)>(StringComparer.Ordinal);
        foreach (var rule in rules.Values)
        {
            foreach (var (name, template) in rule.NamedTemplates) namedTemplates.TryAdd(name, (rule.Name, template));
        }

        foreach (var rule in rules.Values)
        {
            // XSLT: each template names its feature type, and may call named templates.
            foreach (var call in rule.Calls)
            {
                if (call.Owner is not { } caller) continue;
                var linked = For(caller);
                result[caller] = (linked.RuleFile ?? rule.Name, linked.Facts);
                linked.Facts.AddRange(CalledFacts(call, namedTemplates));
            }

            foreach (var fact in rule.Facts)
            {
                if (fact.Owner is { } owner)
                {
                    var linked = For(owner);
                    linked.Facts.Add(new LinkedFact(fact, rule.Name, []));
                    result[owner] = (linked.RuleFile ?? rule.Name, linked.Facts);
                }
            }

            // Lua: the rule file named after the feature type, and what it requires.
            var isOwner = featureCodes is null
                ? rule.FeatureEntryPoints.Contains(rule.Name)
                : featureCodes.Contains(rule.Name);
            if (!isOwner) continue;

            var facts = For(rule.Name).Facts;
            result[rule.Name] = (rule.Name, facts);
            facts.AddRange(rule.Facts.Where(f => f.Owner is null).Select(f => new LinkedFact(f, rule.Name, [])));

            var visited = new HashSet<string>(StringComparer.Ordinal) { rule.Name };
            var queue = new Queue<(string Name, IReadOnlyList<string> Path)>();
            foreach (var required in rule.Requires) queue.Enqueue((required, [required]));
            while (queue.Count > 0)
            {
                var (name, path) = queue.Dequeue();
                if (!visited.Add(name) || !rules.TryGetValue(name, out var shared)) continue;
                facts.AddRange(shared.Facts.Where(f => f.Owner is null).Select(f => new LinkedFact(f, shared.Name, path)));
                foreach (var next in shared.Requires) queue.Enqueue((next, [.. path, next]));
            }
        }

        return result;
    }

    /// <summary>
    /// The facts of the named templates <paramref name="call"/> reaches,
    /// transitively, under the call's conditions.
    /// </summary>
    private static IEnumerable<LinkedFact> CalledFacts(
        TemplateCall call, Dictionary<string, (string RuleFile, NamedTemplate Template)> namedTemplates)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(string Name, IReadOnlyList<string> Path, IReadOnlyList<RuleCondition> Conditions)>();
        queue.Enqueue((call.Template, [call.Template], call.Conditions));
        while (queue.Count > 0)
        {
            var (name, path, conditions) = queue.Dequeue();
            if (!visited.Add(name) || !namedTemplates.TryGetValue(name, out var target)) continue;
            foreach (var fact in target.Template.Facts)
            {
                yield return new LinkedFact(fact with { Conditions = [.. conditions, .. fact.Conditions] }, target.RuleFile, path);
            }

            foreach (var next in target.Template.Calls)
            {
                queue.Enqueue((next.Template, [.. path, next.Template], [.. conditions, .. next.Conditions]));
            }
        }
    }

    private static List<FeatureTypeEntry> BuildFeatureTypes(
        SpecRef spec,
        FeatureCatalogue? features,
        Dictionary<string, (string? RuleFile, List<LinkedFact> Facts)> featureFacts,
        ViewingGroupCatalogue viewingGroups)
    {
        var types = features?.FeatureTypes.ToList()
            ?? featureFacts.Keys.Select(code => new FeatureType { Code = code, Name = code }).ToList();

        var entries = new List<FeatureTypeEntry>(types.Count);
        foreach (var type in types)
        {
            featureFacts.TryGetValue(type.Code, out var linked);
            var facts = linked.Facts ?? [];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var uses = new List<SymbolUse>();
            var fills = new List<ColorFillUse>();
            var pens = new List<PenUse>();
            var groups = new List<ViewingGroupUse>();

            foreach (var (fact, ruleFile, path) in facts)
            {
                if (IsForOtherFeatureType(fact, type.Code) || !seen.Add(DedupeKey(fact, ruleFile))) continue;
                var via = path.Count > 0 ? path[0] : null;
                switch (fact.Kind)
                {
                    case RuleFactKind.Item:
                    case RuleFactKind.Family:
                        uses.Add(new SymbolUse
                        {
                            ItemId = fact.Value,
                            ItemKind = fact.Kind == RuleFactKind.Family ? ReferenceKind.Symbol : fact.ItemKind,
                            IsFamily = fact.Kind == RuleFactKind.Family,
                            Conditions = fact.Conditions,
                            RuleFile = ruleFile,
                            Line = fact.Line,
                            ViaRule = via,
                            RulePath = path,
                        });
                        break;
                    case RuleFactKind.ColorFill:
                        fills.Add(new ColorFillUse
                        {
                            Token = fact.Value,
                            Conditions = fact.Conditions,
                            RuleFile = ruleFile,
                            Line = fact.Line,
                            ViaRule = via,
                            RulePath = path,
                        });
                        break;
                    case RuleFactKind.Pen:
                        pens.Add(new PenUse
                        {
                            Dash = fact.Dash,
                            Width = fact.Width,
                            Token = fact.Value,
                            Conditions = fact.Conditions,
                            RuleFile = ruleFile,
                            Line = fact.Line,
                            ViaRule = via,
                            RulePath = path,
                        });
                        break;
                    case RuleFactKind.ViewingGroup:
                        groups.Add(new ViewingGroupUse
                        {
                            ViewingGroup = viewingGroups.Get(fact.Value),
                            Conditions = fact.Conditions,
                            RuleFile = ruleFile,
                            Line = fact.Line,
                            ViaRule = via,
                            RulePath = path,
                        });
                        break;
                }
            }

            entries.Add(new FeatureTypeEntry
            {
                Key = new ReferenceKey(spec, ReferenceKind.FeatureType, type.Code),
                Name = type.Name,
                Definition = type.Definition,
                Alias = type.Alias,
                IsAbstract = type.IsAbstract,
                PermittedPrimitives = type.PermittedPrimitives,
                AttributeCodes = type.AttributeBindings.Select(b => b.AttributeRef).ToList(),
                RuleFile = linked.RuleFile,
                Uses = uses,
                ColorFills = fills,
                Pens = pens,
                ViewingGroups = groups,
                PrimaryViewingGroup = viewingGroups.MostVisible(groups),
            });
        }

        entries.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return entries;
    }

    /// <summary>
    /// Whether a shared rule draws the fact only for another feature type,
    /// as DEPARE03 draws DRGARE01 only when <c>feature.Code == 'DredgedArea'</c>.
    /// </summary>
    private static bool IsForOtherFeatureType(RuleFact fact, string code)
    {
        foreach (var condition in fact.Conditions)
        {
            if (condition.Kind != RuleConditionKind.FeatureType) continue;
            var same = string.Equals(condition.Value, code, StringComparison.Ordinal);
            if (condition.Operator == ConditionOperator.Equal ? !same : same) return true;
        }

        return false;
    }

    private static string DedupeKey(RuleFact fact, string ruleFile)
    {
        var sb = new StringBuilder()
            .Append((int)fact.Kind).Append('|').Append(fact.Value).Append('|').Append((int)fact.ItemKind)
            .Append('|').Append(fact.Dash).Append('|').Append(fact.Width.ToString(CultureInfo.InvariantCulture))
            .Append('|').Append(ruleFile).Append('|').Append(fact.Line);
        foreach (var condition in fact.Conditions) sb.Append('|').Append(condition);
        return sb.ToString();
    }

    [GeneratedRegex(@"iho:source\s*=\s*""([^""]*)""")]
    private static partial Regex SourcePattern();

    private static async Task<List<PortrayalItemEntry>> BuildItemsAsync(
        SpecRef spec,
        PortrayalCatalogueProvider portrayal,
        List<(CatalogItem Item, ReferenceKind Kind, string Folder)> catalogueItems,
        Dictionary<string, RuleFacts> rules,
        List<FeatureTypeEntry> featureTypes,
        CancellationToken cancellationToken)
    {
        // Which rules name each item or family, and which feature types draw it.
        var namedBy = new Dictionary<(ReferenceKind, string), SortedSet<string>>();
        var familiesNamedBy = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var rule in rules.Values)
        {
            foreach (var fact in rule.Facts.Concat(rule.NamedTemplates.Values.SelectMany(t => t.Facts)))
            {
                if (fact.Kind == RuleFactKind.Item)
                {
                    Add(namedBy, (fact.ItemKind, fact.Value), rule.Name);
                }
                else if (fact.Kind == RuleFactKind.Family)
                {
                    Add(familiesNamedBy, fact.Value, rule.Name);
                }
            }
        }

        var usedBy = new Dictionary<(ReferenceKind, string), SortedSet<string>>();
        var familiesUsedBy = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var feature in featureTypes)
        {
            foreach (var use in feature.Uses)
            {
                if (use.IsFamily) Add(familiesUsedBy, use.ItemId, feature.Code);
                else Add(usedBy, (use.ItemKind, use.ItemId), feature.Code);
            }
        }

        // Which line styles and area fills place each symbol (open point 6 of #1643).
        var places = await ReadPlacementsAsync(portrayal, catalogueItems, cancellationToken).ConfigureAwait(false);
        var usedIn = new Dictionary<string, List<ReferenceKey>>(StringComparer.Ordinal);
        foreach (var ((kind, id), symbols) in places.OrderBy(p => p.Key.Kind).ThenBy(p => p.Key.Id, StringComparer.Ordinal))
        {
            foreach (var symbol in symbols)
            {
                if (!usedIn.TryGetValue(symbol, out var list)) usedIn[symbol] = list = [];
                list.Add(new ReferenceKey(spec, kind, id));
            }
        }

        var members = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var entries = new List<PortrayalItemEntry>(catalogueItems.Count + familiesNamedBy.Count);
        foreach (var (item, kind, folder) in catalogueItems.OrderBy(i => i.Item.Id, StringComparer.Ordinal).ThenBy(i => i.Kind))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var family = kind == ReferenceKind.Symbol && item.Id.Length > 4 && familiesNamedBy.ContainsKey(item.Id[..^2])
                ? item.Id[..^2]
                : null;
            if (family is not null)
            {
                if (!members.TryGetValue(family, out var list)) members[family] = list = [];
                list.Add(item.Id);
            }

            var named = new SortedSet<string>(StringComparer.Ordinal);
            if (namedBy.TryGetValue((kind, item.Id), out var direct)) named.UnionWith(direct);
            if (family is not null) named.UnionWith(familiesNamedBy[family]);
            var users = new SortedSet<string>(StringComparer.Ordinal);
            if (usedBy.TryGetValue((kind, item.Id), out var directUsers)) users.UnionWith(directUsers);
            if (family is not null && familiesUsedBy.TryGetValue(family, out var familyUsers)) users.UnionWith(familyUsers);
            var containers = kind == ReferenceKind.Symbol && usedIn.TryGetValue(item.Id, out var placedBy) ? placedBy : [];

            // A symbol a line style or fill places is drawn when a rule names that line style or fill.
            var reached = named.Count > 0 || containers.Any(c => namedBy.ContainsKey((c.Kind, c.Code)));

            entries.Add(new PortrayalItemEntry
            {
                Key = new ReferenceKey(spec, kind, item.Id),
                Name = string.IsNullOrWhiteSpace(item.Description.Name) ? item.Id : item.Description.Name,
                Definition = string.IsNullOrWhiteSpace(item.Description.DescriptionText) ? null : item.Description.DescriptionText.Trim(),
                FileName = item.FileName,
                Source = kind == ReferenceKind.Symbol
                    ? await ReadSourceAsync(portrayal, item, folder, cancellationToken).ConfigureAwait(false)
                    : null,
                Family = family,
                UsedBy = users.ToList(),
                NamedByRules = named.ToList(),
                UsedIn = containers,
                Places = places.TryGetValue((kind, item.Id), out var placed) ? placed : [],
                IsUnreached = !reached,
            });
        }

        foreach (var (prefix, rulesNaming) in familiesNamedBy.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            entries.Add(new PortrayalItemEntry
            {
                Key = new ReferenceKey(spec, ReferenceKind.Symbol, prefix),
                Name = prefix,
                IsFamily = true,
                Members = members.TryGetValue(prefix, out var list) ? list : [],
                UsedBy = familiesUsedBy.TryGetValue(prefix, out var users) ? users.ToList() : [],
                NamedByRules = rulesNaming.ToList(),
                IsUnreached = rulesNaming.Count == 0,
            });
        }

        return entries;

        static void Add<TKey>(Dictionary<TKey, SortedSet<string>> map, TKey key, string value)
            where TKey : notnull
        {
            if (!map.TryGetValue(key, out var set)) map[key] = set = new SortedSet<string>(StringComparer.Ordinal);
            set.Add(value);
        }
    }

    /// <summary>
    /// Reads the symbols each line style places along its line and each
    /// area fill tiles as its pattern, keyed by the line style or fill, in
    /// file order without repeats. A file the source lacks or can't parse
    /// places nothing.
    /// </summary>
    private static async Task<Dictionary<(ReferenceKind Kind, string Id), List<string>>> ReadPlacementsAsync(
        PortrayalCatalogueProvider portrayal,
        List<(CatalogItem Item, ReferenceKind Kind, string Folder)> catalogueItems,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<(ReferenceKind Kind, string Id), List<string>>();
        foreach (var (item, kind, folder) in catalogueItems)
        {
            if (kind is not (ReferenceKind.LineStyle or ReferenceKind.AreaFill)) continue;
            cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<string?> symbols;
            try
            {
                await using var stream = await portrayal.FetchAssetAsync(item, folder, cancellationToken).ConfigureAwait(false);
                symbols = kind == ReferenceKind.LineStyle
                    ? LineStyleSymbols(LineStyleReader.Read(stream, item.Id))
                    : [AreaFillReader.Read(stream, item.Id).PatternSymbol];
            }
            catch (Exception ex) when (IsMissingAsset(ex) || ex is System.Xml.XmlException)
            {
                continue;
            }

            var ids = symbols
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (ids.Count > 0) result.TryAdd((kind, item.Id), ids);
        }

        return result;

        // A composite line style places the symbols of its components.
        static IEnumerable<string?> LineStyleSymbols(LineStyle style) =>
            style.Symbols.Select(s => s.Reference).Concat(style.Components.SelectMany(LineStyleSymbols));
    }

    /// <summary>Reads a symbol's <c>iho:source</c> from the head of its SVG.</summary>
    private static async Task<string?> ReadSourceAsync(
        PortrayalCatalogueProvider portrayal, CatalogItem item, string folder, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await portrayal.FetchAssetAsync(item, folder, cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var buffer = new char[SourceHeadLength];
            var read = await reader.ReadBlockAsync(buffer, cancellationToken).ConfigureAwait(false);
            var match = SourcePattern().Match(new string(buffer, 0, read));
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception ex) when (IsMissingAsset(ex))
        {
            return null;
        }
    }

    private static List<AttributeEntry> BuildAttributes(
        SpecRef spec, FeatureCatalogue? features, List<FeatureTypeEntry> featureTypes)
    {
        if (features is null) return [];

        var conditioned = ConditionSubjects(featureTypes, RuleConditionKind.Attribute);
        return features.SimpleAttributes
            .Select(a => new AttributeEntry
            {
                Key = new ReferenceKey(spec, ReferenceKind.Attribute, a.Code),
                Name = a.Name,
                Definition = a.Definition,
                Alias = a.Alias,
                ValueType = a.ValueType,
                ListedValues = a.ListedValues,
                ConditionedFeatureTypes = conditioned.TryGetValue(a.Code, out var codes) ? codes.ToList() : [],
            })
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    [GeneratedRegex(@"contextParameters\.(\w+)")]
    private static partial Regex ContextParameterPattern();

    private static List<DisplaySettingEntry> BuildDisplaySettings(
        SpecRef spec, PortrayalCatalogue catalogue, List<FeatureTypeEntry> featureTypes)
    {
        var conditioned = ConditionSubjects(featureTypes, RuleConditionKind.ContextParameter);
        return catalogue.ContextParameters
            .Select(p => new DisplaySettingEntry
            {
                Key = new ReferenceKey(spec, ReferenceKind.DisplaySetting, p.Id),
                Name = string.IsNullOrWhiteSpace(p.Description.Name) ? p.Id : p.Description.Name,
                Definition = string.IsNullOrWhiteSpace(p.Description.DescriptionText) ? null : p.Description.DescriptionText.Trim(),
                Type = p.Type,
                Default = p.Default,
                ConditionedFeatureTypes = conditioned.TryGetValue(p.Id, out var codes) ? codes.ToList() : [],
            })
            .ToList();
    }

    private static IEnumerable<RuleUse> AllUses(FeatureTypeEntry feature) =>
        feature.Uses.Cast<RuleUse>().Concat(feature.ColorFills).Concat(feature.Pens).Concat(feature.ViewingGroups);

    /// <summary>
    /// Maps each attribute code or context parameter id that a rule tests to
    /// the feature types whose rules test it.
    /// </summary>
    private static Dictionary<string, SortedSet<string>> ConditionSubjects(
        List<FeatureTypeEntry> featureTypes, RuleConditionKind kind)
    {
        var result = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var feature in featureTypes)
        {
            foreach (var condition in AllUses(feature).SelectMany(u => u.Conditions))
            {
                if (condition.Kind == kind && condition.Subject is { } subject)
                {
                    Add(subject, feature.Code);
                }
                else if (kind == RuleConditionKind.ContextParameter
                    && condition.Kind == RuleConditionKind.Expression
                    && condition.Text is { } text)
                {
                    foreach (Match m in ContextParameterPattern().Matches(text)) Add(m.Groups[1].Value, feature.Code);
                }
            }
        }

        return result;

        void Add(string key, string code)
        {
            if (!result.TryGetValue(key, out var set)) result[key] = set = new SortedSet<string>(StringComparer.Ordinal);
            set.Add(code);
        }
    }

    /// <summary>
    /// For each feature type, the labels of the listed values its rule
    /// conditions test, so that a search for "red" finds Lateral Beacon.
    /// </summary>
    private static Dictionary<string, string> ConditionValueText(
        FeatureCatalogue? features, List<FeatureTypeEntry> featureTypes)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (features is null) return result;

        // Catalogues can repeat a code (S-125 does), so the first one wins.
        var labels = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var attribute in features.SimpleAttributes)
        {
            if (labels.ContainsKey(attribute.Code)) continue;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var value in attribute.ListedValues) values.TryAdd(value.Code, value.Label);
            labels[attribute.Code] = values;
        }

        foreach (var feature in featureTypes)
        {
            var words = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var condition in AllUses(feature).SelectMany(u => u.Conditions))
            {
                if (condition is { Kind: RuleConditionKind.Attribute, Subject: { } subject, Value: { } value }
                    && labels.TryGetValue(subject, out var values)
                    && values.TryGetValue(value, out var label))
                {
                    words.Add(label);
                }
            }

            if (words.Count > 0) result[feature.Code] = string.Join(' ', words);
        }

        return result;
    }

    /// <summary>
    /// Resolves viewing group ids to their names and display categories.
    /// </summary>
    private sealed class ViewingGroupCatalogue
    {
        private readonly Dictionary<string, ViewingGroupInfo> _cache = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ViewingGroup> _groups;
        private readonly List<(DisplayMode Mode, IReadOnlySet<int> Members)> _modes;

        public ViewingGroupCatalogue(PortrayalCatalogue catalogue)
        {
            _groups = catalogue.ViewingGroups
                .GroupBy(g => g.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            // The most restrictive mode first, so a group's category is the
            // smallest mode that shows it (Display Base before Standard).
            _modes = catalogue.DisplayModes
                .Select(m => (m, DisplayModeMembership.Resolve(catalogue, m.Id)))
                .Where(m => m.Item2.Count > 0)
                .OrderBy(m => m.Item2.Count)
                .ToList();
        }

        public ViewingGroupInfo Get(string id)
        {
            if (_cache.TryGetValue(id, out var info)) return info;

            _groups.TryGetValue(id, out var group);
            DisplayMode? mode = null;
            if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                mode = _modes.FirstOrDefault(m => m.Members.Contains(number)).Mode;
            }

            info = new ViewingGroupInfo(
                id,
                group?.Description.Name,
                string.IsNullOrWhiteSpace(group?.Description.DescriptionText) ? null : group.Description.DescriptionText.Trim(),
                mode?.Id,
                mode?.Description.Name);
            _cache[id] = info;
            return info;
        }

        /// <summary>
        /// The viewing group, among those the feature type's own rule sets
        /// (or else any), that the most restrictive display mode shows.
        /// </summary>
        public ViewingGroupInfo? MostVisible(List<ViewingGroupUse> uses)
        {
            var own = uses.Where(u => u.ViaRule is null).ToList();
            var candidates = own.Count > 0 ? own : uses;
            return candidates
                .Select(u => u.ViewingGroup)
                .OrderBy(Rank)
                .FirstOrDefault();
        }

        private int Rank(ViewingGroupInfo info)
        {
            if (info.DisplayModeId is null) return int.MaxValue;
            var index = _modes.FindIndex(m => m.Mode.Id == info.DisplayModeId);
            return index < 0 ? int.MaxValue : index;
        }
    }

    /// <summary>Lower-cased search fields for one entry.</summary>
    private sealed class SearchRow
    {
        private SearchRow(ReferenceEntry entry, string code, string? alias, string name, string text)
        {
            Entry = entry;
            _code = code;
            _alias = alias;
            _name = name;
            _text = text;
        }

        private readonly string _code;
        private readonly string? _alias;
        private readonly string _name;
        private readonly string _text;

        public ReferenceEntry Entry { get; }

        public static SearchRow For(ReferenceEntry entry, string? conditionValues)
        {
            var alias = entry switch
            {
                FeatureTypeEntry f => f.Alias,
                AttributeEntry a => a.Alias,
                _ => null,
            };

            var text = new StringBuilder()
                .Append(entry.Name).Append('\n')
                .Append(entry.Definition).Append('\n')
                .Append(conditionValues);
            if (entry is AttributeEntry attribute)
            {
                foreach (var value in attribute.ListedValues) text.Append('\n').Append(value.Label);
            }

            return new SearchRow(
                entry,
                entry.Code,
                string.IsNullOrWhiteSpace(alias) ? null : alias,
                entry.Name,
                text.ToString());
        }

        public ReferenceMatch? Match(string query)
        {
            if (string.Equals(_code, query, StringComparison.OrdinalIgnoreCase)
                || string.Equals(_alias, query, StringComparison.OrdinalIgnoreCase))
            {
                return ReferenceMatch.ExactCode;
            }

            if (_name.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                || _code.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                return ReferenceMatch.NamePrefix;
            }

            return _text.Contains(query, StringComparison.OrdinalIgnoreCase) ? ReferenceMatch.Text : null;
        }
    }

    /// <summary>Compares entry keys by kind, and by code without regard to case.</summary>
    private sealed class KeyComparer : IEqualityComparer<(ReferenceKind Kind, string Code)>
    {
        public static KeyComparer Instance { get; } = new();

        public bool Equals((ReferenceKind Kind, string Code) x, (ReferenceKind Kind, string Code) y) =>
            x.Kind == y.Kind && string.Equals(x.Code, y.Code, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((ReferenceKind Kind, string Code) obj) =>
            HashCode.Combine(obj.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Code));
    }
}
