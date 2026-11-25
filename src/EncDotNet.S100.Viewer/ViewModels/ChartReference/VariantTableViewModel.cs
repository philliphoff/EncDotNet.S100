using System.Globalization;
using Avalonia.Media;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.ChartReference;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// A feature type's <b>How it is drawn</b> as a table of its variants
/// (symbol dictionary design note §4.4, C1; §7, R13b, #1721), from the rule
/// probe results (R13a).
/// </summary>
/// <remarks>
/// <para>
/// The rows and columns are the two inputs that split the most variants:
/// for each input, the variants are grouped by every other input's value,
/// and each group adds one for every drawing beyond its first. The one with
/// more values gives the rows. Other inputs that change the drawing are
/// filters; those that never do are left out. Simplified symbols is never an
/// axis: the <b>Simplified / Paper chart</b> switch shows either style
/// without changing the chart setting.
/// </para>
/// <para>
/// Each cell is the glyph of the variant's lead part, a term that opens its
/// entry. Opened from a pick, the filters start on the feature's values and
/// the cell that matches them is ringed.
/// </para>
/// </remarks>
internal sealed class VariantTableViewModel : ViewModelBase
{
    /// <summary>A cell glyph's size, in logical pixels.</summary>
    public const int CellGlyphSize = 32;

    /// <summary>The row headers' width, in logical pixels: with five 46 px columns the table fits the 328 px inside the entry.</summary>
    public const double RowHeaderWidth = 96;

    /// <summary>A cell's width and height, in logical pixels.</summary>
    public const double CellWidth = 44;

    private readonly FeatureTypeEntry _feature;
    private readonly FeatureTypeProbe _probe;
    private readonly ReferenceEntryContext _context;
    private readonly int _style;
    private readonly int _rows;
    private readonly int _columns;
    private readonly int?[] _picked;
    private readonly int[] _held;
    private readonly List<Task> _drawing = [];
    private IReadOnlyList<VariantRowViewModel> _rowItems = [];
    private bool _showsSimplified;
    private int _drawVersion;

    /// <summary>Creates the table.</summary>
    /// <param name="feature">The feature type.</param>
    /// <param name="probe">Its probe results.</param>
    /// <param name="context">The host: the index, glyphs, symbol style and the picked feature.</param>
    public VariantTableViewModel(FeatureTypeEntry feature, FeatureTypeProbe probe, ReferenceEntryContext context)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(context);
        _feature = feature;
        _probe = probe;
        _context = context;
        var index = context.Index;

        _style = ProbeValues.StyleInput(probe);
        _picked = context.Feature is { IsPick: true } picked
            ? ProbeValues.IndexesOf(probe, index, picked)
            : new int?[probe.Inputs.Count];
        _held = [.. _picked.Select(v => v ?? 0)];
        _showsSimplified = context.SimplifiedSymbols;

        // The inputs that change the drawing, the ones that split most first.
        var axes = Enumerable.Range(0, probe.Inputs.Count)
            .Where(i => i != _style && probe.Inputs[i].Values.Count > 1)
            .Select(i => (Input: i, Score: SplitOf(i)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Input)
            .Select(x => x.Input)
            .ToList();
        var two = axes.Take(2).OrderByDescending(i => probe.Inputs[i].Values.Count).ThenBy(i => i).ToList();
        _rows = two.Count > 0 ? two[0] : -1;
        _columns = two.Count > 1 ? two[1] : -1;

        RowHeading = _rows < 0 ? null : VariantLabel.Of(NameOf(_rows));
        ColumnHeading = _columns < 0 ? null : VariantLabel.Of(NameOf(_columns));
        Columns = _columns < 0
            ? [VariantLabel.Of(TermText.Empty)]
            : [.. probe.Inputs[_columns].Values.Select(v => VariantLabel.Of(ValueOf(_columns, v)))];
        Filters = [.. axes.Skip(2).Select(i => new VariantFilterViewModel(
            NameOf(i).Text,
            [.. probe.Inputs[i].Values.Select(v => ValueOf(i, v).Text)],
            _held[i],
            selected =>
            {
                _held[i] = selected;
                Build();
            })
        {
            Input = i,
        })];
        HasStyleSwitch = _style >= 0;
        IsPartial = probe.IsPartial;
        PartialText = string.Format(CultureInfo.CurrentCulture, Strings.Reference_Variants_Partial, index.Probes?.Cap ?? RuleProbeSet.DefaultCap);
        Build();
    }

    /// <summary>The row input's name ("Beacon shape"), or <see langword="null"/> when nothing splits the variants.</summary>
    public VariantLabel? RowHeading { get; }

    /// <summary>The column input's name ("Colour"), or <see langword="null"/> for a single column.</summary>
    public VariantLabel? ColumnHeading { get; }

    /// <summary>Whether there is a column heading.</summary>
    public bool HasColumnHeading => ColumnHeading is not null;

    /// <summary>The column headers: each value of the column input, or one blank header for a single column.</summary>
    public IReadOnlyList<VariantLabel> Columns { get; }

    /// <summary>The rows, each with its cells; rebuilt when the style or a filter changes.</summary>
    public IReadOnlyList<VariantRowViewModel> Rows
    {
        get => _rowItems;
        private set => SetProperty(ref _rowItems, value);
    }

    /// <summary>Every cell, row by row.</summary>
    public IEnumerable<VariantCellViewModel> Cells => _rowItems.SelectMany(r => r.Cells);

    /// <summary>The other inputs that change the drawing, each a filter.</summary>
    public IReadOnlyList<VariantFilterViewModel> Filters { get; }

    /// <summary>Whether there are filters.</summary>
    public bool HasFilters => Filters.Count > 0;

    /// <summary>Whether the rule draws differently with Simplified symbols on and off, so the switch shows.</summary>
    public bool HasStyleSwitch { get; }

    /// <summary>
    /// Whether the table shows the Simplified symbols style; it starts on the
    /// chart's style and never changes the chart setting.
    /// </summary>
    public bool ShowsSimplified
    {
        get => _showsSimplified;
        set
        {
            if (!SetProperty(ref _showsSimplified, value))
                return;
            OnPropertyChanged(nameof(ShowsPaperChart));
            Build();
        }
    }

    /// <summary>Whether the table shows the paper chart style.</summary>
    public bool ShowsPaperChart
    {
        get => !_showsSimplified;
        set => ShowsSimplified = !value;
    }

    /// <summary>Whether the probe hit its cap, so some variants may be missing.</summary>
    public bool IsPartial { get; }

    /// <summary>"Partial: the first 512 combinations".</summary>
    public string PartialText { get; }

    /// <summary>The cell ringed for the picked feature, or <see langword="null"/>.</summary>
    public VariantCellViewModel? Ringed => Cells.FirstOrDefault(c => c.IsRinged);

    /// <summary>Completes when the cell glyphs being drawn now are done; tests wait on it.</summary>
    internal Task GlyphsDrawn
    {
        get
        {
            lock (_drawing)
                return Task.WhenAll(_drawing);
        }
    }

    /// <summary>The cells' tile: the palette's chart background.</summary>
    public IBrush Tile => _context.Glyphs.Tile;

    /// <summary>Draws the cell glyphs again, after the palette changed.</summary>
    public void ReloadGlyphs()
    {
        OnPropertyChanged(nameof(Tile));
        foreach (var cell in Cells)
            cell.Glyph = null;
        DrawGlyphs();
    }

    /// <summary>Starts drawing the cell glyphs; the entry calls it once shown.</summary>
    public void Start() => DrawGlyphs();

    private void Build()
    {
        var spec = _context.Index.Spec;
        int rowCount = _rows < 0 ? 1 : _probe.Inputs[_rows].Values.Count;
        int columnCount = _columns < 0 ? 1 : _probe.Inputs[_columns].Values.Count;
        var styleValue = _style < 0 ? -1 : ProbeValues.SettingValue(_probe.Inputs[_style], _showsSimplified);
        var rows = new List<VariantRowViewModel>(rowCount);
        for (int r = 0; r < rowCount; r++)
        {
            var cells = new List<VariantCellViewModel>(columnCount);
            for (int c = 0; c < columnCount; c++)
            {
                var values = (int[])_held.Clone();
                if (_rows >= 0)
                    values[_rows] = r;
                if (_columns >= 0)
                    values[_columns] = c;
                if (styleValue >= 0)
                    values[_style] = styleValue;
                var outcome = _probe.OutcomeOf(values);
                cells.Add(Cell(spec, outcome, values, IsPicked(values)));
            }

            var label = _rows < 0 ? (TermText)Strings.Reference_Variants_EveryCase : ValueOf(_rows, _probe.Inputs[_rows].Values[r]);
            rows.Add(new VariantRowViewModel(VariantLabel.Of(label), cells));
        }

        Rows = rows;
        OnPropertyChanged(nameof(Ringed));
        if (_drawVersion > 0)
            DrawGlyphs();
    }

    // The cell whose values are the picked feature's, for every input the table shows.
    private bool IsPicked(int[] values)
    {
        if (_context.Feature is not { IsPick: true })
            return false;
        foreach (var i in ShownInputs())
        {
            if (_picked[i] != values[i])
                return false;
        }

        return true;
    }

    private IEnumerable<int> ShownInputs()
    {
        if (_rows >= 0)
            yield return _rows;
        if (_columns >= 0)
            yield return _columns;
        for (int i = 0; i < _probe.Inputs.Count; i++)
        {
            if (i != _rows && i != _columns && i != _style && Filters.Any(f => f.Input == i))
                yield return i;
        }
    }

    private VariantCellViewModel Cell(Core.SpecRef spec, ProbeOutcome? outcome, int[] values, bool ringed)
    {
        var where = string.Join(
            RuleConditionText.Separator,
            ShownInputs().Select(i => string.Format(CultureInfo.CurrentCulture, Strings.Reference_Variants_InputValue, NameOf(i).Text, ValueOf(i, _probe.Inputs[i].Values[values[i]]).Text)));
        if (outcome is null || ProbeValues.MainPart(outcome) is not { } lead)
            return new VariantCellViewModel(null, null, Strings.Reference_Variants_NotDrawn, Strings.Reference_Variants_NotDrawn, where, ringed, isFallback: false);

        var key = ProbeValues.KeyOf(spec, lead);
        var (kind, id) = ProbeValues.Split(lead);
        LeadGlyph? glyph = kind switch
        {
            "symbol" when key is { } k => new LeadGlyph(LeadGlyphKind.Symbol, k, id),
            "fill" when key is { } k => new LeadGlyph(LeadGlyphKind.AreaFill, k, id),
            "line" when key is { } k => new LeadGlyph(LeadGlyphKind.LineStyle, k, id),
            // A plain colour fill has no entry: its glyph is keyed by the token.
            "colour" => new LeadGlyph(LeadGlyphKind.Area, new ReferenceKey(spec, ReferenceKind.FeatureType, $"{_feature.Code}|{lead}"), Tokens: [id]),
            _ => null,
        };
        var text = kind == "text" ? id : key is null && glyph is null ? lead : null;
        var name = key?.Code ?? (kind == "text" ? id : lead);
        var drawn = string.Join(", ", outcome.Parts.Select(p => ProbeValues.Split(p).Id).Where(p => p.Length > 0 && p[0] != '_'));
        bool fallback = outcome.IsError || (kind == "symbol" && LeadGlyph.DefaultSymbols.Contains(id));
        if (fallback)
            drawn = string.Format(CultureInfo.CurrentCulture, Strings.Reference_Variants_FallbackDrawn, drawn);
        return new VariantCellViewModel(key, glyph, text, name, string.IsNullOrEmpty(where) ? drawn : drawn + RuleConditionText.Separator + where, ringed, fallback)
        {
            IsKnown = key is { } known && _context.Index.TryGet(known, out _),
        };
    }

    // How many more drawings the variants show once split by an input: grouped
    // by every other input's value, a group with three drawings adds two.
    private int SplitOf(int input)
    {
        var groups = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var variant in _probe.GetVariants())
        {
            var rest = string.Join(',', variant.Values.Select((v, i) => i == input ? "*" : v.ToString(CultureInfo.InvariantCulture)));
            if (!groups.TryGetValue(rest, out var drawn))
                groups[rest] = drawn = new HashSet<string>(StringComparer.Ordinal);
            drawn.Add(string.Join('|', variant.Outcome.Parts));
        }

        return groups.Values.Sum(g => g.Count - 1);
    }

    private TermText NameOf(int input)
    {
        var probe = _probe.Inputs[input];
        var index = _context.Index;
        return probe.Kind switch
        {
            ProbeInputKind.Primitive => Strings.Reference_Variants_Geometry,
            ProbeInputKind.ContextParameter => RuleConditionText.SettingName(index, probe.Code),
            _ => RuleConditionText.AttributeName(AttributeOf(probe.Code), probe.Code),
        };
    }

    private TermText ValueOf(int input, string? value)
    {
        var probe = _probe.Inputs[input];
        if (value is null)
            return Strings.Reference_Variants_NotGiven;
        switch (probe.Kind)
        {
            case ProbeInputKind.Primitive:
                var geometry = RuleConditionText.Geometry(value);
                return geometry.Length == 0 ? value : char.ToUpper(geometry[0], CultureInfo.CurrentCulture) + geometry[1..];
            case ProbeInputKind.ContextParameter:
                return value.ToLowerInvariant() switch
                {
                    "true" => Strings.Reference_Variants_On,
                    "false" => Strings.Reference_Variants_Off,
                    _ => value,
                };
            default:
                var attribute = AttributeOf(probe.Code);
                if (attribute is not { ListedValues.Count: > 0 })
                    return value;
                var codes = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var label = TermText.Join(", ", codes.Select(c => RuleConditionText.ValueLabel(attribute, c)));
                return label.IsEmpty ? value : Capitalise(label);
        }
    }

    // "red, green" leads with a capital in a header: "Red, green".
    private static TermText Capitalise(TermText text)
    {
        if (text.Runs.Count == 0 || text.Runs[0].Text.Length == 0 || char.IsUpper(text.Runs[0].Text[0]))
            return text;
        var first = text.Runs[0];
        var head = char.ToUpper(first.Text[0], CultureInfo.CurrentCulture) + first.Text[1..];
        return TermText.Of([first with { Text = head }, .. text.Runs.Skip(1)]);
    }

    private AttributeEntry? AttributeOf(string code) =>
        _context.Index.TryGet(new ReferenceKey(_context.Index.Spec, ReferenceKind.Attribute, code), out var entry) ? entry as AttributeEntry : null;

    private void DrawGlyphs()
    {
        var version = ++_drawVersion;
        if (_context.Artwork is not { } artwork)
            return;
        var glyphs = _context.Glyphs;
        var palette = glyphs.Palette;
        foreach (var group in Cells.Where(c => c.Lead is not null).GroupBy(c => c.Lead!.Key))
        {
            var lead = group.First().Lead!;
            var cells = group.ToList();
            if (glyphs.TryGet(lead.Key, CellGlyphSize, CellGlyphSize, out var held))
            {
                foreach (var cell in cells)
                    cell.Glyph = held;
                continue;
            }

            var task = Task.Run(async () =>
            {
                try
                {
                    var row = await artwork.BuildAsync(lead, palette).ConfigureAwait(false);
                    return glyphs.Palette != palette ? null : glyphs.Get(lead.Key, CellGlyphSize, CellGlyphSize, _ => row);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A glyph that can't be drawn leaves the cell's name.
                    return null;
                }
            });
            var applied = task.ContinueWith(
                t => _context.Post(() =>
                {
                    if (version != _drawVersion)
                        return;
                    foreach (var cell in cells)
                        cell.Glyph = t.Result;
                }),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
            lock (_drawing)
                _drawing.Add(applied);
        }
    }
}

/// <summary>
/// A heading or value of a variant table, shown as one term that wraps in
/// its column: an attribute, a setting or a single listed value is a term;
/// a list of values ("Red, green, red") is plain text.
/// </summary>
/// <param name="Text">The words.</param>
/// <param name="Key">The entry the words name, or <see langword="null"/> for plain text.</param>
/// <param name="Value">A listed value's code, when the words name one.</param>
internal sealed record VariantLabel(string Text, ReferenceKey? Key = null, string? Value = null)
{
    /// <summary>The label of words with terms: a term when they are one term alone.</summary>
    /// <param name="words">The words.</param>
    public static VariantLabel Of(TermText words) =>
        words.Runs is [{ IsTerm: true } only]
            ? new VariantLabel(only.Text, only.Key, only.Value)
            : new VariantLabel(words.Text);
}

/// <summary>A row of a variant table: a value of the row input, and its cells.</summary>
/// <param name="Label">The value.</param>
/// <param name="Cells">A cell per column.</param>
internal sealed record VariantRowViewModel(VariantLabel Label, IReadOnlyList<VariantCellViewModel> Cells);

/// <summary>
/// A cell of a variant table: the glyph of what the rule draws for one
/// combination, a term that opens the drawn item's entry.
/// </summary>
internal sealed class VariantCellViewModel : ViewModelBase
{
    private IImage? _glyph;

    /// <summary>Creates a cell.</summary>
    /// <param name="key">The drawn item's entry, or <see langword="null"/> for one without (a colour, text).</param>
    /// <param name="lead">The glyph to draw, or <see langword="null"/>.</param>
    /// <param name="text">Text shown in place of a glyph (drawn text, or nothing drawn), or <see langword="null"/>.</param>
    /// <param name="name">The drawn item's name: its id, or the text.</param>
    /// <param name="description">Everything drawn, then the values the cell stands for.</param>
    /// <param name="isRinged">Whether it is the picked feature's cell.</param>
    /// <param name="isFallback">Whether the catalogue's fallback symbol is drawn.</param>
    public VariantCellViewModel(ReferenceKey? key, LeadGlyph? lead, string? text, string name, string description, bool isRinged, bool isFallback)
    {
        Key = key;
        Lead = lead;
        Text = text;
        Name = name;
        Description = description;
        IsRinged = isRinged;
        IsFallback = isFallback;
    }

    /// <summary>The drawn item's entry, or <see langword="null"/>.</summary>
    public ReferenceKey? Key { get; }

    /// <summary>Whether the index holds <see cref="Key"/>'s entry, so the cell is a term.</summary>
    public bool IsKnown { get; init; }

    /// <summary>The glyph drawn, or <see langword="null"/>.</summary>
    public LeadGlyph? Lead { get; }

    /// <summary>The text shown when there is no glyph, or <see langword="null"/>.</summary>
    public string? Text { get; }

    /// <summary>Whether the cell shows text.</summary>
    public bool HasText => Text is not null && _glyph is null;

    /// <summary>The drawn item's name: the cell's accessible name.</summary>
    public string Name { get; }

    /// <summary>Everything drawn, then the values the cell stands for: the tooltip and help text.</summary>
    public string Description { get; }

    /// <summary>Whether this is the picked feature's cell.</summary>
    public bool IsRinged { get; }

    /// <summary>Whether the catalogue's fallback symbol is drawn: the rule found nothing to draw.</summary>
    public bool IsFallback { get; }

    /// <summary>The glyph, or <see langword="null"/> until drawn.</summary>
    public IImage? Glyph
    {
        get => _glyph;
        set
        {
            if (!SetProperty(ref _glyph, value))
                return;
            OnPropertyChanged(nameof(HasGlyph));
            OnPropertyChanged(nameof(HasText));
        }
    }

    /// <summary>Whether the glyph is drawn.</summary>
    public bool HasGlyph => _glyph is not null;
}

/// <summary>An input a variant table holds at one value: a filter above the table.</summary>
internal sealed class VariantFilterViewModel : ViewModelBase
{
    private readonly Action<int> _changed;
    private int _selected;

    /// <summary>Creates a filter.</summary>
    /// <param name="name">The input's name.</param>
    /// <param name="options">Its values in words.</param>
    /// <param name="selected">The value shown first.</param>
    /// <param name="changed">Rebuilds the table for a value.</param>
    public VariantFilterViewModel(string name, IReadOnlyList<string> options, int selected, Action<int> changed)
    {
        Name = name;
        Options = options;
        _selected = selected;
        _changed = changed;
    }

    /// <summary>The input, as an index into the probe's inputs.</summary>
    internal int Input { get; init; } = -1;

    /// <summary>The input's name: the filter's label and accessible name.</summary>
    public string Name { get; }

    /// <summary>Its values in words.</summary>
    public IReadOnlyList<string> Options { get; }

    /// <summary>The value the table holds the input at.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value < 0 || value >= Options.Count || !SetProperty(ref _selected, value))
                return;
            _changed(value);
        }
    }
}
