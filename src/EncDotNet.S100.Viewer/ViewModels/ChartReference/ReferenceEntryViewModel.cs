using System.Globalization;
using Avalonia.Media;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Skia;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.ChartReference;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// One entry of the chart reference as the Reference tab and the study
/// page's entry pane show it (symbol dictionary design note §4.4, R7,
/// #1650): the heading, the glyph large and at chart size, and the parts of
/// its kind (<see cref="FeatureTypeEntryViewModel"/>,
/// <see cref="SymbolEntryViewModel"/>, <see cref="AttributeEntryViewModel"/>,
/// <see cref="DisplaySettingEntryViewModel"/>).
/// </summary>
/// <remarks>
/// Glyphs are drawn off the UI thread once <see cref="Start"/> is called and
/// come back through <see cref="ReferenceEntryContext.Post"/>. The large
/// glyph goes through the host's <see cref="ChartReferenceGlyphCache"/>, so
/// it follows the palette; the others are drawn for the entry alone.
/// </remarks>
internal abstract class ReferenceEntryViewModel : ViewModelBase
{
    /// <summary>The large glyph's size, in logical pixels.</summary>
    public const int HeroGlyphSize = 96;

    /// <summary>The size of each palette's glyph in a symbol entry, in logical pixels.</summary>
    public const int PaletteGlyphSize = 56;

    /// <summary>The largest a glyph at chart size gets, in logical pixels.</summary>
    public const int MaxChartGlyphSize = 120;

    private readonly List<Task> _drawing = [];
    private int _glyphVersion;
    private IImage? _heroGlyph;
    private IImage? _chartGlyph;
    private double _chartGlyphWidth;
    private double _chartGlyphHeight;
    private string? _chartSize;
    private ReferenceTerms? _terms;

    /// <summary>Creates an entry view.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="context">The host.</param>
    /// <param name="lead">The glyph the entry leads with, or <see langword="null"/> for an entry that has none (an attribute, a display setting).</param>
    protected ReferenceEntryViewModel(ReferenceEntry entry, ReferenceEntryContext context, LeadGlyph? lead)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(context);
        Entry = entry;
        Context = context;
        Lead = lead;
        Name = ChartReferenceRowViewModel.NameOf(entry);
        Description = ChartReferenceRowViewModel.DescriptionRepeatsName(entry) || string.IsNullOrWhiteSpace(entry.Definition)
            ? null
            : entry.Definition.Trim();
    }

    /// <summary>The entry.</summary>
    public ReferenceEntry Entry { get; }

    /// <summary>The entry's key.</summary>
    public ReferenceKey Key => Entry.Key;

    /// <summary>The heading: the entry's name, or a symbol's description (as its row shows it).</summary>
    public string Name { get; }

    /// <summary>The line under the heading: the code, and an S-57 acronym where there is one.</summary>
    public abstract string CodeLine { get; }

    /// <summary>What kind of entry it is: "Feature type", "Symbol", "Attribute"…</summary>
    public abstract string KindLabel { get; }

    /// <summary>The definition or description, unless it only repeats the heading.</summary>
    public string? Description { get; }

    /// <summary>Whether there is a description to show.</summary>
    public bool HasDescription => Description is not null;

    /// <summary>The glyph the entry leads with, or <see langword="null"/>.</summary>
    public LeadGlyph? Lead { get; }

    /// <summary>Whether the entry shows a glyph band: it is a feature type or portrayal item.</summary>
    public bool ShowsGlyph => Lead is not null;

    /// <summary>Whether the glyph band has a glyph.</summary>
    public bool HasGlyph => Lead is { HasGlyph: true };

    /// <summary>Whether the glyph band says the entry has no symbol of its own.</summary>
    public bool HasNoGlyph => Lead is { HasGlyph: false };

    /// <summary>The tile under the large glyph: the palette's chart background.</summary>
    public IBrush Tile => Context.Glyphs.Tile;

    /// <summary>The large glyph, or <see langword="null"/> until it is drawn.</summary>
    public IImage? HeroGlyph
    {
        get => _heroGlyph;
        private set => SetProperty(ref _heroGlyph, value);
    }

    /// <summary>The glyph at the size the chart draws it, or <see langword="null"/>: only a point symbol has one.</summary>
    public IImage? ChartGlyph
    {
        get => _chartGlyph;
        private set
        {
            if (SetProperty(ref _chartGlyph, value))
                OnPropertyChanged(nameof(HasChartGlyph));
        }
    }

    /// <summary>Whether there is a glyph at chart size.</summary>
    public bool HasChartGlyph => _chartGlyph is not null;

    /// <summary>The glyph at chart size's width, in logical pixels.</summary>
    public double ChartGlyphWidth
    {
        get => _chartGlyphWidth;
        private set => SetProperty(ref _chartGlyphWidth, value);
    }

    /// <summary>The glyph at chart size's height, in logical pixels.</summary>
    public double ChartGlyphHeight
    {
        get => _chartGlyphHeight;
        private set => SetProperty(ref _chartGlyphHeight, value);
    }

    /// <summary>The symbol's size on the chart, "At chart size, 1.65 × 5.32 mm", or <see langword="null"/>.</summary>
    public string? ChartSize
    {
        get => _chartSize;
        private set => SetProperty(ref _chartSize, value);
    }

    /// <summary>Completes when the glyphs being drawn now are done; tests wait on it.</summary>
    internal Task GlyphsDrawn
    {
        get
        {
            lock (_drawing)
                return Task.WhenAll(_drawing);
        }
    }

    /// <summary>The terms its names and conditions read (R8, #1651); the view hands them to its terms.</summary>
    public ReferenceTerms Terms => _terms ??= Context.Terms ?? ReferenceTerms.Of(Context);

    /// <summary>The host.</summary>
    protected ReferenceEntryContext Context { get; }

    /// <summary>The index the entry belongs to.</summary>
    protected ChartReferenceIndex Index => Context.Index;

    /// <summary>Makes the entry view of <paramref name="entry"/>'s kind.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="context">The host.</param>
    /// <returns>The entry view.</returns>
    public static ReferenceEntryViewModel Create(ReferenceEntry entry, ReferenceEntryContext context) => entry switch
    {
        FeatureTypeEntry f => new FeatureTypeEntryViewModel(f, context),
        PortrayalItemEntry i => new SymbolEntryViewModel(i, context),
        AttributeEntry a => new AttributeEntryViewModel(a, context),
        DisplaySettingEntry d => new DisplaySettingEntryViewModel(d, context),
        _ => throw new ArgumentException($"No entry view for {entry.GetType().Name}.", nameof(entry)),
    };

    /// <summary>Starts drawing the glyphs and reads the map; the host calls it once the entry is shown.</summary>
    public void Start()
    {
        DrawGlyphs();
        RefreshInView();
        OnStart();
    }

    /// <summary>Draws the glyphs again, after the palette changed.</summary>
    public void ReloadGlyphs()
    {
        HeroGlyph = null;
        OnPropertyChanged(nameof(Tile));
        DrawGlyphs();
        OnReloadGlyphs();
    }

    /// <summary>Called by <see cref="ReloadGlyphs"/>: an entry with glyphs of its own draws them again.</summary>
    protected virtual void OnReloadGlyphs()
    {
    }

    /// <summary>Reads what is drawn in view again, after the map moved; only a feature type shows it.</summary>
    public virtual void RefreshInView()
    {
    }

    /// <summary>Called by <see cref="Start"/>: an entry that follows the settings subscribes here.</summary>
    protected virtual void OnStart()
    {
    }

    /// <summary>The entry stops being shown: clears anything it rang on the map, and stops following the settings.</summary>
    public virtual void Close()
    {
    }

    /// <summary>A link to the entry <paramref name="key"/> names, or <see langword="null"/> when the index has none.</summary>
    /// <param name="key">The entry.</param>
    /// <param name="cases">Its cases in words.</param>
    /// <param name="note">A note under them.</param>
    protected ChartReferenceLinkViewModel? LinkTo(ReferenceKey key, IReadOnlyList<TermText>? cases = null, string? note = null) =>
        Index.TryGet(key, out var entry)
            ? new ChartReferenceLinkViewModel(entry.Key, LinkName(entry), cases ?? [], note, Context.Open)
            : null;

    /// <summary>A link's text: a feature type's name, or a symbol's code and title.</summary>
    /// <param name="entry">The linked entry.</param>
    protected static string LinkName(ReferenceEntry entry)
    {
        var title = ChartReferenceRowViewModel.NameOf(entry);
        return entry is PortrayalItemEntry && !string.Equals(title, entry.Code, StringComparison.OrdinalIgnoreCase)
            ? string.Format(CultureInfo.CurrentCulture, Strings.ChartReference_ViewingGroupFormat, entry.Code, title)
            : title;
    }

    /// <summary>Called once a palette's glyph is drawn; a symbol entry shows it.</summary>
    /// <param name="palette">The palette.</param>
    /// <param name="glyph">The glyph, or <see langword="null"/>.</param>
    protected virtual void OnPaletteGlyph(PaletteType palette, IImage? glyph)
    {
    }

    /// <summary>Called once a palette's tile colour is read from the catalogue; a symbol entry shows it.</summary>
    /// <param name="palette">The palette.</param>
    /// <param name="tile">The palette's chart background.</param>
    protected virtual void OnPaletteTile(PaletteType palette, IBrush tile)
    {
    }

    /// <summary>The colour token a palette's tile is filled with: the chart's deep water.</summary>
    internal const string TileToken = "DEPDW";

    /// <summary>Whether the entry shows its glyph in each palette (a symbol does).</summary>
    protected virtual bool DrawsPalettes => false;

    private void DrawGlyphs()
    {
        var artwork = Context.Artwork;
        if (artwork is null || Lead is not { HasGlyph: true } lead)
            return;
        var glyphs = Context.Glyphs;
        var palette = glyphs.Palette;
        var version = Interlocked.Increment(ref _glyphVersion);

        if (glyphs.TryGet(lead.Key, HeroGlyphSize, HeroGlyphSize, out var held))
        {
            HeroGlyph = held;
        }
        else
        {
            Track(
                async () =>
                {
                    var row = await artwork.BuildAsync(lead, palette).ConfigureAwait(false);
                    return glyphs.Palette != palette ? null : glyphs.Get(lead.Key, HeroGlyphSize, HeroGlyphSize, _ => row);
                },
                glyph =>
                {
                    if (version == _glyphVersion)
                        HeroGlyph = glyph;
                });
        }

        if (lead is { Kind: LeadGlyphKind.Symbol, ItemId: { } symbol })
        {
            Track(
                async () =>
                {
                    if (await artwork.SymbolSizeAsync(symbol).ConfigureAwait(false) is not { } mm
                        || await artwork.BuildAsync(lead, palette).ConfigureAwait(false) is not { } row)
                    {
                        return null;
                    }

                    var (w, h) = ChartPixels(mm.Width, mm.Height);
                    SetChartSize(w, h, mm.Width, mm.Height);
                    return (IImage)ChartReferenceGlyphCache.Render(row, w, h, padding: 0);
                },
                glyph =>
                {
                    if (version == _glyphVersion)
                        ChartGlyph = glyph;
                });
        }

        if (!DrawsPalettes)
            return;
        foreach (var each in (PaletteType[])[PaletteType.Day, PaletteType.Dusk, PaletteType.Night])
        {
            var p = each;
            Track(
                async () => await artwork.BuildAsync(lead, p).ConfigureAwait(false) is { } row
                    ? ChartReferenceGlyphCache.Render(row, PaletteGlyphSize, PaletteGlyphSize, ChartReferenceGlyphCache.PaddingOf(PaletteGlyphSize, PaletteGlyphSize))
                    : null,
                glyph => OnPaletteGlyph(p, glyph));
            // Each palette's tile is that palette's deep water, from the catalogue's colour profile.
            Track(
                () => artwork.ColorAsync(TileToken, p),
                hex =>
                {
                    if (hex is not null && Avalonia.Media.Color.TryParse(hex, out var color))
                        OnPaletteTile(p, new Avalonia.Media.Immutable.ImmutableSolidColorBrush(color));
                });
        }
    }

    /// <summary>A symbol's size on the chart in logical pixels, at the chart's 0.32 mm per pixel, capped at <see cref="MaxChartGlyphSize"/>.</summary>
    /// <param name="width">The width in millimetres.</param>
    /// <param name="height">The height in millimetres.</param>
    internal static (int Width, int Height) ChartPixels(double width, double height)
    {
        var scale = Math.Min(1, MaxChartGlyphSize * LegendGlyphRenderer.ChartMillimetresPerPixel / Math.Max(width, height));
        int Px(double mm) => Math.Max(1, (int)Math.Ceiling(mm * scale / LegendGlyphRenderer.ChartMillimetresPerPixel));
        return (Px(width), Px(height));
    }

    private void SetChartSize(int w, int h, double widthMm, double heightMm) => Context.Post(() =>
    {
        ChartGlyphWidth = w;
        ChartGlyphHeight = h;
        ChartSize = string.Format(
            CultureInfo.CurrentCulture,
            Strings.Reference_ChartSize,
            widthMm.ToString("0.##", CultureInfo.CurrentCulture),
            heightMm.ToString("0.##", CultureInfo.CurrentCulture));
    });

    private void Track<T>(Func<Task<T?>> draw, Action<T?> apply)
        where T : class
    {
        var task = Task.Run(async () =>
        {
            try
            {
                return await draw().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A glyph that can't be drawn leaves its tile empty.
                return null;
            }
        });
        var applied = task.ContinueWith(
            t => Context.Post(() => apply(t.Result)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
        lock (_drawing)
            _drawing.Add(applied);
    }
}
