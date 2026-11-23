using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Reference;
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
    private PickHit? _hit;
    private PickDrawing? _drawing;
    private IImage? _glyph;
    private string? _glyphName;
    private int _version;
    private Task _loading = Task.CompletedTask;

    /// <summary>Creates the section.</summary>
    /// <param name="read">Reads a picked feature's drawn parts (<see cref="LegendService.DrawnParts"/>).</param>
    /// <param name="terms">The chart reference behind the parts' terms, whose glyph cache draws the glyphs.</param>
    /// <param name="artwork">A product's artwork reader (<see cref="ChartReferenceService.GetArtwork"/>).</param>
    /// <param name="post">Runs an action on the UI thread.</param>
    public PickDrawnAsViewModel(
        Func<PickHit, PickDrawing?> read,
        ReferenceTerms terms,
        Func<SpecRef, ChartReferenceArtwork?> artwork,
        Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(artwork);
        ArgumentNullException.ThrowIfNull(post);
        _read = read;
        Terms = terms;
        _artwork = artwork;
        _post = post;
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
    public void Dispose() => Glyphs.PaletteChanged -= OnPaletteChanged;

    private void Apply(PickDrawing? drawing, bool force)
    {
        if (!force && Same(_drawing, drawing))
            return;
        _drawing = drawing;
        var version = ++_version;
        Parts.Clear();
        Glyph = null;
        GlyphName = null;
        if (drawing is not null)
        {
            foreach (var part in drawing.Parts)
                Parts.Add(new PickDrawnPartViewModel(part, Tile));
            GlyphName = NameOf(drawing);
        }

        OnPropertyChanged(nameof(HasParts));
        OnPropertyChanged(nameof(Drawing));
        OnPropertyChanged(nameof(Tile));
        _loading = drawing is null ? Task.CompletedTask : LoadAsync(drawing, version);
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
            if (namesToo)
            {
                foreach (var part in parts)
                {
                    if (part.Part.Key is not { } key)
                        continue;
                    var info = await Terms.DescribeAsync(key).ConfigureAwait(false);
                    if (info is not null)
                        _post(() => { if (version == _version) part.SetName(info.Name); });
                }

                _post(() => { if (version == _version) GlyphName = NameOf(drawing); });
            }

            if (_artwork(drawing.Spec) is not { } artwork)
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

    /// <summary>Follows a palette change.</summary>
    /// <param name="tile">The new palette's chart background.</param>
    internal void SetTile(IBrush tile) => Tile = tile;

    /// <summary>Names the part from its entry.</summary>
    /// <param name="name">What the catalogue says the item shows.</param>
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
        PickDrawnPartKind.ColourFill => string.Format(CultureInfo.CurrentCulture, Strings.Pick_DrawnAs_ColourFillFormat, part.Colour),
        PickDrawnPartKind.Line => string.Format(
            CultureInfo.CurrentCulture,
            part.Dash switch
            {
                "dash" => Strings.Pick_DrawnAs_DashedLineFormat,
                "dot" => Strings.Pick_DrawnAs_DottedLineFormat,
                _ => Strings.Pick_DrawnAs_LineFormat,
            },
            part.Colour),
        PickDrawnPartKind.Text => string.Format(CultureInfo.CurrentCulture, Strings.Pick_DrawnAs_TextFormat, part.Text),
        _ when string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), part.ItemId, StringComparison.OrdinalIgnoreCase) => part.ItemId ?? string.Empty,
        _ => string.Format(CultureInfo.CurrentCulture, Strings.Pick_DrawnAs_ItemFormat, part.ItemId, name.Trim()),
    };
}
