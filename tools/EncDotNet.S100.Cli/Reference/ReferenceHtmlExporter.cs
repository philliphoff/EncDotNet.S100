using System.Globalization;
using System.Text;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Cli.Reference;

/// <summary>
/// Writes a product's chart reference as a static HTML site (symbol
/// dictionary design note §7, R14, #1723): an index grouped by kind with a
/// search box, and one page per feature type, symbol, line style, area fill,
/// attribute and display setting with the Reference tab's sections, linked
/// to each other, with glyphs as PNG files in the chosen palette.
/// </summary>
/// <remarks>
/// <para>
/// When the index carries probe results (<see cref="ChartReferenceIndex.Probes"/>,
/// R13a), feature type pages get the variant tables, attribute pages the
/// symbol each listed value gives, and display setting pages before/after
/// pairs.
/// </para>
/// <para>
/// The site works offline: no external scripts, fonts or images. The output
/// is deterministic: entries are written in a fixed order, numbers in the
/// invariant culture, and nothing records the time, so a re-run with the
/// same catalogues writes the same bytes.
/// </para>
/// </remarks>
internal sealed partial class ReferenceHtmlExporter
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly PaletteType[] Palettes = [PaletteType.Day, PaletteType.Dusk, PaletteType.Night];

    private readonly ChartReferenceIndex _index;
    private readonly ReferenceGlyphs _glyphs;
    private readonly PaletteType _palette;
    private readonly string _root;
    private readonly string _specName;
    private readonly bool _simplified;
    private CancellationToken _cancellationToken;

    /// <summary>Creates the exporter.</summary>
    /// <param name="index">The product's chart reference, with probe results when there are any.</param>
    /// <param name="artwork">The product's catalogue artwork.</param>
    /// <param name="palette">The palette glyphs are drawn in, and the page colours follow.</param>
    /// <param name="root">The export folder; it must exist and be empty.</param>
    public ReferenceHtmlExporter(ChartReferenceIndex index, ChartReferenceArtwork artwork, PaletteType palette, string root)
    {
        _index = index;
        _palette = palette;
        _root = root;
        _glyphs = new ReferenceGlyphs(artwork, root);
        _specName = index.Spec.Name ?? "?";
        _simplified = !string.Equals(
            index.DisplaySettings.FirstOrDefault(s => s.Code == "SimplifiedSymbols")?.Default?.Trim(),
            "false",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What an export wrote.</summary>
    /// <param name="Pages">The HTML pages, the index included.</param>
    /// <param name="Glyphs">The glyph files.</param>
    /// <param name="Files">Every file.</param>
    /// <param name="Bytes">Their total size.</param>
    public sealed record Result(int Pages, int Glyphs, int Files, long Bytes);

    /// <summary>Writes the site.</summary>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>What was written.</returns>
    public async Task<Result> ExportAsync(CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
        var pages = 0;

        foreach (var feature in _index.FeatureTypes.OrderBy(f => f.Code, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WritePageAsync(feature.Key, await FeatureTypePageAsync(feature).ConfigureAwait(false)).ConfigureAwait(false);
            pages++;
        }

        foreach (var item in _index.Items.OrderBy(i => i.Key.Kind).ThenBy(i => i.Code, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WritePageAsync(item.Key, await ItemPageAsync(item).ConfigureAwait(false)).ConfigureAwait(false);
            pages++;
        }

        foreach (var attribute in _index.Attributes.OrderBy(a => a.Code, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WritePageAsync(attribute.Key, await AttributePageAsync(attribute).ConfigureAwait(false)).ConfigureAwait(false);
            pages++;
        }

        foreach (var setting in _index.DisplaySettings.OrderBy(s => s.Code, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WritePageAsync(setting.Key, await DisplaySettingPageAsync(setting).ConfigureAwait(false)).ConfigureAwait(false);
            pages++;
        }

        await WriteAsync(ReferenceSite.IndexPage, await IndexPageAsync().ConfigureAwait(false)).ConfigureAwait(false);
        pages++;
        await WriteAsync(ReferenceSite.StyleSheet, await StyleSheetAsync().ConfigureAwait(false)).ConfigureAwait(false);
        await WriteAsync(ReferenceSite.SearchScript, ReferenceAssets.SearchScript).ConfigureAwait(false);
        await WriteAsync(ReferenceSite.Marker, $"Chart reference export of {_specName}. A re-run of s100 reference export may empty this folder.\n").ConfigureAwait(false);

        long bytes = 0;
        var files = 0;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            bytes += new FileInfo(file).Length;
            files++;
        }

        return new Result(pages, _glyphs.Count, files, bytes);
    }

    // ---- Pages ----

    private async Task<Page> FeatureTypePageAsync(FeatureTypeEntry feature)
    {
        var page = new Page(this, feature);
        var lead = LeadGlyph.For(feature, _index, _simplified);
        var probe = _index.Probes?.Find(feature.Code);
        var badges = new List<string>();
        if (probe is { IsPartial: true })
            badges.Add(Badge("Partial", "The rule tests more combinations than were probed, so some cases may be missing."));
        if (feature.IsAbstract)
            badges.Add(Badge("Abstract", null));
        if (!feature.IsPortrayed)
            badges.Add(Badge("Not portrayed", "No rule draws anything for it."));
        await page.HeaderAsync(lead, feature.Alias is { Length: > 0 } alias ? $"{feature.Code} · {alias}" : feature.Code, badges).ConfigureAwait(false);
        page.Definition(feature.Definition);

        var facts = new List<(string, string)>();
        var categories = ReferenceWords.CategoriesOf(feature);
        if (categories.Count > 0)
            facts.Add(("Display category", string.Join("<br>", categories.Select(Html.E))));
        var group = feature.PrimaryViewingGroup;
        if (ReferenceWords.SectionOf(group) is { } section)
            facts.Add(("Chart 1 section", Html.E(section)));
        if (group is not null)
            facts.Add(("Viewing group", Html.E(group.Name is { Length: > 0 } name ? $"{group.Id} {name}" : group.Id)));
        if (feature.PermittedPrimitives.Count > 0)
            facts.Add(("Geometry", Html.E(string.Join(", ", feature.PermittedPrimitives.Select(p => ReferenceWords.Capitalise(RuleConditionText.Geometry(p)))))));
        if (feature.RuleFile is { Length: > 0 } rule)
            facts.Add(("Rule", $"<code>{Html.E(rule)}</code>"));
        page.Facts(facts);

        if (probe is not null)
            await VariantTablesAsync(page, feature, probe).ConfigureAwait(false);

        // Drawn with: each item the rule draws, once, with its cases in
        // words; paper-chart items folded; shared-rule items and the
        // fallback apart.
        var geometry = feature.PermittedPrimitives.Count != 1;
        var own = new List<(ReferenceKey Key, List<TermText> Cases, bool Paper)>();
        var shared = new List<(ReferenceKey Key, string Via)>();
        ReferenceKey? fallback = null;
        foreach (var g in feature.Uses.GroupBy(u => (u.ItemKind, u.ItemId)))
        {
            var key = new ReferenceKey(_index.Spec, g.Key.ItemKind, g.Key.ItemId);
            if (LeadGlyph.DefaultSymbols.Contains(g.Key.ItemId))
            {
                fallback ??= key;
                continue;
            }

            if (g.All(u => u.ViaRule is not null))
            {
                shared.Add((key, string.Join(", ", g.Select(u => u.ViaRule).OfType<string>().Distinct(StringComparer.Ordinal))));
                continue;
            }

            own.Add((key, ReferenceWords.CasesOf(g, _index, geometry), g.All(ReferenceWords.IsPaperChart)));
        }

        await page.ItemsAsync("Drawn with", own.Where(o => !o.Paper).Select(o => (o.Key, o.Cases, (string?)null)), folded: false).ConfigureAwait(false);
        await page.ItemsAsync($"Paper chart symbols ({own.Count(o => o.Paper)})", own.Where(o => o.Paper).Select(o => (o.Key, o.Cases, (string?)null)), folded: true).ConfigureAwait(false);
        await page.ItemsAsync(
            $"Drawn by shared rules ({shared.Count})",
            shared.Select(s => (s.Key, new List<TermText>(), (string?)$"Drawn by the shared rule {s.Via}")),
            folded: true).ConfigureAwait(false);
        if (fallback is { } f)
            await page.ItemsAsync("Fallback", [(f, [], "The catalogue's fallback symbol, drawn when nothing else fits")], folded: false).ConfigureAwait(false);

        var attributes = feature.AttributeCodes
            .Select(code => _index.TryGet(new ReferenceKey(_index.Spec, ReferenceKind.Attribute, code), out var e) ? e as AttributeEntry : null)
            .OfType<AttributeEntry>()
            .OrderBy(a => a.Name, StringComparer.Ordinal)
            .ToList();
        if (attributes.Count > 0)
        {
            page.Section($"Attributes ({attributes.Count})", folded: true, body =>
            {
                body.Append("<ul class=\"links\">");
                foreach (var attribute in attributes)
                {
                    body.Append("<li>").Append(page.Link(attribute.Key));
                    if (attribute.ConditionedFeatureTypes.Contains(feature.Code, StringComparer.Ordinal))
                        body.Append(" <span class=\"note\">changes how it is drawn</span>");
                    body.Append("</li>");
                }

                body.Append("</ul>");
            });
        }

        return page;
    }

    private async Task<Page> ItemPageAsync(PortrayalItemEntry item)
    {
        var page = new Page(this, item);
        var lead = LeadGlyph.For(item);
        var badges = new List<string>();
        if (ReferenceWords.IsNew(item))
            badges.Add(Badge($"New in {_specName}", "Not from the S-52 Presentation Library."));
        if (item.IsUnreached)
            badges.Add(Badge("Not used by a rule", "No rule names it, and no line style or area fill a rule names places it."));
        await page.HeaderAsync(lead, item.Code, badges).ConfigureAwait(false);
        page.Definition(item.Definition);

        if (lead.HasGlyph)
        {
            var tiles = new StringBuilder("<div class=\"palettes\">");
            foreach (var palette in Palettes)
            {
                var src = await _glyphs.PathAsync(lead, palette, _cancellationToken).ConfigureAwait(false);
                tiles.Append("<figure>").Append(Tile(page.Path, src, palette, "big")).Append("<figcaption>")
                    .Append(ReferenceWords.Capitalise(ReferenceGlyphs.Name(palette))).Append("</figcaption></figure>");
            }

            tiles.Append("</div>");
            page.Raw("Day, Dusk and Night", tiles.ToString());
        }

        var facts = new List<(string, string)>();
        if (ReferenceWords.OriginOf(item, _specName) is { } origin)
            facts.Add(("Origin", Html.E(origin)));
        if (item.FileName is { Length: > 0 } file)
            facts.Add(("File", $"<code>{Html.E(file)}</code>"));
        if (item.Family is { } family && _index.TryGet(new ReferenceKey(_index.Spec, ReferenceKind.Symbol, family), out _))
            facts.Add(("Family", page.Link(new ReferenceKey(_index.Spec, ReferenceKind.Symbol, family))));
        page.Facts(facts);

        // Depends on: the display settings the uses of the item test.
        var usedBy = item.UsedBy
            .Select(code => _index.TryGet(new ReferenceKey(_index.Spec, ReferenceKind.FeatureType, code), out var e) ? e as FeatureTypeEntry : null)
            .OfType<FeatureTypeEntry>()
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ThenBy(f => f.Code, StringComparer.Ordinal)
            .ToList();
        var settings = usedBy
            .SelectMany(f => UsesOf(item, f))
            .SelectMany(u => u.Conditions)
            .Where(c => c is { Kind: RuleConditionKind.ContextParameter, Subject: not null })
            .GroupBy(c => c.Subject!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (Key: new ReferenceKey(_index.Spec, ReferenceKind.DisplaySetting, g.Key), Cases: g.Select(c => RuleConditionText.DescribeTerms(c, _index)).Distinct().ToList()))
            .Where(s => _index.TryGet(s.Key, out _))
            .ToList();
        await page.ItemsAsync("Depends on", settings.Select(s => (s.Key, s.Cases, (string?)null)), folded: false).ConfigureAwait(false);

        await page.ItemsAsync(
            $"Members ({item.Members.Count})",
            item.Members.Select(m => (new ReferenceKey(_index.Spec, ReferenceKind.Symbol, m), new List<TermText>(), (string?)null)),
            folded: item.Members.Count > 24).ConfigureAwait(false);
        await page.ItemsAsync(
            $"Used by ({usedBy.Count})",
            usedBy.Select(f => (f.Key, ReferenceWords.CasesOf(UsesOf(item, f), _index, f.PermittedPrimitives.Count != 1), (string?)null)),
            folded: false).ConfigureAwait(false);
        await page.ItemsAsync(
            "Used inside",
            item.UsedIn.OrderBy(k => k.Kind).ThenBy(k => k.Code, StringComparer.Ordinal).Select(k => (k, new List<TermText>(), (string?)null)),
            folded: false).ConfigureAwait(false);
        await page.ItemsAsync(
            "Places",
            item.Places.Select(id => (new ReferenceKey(_index.Spec, ReferenceKind.Symbol, id), new List<TermText>(), (string?)null)),
            folded: false).ConfigureAwait(false);
        return page;
    }

    private async Task<Page> AttributePageAsync(AttributeEntry attribute)
    {
        var page = new Page(this, attribute);
        await page.HeaderAsync(null, attribute.Alias is { Length: > 0 } alias ? $"{attribute.Code} · {alias}" : attribute.Code, []).ConfigureAwait(false);
        page.Definition(attribute.Definition);
        page.Facts([("Value type", Html.E(attribute.ValueType))]);

        if (attribute.ListedValues.Count > 0)
        {
            page.Section($"Listed values ({attribute.ListedValues.Count})", folded: false, body =>
            {
                var definitions = attribute.ListedValues.Any(v => !string.IsNullOrWhiteSpace(v.Definition));
                body.Append("<table class=\"values\"><thead><tr><th>Code</th><th>Value</th>").Append(definitions ? "<th>Definition</th>" : string.Empty).Append("</tr></thead><tbody>");
                foreach (var value in attribute.ListedValues)
                {
                    body.Append("<tr id=\"").Append(ReferenceSite.ValueAnchor(value.Code)).Append("\"><td>").Append(Html.E(value.Code))
                        .Append("</td><td>").Append(Html.E(value.Label)).Append("</td>");
                    if (definitions)
                        body.Append("<td>").Append(Html.E(value.Definition)).Append("</td>");
                    body.Append("</tr>");
                }

                body.Append("</tbody></table>");
            });
        }

        if (_index.Probes is not null)
            await ValueColumnsAsync(page, attribute).ConfigureAwait(false);
        await ChangesAsync(page, attribute.ConditionedFeatureTypes).ConfigureAwait(false);
        return page;
    }

    private async Task<Page> DisplaySettingPageAsync(DisplaySettingEntry setting)
    {
        var page = new Page(this, setting);
        await page.HeaderAsync(null, setting.Code, []).ConfigureAwait(false);
        page.Definition(setting.Definition);
        page.Facts(
        [
            ("Default", Html.E(ReferenceWords.SettingValue(setting.Default))),
            ("Type", Html.E(setting.Type)),
        ]);
        if (_index.Probes is not null)
            await BeforeAfterAsync(page, setting).ConfigureAwait(false);
        await ChangesAsync(page, setting.ConditionedFeatureTypes).ConfigureAwait(false);
        return page;
    }

    // The feature types whose rules test an attribute or setting; folded when long.
    private Task ChangesAsync(Page page, IEnumerable<string> codes)
    {
        var keys = FeatureTypes(codes).ToList();
        return page.ItemsAsync(
            string.Create(CultureInfo.InvariantCulture, $"Changes how these are drawn ({keys.Count})"),
            keys.Select(k => (k, new List<TermText>(), (string?)null)),
            folded: keys.Count > 12);
    }

    private async Task<string> IndexPageAsync()
    {
        var body = new StringBuilder();
        body.Append("<header class=\"top\"><h1>Chart reference · ").Append(Html.E(_specName)).Append("</h1>")
            .Append("<p class=\"note\">Every feature type, symbol, line style, area fill, attribute and display setting in the ")
            .Append(Html.E(_specName)).Append(" catalogues, with glyphs in the ").Append(ReferenceGlyphs.Name(_palette)).Append(" palette.");
        if (_index.Probes is null)
            body.Append(" The rules haven't been probed for these catalogues, so feature types list their symbols with their conditions, without variant tables.");
        body.Append("</p>");
        body.Append("<input id=\"search\" type=\"search\" placeholder=\"Search names, codes and S-57 acronyms\" aria-label=\"Search\" autocomplete=\"off\">")
            .Append("<p id=\"count\" class=\"note\" aria-live=\"polite\"></p>");
        body.Append("<nav class=\"kinds\">");
        foreach (var kind in Enum.GetValues<ReferenceKind>())
        {
            var count = CountOf(kind);
            if (count > 0)
                body.Append("<a href=\"#").Append(ReferenceSite.Folder(kind)).Append("\">").Append(ReferenceWords.GroupOf(kind)).Append(" (").Append(count.ToString(CultureInfo.InvariantCulture)).Append(")</a>");
        }

        body.Append("</nav></header><main>");

        await GroupAsync(body, ReferenceKind.FeatureType, _index.FeatureTypes.OrderBy(f => f.Name, StringComparer.Ordinal).ThenBy(f => f.Code, StringComparer.Ordinal).Cast<ReferenceEntry>()).ConfigureAwait(false);
        foreach (var kind in new[] { ReferenceKind.Symbol, ReferenceKind.LineStyle, ReferenceKind.AreaFill })
        {
            var items = _index.Items.Where(i => i.Key.Kind == kind).OrderBy(i => i.IsUnreached).ThenBy(i => i.Code, StringComparer.Ordinal).Cast<ReferenceEntry>();
            await GroupAsync(body, kind, items).ConfigureAwait(false);
        }

        await GroupAsync(body, ReferenceKind.Attribute, _index.Attributes.OrderBy(a => a.Name, StringComparer.Ordinal).ThenBy(a => a.Code, StringComparer.Ordinal).Cast<ReferenceEntry>()).ConfigureAwait(false);
        await GroupAsync(body, ReferenceKind.DisplaySetting, _index.DisplaySettings.Cast<ReferenceEntry>()).ConfigureAwait(false);
        body.Append("</main>");
        return Layout(ReferenceSite.IndexPage, $"Chart reference · {_specName}", body.ToString(), script: true);
    }

    private async Task GroupAsync(StringBuilder body, ReferenceKind kind, IEnumerable<ReferenceEntry> entries)
    {
        var list = entries.ToList();
        if (list.Count == 0)
            return;
        body.Append("<section class=\"group\" id=\"").Append(ReferenceSite.Folder(kind)).Append("\"><h2>").Append(ReferenceWords.GroupOf(kind))
            .Append(" <span class=\"count\">").Append(list.Count.ToString(CultureInfo.InvariantCulture)).Append("</span></h2><ul class=\"entries\">");
        var unreached = false;
        foreach (var entry in list)
        {
            if (entry is PortrayalItemEntry { IsUnreached: true } && !unreached)
            {
                unreached = true;
                body.Append("</ul><h3>Not used by a rule</h3><ul class=\"entries\">");
            }

            var lead = entry switch
            {
                FeatureTypeEntry f => LeadGlyph.For(f, _index, _simplified),
                PortrayalItemEntry i => LeadGlyph.For(i),
                _ => null,
            };
            var src = lead is null ? null : await _glyphs.PathAsync(lead, _palette, _cancellationToken).ConfigureAwait(false);
            var alias = entry switch
            {
                FeatureTypeEntry f => f.Alias,
                AttributeEntry a => a.Alias,
                _ => null,
            };
            // A symbol's name is its code, so its description ("minor lateral beacon, red") is searched too.
            var described = entry is PortrayalItemEntry ? entry.Definition : null;
            var search = string.Join(' ', new[] { entry.Name, entry.Code, alias, described }.Where(s => !string.IsNullOrEmpty(s))).ToLowerInvariant();
            body.Append("<li data-s=\"").Append(Html.E(search)).Append("\"><a href=\"").Append(Html.E(ReferenceSite.PageOf(entry.Key))).Append("\">");
            if (lead is not null)
                body.Append(Tile(ReferenceSite.IndexPage, src, _palette, "small"));
            body.Append("<span class=\"label\"><span class=\"name\">").Append(Html.E(entry.Name)).Append("</span>");
            if (!string.Equals(entry.Name, entry.Code, StringComparison.Ordinal) || alias is { Length: > 0 })
                body.Append("<span class=\"code\">").Append(Html.E(alias is { Length: > 0 } ? $"{entry.Code} · {alias}" : entry.Code)).Append("</span>");
            else if (described is { Length: > 0 })
                body.Append("<span class=\"code\">").Append(Html.E(described)).Append("</span>");
            body.Append("</span>");
            if (entry is FeatureTypeEntry ft && _index.Probes?.Find(ft.Code) is { IsPartial: true })
                body.Append(" <span class=\"badge\">Partial</span>");
            if (entry is PortrayalItemEntry pi && ReferenceWords.IsNew(pi))
                body.Append(" <span class=\"badge\">New</span>");
            body.Append("</a></li>");
        }

        body.Append("</ul></section>");
    }

    private async Task<string> StyleSheetAsync()
    {
        var css = new StringBuilder(ReferenceAssets.StyleSheet);
        css.Append('\n');
        foreach (var palette in Palettes)
        {
            var background = await _glyphs.ColourAsync("DEPDW", palette, _cancellationToken).ConfigureAwait(false)
                ?? (palette == PaletteType.Day ? "#C9EDFF" : "#000000");
            css.Append(".tile.").Append(ReferenceGlyphs.Name(palette)).Append(" { background: ").Append(background).Append("; }\n");
        }

        return css.ToString();
    }

    // ---- Helpers ----

    private int CountOf(ReferenceKind kind) => kind switch
    {
        ReferenceKind.FeatureType => _index.FeatureTypes.Count,
        ReferenceKind.Attribute => _index.Attributes.Count,
        ReferenceKind.DisplaySetting => _index.DisplaySettings.Count,
        _ => _index.Items.Count(i => i.Key.Kind == kind),
    };

    private IEnumerable<ReferenceKey> FeatureTypes(IEnumerable<string> codes) =>
        codes
            .Select(code => _index.TryGet(new ReferenceKey(_index.Spec, ReferenceKind.FeatureType, code), out var e) ? e : null)
            .OfType<ReferenceEntry>()
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ThenBy(e => e.Code, StringComparer.Ordinal)
            .Select(e => e.Key);

    // The uses of the item, or of a family's members, by a feature type.
    private static IEnumerable<SymbolUse> UsesOf(PortrayalItemEntry item, FeatureTypeEntry feature) =>
        feature.Uses.Where(u => string.Equals(u.ItemId, item.Code, StringComparison.OrdinalIgnoreCase)
            || item.IsFamily && item.Members.Contains(u.ItemId, StringComparer.OrdinalIgnoreCase));

    private static string Badge(string text, string? title) =>
        title is null
            ? $"<span class=\"badge\">{Html.E(text)}</span>"
            : $"<span class=\"badge\" title=\"{Html.E(title)}\">{Html.E(text)}</span>";

    private static string Tile(string from, string? src, PaletteType palette, string size) =>
        src is null
            ? $"<span class=\"tile {ReferenceGlyphs.Name(palette)} {size} empty\"></span>"
            : $"<span class=\"tile {ReferenceGlyphs.Name(palette)} {size}\"><img src=\"{Html.E(ReferenceSite.Href(from, src))}\" alt=\"\" loading=\"lazy\"></span>";

    private string Layout(string path, string title, string body, bool script)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html>\n<html lang=\"en\" class=\"").Append(ReferenceGlyphs.Name(_palette)).Append("\">\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
            .Append("<title>").Append(Html.E(title)).Append("</title>\n")
            .Append("<link rel=\"stylesheet\" href=\"").Append(ReferenceSite.Href(path, ReferenceSite.StyleSheet)).Append("\">\n");
        if (script)
            html.Append("<script src=\"").Append(ReferenceSite.Href(path, ReferenceSite.SearchScript)).Append("\" defer></script>\n");
        html.Append("</head>\n<body>\n").Append(body).Append("\n</body>\n</html>\n");
        return html.ToString();
    }

    private Task WritePageAsync(ReferenceKey key, Page page) =>
        WriteAsync(ReferenceSite.PageOf(key), Layout(page.Path, $"{page.Entry.Name} · {ReferenceWords.KindOf(page.Entry)} · {_specName} chart reference", page.Body(), script: false));

    private async Task WriteAsync(string path, string text)
    {
        var full = Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, text.ReplaceLineEndings("\n"), Utf8, _cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One entry's page, built section by section.</summary>
    private sealed class Page(ReferenceHtmlExporter owner, ReferenceEntry entry)
    {
        private readonly StringBuilder _body = new();

        public ReferenceEntry Entry { get; } = entry;

        public string Path { get; } = ReferenceSite.PageOf(entry.Key);

        public string Body() =>
            $"<nav class=\"crumbs\"><a href=\"{ReferenceSite.Href(Path, ReferenceSite.IndexPage)}\">Chart reference · {Html.E(owner._specName)}</a> › "
            + $"<a href=\"{ReferenceSite.Href(Path, ReferenceSite.IndexPage)}#{ReferenceSite.Folder(Entry.Key.Kind)}\">{ReferenceWords.GroupOf(Entry.Key.Kind)}</a></nav>\n<main class=\"entry\">"
            + _body + "</main>";

        public async Task HeaderAsync(LeadGlyph? lead, string codeLine, IReadOnlyList<string> badges)
        {
            _body.Append("<header class=\"head\">");
            if (lead is { HasGlyph: true })
            {
                var src = await owner._glyphs.PathAsync(lead, owner._palette, owner._cancellationToken).ConfigureAwait(false);
                _body.Append(Tile(Path, src, owner._palette, "big"));
            }

            _body.Append("<div><p class=\"kind\">").Append(ReferenceWords.KindOf(Entry)).Append("</p><h1>").Append(Html.E(Entry.Name)).Append("</h1>")
                .Append(string.Equals(codeLine, Entry.Name, StringComparison.Ordinal) ? string.Empty : $"<p class=\"code\">{Html.E(codeLine)}</p>");
            if (badges.Count > 0)
                _body.Append("<p>").Append(string.Join(' ', badges)).Append("</p>");
            _body.Append("</div></header>");
        }

        public void Definition(string? text)
        {
            if (!string.IsNullOrWhiteSpace(text))
                _body.Append("<p class=\"definition\">").Append(Html.E(text.Trim())).Append("</p>");
        }

        public void Facts(IReadOnlyList<(string Name, string Html)> facts)
        {
            if (facts.Count == 0)
                return;
            _body.Append("<dl class=\"facts\">");
            foreach (var (name, html) in facts)
                _body.Append("<dt>").Append(Html.E(name)).Append("</dt><dd>").Append(html).Append("</dd>");
            _body.Append("</dl>");
        }

        public void Raw(string heading, string html) =>
            Section(heading, folded: false, body => body.Append(html));

        public void Section(string heading, bool folded, Action<StringBuilder> write)
        {
            if (folded)
                _body.Append("<details class=\"section\"><summary><h2>").Append(Html.E(heading)).Append("</h2></summary>");
            else
                _body.Append("<section class=\"section\"><h2>").Append(Html.E(heading)).Append("</h2>");
            write(_body);
            _body.Append(folded ? "</details>" : "</section>");
        }

        // A list of entries, each with its glyph, a link, its cases and a note.
        public async Task ItemsAsync(string heading, IEnumerable<(ReferenceKey Key, List<TermText> Cases, string? Note)> items, bool folded)
        {
            var list = items.ToList();
            if (list.Count == 0)
                return;
            var html = new StringBuilder("<ul class=\"items\">");
            foreach (var (key, cases, note) in list)
            {
                html.Append("<li>").Append(await LinkWithGlyphAsync(key).ConfigureAwait(false));
                if (note is not null)
                    html.Append("<p class=\"note\">").Append(Html.E(note)).Append("</p>");
                if (cases.Count > 0)
                {
                    html.Append("<ul class=\"cases\">");
                    foreach (var c in cases)
                        html.Append("<li>").Append(Terms(c)).Append("</li>");
                    html.Append("</ul>");
                }

                html.Append("</li>");
            }

            html.Append("</ul>");
            Section(heading, folded, body => body.Append(html));
        }

        // A link to an entry, with its name and code; plain text when the index lacks it.
        public string Link(ReferenceKey key, string? text = null)
        {
            if (!owner._index.TryGet(key, out var target))
                return $"<span class=\"missing\">{Html.E(text ?? key.Code)}</span>";
            var label = text ?? target.Name;
            var code = text is null && !string.Equals(target.Name, target.Code, StringComparison.Ordinal)
                ? $" <span class=\"code\">{Html.E(target.Code)}</span>"
                : string.Empty;
            return $"<a href=\"{Html.E(ReferenceSite.Href(Path, ReferenceSite.PageOf(target.Key)))}\">{Html.E(label)}</a>{code}";
        }

        public async Task<string> LinkWithGlyphAsync(ReferenceKey key)
        {
            var lead = owner._index.TryGet(key, out var target)
                ? target switch
                {
                    FeatureTypeEntry f => LeadGlyph.For(f, owner._index, owner._simplified),
                    PortrayalItemEntry i => LeadGlyph.For(i),
                    _ => null,
                }
                : null;
            if (lead is not { HasGlyph: true })
                return Link(key);
            var src = await owner._glyphs.PathAsync(lead, owner._palette, owner._cancellationToken).ConfigureAwait(false);
            return Tile(Path, src, owner._palette, "small") + Link(key);
        }

        // Text whose names are terms, each a link to its entry, a listed value to its row.
        public string Terms(TermText text)
        {
            var html = new StringBuilder();
            foreach (var run in text.Runs)
            {
                if (run.Key is { } key && owner._index.TryGet(key, out var target))
                {
                    var href = ReferenceSite.Href(Path, ReferenceSite.PageOf(target.Key));
                    if (run.Value is { } value)
                        href += "#" + ReferenceSite.ValueAnchor(value);
                    html.Append("<a class=\"term\" href=\"").Append(Html.E(href)).Append("\">").Append(Html.E(run.Text)).Append("</a>");
                }
                else
                {
                    html.Append(Html.E(run.Text));
                }
            }

            return html.ToString();
        }

        public void Append(string html) => _body.Append(html);
    }
}
