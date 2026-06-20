using EncDotNet.S100.Viewer.ViewModels.Notifications;

namespace EncDotNet.S100.Viewer.Services.Notifications;

/// <summary>
/// Default <see cref="INotificationBuilder"/>. Accumulates configuration, then
/// constructs a <see cref="NotificationViewModel"/> and registers it with the
/// owning <see cref="NotificationService"/> on <see cref="Show"/>.
/// </summary>
internal sealed class NotificationBuilder : INotificationBuilder
{
    private readonly NotificationService _service;
    private readonly string _title;
    private readonly List<NotificationActionDescriptor> _actions = new();

    private NotificationSeverity _severity = NotificationSeverity.Info;
    private string? _message;
    private bool _persistent;
    private TimeSpan? _autoDismiss;
    private bool _hasProgress;
    private double _progress;
    private bool _indeterminate;
    private object? _customContent;

    /// <summary>Creates a builder for the given service and title.</summary>
    public NotificationBuilder(NotificationService service, string title)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrEmpty(title);
        _service = service;
        _title = title;
    }

    /// <inheritdoc />
    public INotificationBuilder WithSeverity(NotificationSeverity severity)
    {
        _severity = severity;
        return this;
    }

    /// <inheritdoc />
    public INotificationBuilder WithContent(string? message)
    {
        if (!string.IsNullOrEmpty(message))
            _message = message;
        return this;
    }

    /// <inheritdoc />
    public INotificationBuilder AutoDismiss(TimeSpan after)
    {
        _persistent = false;
        _autoDismiss = after;
        return this;
    }

    /// <inheritdoc />
    public INotificationBuilder Persistent()
    {
        _persistent = true;
        _autoDismiss = null;
        return this;
    }

    /// <inheritdoc />
    public INotificationBuilder AsProgress(double value = 0d, bool indeterminate = false)
    {
        _hasProgress = true;
        _progress = value;
        _indeterminate = indeterminate;
        return this;
    }

    /// <inheritdoc />
    public INotificationBuilder WithAction(
        string label,
        Action invoke,
        bool isPrimary = false,
        bool dismissOnInvoke = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(label);
        ArgumentNullException.ThrowIfNull(invoke);
        _actions.Add(new NotificationActionDescriptor(label, invoke, isPrimary, dismissOnInvoke));
        return this;
    }

    /// <inheritdoc />
    public INotificationBuilder WithCustomContent(object content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _customContent = content;
        return this;
    }

    /// <inheritdoc />
    public INotificationHandle Show()
    {
        var vm = new NotificationViewModel(Guid.NewGuid(), _severity, _title, _message, _persistent)
        {
            HasProgress = _hasProgress,
            Progress = _progress,
            IsIndeterminate = _indeterminate,
            CustomContent = _customContent,
        };

        if (_actions.Count > 0)
            vm.SetActions(_actions);

        var autoDismissAfter = _persistent
            ? (TimeSpan?)null
            : _autoDismiss ?? NotificationService.DefaultDelayFor(_severity);

        return _service.Show(vm, autoDismissAfter);
    }
}
