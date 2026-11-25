using System.Globalization;
using System.Text;
using EncDotNet.S100.Datasets.Pipelines.Reference;

namespace EncDotNet.S100.Cli.Reference;

/// <summary>
/// The sections an export takes from the rule probe results (R13a, R13b,
/// R13c): a feature type's variant tables, the symbol each listed value of
/// an attribute gives, and a display setting's before/after pairs.
/// </summary>
internal sealed partial class ReferenceHtmlExporter
{
    /// <summary>The distinct variant tables a feature type shows; more are counted, not drawn.</summary>
    internal const int MaxTables = 16;

    /// <summary>The captions a variant table lists for the other inputs it holds for.</summary>
    internal const int MaxCaptions = 6;

    /// <summary>The distinct drawings a value-to-symbol cell shows.</summary>
    internal const int MaxCellParts = 6;

    /// <summary>The before/after pairs shown per feature type.</summary>
    internal const int MaxPairs = 12;

    private const string SimplifiedSymbols = "SimplifiedSymbols";

    // How it is drawn (C1): two inputs as rows and columns, one table per
    // combination of the other varied inputs, tables that draw alike merged.
    private async Task VariantTablesAsync(Page page, FeatureTypeEntry feature, FeatureTypeProbe probe)
    {
        var html = new StringBuilder();
        if (probe.IsPartial)
        {
            html.Append("<p class=\"note\"><span class=\"badge\">Partial</span> The rule tests more combinations than the ")
                .Append(_index.Probes!.Cap.ToString(CultureInfo.InvariantCulture))
                .Append(" probed, so some inputs were held at fewer values and some cases may be missing.</p>");
        }

        var varied = Enumerable.Range(0, probe.Inputs.Count).Where(i => probe.Inputs[i].Values.Count > 1).ToList();
        if (varied.Count == 0)
        {
            var only = probe.Variants.Count > 0 ? probe.Outcomes[probe.Variants[0]] : null;
            html.Append("<p>").Append(only is null ? "Nothing drawn" : await OutcomeAsync(page, only).ConfigureAwait(false)).Append("</p>");
            page.Raw("How it is drawn", html.ToString());
            return;
        }

        // Rows and columns: the attributes first, in the probe's order of
        // importance; then the other inputs, with Simplified symbols last so
        // it splits tables rather than crowding a row.
        var axes = varied
            .OrderBy(i => probe.Inputs[i].Kind switch
            {
                ProbeInputKind.Attribute => 0,
                ProbeInputKind.Primitive => 1,
                _ when probe.Inputs[i].Code == SimplifiedSymbols => 3,
                _ => 2,
            })
            .ThenBy(i => i)
            .ToList();
        var row = axes[0];
        int? column = axes.Count > 1 && probe.Inputs[axes[1]].Kind == ProbeInputKind.Attribute ? axes[1] : null;
        var rest = varied.Where(i => i != row && i != column).ToList();

        // Each combination of the other inputs gives a grid of outcome
        // indexes; combinations with the same grid share a table.
        var tables = new List<(int[] Grid, List<int[]> Combos)>();
        var hidden = 0;
        foreach (var combo in Combinations(rest.Select(i => probe.Inputs[i].Values.Count).ToList()))
        {
            var values = new int[probe.Inputs.Count];
            for (var r = 0; r < rest.Count; r++)
                values[rest[r]] = combo[r];
            var columns = column is { } c ? probe.Inputs[c].Values.Count : 1;
            var grid = new int[probe.Inputs[row].Values.Count * columns];
            for (var y = 0; y < probe.Inputs[row].Values.Count; y++)
            {
                values[row] = y;
                for (var x = 0; x < columns; x++)
                {
                    if (column is { } cc)
                        values[cc] = x;
                    grid[(y * columns) + x] = OutcomeIndex(probe, values);
                }
            }

            var same = tables.FindIndex(t => t.Grid.AsSpan().SequenceEqual(grid));
            if (same >= 0)
                tables[same].Combos.Add(combo);
            else if (tables.Count < MaxTables)
                tables.Add((grid, [combo]));
            else
                hidden++;
        }

        foreach (var (grid, combos) in tables)
        {
            if (rest.Count > 0)
            {
                html.Append("<ul class=\"caption\">");
                foreach (var combo in combos.Take(MaxCaptions))
                    html.Append("<li>").Append(string.Join(" · ", rest.Select((input, r) => InputValue(page, probe.Inputs[input], combo[r], withName: true)))).Append("</li>");
                if (combos.Count > MaxCaptions)
                    html.Append(CultureInfo.InvariantCulture, $"<li>and {combos.Count - MaxCaptions} more combinations that draw the same</li>");
                html.Append("</ul>");
            }

            var columns = column is { } c ? probe.Inputs[c].Values.Count : 1;
            html.Append("<div class=\"scroll\"><table class=\"variants\"><thead><tr><th>").Append(InputName(page, probe.Inputs[row]));
            if (column is { } cn)
                html.Append(" \\ ").Append(InputName(page, probe.Inputs[cn]));
            html.Append("</th>");
            for (var x = 0; x < columns; x++)
                html.Append("<th>").Append(column is { } cx ? InputValue(page, probe.Inputs[cx], x, withName: false) : "Drawn").Append("</th>");
            html.Append("</tr></thead><tbody>");
            for (var y = 0; y < probe.Inputs[row].Values.Count; y++)
            {
                html.Append("<tr><th>").Append(InputValue(page, probe.Inputs[row], y, withName: false)).Append("</th>");
                for (var x = 0; x < columns; x++)
                {
                    var outcome = grid[(y * columns) + x];
                    html.Append("<td>").Append(outcome < 0 ? "–" : await OutcomeAsync(page, probe.Outcomes[outcome]).ConfigureAwait(false)).Append("</td>");
                }

                html.Append("</tr>");
            }

            html.Append("</tbody></table></div>");
        }

        if (hidden > 0)
            html.Append(CultureInfo.InvariantCulture, $"<p class=\"note\">{hidden} more combinations of the other inputs draw differently and aren't shown.</p>");

        page.Raw("How it is drawn", html.ToString());
    }

    // Symbols by value (C3): for each feature type whose probe varies the
    // attribute, what each listed value gives it, in both symbol styles.
    private async Task ValueColumnsAsync(Page page, AttributeEntry attribute)
    {
        var html = new StringBuilder();
        foreach (var featureKey in FeatureTypes(attribute.ConditionedFeatureTypes))
        {
            if (_index.Probes!.Find(featureKey.Code) is not { } probe)
                continue;
            var input = IndexOf(probe, ProbeInputKind.Attribute, attribute.Code);
            if (input < 0 || probe.Inputs[input].Values.Count < 2)
                continue;
            var simplified = IndexOf(probe, ProbeInputKind.ContextParameter, SimplifiedSymbols);
            var styles = simplified >= 0 && probe.Inputs[simplified].Values.Count > 1 ? probe.Inputs[simplified].Values.Count : 1;

            // Each value's distinct drawings, across the other inputs.
            var cells = new List<string>[probe.Inputs[input].Values.Count, styles];
            for (var v = 0; v < cells.GetLength(0); v++)
            {
                for (var s = 0; s < styles; s++)
                    cells[v, s] = [];
            }

            foreach (var variant in probe.GetVariants())
            {
                var s = styles > 1 ? variant.Values[simplified] : 0;
                var parts = cells[variant.Values[input], s];
                foreach (var part in variant.Outcome.Parts.Where(IsDrawing))
                {
                    if (!parts.Contains(part, StringComparer.Ordinal))
                        parts.Add(part);
                }
            }

            if (Enumerable.Range(0, styles).All(s => Enumerable.Range(1, cells.GetLength(0) - 1).All(v => cells[v, s].SequenceEqual(cells[0, s]))))
                continue;

            html.Append("<h3>").Append(page.Link(featureKey)).Append(probe.IsPartial ? " <span class=\"badge\">Partial</span>" : string.Empty).Append("</h3>")
                .Append("<div class=\"scroll\"><table class=\"variants\"><thead><tr><th>").Append(Html.E(ReferenceWords.Capitalise(RuleConditionText.SentenceCase(attribute.Name)))).Append("</th>");
            for (var s = 0; s < styles; s++)
            {
                html.Append("<th>").Append(styles > 1 ? StyleName(probe.Inputs[simplified].Values[s]) : "Drawn").Append("</th>");
            }

            html.Append("</tr></thead><tbody>");
            for (var v = 0; v < cells.GetLength(0); v++)
            {
                html.Append("<tr><th>").Append(InputValue(page, probe.Inputs[input], v, withName: false)).Append("</th>");
                for (var s = 0; s < styles; s++)
                    html.Append("<td>").Append(await PartsAsync(page, cells[v, s]).ConfigureAwait(false)).Append("</td>");
                html.Append("</tr>");
            }

            html.Append("</tbody></table></div>");
        }

        if (html.Length > 0)
        {
            page.Raw(
                "Symbols by value",
                "<p class=\"note\">What each value draws, across the values of the other attributes and settings the rule reads.</p>" + html);
        }
    }

    // Before and after (C4): for each feature type whose probe varies the
    // setting, the drawings that change when it is turned on or off.
    private async Task BeforeAfterAsync(Page page, DisplaySettingEntry setting)
    {
        var html = new StringBuilder();
        var total = 0;
        foreach (var featureKey in FeatureTypes(setting.ConditionedFeatureTypes))
        {
            if (_index.Probes!.Find(featureKey.Code) is not { } probe)
                continue;
            var input = IndexOf(probe, ProbeInputKind.ContextParameter, setting.Code);
            if (input < 0 || probe.Inputs[input].Values.Count != 2)
                continue;

            var pairs = new List<(List<string> Before, List<string> After)>();
            var values = new int[probe.Inputs.Count];
            foreach (var variant in probe.GetVariants())
            {
                if (variant.Values[input] != 0)
                    continue;
                for (var i = 0; i < values.Length; i++)
                    values[i] = variant.Values[i];
                values[input] = 1;
                var other = OutcomeIndex(probe, values);
                if (other < 0)
                    continue;
                var before = variant.Outcome.Parts.Where(IsDrawing).ToList();
                var after = probe.Outcomes[other].Parts.Where(IsDrawing).ToList();
                if (before.SequenceEqual(after, StringComparer.Ordinal)
                    || pairs.Any(p => p.Before.SequenceEqual(before, StringComparer.Ordinal) && p.After.SequenceEqual(after, StringComparer.Ordinal)))
                {
                    continue;
                }

                pairs.Add((before, after));
            }

            if (pairs.Count == 0)
                continue;
            total += pairs.Count;
            html.Append("<h3>").Append(page.Link(featureKey)).Append(probe.IsPartial ? " <span class=\"badge\">Partial</span>" : string.Empty).Append("</h3>")
                .Append("<table class=\"pairs\"><thead><tr><th>").Append(Html.E(ReferenceWords.SettingValue(probe.Inputs[input].Values[0])))
                .Append("</th><th>").Append(Html.E(ReferenceWords.SettingValue(probe.Inputs[input].Values[1]))).Append("</th></tr></thead><tbody>");
            foreach (var (before, after) in pairs.Take(MaxPairs))
            {
                html.Append("<tr><td>").Append(await PartsAsync(page, before).ConfigureAwait(false)).Append("</td><td>")
                    .Append(await PartsAsync(page, after).ConfigureAwait(false)).Append("</td></tr>");
            }

            html.Append("</tbody></table>");
            if (pairs.Count > MaxPairs)
                html.Append(CultureInfo.InvariantCulture, $"<p class=\"note\">and {pairs.Count - MaxPairs} more pairs</p>");
        }

        if (html.Length > 0)
        {
            page.Raw(
                "Before and after",
                string.Create(CultureInfo.InvariantCulture, $"<p class=\"note\">{total} drawings change with this setting, with every other input the same.</p>") + html);
        }
    }

    // ---- Outcomes ----

    private async Task<string> OutcomeAsync(Page page, ProbeOutcome outcome)
    {
        var html = new StringBuilder();
        if (outcome.IsError)
            html.Append("<span class=\"badge\" title=\"The rule raised an error, so the catalogue's default symbology was drawn.\">Rule error</span> ");
        html.Append(await PartsAsync(page, outcome.Parts).ConfigureAwait(false));
        return html.ToString();
    }

    private async Task<string> PartsAsync(Page page, IReadOnlyList<string> parts)
    {
        if (parts.Count == 0)
            return "<span class=\"none\">Nothing drawn</span>";
        var html = new StringBuilder("<span class=\"parts\">");
        var shown = 0;
        foreach (var part in parts)
        {
            if (shown == MaxCellParts)
            {
                html.Append(CultureInfo.InvariantCulture, $"<span class=\"more\">+{parts.Count - shown}</span>");
                break;
            }

            html.Append(await PartAsync(page, part).ConfigureAwait(false));
            shown++;
        }

        return html.Append("</span>").ToString();
    }

    private async Task<string> PartAsync(Page page, string part)
    {
        var colon = part.IndexOf(':', StringComparison.Ordinal);
        var (kind, id) = colon < 0 ? (part, string.Empty) : (part[..colon], part[(colon + 1)..]);
        switch (kind)
        {
            case "symbol":
            case "line":
            case "fill":
                {
                    var refKind = kind switch { "symbol" => ReferenceKind.Symbol, "line" => ReferenceKind.LineStyle, _ => ReferenceKind.AreaFill };
                    var key = new ReferenceKey(_index.Spec, refKind, id);
                    var glyph = new LeadGlyph(
                        refKind switch { ReferenceKind.Symbol => LeadGlyphKind.Symbol, ReferenceKind.LineStyle => LeadGlyphKind.LineStyle, _ => LeadGlyphKind.AreaFill },
                        key,
                        id);
                    var src = await _glyphs.PathAsync(glyph, _palette, _cancellationToken).ConfigureAwait(false);
                    var tile = Tile(page.Path, src, _palette, "small");
                    return _index.TryGet(key, out var entry)
                        ? $"<a class=\"part\" href=\"{Html.E(ReferenceSite.Href(page.Path, ReferenceSite.PageOf(entry.Key)))}\" title=\"{Html.E(id)}\">{tile}<span class=\"id\">{Html.E(id)}</span></a>"
                        : $"<span class=\"part\" title=\"{Html.E(id)}\">{tile}<span class=\"id\">{Html.E(id)}</span></span>";
                }

            case "colour":
                {
                    var glyph = new LeadGlyph(LeadGlyphKind.Area, new ReferenceKey(_index.Spec, ReferenceKind.FeatureType, id), Tokens: [id]);
                    var src = await _glyphs.PathAsync(glyph, _palette, _cancellationToken).ConfigureAwait(false);
                    return $"<span class=\"part\" title=\"Colour fill {Html.E(id)}\">{Tile(page.Path, src, _palette, "small")}<span class=\"id\">{Html.E(id)}</span></span>";
                }

            case "pen":
                {
                    var bits = id.Split(',');
                    var pen = new PenUse
                    {
                        Token = bits[0],
                        Width = bits.Length > 1 && double.TryParse(bits[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : 0.32,
                        Dash = bits.Length > 2 ? bits[2] : "solid",
                        RuleFile = string.Empty,
                    };
                    var glyph = new LeadGlyph(LeadGlyphKind.Pen, new ReferenceKey(_index.Spec, ReferenceKind.FeatureType, id), Pen: pen);
                    var src = await _glyphs.PathAsync(glyph, _palette, _cancellationToken).ConfigureAwait(false);
                    return $"<span class=\"part\" title=\"Line {Html.E(id)}\">{Tile(page.Path, src, _palette, "small")}<span class=\"id\">{Html.E(bits[0])}</span></span>";
                }

            case "text":
                return $"<span class=\"part text\" title=\"Text\">“{Html.E(id)}”</span>";

            default:
                return $"<span class=\"part\">{Html.E(part)}</span>";
        }
    }

    // ---- Inputs ----

    private string InputName(Page page, ProbeInput input) => input.Kind switch
    {
        ProbeInputKind.Primitive => "Geometry",
        ProbeInputKind.Attribute => _index.TryGet(new ReferenceKey(_index.Spec, ReferenceKind.Attribute, input.Code), out var a)
            ? page.Link(a.Key, RuleConditionText.SentenceCase(a.Name))
            : Html.E(input.Code),
        _ => _index.TryGet(new ReferenceKey(_index.Spec, ReferenceKind.DisplaySetting, input.Code), out var s)
            ? page.Link(s.Key, RuleConditionText.SentenceCase(s.Name))
            : Html.E(input.Code),
    };

    // A value of an input in words: a listed value's label, linked to its
    // row; a setting's On or Off; a geometry's name.
    private string InputValue(Page page, ProbeInput input, int index, bool withName)
    {
        var raw = input.Values[index];
        string value;
        switch (input.Kind)
        {
            case ProbeInputKind.Primitive:
                value = Html.E(ReferenceWords.Capitalise(RuleConditionText.Geometry(raw)));
                return value;

            case ProbeInputKind.Attribute:
                {
                    var attribute = _index.TryGet(new ReferenceKey(_index.Spec, ReferenceKind.Attribute, input.Code), out var e) ? e as AttributeEntry : null;
                    if (raw is null)
                    {
                        value = "<span class=\"none\">not given</span>";
                    }
                    else if (attribute?.ListedValuesOf(raw) is { } listed)
                    {
                        value = string.Join(", ", listed.Select(l =>
                            $"<a class=\"term\" href=\"{Html.E(ReferenceSite.Href(page.Path, ReferenceSite.PageOf(attribute.Key)))}#{ReferenceSite.ValueAnchor(l.Code)}\">{Html.E(l.Label)}</a>"));
                    }
                    else
                    {
                        value = Html.E(raw);
                    }

                    break;
                }

            default:
                value = Html.E(withName ? ReferenceWords.SettingValue(raw).ToLowerInvariant() : ReferenceWords.SettingValue(raw));
                break;
        }

        return withName ? $"{InputName(page, input)} {(input.Kind == ProbeInputKind.ContextParameter ? "is" : "=")} {value}" : value;
    }

    private static string StyleName(string? simplified) =>
        string.Equals(simplified, "false", StringComparison.OrdinalIgnoreCase) ? "Paper chart" : "Simplified";

    private static bool IsDrawing(string part) => !part.StartsWith("text:", StringComparison.Ordinal);

    private static int IndexOf(FeatureTypeProbe probe, ProbeInputKind kind, string code)
    {
        for (var i = 0; i < probe.Inputs.Count; i++)
        {
            if (probe.Inputs[i].Kind == kind && string.Equals(probe.Inputs[i].Code, code, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    // The outcome index of a combination, or -1 when it names none.
    private static int OutcomeIndex(FeatureTypeProbe probe, IReadOnlyList<int> values)
    {
        var offset = 0;
        for (var i = 0; i < probe.Inputs.Count; i++)
        {
            var count = Math.Max(1, probe.Inputs[i].Values.Count);
            if (values[i] < 0 || values[i] >= count)
                return -1;
            offset = (offset * count) + values[i];
        }

        return offset < probe.Variants.Count ? probe.Variants[offset] : -1;
    }

    // Every combination of indexes below each count, the first varying slowest.
    private static IEnumerable<int[]> Combinations(IReadOnlyList<int> counts)
    {
        var current = new int[counts.Count];
        while (true)
        {
            yield return [.. current];
            var i = counts.Count - 1;
            while (i >= 0 && ++current[i] == counts[i])
            {
                current[i] = 0;
                i--;
            }

            if (i < 0)
                yield break;
        }
    }
}
