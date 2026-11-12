using System.Collections.ObjectModel;
using System.Globalization;
using EncDotNet.S100.Features;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Warning cards of Object Information (#932, #1401): one card per
/// navigational warning part a pick hit when it hit two or more, else the
/// lone card of a selected warning. Cards link to, zoom through, classify and
/// open in the Navigational warnings panel; their status follows the view time
/// and their times the time format.
/// </summary>
internal sealed class PickWarningCardsViewModel : ViewModelBase
{
    private readonly INavigationalWarningNavigator _navigator;
    private readonly IViewTime _viewTime;
    private readonly ITimeFormatProvider _timeFormat;
    private NavigationalWarningCardViewModel? _card;
    private PickHit? _previewHit;

    /// <summary>Creates the Warning cards.</summary>
    /// <param name="navigator">The Navigational warnings panel, which cards link to, zoom through and classify in.</param>
    /// <param name="viewTime">The view time a card's status is read at.</param>
    /// <param name="timeFormat">The time format of a card's times.</param>
    public PickWarningCardsViewModel(
        INavigationalWarningNavigator navigator,
        IViewTime viewTime,
        ITimeFormatProvider timeFormat)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(viewTime);
        ArgumentNullException.ThrowIfNull(timeFormat);
        _navigator = navigator;
        _viewTime = viewTime;
        _timeFormat = timeFormat;
        _navigator.WarningsChanged += (_, _) => RefreshTargets();
        _viewTime.Changed += (_, _) =>
        {
            foreach (var card in AllCards())
                card.SetViewTime(_viewTime.Current);
        };
        _timeFormat.TimeFormatChanged += format =>
        {
            foreach (var card in AllCards())
                card.SetTimeFormat(format);
        };
    }

    /// <summary>
    /// One Warning card per navigational warning part the pick hit, when it
    /// hit two or more (#932): points before lines before areas, then by
    /// number. One card is open at a time, the selected hit's. Empty
    /// otherwise; a lone warning shows as <see cref="Card"/>.
    /// </summary>
    public ObservableCollection<NavigationalWarningCardViewModel> Cards { get; } = new();

    /// <summary>Whether <see cref="Cards"/> replaces the hit list.</summary>
    public bool HasList => Cards.Count > 0;

    /// <summary>
    /// The Warning card of the selected hit when it is part of a navigational
    /// warning: the open one of <see cref="Cards"/>, or the lone card;
    /// <see langword="null"/> otherwise.
    /// </summary>
    public NavigationalWarningCardViewModel? Card
    {
        get => _card;
        private set
        {
            if (ReferenceEquals(_card, value))
                return;
            if (_card is not null && !Cards.Contains(_card))
                Detach(_card);
            _card = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCard));
            OnPropertyChanged(nameof(HasSingleCard));
        }
    }

    /// <summary>True when <see cref="Card"/> is shown.</summary>
    public bool HasCard => _card is not null;

    /// <summary>True when <see cref="Card"/> shows alone, not as one of <see cref="Cards"/>.</summary>
    public bool HasSingleCard => _card is not null && !HasList;

    /// <summary>
    /// The hits beneath the Warning cards that aren't warnings, listed as
    /// rows below them. Empty unless <see cref="HasList"/>.
    /// </summary>
    public ObservableCollection<PickHit> OtherHits { get; } = new();

    /// <summary>Whether <see cref="OtherHits"/> has rows.</summary>
    public bool HasOtherHits => OtherHits.Count > 0;

    /// <summary>
    /// The hit whose card the pointer is over, previewed on the map with the
    /// pick highlight; <see langword="null"/> when none is.
    /// </summary>
    public PickHit? PreviewHit
    {
        get => _previewHit;
        private set => SetProperty(ref _previewHit, value);
    }

    /// <summary>Previews <paramref name="card"/>'s part on the map, or ends the preview with <see langword="null"/>.</summary>
    /// <param name="card">The card the pointer is over, or <see langword="null"/>.</param>
    public void SetPreview(NavigationalWarningCardViewModel? card) => PreviewHit = card?.Hit;

    /// <summary>Raised when a card asks for its hit to be selected: it was opened, or zoomed to.</summary>
    public event EventHandler<PickHit>? SelectRequested;

    /// <summary>Raised when a card's text is copied; the payload is the clipboard text.</summary>
    public event EventHandler<string>? CopyRequested;

    /// <summary>
    /// Shows the cards of a new pick: a card per warning part when it hit two
    /// or more, with the other hits as rows below. The lone card of a single
    /// warning is made when its hit is selected (<see cref="Select"/>).
    /// </summary>
    /// <param name="hits">Every hit of the pick.</param>
    public void Show(IReadOnlyList<PickHit> hits)
    {
        ArgumentNullException.ThrowIfNull(hits);
        Clear(raise: false);
        var warningHits = hits.Where(h => h.NavigationalWarning is not null).ToList();
        if (warningHits.Count > 1)
        {
            foreach (var hit in OrderWarningHits(warningHits, GeometryOf))
                Cards.Add(Create(hit, collapsible: true));
            foreach (var hit in hits)
            {
                if (hit.NavigationalWarning is null)
                    OtherHits.Add(hit);
            }
        }
        RaiseListChanged();
    }

    /// <summary>
    /// Opens the selected hit's card: one card open at a time in the list (none
    /// when a row below the cards is selected), or the lone card of a warning.
    /// </summary>
    /// <param name="hit">The selected hit, or <see langword="null"/>.</param>
    public void Select(PickHit? hit)
    {
        if (hit is null)
        {
            Card = null;
            return;
        }

        if (Cards.Count > 0)
        {
            var open = Cards.FirstOrDefault(c => ReferenceEquals(c.Hit, hit));
            foreach (var card in Cards)
                card.IsOpen = ReferenceEquals(card, open);
            Card = open;
        }
        else
        {
            Card = hit.NavigationalWarning is not null ? Create(hit, collapsible: false) : null;
        }
    }

    /// <summary>Removes every card.</summary>
    public void Clear() => Clear(raise: true);

    private void Clear(bool raise)
    {
        PreviewHit = null;
        if (_card is not null)
        {
            Detach(_card);
            _card = null;
            OnPropertyChanged(nameof(Card));
            OnPropertyChanged(nameof(HasCard));
            OnPropertyChanged(nameof(HasSingleCard));
        }
        foreach (var card in Cards)
            Detach(card);
        Cards.Clear();
        OtherHits.Clear();
        if (raise)
            RaiseListChanged();
    }

    private void RaiseListChanged()
    {
        OnPropertyChanged(nameof(HasList));
        OnPropertyChanged(nameof(HasOtherHits));
        OnPropertyChanged(nameof(HasSingleCard));
    }

    private IEnumerable<NavigationalWarningCardViewModel> AllCards()
    {
        foreach (var card in Cards)
            yield return card;
        if (_card is not null && !Cards.Contains(_card))
            yield return _card;
    }

    private void RefreshTargets()
    {
        foreach (var card in AllCards())
            UpdateTargets(card);
    }

    private void UpdateTargets(NavigationalWarningCardViewModel card)
    {
        card.SetLinkTargets(_navigator.Warnings);
        card.SetWarningSet(_navigator.WarningSet);
        var entry = FindEntry(card.Hit);
        card.CanZoom = entry is { Extent: not null };
        card.CanClassify = entry is not null;
        card.Classification = entry is null ? NavigationalWarningClassification.OnChart : _navigator.ClassificationOf(entry);
        card.SetRouteFinding(entry is null ? null : _navigator.RouteFindingFor(entry));
    }

    private NavigationalWarningEntry? FindEntry(PickHit? hit) =>
        hit is { DatasetFileName: { } datasetId } ? _navigator.Find(datasetId, hit.FeatureRef) : null;

    private NavigationalWarningCardViewModel Create(PickHit hit, bool collapsible)
    {
        var card = new NavigationalWarningCardViewModel(
            hit.NavigationalWarning!,
            hit.DatasetFileName,
            _timeFormat.Current,
            _viewTime.Current)
        {
            Hit = hit,
            IsCollapsible = collapsible,
            IsOpen = !collapsible,
            NotForNavigation = hit.NotForNavigation,
            // As the card is made: opening it marks the warning seen, and the card keeps saying it was new.
            IsNew = FindEntry(hit) is { } entry && _navigator.IsNew(entry),
        };
        UpdateTargets(card);
        card.CopyRequested += OnCopyRequested;
        card.OpenRequested += OnOpenRequested;
        card.ZoomRequested += OnZoomRequested;
        card.LinkRequested += OnLinkRequested;
        card.ClassificationRequested += OnClassificationRequested;
        card.RouteFindingRequested += OnRouteFindingRequested;
        return card;
    }

    private void Detach(NavigationalWarningCardViewModel card)
    {
        card.CopyRequested -= OnCopyRequested;
        card.OpenRequested -= OnOpenRequested;
        card.ZoomRequested -= OnZoomRequested;
        card.LinkRequested -= OnLinkRequested;
        card.ClassificationRequested -= OnClassificationRequested;
        card.RouteFindingRequested -= OnRouteFindingRequested;
    }

    private void OnCopyRequested(object? sender, string text) => CopyRequested?.Invoke(this, text);

    // The navigator raises WarningsChanged, which updates every card (#996).
    private void OnClassificationRequested(object? sender, NavigationalWarningClassification classification)
    {
        if (sender is NavigationalWarningCardViewModel { Hit: { } hit } && FindEntry(hit) is { } entry)
            _navigator.SetClassification(entry, classification);
    }

    private void OnOpenRequested(object? sender, EventArgs e)
    {
        if (sender is NavigationalWarningCardViewModel { Hit: { } hit })
            SelectRequested?.Invoke(this, hit);
    }

    private void OnZoomRequested(object? sender, EventArgs e)
    {
        if (sender is not NavigationalWarningCardViewModel { Hit: { } hit } || FindEntry(hit) is not { } entry)
            return;
        SelectRequested?.Invoke(this, hit);
        _navigator.ZoomTo(entry);
    }

    private void OnLinkRequested(object? sender, NavigationalWarningEntry target) =>
        _navigator.Select(target);

    // #959: "Crosses route · leg 3 → 4" opens the finding in the Findings list.
    private void OnRouteFindingRequested(object? sender, EncDotNet.S100.Navigation.RouteFinding finding) =>
        _navigator.OpenRouteFinding(finding);

    /// <summary>
    /// Orders the warning parts of one pick for their cards: points, then
    /// lines, then areas, then parts with no location; within each, by year
    /// and number, then label.
    /// </summary>
    /// <param name="hits">The warning parts.</param>
    /// <param name="geometryOf">A part's geometry.</param>
    /// <returns>The parts in card order.</returns>
    internal static IReadOnlyList<PickHit> OrderWarningHits(
        IEnumerable<PickHit> hits, Func<PickHit, S100GeometryType> geometryOf)
    {
        ArgumentNullException.ThrowIfNull(hits);
        ArgumentNullException.ThrowIfNull(geometryOf);
        return hits
            .OrderBy(h => GeometryRank(geometryOf(h)))
            .ThenBy(h => NumberKey(h.NavigationalWarning?.Number))
            .ThenBy(h => h.NavigationalWarning?.Label ?? h.FeatureRef, StringComparer.Ordinal)
            .ToList();
    }

    private static int GeometryRank(S100GeometryType geometry) => geometry switch
    {
        S100GeometryType.Point => 0,
        S100GeometryType.Curve => 1,
        S100GeometryType.Surface => 2,
        _ => 3,
    };

    // "106/26" sorts as (26, 106); a number without a year sorts by number
    // alone, and no number last.
    private static (int Year, int Number) NumberKey(string? number)
    {
        if (number is null)
            return (int.MaxValue, int.MaxValue);
        var slash = number.IndexOf('/', StringComparison.Ordinal);
        var digits = slash < 0 ? number : number[..slash];
        var year = slash < 0 ? -1 : int.TryParse(number.AsSpan(slash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var y) ? y : -1;
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? (year, n)
            : (int.MaxValue, int.MaxValue);
    }

    private S100GeometryType GeometryOf(PickHit hit) =>
        FindEntry(hit)?.Geometry ?? S100GeometryType.None;
}
