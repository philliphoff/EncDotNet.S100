using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels.Activities;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>A choice in the Logs panel's level filter: show entries at <see cref="Level"/> and above.</summary>
/// <param name="Level">The lowest level shown.</param>
/// <param name="Label">The localized label.</param>
internal sealed record LogLevelFilterOption(LogLevel Level, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

/// <summary>
/// View-model for the Logs tab in the bottom dock (#1080): the recent entries
/// of the <see cref="LogBuffer"/>, filtered by level and text, with Copy,
/// Clear, a Verbose toggle and Send feedback.
/// </summary>
/// <remarks>
/// The buffer raises <see cref="LogBuffer.Changed"/> at most once per
/// notification interval, so a burst of log messages during a heavy load
/// becomes one refresh here: new entries are appended, entries the buffer
/// dropped are removed from the front and a collapsed repeat updates its row's
/// count, all without rebuilding the list. Only a very large batch resets it.
/// <para>
/// The list follows the tail (#1173): while <see cref="IsFollowing"/>, the view
/// keeps the newest row in view. Scrolling up, selecting a row or focusing the
/// search stops following; rows keep arriving, the view keeps the rows the user
/// is reading in place and <see cref="NewEntriesCount"/> counts the rows below.
/// The new-entries pill, <c>End</c> or scrolling back to the bottom resumes.
/// </para>
/// <para>
/// Times follow the user's Local/UTC preference (#1174, D14): the machine's
/// zone or UTC, never a zone of the data. When the shown rows span more than
/// one day, the first row of each day carries a date divider.
/// </para>
/// </remarks>
internal sealed class LogsViewModel : ViewModelBase, IActivityTabBadge, IActivityTabShown, IDisposable
{
    /// <summary>Above this many added or removed rows in one refresh, the list is rebuilt rather than edited row by row.</summary>
    internal const int IncrementalLimit = 200;

    private readonly LogBuffer _buffer;
    private readonly Action? _showFeedback;
    private readonly Action<Action> _post;
    private readonly ITimeFormatProvider? _timeFormat;
    private readonly TimeZoneInfo _localZone;
    private readonly List<LogRowViewModel> _all = [];
    private LogSearchQuery _query = LogSearchQuery.Empty;
    private long _lastSequence;
    private int _refreshPosted;
    private long _problemsSeen;
    private long _followStoppedAt;
    private bool _isTabShown;
    private bool _disposed;

    /// <summary>Creates the view-model.</summary>
    /// <param name="buffer">The log buffer to show.</param>
    /// <param name="showFeedback">Opens the feedback dialog (Send feedback); the button is disabled without it.</param>
    /// <param name="post">
    /// Runs an action on the UI thread; defaults to posting to Avalonia's
    /// dispatcher, or running inline when no Avalonia app is running.
    /// </param>
    /// <param name="timeFormat">The Local/UTC preference; times are local without it.</param>
    /// <param name="time">The clock whose zone is "local"; defaults to the machine's.</param>
    public LogsViewModel(
        LogBuffer buffer,
        Action? showFeedback = null,
        Action<Action>? post = null,
        ITimeFormatProvider? timeFormat = null,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
        _showFeedback = showFeedback;
        _post = post ?? PostToUiThread;
        _timeFormat = timeFormat;
        _localZone = (time ?? TimeProvider.System).LocalTimeZone;

        LevelFilters =
        [
            new(LogLevel.Debug, Strings.Logs_Filter_All),
            new(LogLevel.Information, Strings.Logs_Filter_Information),
            new(LogLevel.Warning, Strings.Logs_Filter_Warning),
            new(LogLevel.Error, Strings.Logs_Filter_Error),
        ];
        _selectedLevelFilter = LevelFilters[buffer.MinimumLevel <= LogLevel.Debug ? 0 : 1];

        CopyCommand = new RelayCommand(Copy, () => Entries.Count > 0);
        ClearCommand = new RelayCommand(Clear);
        SendFeedbackCommand = new RelayCommand(() => _showFeedback?.Invoke(), () => _showFeedback is not null);
        CloseDetailsCommand = new RelayCommand(() => SelectedEntry = null);
        CopySelectedEntryCommand = new RelayCommand(CopySelectedEntry, () => _selectedEntry is not null);
        CopySelectedMessageCommand = new RelayCommand(CopySelectedMessage, () => _selectedEntry is not null);
        ShowOnlySelectedAreaCommand = new RelayCommand(() => AddSelectedAreaToken(hide: false), () => _selectedEntry is not null);
        HideSelectedAreaCommand = new RelayCommand(() => AddSelectedAreaToken(hide: true), () => _selectedEntry is not null);
        ResumeFollowingCommand = new RelayCommand(ResumeFollowing);

        // _problemsSeen starts at 0, so warnings logged at startup, before the
        // panel existed, show on the badge too.
        _buffer.Changed += OnBufferChanged;
        _buffer.MinimumLevelChanged += OnMinimumLevelChanged;
        if (_timeFormat is not null)
            _timeFormat.TimeFormatChanged += OnTimeFormatChanged;
        Apply(_buffer.Snapshot());
    }

    /// <summary>The rows shown, oldest first: the buffer's entries that pass the level filter and the search.</summary>
    public ObservableCollection<LogRowViewModel> Entries { get; } = new BulkObservableCollection<LogRowViewModel>();

    /// <summary>Raised when the view should scroll to the newest row: rows arrived or the list was rebuilt while <see cref="IsFollowing"/>, or following resumed.</summary>
    public event EventHandler? ScrollToNewestRequested;

    /// <summary>Raised with the text to put on the clipboard (Copy).</summary>
    public event EventHandler<string>? CopyRequested;

    /// <summary>The level filter's choices.</summary>
    public IReadOnlyList<LogLevelFilterOption> LevelFilters { get; }

    private LogLevelFilterOption _selectedLevelFilter;
    /// <summary>The level filter: entries below its level are hidden. Debug is hidden by default.</summary>
    public LogLevelFilterOption SelectedLevelFilter
    {
        get => _selectedLevelFilter;
        set
        {
            if (value is null || !SetProperty(ref _selectedLevelFilter, value))
                return;
            Refilter();
        }
    }

    private string _searchText = string.Empty;
    /// <summary>
    /// Shows only entries whose message, area, category or exception contains
    /// this text (case-insensitive). <c>area:Name</c> and <c>-area:Name</c>
    /// tokens show only or hide an area (see <see cref="LogSearchQuery"/>).
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? string.Empty))
                return;
            _query = LogSearchQuery.Parse(_searchText);
            Refilter();
        }
    }

    /// <summary>Whether times show in UTC rather than the machine's zone.</summary>
    public bool IsUtc => _timeFormat?.Current == TimeFormat.Utc;

    private bool _isFollowing = true;
    /// <summary>
    /// Whether the list follows the tail, keeping the newest row in view. Off
    /// once the user scrolls up, selects a row or focuses the search.
    /// </summary>
    public bool IsFollowing
    {
        get => _isFollowing;
        private set
        {
            if (!SetProperty(ref _isFollowing, value))
                return;
            if (!value)
                _followStoppedAt = _lastSequence;
            RaiseNewEntries();
        }
    }

    /// <summary>How many shown rows arrived since following stopped; 0 while following.</summary>
    public int NewEntriesCount
    {
        get
        {
            if (_isFollowing)
                return 0;
            var count = 0;
            for (var i = Entries.Count - 1; i >= 0 && Entries[i].Sequence > _followStoppedAt; i--)
                count++;
            return count;
        }
    }

    /// <summary>Whether the new-entries pill shows: not following, and rows arrived below.</summary>
    public bool HasNewEntries => NewEntriesCount > 0;

    /// <summary>"↓ 12 new" for the new-entries pill.</summary>
    public string NewEntriesText => string.Format(CultureInfo.CurrentCulture, Strings.Logs_NewEntriesFormat, NewEntriesCount);

    /// <summary>The accessible name of the new-entries pill.</summary>
    public string NewEntriesName => string.Format(CultureInfo.CurrentCulture, Strings.Label_LogsNewEntriesFormat, NewEntriesCount);

    /// <summary>
    /// Whether the buffer also captures Debug messages. Changes only what the
    /// panel and feedback reports get, not the logging pipeline's own minimum
    /// (<c>--verbose</c>) or what goes to OTLP. Turning it on shows Debug
    /// entries; turning it off hides them again.
    /// </summary>
    public bool IsVerbose
    {
        get => _buffer.MinimumLevel <= LogLevel.Debug;
        set
        {
            if (value == IsVerbose)
                return;
            _buffer.MinimumLevel = value ? LogLevel.Debug : LogLevel.Information;
            if (value && SelectedLevelFilter.Level > LogLevel.Debug && SelectedLevelFilter.Level <= LogLevel.Information)
                SelectedLevelFilter = LevelFilters[0];
            else if (!value && SelectedLevelFilter.Level <= LogLevel.Debug)
                SelectedLevelFilter = LevelFilters[1];
        }
    }

    private LogRowViewModel? _selectedEntry;
    /// <summary>The selected row, whose full message and exception the details pane shows.</summary>
    public LogRowViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!SetProperty(ref _selectedEntry, value))
                return;
            OnPropertyChanged(nameof(HasSelectedEntry));
            OnPropertyChanged(nameof(ShowOnlySelectedAreaText));
            OnPropertyChanged(nameof(HideSelectedAreaText));
            ((RelayCommand)CopySelectedEntryCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CopySelectedMessageCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ShowOnlySelectedAreaCommand).NotifyCanExecuteChanged();
            ((RelayCommand)HideSelectedAreaCommand).NotifyCanExecuteChanged();
            // A selection pins the view, so the row stays where the user picked it.
            if (value is not null && !_restoringSelection)
                StopFollowing();
        }
    }

    private bool _restoringSelection;

    /// <summary>Whether a row is selected.</summary>
    public bool HasSelectedEntry => _selectedEntry is not null;

    /// <summary>"Show only Datasets", the row menu item for the selected row's area.</summary>
    public string ShowOnlySelectedAreaText => string.Format(
        CultureInfo.CurrentCulture, Strings.Logs_Menu_ShowOnlyAreaFormat, _selectedEntry?.AreaText ?? string.Empty);

    /// <summary>"Hide Datasets", the row menu item for the selected row's area.</summary>
    public string HideSelectedAreaText => string.Format(
        CultureInfo.CurrentCulture, Strings.Logs_Menu_HideAreaFormat, _selectedEntry?.AreaText ?? string.Empty);

    /// <summary>Whether the buffer holds any entries, filtered out or not.</summary>
    public bool HasAnyEntries => _all.Count > 0;

    /// <summary>The empty-state text, or empty when rows are shown.</summary>
    public string EmptyText => Entries.Count > 0
        ? string.Empty
        : _all.Count == 0 ? Strings.Logs_Empty : Strings.Logs_EmptyFiltered;

    /// <summary>Whether the empty state shows.</summary>
    public bool IsEmpty => Entries.Count == 0;

    /// <summary>Copies the shown entries as text.</summary>
    public ICommand CopyCommand { get; }

    /// <summary>Removes every entry from the buffer and the list.</summary>
    public ICommand ClearCommand { get; }

    /// <summary>Opens the feedback dialog, which carries these logs.</summary>
    public ICommand SendFeedbackCommand { get; }

    /// <summary>Closes the details pane by clearing the selection.</summary>
    public ICommand CloseDetailsCommand { get; }

    /// <summary>Copies the selected entry as text, home directory redacted like Copy.</summary>
    public ICommand CopySelectedEntryCommand { get; }

    /// <summary>Copies the selected entry's message, home directory redacted (the row menu).</summary>
    public ICommand CopySelectedMessageCommand { get; }

    /// <summary>Adds <c>area:</c> for the selected row's area to the search, replacing other <c>area:</c> tokens (the row menu).</summary>
    public ICommand ShowOnlySelectedAreaCommand { get; }

    /// <summary>Adds <c>-area:</c> for the selected row's area to the search (the row menu).</summary>
    public ICommand HideSelectedAreaCommand { get; }

    /// <summary>Scrolls to the newest row and follows the tail again (the new-entries pill, <c>End</c>).</summary>
    public ICommand ResumeFollowingCommand { get; }

    /// <summary>Stops following the tail: the view keeps the rows the user is reading in place.</summary>
    public void StopFollowing() => IsFollowing = false;

    /// <summary>Follows the tail again and asks the view to scroll to the newest row.</summary>
    public void ResumeFollowing()
    {
        IsFollowing = true;
        ScrollToNewestRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Takes a scroll the user made: away from the bottom stops following, and
    /// back to the bottom resumes it unless a row is selected (the selection
    /// keeps the view pinned until <see cref="ResumeFollowing"/>).
    /// </summary>
    /// <param name="atBottom">Whether the list is now scrolled to its last row.</param>
    public void OnUserScrolled(bool atBottom)
    {
        if (!atBottom)
            StopFollowing();
        else if (_selectedEntry is null && !_isFollowing)
            ResumeFollowing();
    }

    /// <summary>
    /// Moves the selection by <paramref name="delta"/> rows (<c>↑</c>/<c>↓</c>),
    /// clamped to the list. With nothing selected it selects the newest row.
    /// </summary>
    /// <returns>Whether the list had a row to select.</returns>
    public bool MoveSelection(int delta)
    {
        if (Entries.Count == 0)
            return false;
        var index = _selectedEntry is null ? -1 : Entries.IndexOf(_selectedEntry);
        index = index < 0 ? Entries.Count - 1 : Math.Clamp(index + delta, 0, Entries.Count - 1);
        SelectedEntry = Entries[index];
        return true;
    }

    /// <summary>Clears the selection, or the search when nothing is selected (<c>Esc</c>).</summary>
    /// <returns>Whether there was anything to clear.</returns>
    public bool ClearSelectionOrSearch()
    {
        if (_selectedEntry is not null)
        {
            SelectedEntry = null;
            return true;
        }
        if (_searchText.Length > 0)
        {
            SearchText = string.Empty;
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public int BadgeCount => _isTabShown ? 0 : (int)Math.Min(int.MaxValue, Math.Max(0, _buffer.ProblemsAdded - _problemsSeen));

    /// <inheritdoc />
    public string BadgeName => string.Format(CultureInfo.CurrentCulture, Strings.Logs_BadgeName, BadgeCount);

    /// <inheritdoc />
    public bool IsTabShown
    {
        get => _isTabShown;
        set
        {
            if (_isTabShown == value)
                return;
            _isTabShown = value;
            _problemsSeen = _buffer.ProblemsAdded;
            RaiseBadge();
        }
    }

    /// <summary>The text Copy puts on the clipboard: the shown entries, home directory redacted.</summary>
    internal string CopyText() => LogEntry.ToText(Entries.Select(r => r.Entry), HomePathRedactor.Redact);

    /// <summary>The text Copy entry puts on the clipboard: the selected entry, home directory redacted, or empty.</summary>
    internal string CopySelectedEntryText() => _selectedEntry is null
        ? string.Empty
        : LogEntry.ToText([_selectedEntry.Entry], HomePathRedactor.Redact);

    /// <summary>The text Copy message puts on the clipboard: the selected entry's message, home directory redacted, or empty.</summary>
    internal string CopySelectedMessageText() => _selectedEntry?.DetailsMessage ?? string.Empty;

    /// <summary>Applies the buffer's current state now (tests; the app refreshes on <see cref="LogBuffer.Changed"/>).</summary>
    internal void Refresh()
    {
        Interlocked.Exchange(ref _refreshPosted, 0);
        if (_disposed)
            return;
        RaiseBadge();
        Apply(_buffer.Snapshot());
    }

    private void OnBufferChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _refreshPosted, 1) == 0)
            _post(Refresh);
    }

    private void OnMinimumLevelChanged(object? sender, EventArgs e) =>
        _post(() => OnPropertyChanged(nameof(IsVerbose)));

    private void OnTimeFormatChanged(TimeFormat format) => _post(() =>
    {
        if (_disposed)
            return;
        var zone = Zone;
        foreach (var row in _all)
            row.SetTimeZone(zone, IsUtc);
        OnPropertyChanged(nameof(IsUtc));
        UpdateDayDividers();
    });

    private TimeZoneInfo Zone => IsUtc ? TimeZoneInfo.Utc : _localZone;

    private void Apply(IReadOnlyList<LogEntry> snapshot)
    {
        var oldest = snapshot.Count > 0 ? snapshot[0].Sequence : long.MaxValue;

        // Rows the buffer dropped (or cleared) leave from the front.
        var evicted = 0;
        while (evicted < _all.Count && _all[evicted].Sequence < oldest)
            evicted++;
        var shownEvicted = 0;
        for (var i = 0; i < evicted; i++)
        {
            if (_all[i].IsShown)
                shownEvicted++;
        }
        _all.RemoveRange(0, evicted);

        // A collapsed repeat updates the newest row we already have.
        if (_all.Count > 0)
        {
            var newest = _all[^1];
            for (var i = snapshot.Count - 1; i >= 0; i--)
            {
                if (snapshot[i].Sequence == newest.Sequence)
                {
                    newest.Update(snapshot[i]);
                    break;
                }
                if (snapshot[i].Sequence < newest.Sequence)
                    break;
            }
        }

        var added = new List<LogRowViewModel>();
        var zone = Zone;
        var isUtc = IsUtc;
        foreach (var entry in snapshot)
        {
            if (entry.Sequence <= _lastSequence)
                continue;
            var row = new LogRowViewModel(entry, zone, isUtc);
            _all.Add(row);
            _lastSequence = entry.Sequence;
            if (Matches(row))
            {
                row.IsShown = true;
                added.Add(row);
            }
        }

        if (shownEvicted == 0 && added.Count == 0)
        {
            if (evicted > 0)
                RaiseListState();
            return;
        }

        var selected = _selectedEntry;
        if (shownEvicted + added.Count > IncrementalLimit)
        {
            RebuildEntries();
        }
        else
        {
            for (var i = 0; i < shownEvicted; i++)
                Entries.RemoveAt(0);
            foreach (var row in added)
                Entries.Add(row);
        }
        UpdateDayDividers();
        RestoreSelection(selected);
        RaiseListState();
        RaiseNewEntries();
        if (added.Count > 0 && _isFollowing)
            ScrollToNewestRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Refilter()
    {
        var selected = _selectedEntry;
        foreach (var row in _all)
            row.IsShown = Matches(row);
        RebuildEntries();
        UpdateDayDividers();
        RestoreSelection(selected);
        RaiseListState();
        RaiseNewEntries();
        if (_isFollowing)
            ScrollToNewestRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildEntries()
    {
        var bulk = (BulkObservableCollection<LogRowViewModel>)Entries;
        bulk.Clear();
        bulk.InsertRange(0, _all.Where(r => r.IsShown).ToList());
    }

    private void RestoreSelection(LogRowViewModel? selected)
    {
        // A rebuild can clear the list's selection; keep the user's row while it's still listed.
        _restoringSelection = true;
        try
        {
            SelectedEntry = selected is not null && selected.IsShown && _all.Contains(selected) ? selected : null;
        }
        finally
        {
            _restoringSelection = false;
        }
    }

    /// <summary>
    /// Marks the first shown row of each day with a date divider, when the
    /// shown rows span more than one day; the oldest row then gets one too,
    /// so every day is labelled. Rows are in time order, so the span is the
    /// first row's day against the last's.
    /// </summary>
    private void UpdateDayDividers()
    {
        var spansDays = Entries.Count > 1 && Entries[0].Day != Entries[^1].Day;
        DateOnly? previous = null;
        foreach (var row in Entries)
        {
            row.ShowsDayDivider = spansDays && row.Day != previous;
            previous = row.Day;
        }
    }

    private bool Matches(LogRowViewModel row)
    {
        if (row.Level < _selectedLevelFilter.Level)
            return false;
        if (_query.IsEmpty)
            return true;
        if (!_query.MatchesArea(row.AreaText, row.Entry.Area))
            return false;
        var search = _query.Text;
        if (search.Length == 0)
            return true;
        var entry = row.Entry;
        return entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.AreaText.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (entry.Exception?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void Copy()
    {
        var text = CopyText();
        if (text.Length > 0)
            CopyRequested?.Invoke(this, text);
    }

    private void CopySelectedEntry()
    {
        var text = CopySelectedEntryText();
        if (text.Length > 0)
            CopyRequested?.Invoke(this, text);
    }

    private void CopySelectedMessage()
    {
        var text = CopySelectedMessageText();
        if (text.Length > 0)
            CopyRequested?.Invoke(this, text);
    }

    private void AddSelectedAreaToken(bool hide)
    {
        if (_selectedEntry is { } row)
            SearchText = LogSearchQuery.WithAreaToken(_searchText, row.AreaText, hide);
    }

    private void Clear()
    {
        _buffer.Clear();
        _problemsSeen = _buffer.ProblemsAdded;
        Apply(_buffer.Snapshot());
        RaiseBadge();
        // Nothing is left to read, so the next entries are followed again.
        ResumeFollowing();
    }

    private void RaiseListState()
    {
        OnPropertyChanged(nameof(HasAnyEntries));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
        ((RelayCommand)CopyCommand).NotifyCanExecuteChanged();
    }

    private void RaiseNewEntries()
    {
        OnPropertyChanged(nameof(NewEntriesCount));
        OnPropertyChanged(nameof(HasNewEntries));
        OnPropertyChanged(nameof(NewEntriesText));
        OnPropertyChanged(nameof(NewEntriesName));
    }

    private void RaiseBadge()
    {
        OnPropertyChanged(nameof(BadgeCount));
        OnPropertyChanged(nameof(BadgeName));
    }

    private static void PostToUiThread(Action action)
    {
        if (Avalonia.Application.Current is not null && !Dispatcher.UIThread.CheckAccess())
            Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
        else
            action();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _buffer.Changed -= OnBufferChanged;
        _buffer.MinimumLevelChanged -= OnMinimumLevelChanged;
        if (_timeFormat is not null)
            _timeFormat.TimeFormatChanged -= OnTimeFormatChanged;
    }
}

/// <summary>One row of the Logs panel: a <see cref="LogEntry"/>, whose repeat count can grow.</summary>
internal sealed class LogRowViewModel : ViewModelBase
{
    /// <summary>Creates a row for <paramref name="entry"/>.</summary>
    /// <param name="entry">The buffered entry.</param>
    /// <param name="zone">The zone its times show in; the machine's when omitted.</param>
    /// <param name="isUtc">Whether <paramref name="zone"/> is the UTC preference, which the full time spells out.</param>
    public LogRowViewModel(LogEntry entry, TimeZoneInfo? zone = null, bool isUtc = false)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entry = entry;
        AreaText = Strings.LogAreaName(entry.Area);
        LevelText = Strings.LogLevelName(entry.Level);
        LevelShortText = Strings.LogLevelShortName(entry.Level);
        _zone = zone ?? TimeZoneInfo.Local;
        _isUtc = isUtc;
        var newline = entry.Message.IndexOfAny(['\r', '\n']);
        Summary = newline < 0 ? entry.Message : entry.Message[..newline] + " …";
    }

    private LogEntry _entry;
    /// <summary>The buffered entry.</summary>
    public LogEntry Entry => _entry;

    /// <summary>The entry's sequence in the buffer.</summary>
    public long Sequence => _entry.Sequence;

    /// <summary>The entry's level.</summary>
    public LogLevel Level => _entry.Level;

    private TimeZoneInfo _zone;
    private bool _isUtc;

    /// <summary>The time of the entry's first message, <c>HH:mm:ss.fff</c> in the panel's zone.</summary>
    public string TimeText => Time(_entry.Timestamp).ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>
    /// The full time of the entry's first message with its date,
    /// <c>2026-10-30 16:29:52.795</c> in the panel's zone and "UTC" after it in
    /// UTC, for the row tooltip and the details header.
    /// </summary>
    public string FullTimeText
    {
        get
        {
            var text = Time(_entry.Timestamp).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            return _isUtc ? text + " " + Strings.Time_Utc_Suffix : text;
        }
    }

    /// <summary>The day of the entry's first message in the panel's zone.</summary>
    public DateOnly Day => DateOnly.FromDateTime(Time(_entry.Timestamp));

    /// <summary>The day divider's text: the day in the culture's long form, "UTC" after it in UTC.</summary>
    public string DayText
    {
        get
        {
            var text = Time(_entry.Timestamp).ToString("D", CultureInfo.CurrentCulture);
            return _isUtc ? text + " " + Strings.Time_Utc_Suffix : text;
        }
    }

    private bool _showsDayDivider;
    /// <summary>Whether a date divider shows above the row: it starts a day, and the shown rows span more than one.</summary>
    public bool ShowsDayDivider
    {
        get => _showsDayDivider;
        internal set => SetProperty(ref _showsDayDivider, value);
    }

    /// <summary>The localized level name, for the details pane and the accessible name.</summary>
    public string LevelText { get; }

    /// <summary>The short level label for the row's narrow level column ("Warn", "Crit").</summary>
    public string LevelShortText { get; }

    /// <summary>The localized area name.</summary>
    public string AreaText { get; }

    /// <summary>The logger category, shown in the details pane.</summary>
    public string Category => _entry.Category;

    /// <summary>The message's first line, for the row.</summary>
    public string Summary { get; }

    /// <summary>The full message, for the details pane.</summary>
    public string Message => _entry.Message;

    /// <summary>The exception text, or <see langword="null"/>.</summary>
    public string? Exception => _entry.Exception;

    /// <summary>The full message with the home directory shown as <c>~</c>, for the details pane.</summary>
    public string DetailsMessage => HomePathRedactor.Redact(_entry.Message);

    /// <summary>
    /// The exception's first line (type and message) with the home directory
    /// shown as <c>~</c>, for the details pane, or empty.
    /// </summary>
    public string ExceptionHeadline => SplitException().Headline;

    /// <summary>
    /// The rest of the exception (the stack frames and any inner exceptions),
    /// starting with its line break, with the home directory shown as <c>~</c>,
    /// for the details pane, or empty.
    /// </summary>
    public string ExceptionFrames => SplitException().Frames;

    /// <summary>Whether the entry carries an exception.</summary>
    public bool HasException => _entry.Exception is not null;

    /// <summary>How many identical consecutive messages the row stands for.</summary>
    public int Count => _entry.Count;

    /// <summary>"×3" for a repeated message, else empty.</summary>
    public string CountText => _entry.Count > 1
        ? string.Format(CultureInfo.CurrentCulture, Strings.Logs_RepeatFormat, _entry.Count)
        : string.Empty;

    /// <summary>Whether the message repeated.</summary>
    public bool IsRepeated => _entry.Count > 1;

    /// <summary>"Repeated 3 times, last at 10:42:07.120" for the details pane, else empty.</summary>
    public string RepeatedText => _entry.Count > 1
        ? string.Format(
            CultureInfo.CurrentCulture,
            Strings.Logs_Details_Repeated,
            _entry.Count,
            Time(_entry.LastTimestamp).ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
        : string.Empty;

    /// <summary>Whether the entry is a warning.</summary>
    public bool IsWarning => _entry.Level == LogLevel.Warning;

    /// <summary>Whether the entry is an error or critical.</summary>
    public bool IsError => _entry.Level >= LogLevel.Error;

    /// <summary>Whether the entry is Debug or Trace.</summary>
    public bool IsDebug => _entry.Level <= LogLevel.Debug;

    /// <summary>Whether the entry is Information, the baseline level the row mutes.</summary>
    public bool IsInformation => _entry.Level == LogLevel.Information;

    /// <summary>The accessible name of the row: level, area and message.</summary>
    public string AccessibleName => string.Format(CultureInfo.CurrentCulture, Strings.Logs_RowNameFormat, LevelText, AreaText, Summary);

    /// <summary>Whether the row passes the panel's filter.</summary>
    internal bool IsShown { get; set; }

    private (string Headline, string Frames) SplitException()
    {
        if (_entry.Exception is not { } exception)
            return (string.Empty, string.Empty);
        var text = HomePathRedactor.Redact(exception);
        var newline = text.IndexOfAny(['\r', '\n']);
        return newline < 0 ? (text, string.Empty) : (text[..newline], text[newline..]);
    }

    /// <summary>Shows the row's times in <paramref name="zone"/> (the Local/UTC preference changed).</summary>
    internal void SetTimeZone(TimeZoneInfo zone, bool isUtc)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (ReferenceEquals(zone, _zone) && isUtc == _isUtc)
            return;
        _zone = zone;
        _isUtc = isUtc;
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(FullTimeText));
        OnPropertyChanged(nameof(Day));
        OnPropertyChanged(nameof(DayText));
        OnPropertyChanged(nameof(RepeatedText));
    }

    private DateTime Time(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, _zone).DateTime;

    /// <summary>Takes a newer copy of the same entry (a repeat was collapsed into it).</summary>
    internal void Update(LogEntry entry)
    {
        if (entry.Sequence != _entry.Sequence || entry.Count == _entry.Count)
            return;
        _entry = entry;
        OnPropertyChanged(nameof(Entry));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsRepeated));
        OnPropertyChanged(nameof(RepeatedText));
    }
}
