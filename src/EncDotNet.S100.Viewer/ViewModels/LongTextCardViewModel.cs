using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The body every long-text card in Object Information shares (#932): the
/// text block, "Show all N lines", Copy, Examiner and the "Not for
/// navigation" label. The header on top says what the text is: a warning's
/// number and status (<see cref="NavigationalWarningCardViewModel"/>), or a
/// referenced file's heading or an attribute's name
/// (<see cref="PickTextCardViewModel"/>).
/// </summary>
/// <remarks>
/// Text longer than <see cref="ClipThreshold"/> lines stops at
/// <see cref="ClipLines"/> (or at <see cref="ClipAt"/>) until "Show all" is chosen, which holds for the
/// card's lifetime, that is until the pick changes. Copy copies the whole
/// text, never the clipped one.
/// </remarks>
internal abstract class LongTextCardViewModel : ViewModelBase
{
    /// <summary>Texts with more lines than this are clipped.</summary>
    internal const int ClipThreshold = 22;

    /// <summary>The number of lines a clipped text shows.</summary>
    internal const int ClipLines = 20;

    private bool _showAll;
    private bool _canZoom;
    private IReadOnlyList<WarningTextSegment>? _segments;

    protected LongTextCardViewModel()
    {
        ShowAllCommand = new RelayCommand(() => ShowAll = true);
        CopyCommand = new RelayCommand(
            () => { if (CopyText is { Length: > 0 } t) CopyRequested?.Invoke(this, t); },
            () => !string.IsNullOrEmpty(CopyText));
    }

    /// <summary>The whole text the body shows, before clipping.</summary>
    protected abstract string? BodyText { get; }

    /// <summary>
    /// The text <see cref="CopyCommand"/> copies; the whole
    /// <see cref="BodyText"/> unless the card copies more (a referenced file
    /// with its heading).
    /// </summary>
    public virtual string? CopyText => BodyText;

    /// <summary>
    /// Whether the text is set in a monospaced font. Telex warnings align
    /// columns by character, so a warning is; prose reads better proportional.
    /// </summary>
    public abstract bool IsMonospace { get; }

    /// <summary>
    /// What the body says when there is no text; <see langword="null"/> hides
    /// the text block instead.
    /// </summary>
    public virtual string? NoTextMessage => null;

    /// <summary>Whether the body shows <see cref="NoTextMessage"/>.</summary>
    public bool ShowsNoTextMessage => !HasText && NoTextMessage is not null;

    /// <summary>Whether the text block shows: there is text, or a message saying there is none.</summary>
    public bool ShowsTextBlock => HasText || NoTextMessage is not null;

    /// <summary>The Copy button's tooltip.</summary>
    public abstract string CopyTooltip { get; }

    /// <summary>
    /// The attribute Examiner opens, for a card lifted from an attribute;
    /// <see langword="null"/> opens the feature.
    /// </summary>
    public virtual PickAttribute? ExaminerAttribute => null;

    /// <summary>Whether Examiner opens <see cref="ExaminerAttribute"/> rather than the feature.</summary>
    public bool ExaminesAttribute => ExaminerAttribute is not null;

    /// <summary>Whether Examiner opens the feature.</summary>
    public bool ExaminesFeature => ExaminerAttribute is null;

    /// <summary>
    /// Whether the dataset's exchange-set metadata marks it not for
    /// navigation, which shows the footer label.
    /// </summary>
    public bool NotForNavigation { get; init; }

    /// <summary>
    /// Whether the item has a location the map can frame, which shows
    /// "Zoom to" (<see cref="ZoomToCommand"/>).
    /// </summary>
    public bool CanZoom
    {
        get => _canZoom;
        set
        {
            if (SetProperty(ref _canZoom, value))
                (ZoomToCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Frames the item on the map; <see langword="null"/> when the card can't.</summary>
    public virtual ICommand? ZoomToCommand => null;

    /// <summary>Whether there is text to show.</summary>
    public bool HasText => !string.IsNullOrWhiteSpace(BodyText);

    /// <summary>The number of lines in the text.</summary>
    public int LineCount => Lines(BodyText).Length;

    /// <summary>
    /// The number of lines a clipped text shows; <see cref="ClipLines"/> by
    /// default, which clips past <see cref="ClipThreshold"/>. Any other value
    /// clips as soon as the text is longer (the findings list's inline
    /// Warning card clips at 5, #959 E1).
    /// </summary>
    public int ClipAt { get; init; } = ClipLines;

    private int ClipPast => ClipAt == ClipLines ? ClipThreshold : ClipAt;

    /// <summary>Whether the text is clipped to <see cref="ClipAt"/> lines.</summary>
    public bool IsClipped => !_showAll && LineCount > ClipPast;

    /// <summary>The text as shown: the whole text, or its first <see cref="ClipAt"/> lines.</summary>
    public string DisplayedText
    {
        get
        {
            var lines = Lines(BodyText);
            return IsClipped
                ? string.Join('\n', lines, 0, ClipAt)
                : string.Join('\n', lines);
        }
    }

    /// <summary>The "Show all N lines" button's text.</summary>
    public string ShowAllText => string.Format(CultureInfo.CurrentCulture, Strings.Pick_Warning_ShowAllLines, LineCount);

    /// <summary>Whether the whole text is shown. Holds until the pick changes.</summary>
    public bool ShowAll
    {
        get => _showAll;
        set
        {
            if (SetProperty(ref _showAll, value))
                RaiseTextChanged();
        }
    }

    /// <summary>Shows the whole text.</summary>
    public ICommand ShowAllCommand { get; }

    /// <summary>Copies <see cref="CopyText"/>.</summary>
    public ICommand CopyCommand { get; }

    /// <summary>Raised by <see cref="CopyCommand"/> with the text to put on the clipboard.</summary>
    public event EventHandler<string>? CopyRequested;

    /// <summary>
    /// The displayed text as runs. Plain text is one run; a warning's
    /// numbers that name other listed warnings are links.
    /// </summary>
    public IReadOnlyList<WarningTextSegment> Segments => _segments ??= BuildSegments(DisplayedText);

    /// <summary>The links in the displayed text, in order.</summary>
    public IEnumerable<WarningTextSegment> Links => Segments.Where(s => s.IsLink);

    /// <summary>
    /// Opens the target of a link in the text; <see langword="null"/> when the
    /// card's text has no links.
    /// </summary>
    public virtual ICommand? LinkCommand => null;

    /// <summary>Splits <paramref name="displayed"/> into runs; one plain run by default.</summary>
    protected virtual IReadOnlyList<WarningTextSegment> BuildSegments(string displayed) =>
        string.IsNullOrEmpty(displayed) ? [] : [new WarningTextSegment(displayed, null)];

    /// <summary>Raises the text's properties after <see cref="BodyText"/> or the clipping changes.</summary>
    protected void RaiseTextChanged()
    {
        OnPropertyChanged(nameof(HasText));
        OnPropertyChanged(nameof(ShowsNoTextMessage));
        OnPropertyChanged(nameof(ShowsTextBlock));
        OnPropertyChanged(nameof(LineCount));
        OnPropertyChanged(nameof(IsClipped));
        OnPropertyChanged(nameof(DisplayedText));
        OnPropertyChanged(nameof(ShowAllText));
        OnPropertyChanged(nameof(CopyText));
        (CopyCommand as RelayCommand)?.NotifyCanExecuteChanged();
        RefreshSegments();
    }

    /// <summary>Rebuilds <see cref="Segments"/>, for when what links to what changes.</summary>
    protected void RefreshSegments()
    {
        _segments = null;
        OnPropertyChanged(nameof(Segments));
        OnPropertyChanged(nameof(Links));
    }

    /// <summary>The text's lines: line breaks normalised to \n, trailing blank lines dropped.</summary>
    internal static string[] Lines(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];
        return text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n', ' ').Split('\n');
    }
}
