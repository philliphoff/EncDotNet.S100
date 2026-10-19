using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Warning card at the top of a picked navigational warning part in
/// Object Information (#932): number, type and status, then validity, area
/// and issue time, then the full message text in a monospaced block with a
/// tab per language.
/// </summary>
/// <remarks>
/// The status is read at the view time, so it follows the Timeline (now, when
/// the Timeline is Live). Messages longer than <see cref="ClipThreshold"/>
/// lines stop at <see cref="ClipLines"/> until "Show all" is chosen, which
/// holds for the card's lifetime, that is until the pick changes. The
/// language choice holds for the session.
/// <para>
/// Warning numbers in the text that name another listed warning link to it
/// (see <see cref="NavigationalWarningLinks"/>). When a pick hits several
/// warnings, each gets a card and only one is open at a time
/// (<see cref="IsCollapsible"/>, <see cref="IsOpen"/>).
/// </para>
/// </remarks>
internal sealed partial class NavigationalWarningCardViewModel : LongTextCardViewModel
{
    // The language the mariner last chose, held for the session.
    private static string? _sessionLanguage;

    private readonly NavigationalWarningInfo _info;
    private readonly IReadOnlyList<NavigationalWarningText> _texts;
    private readonly RelayCommand _zoomToCommand;
    private TimeFormat _timeFormat;
    private DateTimeOffset _viewTime;
    private NavigationalWarningText? _selectedText;
    private bool _isOpen = true;
    private IReadOnlyList<NavigationalWarningEntry> _linkTargets = [];
    private NavigationalWarningSet _set = NavigationalWarningSet.Empty;

    public NavigationalWarningCardViewModel(
        NavigationalWarningInfo info,
        string? datasetFileName,
        TimeFormat timeFormat,
        DateTimeOffset viewTime)
    {
        ArgumentNullException.ThrowIfNull(info);
        _info = info;
        DatasetId = datasetFileName;
        _timeFormat = timeFormat;
        _viewTime = viewTime;
        Label = info.Label ?? datasetFileName ?? Strings.Pick_Warning_NoNumber;
        _texts = OrderForUi(info.Texts, CultureInfo.CurrentUICulture);
        _selectedText = PickInitial(_texts);
        foreach (var text in _texts)
            Languages.Add(new LanguageTab(this, text));
        ToggleCommand = new RelayCommand(() => OpenRequested?.Invoke(this, EventArgs.Empty));
        _zoomToCommand = new RelayCommand(() => ZoomRequested?.Invoke(this, EventArgs.Empty), () => CanZoom);
        OpenLinkCommand = new RelayCommand<NavigationalWarningEntry>(
            target => { if (target is not null) LinkRequested?.Invoke(this, target); },
            target => target is not null);
    }

    /// <summary>The hit the card describes, when it came from a pick.</summary>
    public PickHit? Hit { get; init; }

    /// <summary>Whether the warning was new since its source's latest refresh when the card was made (the header's NEW).</summary>
    public bool IsNew { get; init; }

    /// <summary>The dataset that holds the part (its display name, which is its catalog id).</summary>
    public string? DatasetId { get; }

    /// <summary>
    /// Whether the card is one of several from one pick, so its header opens
    /// it and its body folds while another is open.
    /// </summary>
    public bool IsCollapsible { get; init; }

    /// <summary>Whether the card's body shows. Always true for a lone card.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (SetProperty(ref _isOpen, value))
                OnPropertyChanged(nameof(IsCollapsed));
        }
    }

    /// <summary>Whether the card shows its header only.</summary>
    public bool IsCollapsed => !_isOpen;

    /// <summary>Opens a collapsed card (its header), raising <see cref="OpenRequested"/>.</summary>
    public ICommand ToggleCommand { get; }

    /// <summary>Raised by <see cref="ToggleCommand"/>.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Centres the map on the part at a scale that fits it, raising <see cref="ZoomRequested"/>.</summary>
    public override ICommand ZoomToCommand => _zoomToCommand;

    /// <summary>Raised by <see cref="ZoomToCommand"/>.</summary>
    public event EventHandler? ZoomRequested;

    /// <summary>Opens the warning a link in the text names; the parameter is its <see cref="WarningTextSegment.Target"/>.</summary>
    public ICommand OpenLinkCommand { get; }

    /// <summary>Raised by <see cref="OpenLinkCommand"/> with the warning to open.</summary>
    public event EventHandler<NavigationalWarningEntry>? LinkRequested;

    /// <summary>Sets the listed warnings that numbers in the text may link to.</summary>
    public void SetLinkTargets(IReadOnlyList<NavigationalWarningEntry> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        _linkTargets = warnings;
        RefreshSegments();
    }

    /// <summary>
    /// Sets every loaded warning, which may cancel this one or leave it out
    /// of an in-force bulletin, so the status agrees with the panel's (#992).
    /// </summary>
    public void SetWarningSet(NavigationalWarningSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (ReferenceEquals(_set, set))
            return;
        var before = State;
        _set = set;
        RaiseStatusChanged(before);
    }

    /// <summary>The underlying warning message.</summary>
    public NavigationalWarningInfo Info => _info;

    /// <summary>
    /// The series and number (e.g. "NAVAREA X 106/26"); the dataset's file
    /// name when the preamble gives no number.
    /// </summary>
    public string Label { get; }

    /// <summary>The detailed warning type, else the general type, else the title.</summary>
    public string? TypeText => _info.WarningType ?? _info.GeneralType ?? _info.Title;

    /// <summary>The general type shown after <see cref="TypeText"/>, when both are given and differ.</summary>
    public string? GeneralTypeText =>
        _info.WarningType is not null && _info.GeneralType is { } general
            && !string.Equals(general, _info.WarningType, StringComparison.OrdinalIgnoreCase)
                ? general
                : null;

    /// <summary>" · " and <see cref="GeneralTypeText"/>, or empty, to follow the type on one line.</summary>
    public string GeneralTypeSuffix => GeneralTypeText is { } general ? " · " + general : string.Empty;

    /// <summary>Whether <see cref="GeneralTypeText"/> has a value.</summary>
    public bool HasGeneralType => GeneralTypeText is not null;

    /// <summary>Whether <see cref="TypeText"/> has a value.</summary>
    public bool HasTypeText => TypeText is not null;

    /// <summary>The warning's status at the view time, with why, judged with every loaded warning.</summary>
    public NavigationalWarningState State => _set.StatusAt(_info, _viewTime);

    /// <summary>The warning's status at the view time.</summary>
    public NavigationalWarningStatus Status => State.Status;

    /// <summary>
    /// Why another warning ended this one, under the status pill: "Cancelled — not
    /// in in-force bulletin NAVAREA X 110/26", "Cancelled by …";
    /// <see langword="null"/> otherwise.
    /// </summary>
    public string? StatusReasonText => NavigationalWarningStatusText.Reason(State);

    /// <summary>Whether <see cref="StatusReasonText"/> has a value.</summary>
    public bool HasStatusReason => StatusReasonText is not null;

    /// <summary>Whether the warning is in force at the view time.</summary>
    public bool IsInForce => Status == NavigationalWarningStatus.InForce;

    /// <summary>Whether the warning is not yet in force at the view time.</summary>
    public bool IsNotYetInForce => Status == NavigationalWarningStatus.NotYetInForce;

    /// <summary>Whether the warning has ended at the view time.</summary>
    public bool IsEnded => Status == NavigationalWarningStatus.Ended;

    /// <summary>The status pill's text.</summary>
    public string StatusText => Status switch
    {
        NavigationalWarningStatus.NotYetInForce => Strings.Pick_Warning_StatusNotYet,
        NavigationalWarningStatus.Ended => Strings.Pick_Warning_StatusEnded,
        _ => Strings.Pick_Warning_StatusInForce,
    };

    /// <summary>
    /// When the warning applies, in the Local/UTC preference: one range per
    /// validity range, "until cancelled" for an open end.
    /// </summary>
    public string ValidityText
    {
        get
        {
            if (_info.Validity.Count == 0)
            {
                return _info.CancellationDate is { } cancel
                    ? string.Format(CultureInfo.CurrentCulture, Strings.Pick_Warning_ValidUntil, Format(cancel))
                    : Strings.Pick_Warning_ValidUntilCancelled;
            }

            return string.Join("; ", _info.Validity.Select(FormatRange));
        }
    }

    /// <summary>
    /// The series, general area and locality; "no location" when the part
    /// has no geometry, so the mariner knows why nothing is drawn.
    /// </summary>
    public string? AreaText
    {
        get
        {
            var segments = new List<string>(4);
            if (_info.Series is { } series)
                segments.Add(series);
            if (_info.GeneralArea is { } area)
                segments.Add(area);
            if (_info.Locality is { } locality && !string.Equals(locality, _info.GeneralArea, StringComparison.OrdinalIgnoreCase))
                segments.Add(locality);
            if (!_info.HasLocation)
                segments.Add(Strings.Pick_Warning_NoLocation);
            return segments.Count > 0 ? string.Join(" · ", segments) : null;
        }
    }

    /// <summary>Whether <see cref="AreaText"/> has a value.</summary>
    public bool HasAreaText => AreaText is not null;

    /// <summary>
    /// The issue date-time group exactly as the message writes it
    /// (e.g. "262222Z SEP 26"); the publication time otherwise.
    /// </summary>
    public string? IssuedText =>
        _texts.Select(t => DtgPattern().Match(t.Text)).FirstOrDefault(m => m.Success)?.Value
        ?? (_info.PublicationTime is { } published ? Format(published) : null);

    /// <summary>The issuing authority, shown after <see cref="IssuedText"/>.</summary>
    public string? IssuerText => _info.Issuer;

    /// <summary>
    /// The issuer after the issue time (" · JRCC Australia"), the issuer alone
    /// when there is no time, or empty.
    /// </summary>
    public string IssuerSuffix => IssuerText is not { } issuer
        ? string.Empty
        : IssuedText is null ? issuer : " · " + issuer;

    /// <summary>Whether there is an issue time or issuer to show.</summary>
    public bool HasIssued => IssuedText is not null || IssuerText is not null;

    /// <summary>Whether <see cref="IssuerText"/> has a value.</summary>
    public bool HasIssuer => IssuerText is not null;

    /// <summary>Whether <see cref="IssuedText"/> has a value.</summary>
    public bool HasIssuedTime => IssuedText is not null;

    /// <summary>One tab per language, the UI language first. Shown only with more than one.</summary>
    public ObservableCollection<LanguageTab> Languages { get; } = new();

    /// <summary>Whether the part gives more than one language.</summary>
    public bool HasLanguageTabs => Languages.Count > 1;

    /// <summary>The selected language's code, for the text block's language.</summary>
    public string? SelectedLanguage => _selectedText?.Language;

    /// <summary>The selected language's full message.</summary>
    protected override string? BodyText => _selectedText?.Text;

    /// <summary>Telex messages line up coordinates and indentation by character, so the text is monospaced.</summary>
    public override bool IsMonospace => true;

    /// <inheritdoc />
    public override string? NoTextMessage => Strings.Pick_Warning_NoText;

    /// <inheritdoc />
    public override string CopyTooltip => Strings.Tooltip_CopyWarningText;

    /// <inheritdoc />
    public override ICommand LinkCommand => OpenLinkCommand;

    /// <summary>Selects the message in <paramref name="language"/>, and holds the choice for the session.</summary>
    public void SelectLanguage(string? language)
    {
        var text = _texts.FirstOrDefault(t => string.Equals(t.Language, language, StringComparison.OrdinalIgnoreCase));
        if (text is null || ReferenceEquals(text, _selectedText))
            return;
        _selectedText = text;
        _sessionLanguage = text.Language;
        foreach (var tab in Languages)
            tab.RaiseSelectionChanged();
        OnPropertyChanged(nameof(SelectedLanguage));
        RaiseTextChanged();
    }

    /// <summary>Re-reads the status at a new view time.</summary>
    public void SetViewTime(DateTimeOffset viewTime)
    {
        if (_viewTime == viewTime)
            return;
        var before = State;
        _viewTime = viewTime;
        RaiseStatusChanged(before);
    }

    private void RaiseStatusChanged(NavigationalWarningState before)
    {
        if (State == before)
            return;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsInForce));
        OnPropertyChanged(nameof(IsNotYetInForce));
        OnPropertyChanged(nameof(IsEnded));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusReasonText));
        OnPropertyChanged(nameof(HasStatusReason));
    }

    /// <summary>Re-formats the times in a new Local/UTC preference.</summary>
    public void SetTimeFormat(TimeFormat timeFormat)
    {
        if (_timeFormat == timeFormat)
            return;
        _timeFormat = timeFormat;
        OnPropertyChanged(nameof(ValidityText));
        OnPropertyChanged(nameof(IssuedText));
    }

    /// <summary>Resets the session's language choice. For tests.</summary>
    internal static void ResetSessionLanguage() => _sessionLanguage = null;

    /// <summary>
    /// Orders the texts for the UI: the UI language first, then the order in
    /// the data.
    /// </summary>
    internal static IReadOnlyList<NavigationalWarningText> OrderForUi(
        IReadOnlyList<NavigationalWarningText> texts, CultureInfo uiCulture)
    {
        var ui = texts.FirstOrDefault(t => LanguageNames.Matches(t.Language, uiCulture));
        return ui is null ? texts : [ui, .. texts.Where(t => !ReferenceEquals(t, ui))];
    }

    private static NavigationalWarningText? PickInitial(IReadOnlyList<NavigationalWarningText> texts) =>
        (_sessionLanguage is { } held
            ? texts.FirstOrDefault(t => string.Equals(t.Language, held, StringComparison.OrdinalIgnoreCase))
            : null)
        ?? texts.FirstOrDefault();

    /// <inheritdoc />
    protected override IReadOnlyList<WarningTextSegment> BuildSegments(string displayed) =>
        NavigationalWarningLinks.Split(displayed, _info, DatasetId, _linkTargets);

    private string FormatRange(NavigationalWarningValidity range) => (range.Start, range.End) switch
    {
        ({ } start, { } end) => $"{Format(start)} – {Format(end)}",
        ({ } start, null) => string.Format(CultureInfo.CurrentCulture, Strings.Pick_Warning_ValidFrom, Format(start)),
        (null, { } end) => string.Format(CultureInfo.CurrentCulture, Strings.Pick_Warning_ValidUntil, Format(end)),
        _ => Strings.Pick_Warning_ValidUntilCancelled,
    };

    private string Format(DateTimeOffset value) => TimeFormatting.Format(value, _timeFormat);

    // A telex date-time group: DDHHMMZ MON YY, e.g. "262222Z SEP 26".
    [GeneratedRegex(@"\b\d{6}Z\s+(?:JAN|FEB|MAR|APR|MAY|JUN|JUL|AUG|SEP|OCT|NOV|DEC)\s+\d{2}(?:\d{2})?\b", RegexOptions.CultureInvariant)]
    private static partial Regex DtgPattern();

    /// <summary>One language tab on the card.</summary>
    internal sealed class LanguageTab : ViewModelBase
    {
        private readonly NavigationalWarningCardViewModel _owner;
        private readonly NavigationalWarningText _text;

        public LanguageTab(NavigationalWarningCardViewModel owner, NavigationalWarningText text)
        {
            _owner = owner;
            _text = text;
            SelectCommand = new RelayCommand(() => _owner.SelectLanguage(_text.Language));
        }

        /// <summary>The language's code as the data writes it.</summary>
        public string? Code => _text.Language;

        /// <summary>The language's own name (e.g. "Français"); the code when unknown.</summary>
        public string Name => LanguageNames.Display(_text.Language);

        /// <summary>Whether this tab's language is shown.</summary>
        public bool IsSelected => ReferenceEquals(_owner._selectedText, _text);

        /// <summary>Shows this tab's language.</summary>
        public ICommand SelectCommand { get; }

        internal void RaiseSelectionChanged() => OnPropertyChanged(nameof(IsSelected));
    }
}
