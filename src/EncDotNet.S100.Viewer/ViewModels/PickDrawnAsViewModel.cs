using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.ChartReference;
using EncDotNet.S100.Viewer.Services.Legends;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Object information's <b>Drawn as</b> and header glyph (symbol dictionary
/// design note §4.3, R10, #1679): the parts the renderer drew for the
/// selected feature of a catalogue-portrayed product, read from the stack's
/// display list, each a term that opens its entry, and the main part's glyph
/// on the palette's chart tile in place of the generic feature icon.
/// </summary>
/// <remarks>
/// <para>
/// Glyphs are drawn through <see cref="ChartReferenceGlyphCache"/> from the
/// <see cref="LeadGlyph"/> of each part, as the Reference tab draws them: a
/// symbol, a line sample, or an area patch in a colour fill's colour. On a
/// palette change they are drawn again.
/// </para>
/// <para>
/// Products that aren't catalogue-portrayed (S-102, S-111, weather fields)
/// have no parts here: their key line (<see cref="PickLegendKeyLine"/>) stands.
/// </para>
/// </remarks>
internal sealed class PickDrawnAsViewModel : ViewModelBase, IDisposable
{
    /// <summary>The header glyph's size, in logical pixels: the identity block's 36 px tile.</summary>
    public const int HeaderGlyphSize = 36;

    /// <summary>A part's glyph size, in logical pixels.</summary>
    public const int PartGlyphSize = 24;

    private readonly Func<PickHit, PickDrawing?> _read;
    private readonly Func<SpecRef, ChartReferenceArtwork?> _artwork;
    private readonly Action<Action> _post;
    private readonly Func<string, string?> _setting;
    private readonly Func<string, string?>? _settingText;
    private readonly Action<string>? _changeSetting;
    private readonly Func<PickHit, CancellationToken, Task<LuaFeatureTrace?>>? _trace;
    private CancellationTokenSource? _tracing;
    private PickHit? _hit;
    private bool _whyExpanded;
    private PickDrawing? _drawing;
    private IImage? _glyph;
    private string? _glyphName;
    private int _version;
    private int _explainVersion;
    private Task _loading = Task.CompletedTask;

    /// <summary>Creates the section.</summary>
    /// <param name="read">Reads a picked feature's drawn parts (<see cref="LegendService.DrawnParts"/>).</param>
    /// <param name="terms">The chart reference behind the parts' terms, whose glyph cache draws the glyphs.</param>
    /// <param name="artwork">A product's artwork reader (<see cref="ChartReferenceService.GetArtwork"/>).</param>
    /// <param name="post">Runs an action on the UI thread.</param>
    /// <param name="setting">
    /// A portrayal context parameter's current value, as the rules compare it
    /// (<see cref="ReferenceTargets.Value"/>), for Why this symbol (R11, #1680);
    /// <see langword="null"/> when the viewer doesn't set it.
    /// </param>
    /// <param name="changeSetting">Opens Settings › Chart at a context parameter's switch, or <see langword="null"/>.</param>
    /// <param name="settingText">A context parameter's current value in words ("30 m"), as Settings shows it, or <see langword="null"/>.</param>
    /// <param name="trace">
    /// Traces a picked feature's rule off the UI thread (<see cref="PickRuleTracer.TraceAsync"/>,
    /// R12b, #1719), or <see langword="null"/>: Why this symbol keeps the match.
    /// It returns <see langword="null"/> for a feature it can't trace.
    /// </param>
    public PickDrawnAsViewModel(
        Func<PickHit, PickDrawing?> read,
        ReferenceTerms terms,
        Func<SpecRef, ChartReferenceArtwork?> artwork,
        Action<Action> post,
        Func<string, string?>? setting = null,
        Action<string>? changeSetting = null,
        Func<string, string?>? settingText = null,
        Func<PickHit, CancellationToken, Task<LuaFeatureTrace?>>? trace = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(artwork);
        ArgumentNullException.ThrowIfNull(post);
        _read = read;
        Terms = terms;
        _artwork = artwork;
        _post = post;
        _setting = setting ?? (static _ => null);
        _changeSetting = changeSetting;
        _settingText = settingText;
        _trace = trace;
        Glyphs.PaletteChanged += OnPaletteChanged;
    }

    /// <summary>The chart reference behind the parts' terms.</summary>
    public ReferenceTerms Terms { get; }

    /// <summary>The parts drawn for the selected feature, in order: symbols, line styles, fills, lines, text.</summary>
    public ObservableCollection<PickDrawnPartViewModel> Parts { get; } = [];

    /// <summary>Whether the section shows: the feature's product is catalogue-portrayed and something of it is drawn.</summary>
    public bool HasParts => Parts.Count > 0;

    /// <summary>What was read for the selected feature, or <see langword="null"/>.</summary>
    public PickDrawing? Drawing => _drawing;

    /// <summary>The main part's glyph (<see cref="PickDrawing.Main"/>), or <see langword="null"/>: the header shows the feature icon.</summary>
    public IImage? Glyph
    {
        get => _glyph;
        private set
        {
            if (SetProperty(ref _glyph, value))
                OnPropertyChanged(nameof(HasGlyph));
        }
    }

    /// <summary>Whether the header shows <see cref="Glyph"/> in place of the feature icon.</summary>
    public bool HasGlyph => _glyph is not null;

    /// <summary>The header glyph's accessible name and tooltip: "Drawn as BCNLAT21 · Minor lateral beacon…".</summary>
    public string? GlyphName
    {
        get => _glyphName;
        private set => SetProperty(ref _glyphName, value);
    }

    /// <summary>
    /// Whether <b>Why this symbol</b> is unfolded (R11, #1680). Folded by
    /// default; the choice holds from pick to pick.
    /// </summary>
    public bool WhyExpanded
    {
        get => _whyExpanded;
        set => SetProperty(ref _whyExpanded, value);
    }

    /// <summary>Whether a part has a reason, so <b>Why this symbol</b> shows.</summary>
    public bool HasReasons => Parts.Any(p => p.HasReason);

    /// <summary>The tile glyphs sit on: the palette's chart background, as on the map.</summary>
    public IBrush Tile => Glyphs.Tile;

    /// <summary>The glyphs and names being read; tests await it.</summary>
    internal Task Loading => _loading;

    private ChartReferenceGlyphCache Glyphs => Terms.Glyphs;

    /// <summary>Shows <paramref name="hit"/>'s drawn parts, or none.</summary>
    /// <param name="hit">The selected feature, or <see langword="null"/>.</param>
    public void Show(PickHit? hit)
    {
        _hit = hit;
        Apply(hit is null ? null : _read(hit), force: true);
    }

    /// <summary>
    /// Reads the selected feature's parts again, after the stack changed: a
    /// display setting, a pan. Nothing changes when the parts are the same.
    /// </summary>
    public void Refresh()
    {
        if (_hit is { } hit)
            Apply(_read(hit), force: false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Glyphs.PaletteChanged -= OnPaletteChanged;
        CancelTrace();
    }

    private void Apply(PickDrawing? drawing, bool force)
    {
        if (!force && Same(_drawing, drawing))
        {
            // The same parts, but a display setting may have changed why.
            if (_drawing is { } held && _hit is { } same)
                _loading = ExplainAsync(held, same);
            return;
        }

        _drawing = drawing;
        CancelTrace();
        var version = ++_version;
        Parts.Clear();
        Glyph = null;
        GlyphName = null;
        if (drawing is not null)
        {
            foreach (var part in drawing.Parts)
                Parts.Add(new PickDrawnPartViewModel(part, Tile) { IsMain = part == drawing.Main });
            GlyphName = NameOf(drawing);
        }

        OnPropertyChanged(nameof(HasParts));
        OnPropertyChanged(nameof(HasReasons));
        OnPropertyChanged(nameof(Drawing));
        OnPropertyChanged(nameof(Tile));
        _loading = drawing is null || _hit is not { } hit
            ? Task.CompletedTask
            : Task.WhenAll(LoadAsync(drawing, version), ExplainAsync(drawing, hit));
    }

    // Why this symbol (R11, #1680): each part's reason, from the feature
    // type's cases for it, matched against the pick's values and the current
    // display settings. Then, for a Lua product, the rule's trace (R12b,
    // #1719): the match shows first, and each part the trace maps is swapped
    // for the case the rule took. A newer pick or setting cancels the trace.
    private async Task ExplainAsync(PickDrawing drawing, PickHit hit)
    {
        var version = ++_explainVersion;
        var parts = Parts.ToList();
        var cancel = StartTrace();
        try
        {
            var index = await Terms.IndexAsync(drawing.Spec).ConfigureAwait(false);
            if (index is null || PickTerms.FeatureType(index, hit.FeatureType) is not { } feature)
                return;
            var facts = new PickFacts(hit.Attributes, hit.Geometry, _setting, _settingText);
            var reasons = parts.Select(p => PickReasons.Explain(p.Part, feature, index, facts)).ToList();
            Post(reasons);

            if (_trace is null || cancel is null)
                return;
            var trace = await _trace(hit, cancel.Token).ConfigureAwait(false);
            if (trace is null || cancel.IsCancellationRequested)
                return;
            var traced = parts.Select(p => PickReasons.Trace(p.Part, feature, index, facts, trace)).ToList();
            if (traced.Any(r => r is not null))
                Post([.. traced.Select((r, i) => r ?? reasons[i])]);

            void Post(List<PickReason> said) => _post(() =>
            {
                if (version != _explainVersion || !ReferenceEquals(drawing, _drawing))
                    return;
                for (int i = 0; i < parts.Count; i++)
                    parts[i].Reason = new PickReasonViewModel(said[i], index, _setting, _changeSetting);
                OnPropertyChanged(nameof(HasReasons));
            });
        }
        catch (OperationCanceledException)
        {
            // A newer pick or setting: its reasons replace these.
        }
        catch (Exception)
        {
            // A catalogue that can't be read, or a rule that fails when
            // traced: the match stands, or Why this symbol stays hidden.
        }
    }

    // Cancels the trace in flight and starts the next one's token, or none
    // when nothing traces.
    private CancellationTokenSource? StartTrace()
    {
        CancelTrace();
        if (_trace is null)
            return null;
        var cancel = new CancellationTokenSource();
        _tracing = cancel;
        return cancel;
    }

    private void CancelTrace()
    {
        // Not disposed: the trace may still hold the token; it is dropped with it.
        _tracing?.Cancel();
        _tracing = null;
    }

    private string? NameOf(PickDrawing drawing)
    {
        var main = drawing.Main is { } part ? Parts.FirstOrDefault(p => p.Part == part) : null;
        return main is null ? null : string.Format(CultureInfo.CurrentCulture, Strings.Pick_DrawnAs_GlyphNameFormat, main.Label);
    }

    private void OnPaletteChanged(object? sender, EventArgs e) => _post(() =>
    {
        OnPropertyChanged(nameof(Tile));
        if (_drawing is not { } drawing)
            return;
        var version = ++_version;
        foreach (var part in Parts)
            part.SetTile(Tile);
        _loading = LoadAsync(drawing, version, namesToo: false);
    });

    // The parts' names from the index, then the glyphs, each posted to the
    // UI thread unless a newer pick or palette has replaced them.
    private async Task LoadAsync(PickDrawing drawing, int version, bool namesToo = true)
    {
        var parts = Parts.ToList();
        try
        {
            var artwork = _artwork(drawing.Spec);
            if (namesToo)
            {
                foreach (var part in parts)
                {
                    if (part.Part.Key is { } key)
                    {
                        var info = await Terms.DescribeAsync(key).ConfigureAwait(false);
                        if (info is not null)
                            _post(() => { if (version == _version) part.SetName(info.Name); });
                    }
                    else if (part.Part.Colour is { } colour && artwork is not null)
                    {
                        // A colour fill or line is named by its colour: "Very shallow water".
                        var name = await artwork.ColourNameAsync(colour).ConfigureAwait(false);
                        if (name is not null)
                            _post(() => { if (version == _version) part.SetName(name); });
                    }
                }

                _post(() => { if (version == _version) GlyphName = NameOf(drawing); });
            }

            if (artwork is null)
                return;
            foreach (var part in parts)
            {
                if (part.Lead(drawing.Spec) is not { } lead)
                    continue;
                var small = await GlyphAsync(artwork, lead, PartGlyphSize).ConfigureAwait(false);
                _post(() => { if (version == _version) part.Glyph = small; });
                if (part.Part == drawing.Main)
                {
                    var large = await GlyphAsync(artwork, lead, HeaderGlyphSize).ConfigureAwait(false);
                    _post(() => { if (version == _version) Glyph = large; });
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A catalogue that can't be read: the parts stay named by id, without glyphs.
        }
    }

    private async Task<IImage?> GlyphAsync(ChartReferenceArtwork artwork, LeadGlyph lead, int size)
    {
        if (Glyphs.TryGet(lead.Key, size, size, out var held))
            return held;
        var palette = Glyphs.Palette;
        var row = await artwork.BuildAsync(lead, palette).ConfigureAwait(false);
        return Glyphs.Palette != palette ? null : Glyphs.Get(lead.Key, size, size, _ => row);
    }

    private static bool Same(PickDrawing? a, PickDrawing? b) =>
        a is null ? b is null
        : b is not null && a.Spec == b.Spec && a.Parts.SequenceEqual(b.Parts);
}

/// <summary>
/// One part in <b>Drawn as</b> (R10, #1679): a term naming what was drawn,
/// "BCNLAT21 · Minor lateral beacon, red, simplified", with its glyph, and a
/// plain note when it is the catalogue's fallback symbol.
/// </summary>
internal sealed class PickDrawnPartViewModel : ViewModelBase
{
    private string _label;
    private IImage? _glyph;
    private IBrush _tile;
    private PickReasonViewModel? _reason;
    private bool _reasonExpanded;

    /// <summary>Creates the part's row.</summary>
    /// <param name="part">The drawn part.</param>
    /// <param name="tile">The palette's chart background, which the glyph sits on.</param>
    public PickDrawnPartViewModel(PickDrawnPart part, IBrush tile)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(tile);
        Part = part;
        _tile = tile;
        _label = LabelOf(part, name: null);
    }

    /// <summary>The drawn part: its kind, its entry and the instruction's feature reference.</summary>
    public PickDrawnPart Part { get; }

    /// <summary>The entry the term opens, or <see langword="null"/> for a part that names no catalogue item.</summary>
    public ReferenceKey? Key => Part.Key;

    /// <summary>The words: the id and what the catalogue says it shows, "Colour fill: DEPMS", "Text: 1".</summary>
    public string Label
    {
        get => _label;
        private set
        {
            if (SetProperty(ref _label, value))
                OnPropertyChanged(nameof(AccessibleName));
        }
    }

    /// <summary>
    /// The colour token of a colour fill or a simple line, "Colour token:
    /// DEPVS", which the row's tooltip and accessible help text give while
    /// the label names the colour; <see langword="null"/> for other parts.
    /// </summary>
    public string? HelpText => Part.Colour is { } colour
        ? string.Format(CultureInfo.CurrentCulture, Strings.Pick_DrawnAs_ColourTokenFormat, colour)
        : null;

    /// <summary>Whether the part is the catalogue's fallback symbol.</summary>
    public bool IsFallback => Part.IsFallback;

    /// <summary>"The catalogue's fallback symbol: nothing else matched".</summary>
    public string FallbackNote => Strings.Pick_DrawnAs_Fallback;

    /// <summary>The row's accessible name: the label, and the fallback note when it is one.</summary>
    public string AccessibleName => IsFallback
        ? string.Format(CultureInfo.CurrentCulture, Strings.Pick_DrawnAs_PartNameFormat, Label, FallbackNote)
        : Label;

    /// <summary>The part's glyph, or <see langword="null"/>: text, or not drawn yet.</summary>
    public IImage? Glyph
    {
        get => _glyph;
        internal set
        {
            if (SetProperty(ref _glyph, value))
                OnPropertyChanged(nameof(HasGlyph));
        }
    }

    /// <summary>Whether <see cref="Glyph"/> shows.</summary>
    public bool HasGlyph => _glyph is not null;

    /// <summary>The palette's chart background, under the glyph.</summary>
    public IBrush Tile
    {
        get => _tile;
        private set => SetProperty(ref _tile, value);
    }

    /// <summary>
    /// Whether this is the main part (<see cref="PickDrawing.Main"/>), whose
    /// reason Why this symbol gives in full (D5). Each other part is one line
    /// naming it, with its reason folded underneath.
    /// </summary>
    public bool IsMain { get; init; }

    /// <summary>Why the part was drawn (R11, #1680), or <see langword="null"/> while it is read or when it can't be.</summary>
    public PickReasonViewModel? Reason
    {
        get => _reason;
        internal set
        {
            if (SetProperty(ref _reason, value))
            {
                OnPropertyChanged(nameof(HasReason));
                OnPropertyChanged(nameof(ShowsReasonInFull));
                OnPropertyChanged(nameof(ShowsReasonFolded));
            }
        }
    }

    /// <summary>Whether <see cref="Reason"/> is read.</summary>
    public bool HasReason => _reason is not null;

    /// <summary>Whether Why this symbol gives the reason in full: the main part's.</summary>
    public bool ShowsReasonInFull => IsMain && HasReason;

    /// <summary>Whether Why this symbol gives the part one line, its reason folded underneath: a topmark, a light, text.</summary>
    public bool ShowsReasonFolded => !IsMain && HasReason;

    /// <summary>Whether an other part's reason is unfolded; folded by default.</summary>
    public bool ReasonExpanded
    {
        get => _reasonExpanded;
        set => SetProperty(ref _reasonExpanded, value);
    }

    /// <summary>Follows a palette change.</summary>
    /// <param name="tile">The new palette's chart background.</param>
    internal void SetTile(IBrush tile) => Tile = tile;

    /// <summary>
    /// Names the part: a symbol, line style or area fill from its entry, a
    /// colour fill or line from its colour's description in the colour profile.
    /// </summary>
    /// <param name="name">What the catalogue says the item or colour shows.</param>
    internal void SetName(string name) => Label = LabelOf(Part, name);

    /// <summary>
    /// The glyph the part shows, as the Reference tab draws it: the item
    /// itself, a patch in a colour fill's colour, a sample of a simple line;
    /// <see langword="null"/> for text.
    /// </summary>
    /// <param name="spec">The product the feature is portrayed with.</param>
    internal LeadGlyph? Lead(SpecRef spec) => Part.Kind switch
    {
        PickDrawnPartKind.Symbol => new LeadGlyph(LeadGlyphKind.Symbol, Part.Key!.Value, Part.ItemId),
        PickDrawnPartKind.LineStyle => new LeadGlyph(LeadGlyphKind.LineStyle, Part.Key!.Value, Part.ItemId),
        PickDrawnPartKind.AreaFill => new LeadGlyph(LeadGlyphKind.AreaFill, Part.Key!.Value, Part.ItemId),
        // Keyed by colour, so areas drawn in one colour share a patch.
        PickDrawnPartKind.ColourFill => new LeadGlyph(
            LeadGlyphKind.Area,
            new ReferenceKey(spec, ReferenceKind.AreaFill, "colour:" + Part.Colour),
            Tokens: [Part.Colour!]),
        PickDrawnPartKind.Line => new LeadGlyph(
            LeadGlyphKind.Pen,
            new ReferenceKey(spec, ReferenceKind.LineStyle, string.Create(CultureInfo.InvariantCulture, $"pen:{Part.Colour}:{Part.Width}:{Part.Dash}")),
            Pen: new PenUse { RuleFile = string.Empty, Token = Part.Colour!, Width = Part.Width, Dash = Part.Dash ?? "solid" }),
        _ => null,
    };

    private static string LabelOf(PickDrawnPart part, string? name) => part.Kind switch
    {
        PickDrawnPartKind.ColourFill => string.Format(CultureInfo.CurrentCulture, Strings.Pick_DrawnAs_ColourFillFormat, ColourOf(part, name)),
        PickDrawnPartKind.Line => string.Format(
            CultureInfo.CurrentCulture,
            part.Dash switch
            {
                "dash" => Strings.Pick_DrawnAs_DashedLineFormat,
                "dot" => Strings.Pick_DrawnAs_DottedLineFormat,
                _ => Strings.Pick_DrawnAs_LineFormat,
            },
            ColourOf(part, name)),
        PickDrawnPartKind.Text => string.Format(CultureInfo.CurrentCulture, Strings.Pick_DrawnAs_TextFormat, part.Text),
        _ when string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), part.ItemId, StringComparison.OrdinalIgnoreCase) => part.ItemId ?? string.Empty,
        _ => string.Format(CultureInfo.CurrentCulture, Strings.Pick_DrawnAs_ItemFormat, part.ItemId, name.Trim()),
    };

    // A colour's name, capitalised as the Legend's labels are ("Very shallow
    // water", "Deep water"), else its token.
    private static string ColourOf(PickDrawnPart part, string? name) =>
        CatalogueSymbolKey.Label(part.Colour ?? string.Empty, name);
}

/// <summary>
/// One part's reason in <b>Why this symbol</b> (R11, #1680): the conditions
/// that held, a line each, with the names of attributes, values and display
/// settings as terms, and a link to each display setting it names.
/// </summary>
internal sealed class PickReasonViewModel
{
    /// <summary>Creates the reason's view.</summary>
    /// <param name="reason">The reason.</param>
    /// <param name="index">The index that names the display settings.</param>
    /// <param name="setting">A context parameter's current value; a setting the viewer doesn't set has no link.</param>
    /// <param name="changeSetting">Opens Settings › Chart at a setting's switch, or <see langword="null"/>: no links.</param>
    public PickReasonViewModel(
        PickReason reason,
        ChartReferenceIndex index,
        Func<string, string?> setting,
        Action<string>? changeSetting)
    {
        ArgumentNullException.ThrowIfNull(reason);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(setting);
        Reason = reason;
        Settings = changeSetting is null
            ? []
            :
            [
                .. reason.Settings
                    .Where(k => setting(k.Code) is not null && index.TryGet(k, out _))
                    .Select(k =>
                    {
                        index.TryGet(k, out var entry);
                        return new PickReasonSettingViewModel(k.Code, RuleConditionText.SentenceCase(entry!.Name), changeSetting);
                    }),
            ];
    }

    /// <summary>The reason.</summary>
    public PickReason Reason { get; }

    /// <summary>How sure it is.</summary>
    public PickReasonKind Kind => Reason.Kind;

    /// <summary>Whether it is a match that isn't sure: "Likely: …".</summary>
    public bool IsLikely => Reason.Kind == PickReasonKind.Likely;

    /// <summary>The reason's lines, with their terms.</summary>
    public IReadOnlyList<TermText> Lines => Reason.Lines;

    /// <summary>The reason as plain text, its lines joined by line breaks.</summary>
    public string Text => Reason.Text;

    /// <summary>The reason as one line, for its accessible help text.</summary>
    public string HelpText => string.Join(". ", Reason.Lines.Select(l => l.Text.TrimEnd(':')).Append(Reason.Note).OfType<string>());

    /// <summary>Whether a rule trace gave the reason (R12b, #1719).</summary>
    public bool IsTraced => Reason.Kind == PickReasonKind.Traced;

    /// <summary>"Also depends on values the rule works out", or <see langword="null"/>.</summary>
    public string? Note => Reason.Note;

    /// <summary>Whether <see cref="Note"/> shows.</summary>
    public bool HasNote => Reason.Note is not null;

    /// <summary>A link to each display setting the reason names that the viewer sets.</summary>
    public IReadOnlyList<PickReasonSettingViewModel> Settings { get; }

    /// <summary>Whether <see cref="Settings"/> has a link.</summary>
    public bool HasSettings => Settings.Count > 0;
}

/// <summary>
/// "Change Simplified symbols in Display settings" under a reason (R11, #1680):
/// opens Settings › Chart at the switch, as the Reference tab's <b>Change in
/// Display settings</b> does.
/// </summary>
internal sealed class PickReasonSettingViewModel
{
    /// <summary>Creates the link.</summary>
    /// <param name="code">The context parameter id, e.g. <c>SimplifiedSymbols</c>.</param>
    /// <param name="name">The setting's name, "Simplified symbols".</param>
    /// <param name="change">Opens Settings at the switch.</param>
    public PickReasonSettingViewModel(string code, string name, Action<string> change)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(change);
        Code = code;
        Label = string.Format(CultureInfo.CurrentCulture, Strings.Pick_Why_ChangeSettingFormat, name);
        ChangeCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => change(code));
    }

    /// <summary>The context parameter id.</summary>
    public string Code { get; }

    /// <summary>"Change Simplified symbols in Display settings".</summary>
    public string Label { get; }

    /// <summary>Opens Settings at the switch.</summary>
    public System.Windows.Input.ICommand ChangeCommand { get; }
}
