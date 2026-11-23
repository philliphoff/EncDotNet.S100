using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Services.ChartReference;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// A term's peek (symbol dictionary design note §4.2, R8, #1651): the kind
/// and name, the glyph on the palette's chart tile, the definition or the
/// first sentence of a symbol's meaning, <b>Open in reference</b>, and the
/// S-100 eXaminer link when examiner links are on.
/// </summary>
internal sealed class ReferencePeekViewModel : ViewModelBase
{
    private readonly ReferenceTerms _terms;
    private IImage? _glyph;

    /// <summary>Creates a peek and starts drawing its glyph.</summary>
    /// <param name="info">What the term says.</param>
    /// <param name="terms">The terms it belongs to.</param>
    /// <param name="post">Runs an action on the UI thread.</param>
    /// <param name="opened">Called after Open in reference or the eXaminer link: the term closes its peek.</param>
    public ReferencePeekViewModel(ReferenceTermInfo info, ReferenceTerms terms, Action<Action> post, Action? opened = null)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(post);
        Info = info;
        _terms = terms;
        ExaminerUrl = terms.ExaminerUrl(info);
        Tile = terms.Glyphs.Tile;
        OpenCommand = new RelayCommand(() =>
        {
            opened?.Invoke();
            terms.Open(info.Key);
        });
        OpenExaminerCommand = new RelayCommand(
            () =>
            {
                opened?.Invoke();
                if (ExaminerUrl is { } url)
                    terms.Urls?.Open(url);
            },
            () => ExaminerUrl is not null);
        GlyphDrawn = ShowsGlyph ? DrawAsync(post) : Task.CompletedTask;
    }

    /// <summary>What the term says.</summary>
    public ReferenceTermInfo Info { get; }

    /// <summary>"Feature type", "Symbol", "Listed value of Colour"…</summary>
    public string KindLabel => Info.KindLabel;

    /// <summary>The entry's name.</summary>
    public string Name => Info.Name;

    /// <summary>The definition, or the first sentence of a symbol's meaning; <see langword="null"/> for none.</summary>
    public string? Definition => Info.Definition;

    /// <summary>Whether there is a definition.</summary>
    public bool HasDefinition => Definition is not null;

    /// <summary>Whether the peek shows a glyph: the entry is a feature type or portrayal item with one.</summary>
    public bool ShowsGlyph => Info.Lead is { HasGlyph: true };

    /// <summary>The palette's chart background, under the glyph.</summary>
    public IBrush Tile { get; }

    /// <summary>The glyph, or <see langword="null"/> until it is drawn.</summary>
    public IImage? Glyph
    {
        get => _glyph;
        private set => SetProperty(ref _glyph, value);
    }

    /// <summary>The entry's S-100 eXaminer page, or <see langword="null"/>: examiner links are off, or it has none.</summary>
    public string? ExaminerUrl { get; }

    /// <summary>Whether the peek offers the eXaminer link.</summary>
    public bool HasExaminer => ExaminerUrl is not null;

    /// <summary>Opens the entry, as a click on the term does.</summary>
    public ICommand OpenCommand { get; }

    /// <summary>Opens the entry's eXaminer page in the browser.</summary>
    public ICommand OpenExaminerCommand { get; }

    /// <summary>Completes when the glyph is drawn; tests wait on it.</summary>
    internal Task GlyphDrawn { get; }

    private async Task DrawAsync(Action<Action> post)
    {
        IImage? glyph;
        try
        {
            glyph = await Task.Run(() => _terms.GlyphAsync(Info)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A glyph that can't be drawn leaves the tile empty.
            return;
        }

        post(() => Glyph = glyph);
    }
}
