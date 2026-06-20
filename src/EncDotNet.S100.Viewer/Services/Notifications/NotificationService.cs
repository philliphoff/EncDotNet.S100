using System.Collections.ObjectModel;
using EncDotNet.S100.Viewer.ViewModels.Notifications;

namespace EncDotNet.S100.Viewer.Services.Notifications;

/// <summary>
/// Default <see cref="INotificationService"/> implementation. Owns the active
/// and history collections, per-notification auto-dismiss timers, and all
/// view-model mutation, funneling every state change through an
/// <see cref="IUiDispatcher"/> so callers on any thread are safe.
/// </summary>
internal sealed class NotificationService : INotificationService
{
    /// <summary>Default history ring-buffer capacity (locked design decision).</summary>
    public const int DefaultHistoryCapacity = 100;

    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly int _historyCapacity;

    private readonly ObservableCollection<NotificationViewModel> _active = new();
    private readonly ObservableCollection<NotificationViewModel> _history = new();
    private readonly Dictionary<Guid, Entry> _entries = new();

    /// <summary>Creates a notification service.</summary>
    /// <param name="dispatcher">UI-thread marshaler.</param>
    /// <param name="timeProvider">
    /// Time source for auto-dismiss timers; inject a fake for deterministic tests.
    /// </param>
    /// <param name="historyCapacity">History ring-buffer capacity.</param>
    public NotificationService(
        IUiDispatcher dispatcher,
        TimeProvider timeProvider,
        int historyCapacity = DefaultHistoryCapacity)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(historyCapacity);

        _dispatcher = dispatcher;
        _time = timeProvider;
        _historyCapacity = historyCapacity;
        Active = new ReadOnlyObservableCollection<NotificationViewModel>(_active);
        History = new ReadOnlyObservableCollection<NotificationViewModel>(_history);
    }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<NotificationViewModel> Active { get; }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<NotificationViewModel> History { get; }

    /// <inheritdoc />
    public INotificationBuilder Create(string title)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        return new NotificationBuilder(this, title);
    }

    /// <inheritdoc />
    public void DismissAll() => Post(() =>
    {
        foreach (var vm in _active.ToArray())
            DismissCore(vm);
    });

    /// <inheritdoc />
    public void ClearHistory() => Post(() => _history.Clear());

    /// <summary>
    /// Returns the default auto-dismiss delay for a severity when a caller marks
    /// a notification ephemeral without specifying one.
    /// </summary>
    internal static TimeSpan DefaultDelayFor(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Success => TimeSpan.FromSeconds(4),
        NotificationSeverity.Info => TimeSpan.FromSeconds(6),
        NotificationSeverity.Warning => TimeSpan.FromSeconds(8),
        NotificationSeverity.Error => TimeSpan.FromSeconds(12),
        _ => TimeSpan.FromSeconds(6),
    };

    /// <summary>
    /// Registers and displays a built notification, returning its handle.
    /// Called by <see cref="NotificationBuilder.Show"/>.
    /// </summary>
    internal INotificationHandle Show(NotificationViewModel vm, TimeSpan? autoDismissAfter)
    {
        ArgumentNullException.ThrowIfNull(vm);
        var handle = new NotificationHandle(this, vm);

        Post(() =>
        {
            var entry = new Entry(vm, handle);
            _entries[vm.Id] = entry;
            vm.CloseRequested += OnCloseRequested;
            _active.Add(vm);
            if (autoDismissAfter is { } delay)
                ScheduleAutoDismissCore(entry, delay);
        });

        return handle;
    }

    /// <summary>Marshals a mutation onto the UI thread (inline when already on it).</summary>
    internal void Post(Action action)
    {
        if (_dispatcher.IsOnUiThread)
            action();
        else
            _dispatcher.Post(action);
    }

    /// <summary>Mutates the view-model of a still-active notification on the UI thread.</summary>
    internal void Mutate(Guid id, Action<NotificationViewModel> mutate) => Post(() =>
    {
        if (_entries.TryGetValue(id, out var entry))
            mutate(entry.Vm);
    });

    /// <summary>Schedules (or reschedules) an auto-dismiss for a notification.</summary>
    internal void ScheduleAutoDismiss(Guid id, TimeSpan after) => Post(() =>
    {
        if (_entries.TryGetValue(id, out var entry))
            ScheduleAutoDismissCore(entry, after);
    });

    /// <summary>Cancels a pending auto-dismiss without dismissing the notification.</summary>
    internal void CancelAutoDismiss(Guid id) => Post(() =>
    {
        if (_entries.TryGetValue(id, out var entry))
            entry.DisposeTimer();
    });

    /// <summary>Dismisses a notification by view-model.</summary>
    internal void Dismiss(NotificationViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        Post(() => DismissCore(vm));
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        if (sender is NotificationViewModel vm)
            DismissCore(vm);
    }

    private void ScheduleAutoDismissCore(Entry entry, TimeSpan after)
    {
        entry.DisposeTimer();
        var clamped = after < TimeSpan.Zero ? TimeSpan.Zero : after;
        entry.Timer = _time.CreateTimer(
            _ => OnTimerElapsed(entry),
            null,
            clamped,
            Timeout.InfiniteTimeSpan);
    }

    private void OnTimerElapsed(Entry entry) => Post(() =>
    {
        // The notification may already be gone (user dismissed, rescheduled to a
        // fresh timer, etc.); only act if this entry is still the live one.
        if (_entries.TryGetValue(entry.Vm.Id, out var current) && current == entry)
            DismissCore(entry.Vm);
    });

    private void DismissCore(NotificationViewModel vm)
    {
        if (!_entries.Remove(vm.Id, out var entry))
            return;

        entry.DisposeTimer();
        vm.CloseRequested -= OnCloseRequested;
        _active.Remove(vm);

        vm.DismissedUtc = _time.GetUtcNow();
        _history.Insert(0, vm);
        while (_history.Count > _historyCapacity)
            _history.RemoveAt(_history.Count - 1);

        entry.Handle.MarkDismissed();
    }

    /// <summary>Per-notification bookkeeping: view-model, handle, and live timer.</summary>
    private sealed class Entry
    {
        public Entry(NotificationViewModel vm, NotificationHandle handle)
        {
            Vm = vm;
            Handle = handle;
        }

        public NotificationViewModel Vm { get; }

        public NotificationHandle Handle { get; }

        public ITimer? Timer { get; set; }

        public void DisposeTimer()
        {
            Timer?.Dispose();
            Timer = null;
        }
    }
}
