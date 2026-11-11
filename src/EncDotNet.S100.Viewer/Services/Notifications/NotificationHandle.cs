using EncDotNet.S100.Viewer.ViewModels.Notifications;

namespace EncDotNet.S100.Viewer.Services.Notifications;

/// <summary>
/// Default <see cref="INotificationHandle"/>. A thin, thread-safe facade over a
/// <see cref="NotificationViewModel"/> that routes every mutation through the
/// owning <see cref="NotificationService"/> (and thus the UI thread) and
/// becomes inert once dismissed.
/// </summary>
internal sealed class NotificationHandle : INotificationHandle
{
    private readonly NotificationService _service;
    private readonly NotificationViewModel _vm;
    private int _dismissed;

    /// <summary>Creates a handle wrapping the given view-model.</summary>
    public NotificationHandle(NotificationService service, NotificationViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(vm);
        _service = service;
        _vm = vm;
    }

    /// <inheritdoc />
    public Guid Id => _vm.Id;

    /// <inheritdoc />
    public bool IsDismissed => Volatile.Read(ref _dismissed) != 0;

    /// <inheritdoc />
    public event EventHandler? Dismissed;

    /// <inheritdoc />
    public void Update(
        string? title = null,
        string? message = null,
        NotificationSeverity? severity = null)
    {
        if (IsDismissed) return;
        _service.Mutate(Id, vm =>
        {
            if (title is not null) vm.Title = title;
            if (message is not null) vm.Message = message;
            if (severity is { } s) vm.Severity = s;
        });
    }

    /// <inheritdoc />
    public void Report(double value)
    {
        if (IsDismissed) return;
        _service.Mutate(Id, vm =>
        {
            vm.IsIndeterminate = false;
            vm.HasProgress = true;
            vm.Progress = value;
        });
    }

    /// <inheritdoc />
    public void SetIndeterminate(bool indeterminate)
    {
        if (IsDismissed) return;
        _service.Mutate(Id, vm =>
        {
            vm.HasProgress = true;
            vm.IsIndeterminate = indeterminate;
        });
    }

    /// <inheritdoc />
    public void ClearProgress()
    {
        if (IsDismissed) return;
        _service.Mutate(Id, vm =>
        {
            vm.HasProgress = false;
            vm.IsIndeterminate = false;
        });
    }

    /// <inheritdoc />
    public void SetActions(params NotificationActionDescriptor[] actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if (IsDismissed) return;
        _service.Mutate(Id, vm => vm.SetActions(actions));
    }

    /// <inheritdoc />
    public void SetCustomContent(object? content)
    {
        if (IsDismissed) return;
        _service.Mutate(Id, vm => vm.CustomContent = content);
    }

    /// <inheritdoc />
    public void ScheduleAutoDismiss(TimeSpan after)
    {
        if (IsDismissed) return;
        _service.ScheduleAutoDismiss(Id, after);
    }

    /// <inheritdoc />
    public void CancelAutoDismiss()
    {
        if (IsDismissed) return;
        _service.CancelAutoDismiss(Id);
    }

    /// <inheritdoc />
    public void Dismiss()
    {
        if (IsDismissed) return;
        _service.Dismiss(_vm);
    }

    /// <summary>
    /// Marks the handle dismissed and raises <see cref="Dismissed"/> exactly
    /// once. Called by the service as the notification moves to history.
    /// </summary>
    internal void MarkDismissed()
    {
        if (Interlocked.Exchange(ref _dismissed, 1) == 0)
            Dismissed?.Invoke(this, EventArgs.Empty);
    }
}
