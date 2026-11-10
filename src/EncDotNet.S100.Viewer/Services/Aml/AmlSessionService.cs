using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.Aml;

/// <summary>
/// Watches the AML cells loaded in the Datasets panel (#1371): feeds the
/// status bar's MARKING pill and says once per exchange set, when it's first
/// opened, that it's military data (decisions 10–12).
/// </summary>
/// <remarks>
/// <para>
/// The notification waits until the set's AML cells stop loading, so it
/// counts them all. It's an Information notification: "{set} is military
/// data", the number of cells and their product codes, whether SoundCharts
/// draws them, and "Marked {marking}." when the highest marking is above
/// UNCLASSIFIED. <em>Details</em> opens the set's Library item, or the set in
/// the Datasets panel when it isn't in the Library.
/// </para>
/// <para>
/// A set is keyed by its path; <see cref="AmlNoticeStore"/> remembers the
/// sets already announced, so reopening one says nothing.
/// </para>
/// </remarks>
internal sealed class AmlSessionService : IDisposable
{
    /// <summary>How long a set's AML cells must be quiet before its notification shows.</summary>
    internal static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(600);

    private readonly DatasetsViewModel _datasets;
    private readonly AmlMarkingIndicatorViewModel _pill;
    private readonly INotificationService _notifications;
    private readonly AmlNoticeStore _store;
    private readonly Action<Action> _post;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action<string, ExchangeSetHeader?>? _openDetails;
    private readonly HashSet<DatasetEntry> _subscribed = [];
    private readonly Dictionary<string, CancellationTokenSource> _pending = new(StringComparer.Ordinal);
    private bool _updatePosted;
    private bool _disposed;

    /// <summary>Creates the service and starts watching.</summary>
    /// <param name="datasets">The Datasets panel.</param>
    /// <param name="pill">The status bar's MARKING pill.</param>
    /// <param name="notifications">Shows the first-open notification.</param>
    /// <param name="store">Remembers the sets already announced.</param>
    /// <param name="post">Queues work on the UI thread (the app passes <see cref="UiThread.Post"/>).</param>
    /// <param name="delay">Waits before a set's notification (the app passes <see cref="Task.Delay(TimeSpan, CancellationToken)"/>).</param>
    /// <param name="openDetails">
    /// Opens the Library item of a cell (by its display name), or the set in
    /// the Datasets panel; <see langword="null"/> leaves the notification without <em>Details</em>.
    /// </param>
    public AmlSessionService(
        DatasetsViewModel datasets,
        AmlMarkingIndicatorViewModel pill,
        INotificationService notifications,
        AmlNoticeStore store,
        Action<Action> post,
        Func<TimeSpan, CancellationToken, Task> delay,
        Action<string, ExchangeSetHeader?>? openDetails)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(pill);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(delay);

        _datasets = datasets;
        _pill = pill;
        _notifications = notifications;
        _store = store;
        _post = post;
        _delay = delay;
        _openDetails = openDetails;

        _datasets.Entries.CollectionChanged += OnEntriesChanged;
        _datasets.ExchangeSetHeaders.CollectionChanged += OnHeadersChanged;
        foreach (var entry in _datasets.Entries)
            Subscribe(entry);
        Update();
    }

    /// <summary>The loaded AML sets, as the pill shows them.</summary>
    internal IReadOnlyList<AmlLoadedSet> LoadedSets()
    {
        var sets = new List<(string Key, string Name, List<string?> Markings)>();
        foreach (var entry in _datasets.Entries)
        {
            if (entry.Aml is not { } aml)
                continue;
            var (key, name, _) = SetOf(entry);
            var set = sets.FirstOrDefault(s => s.Key == key);
            if (set.Key is null)
                sets.Add(set = (key, name, []));
            set.Markings.Add(aml.Info.ProtectiveMarking);
        }

        return sets.Select(s => new AmlLoadedSet(s.Name, s.Markings)).ToArray();
    }

    /// <summary>
    /// The first-open notification's text for a set of AML cells: its title,
    /// and its body with the marking sentence when it's above UNCLASSIFIED.
    /// </summary>
    /// <param name="setName">The set's name.</param>
    /// <param name="cells">The set's loaded AML cells.</param>
    internal static (string Title, string Body) NoticeText(string setName, IReadOnlyList<AmlCellState> cells)
    {
        var codes = string.Join(", ", cells
            .Select(c => c.Info.ProductCode ?? c.Info.TagText)
            .Distinct(StringComparer.Ordinal));
        var drawn = cells.Count > 0 && cells.All(c => c.Drawn);
        var body = (cells.Count, drawn) switch
        {
            (1, false) => string.Format(CultureInfo.CurrentCulture, Strings.Aml_Notice_NotDrawnOneFormat, codes),
            (1, true) => string.Format(CultureInfo.CurrentCulture, Strings.Aml_Notice_DrawnOneFormat, codes),
            (_, false) => string.Format(CultureInfo.CurrentCulture, Strings.Aml_Notice_NotDrawnFormat, cells.Count, codes),
            (_, true) => string.Format(CultureInfo.CurrentCulture, Strings.Aml_Notice_DrawnFormat, cells.Count, codes),
        };

        var highest = AmlProtectiveMarking.Highest(cells.Select(c => c.Info.ProtectiveMarking));
        if (highest is { IsAboveUnclassified: true })
            body += " " + string.Format(CultureInfo.CurrentCulture, Strings.Aml_Notice_MarkedFormat, highest.DisplayText);

        return (string.Format(CultureInfo.CurrentCulture, Strings.Aml_Notice_TitleFormat, setName), body);
    }

    private (string Key, string Name, ExchangeSetHeader? Header) SetOf(DatasetEntry entry)
    {
        var header = _datasets.ExchangeSetHeaders.FirstOrDefault(h => h.Contains(entry.Source));
        return header is not null
            ? (header.GroupKey ?? header.SourcePath, header.DisplayName, header)
            : (entry.FilePath, entry.DisplayName, null);
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (DatasetEntry entry in e.OldItems)
                Unsubscribe(entry);
        }

        if (e.NewItems is not null)
        {
            foreach (DatasetEntry entry in e.NewItems)
                Subscribe(entry);
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var entry in _subscribed)
                entry.PropertyChanged -= OnEntryChanged;
            _subscribed.Clear();
            foreach (var entry in _datasets.Entries)
                Subscribe(entry);
        }

        ScheduleUpdate();
    }

    private void OnHeadersChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleUpdate();

    private void Subscribe(DatasetEntry entry)
    {
        if (_subscribed.Add(entry))
            entry.PropertyChanged += OnEntryChanged;
    }

    private void Unsubscribe(DatasetEntry entry)
    {
        if (_subscribed.Remove(entry))
            entry.PropertyChanged -= OnEntryChanged;
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is DatasetEntry { IsAml: true }
            && e.PropertyName is nameof(DatasetEntry.Aml) or nameof(DatasetEntry.IsLoading))
        {
            ScheduleUpdate();
        }
    }

    private void ScheduleUpdate()
    {
        if (_updatePosted)
            return;
        _updatePosted = true;
        _post(() =>
        {
            _updatePosted = false;
            Update();
        });
    }

    /// <summary>Refreshes the pill and queues the notifications of sets that just finished loading.</summary>
    internal void Update()
    {
        if (_disposed)
            return;

        _pill.Update(LoadedSets());

        foreach (var group in _datasets.Entries.Where(e => e.IsAml).GroupBy(e => SetOf(e).Key, StringComparer.Ordinal))
        {
            var key = group.Key;
            if (_store.HasAnnounced(key))
                continue;

            if (_pending.Remove(key, out var previous))
            {
                previous.Cancel();
                previous.Dispose();
            }

            // Wait for the set to stop loading, so the notification counts every cell.
            if (group.Any(e => e.IsLoading) || !group.Any(e => e.Aml is not null))
                continue;

            var cts = new CancellationTokenSource();
            _pending[key] = cts;
            _ = AnnounceAfterDelayAsync(key, cts.Token);
        }
    }

    private async Task AnnounceAfterDelayAsync(string key, CancellationToken token)
    {
        try
        {
            await _delay(SettleDelay, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _post(() =>
        {
            if (_disposed || token.IsCancellationRequested)
                return;
            if (_pending.Remove(key, out var cts))
                cts.Dispose();
            Announce(key);
        });
    }

    private void Announce(string key)
    {
        if (_store.HasAnnounced(key))
            return;

        var members = _datasets.Entries.Where(e => e.IsAml && SetOf(e).Key == key).ToArray();
        var cells = members.Select(e => e.Aml).OfType<AmlCellState>().ToArray();
        if (cells.Length == 0)
            return;

        var (_, name, header) = SetOf(members[0]);
        var (title, body) = NoticeText(name, cells);
        var builder = _notifications.Create(title)
            .WithSeverity(NotificationSeverity.Info)
            .WithContent(body)
            .Persistent();
        if (_openDetails is { } open)
        {
            var cell = members.First(e => e.Aml is not null).DisplayName;
            builder = builder.WithAction(Strings.Aml_Notice_Details, () => open(cell, header), isPrimary: false);
        }

        builder.Show();
        _store.MarkAnnounced(key);
    }

    /// <summary>Stops watching. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _datasets.Entries.CollectionChanged -= OnEntriesChanged;
        _datasets.ExchangeSetHeaders.CollectionChanged -= OnHeadersChanged;
        foreach (var entry in _subscribed)
            entry.PropertyChanged -= OnEntryChanged;
        _subscribed.Clear();
        foreach (var cts in _pending.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _pending.Clear();
    }
}
