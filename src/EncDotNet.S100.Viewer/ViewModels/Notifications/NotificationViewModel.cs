using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Notifications;

namespace EncDotNet.S100.Viewer.ViewModels.Notifications;

/// <summary>
/// View-model for a single notification card. A notification is composed of
/// optional regions — body text, a progress bar, action buttons, and custom
/// content — that a single card template shows or hides based on which are
/// populated. This composable shape lets a held
/// <see cref="INotificationHandle"/> mutate a notification across its lifetime
/// (for example, a loading progress notification that transitions in place to a
/// success or error terminal state) without ever creating a second card.
/// </summary>
/// <remarks>
/// All mutable state is exposed as observable properties. Mutation is expected
/// to happen on the UI thread; the owning service marshals handle calls
/// accordingly.
/// </remarks>
internal sealed class NotificationViewModel : ViewModelBase
{
    private NotificationSeverity _severity;
    private string _title;
    private string? _message;
    private bool _hasProgress;
    private double _progress;
    private bool _isIndeterminate;
    private object? _customContent;
    private bool _isExpanded;
    private bool _isMessageTruncated;

    /// <summary>Initializes a notification.</summary>
    /// <param name="id">Stable identity, shared with the notification handle.</param>
    /// <param name="severity">Initial severity.</param>
    /// <param name="title">Initial title (caller-localized).</param>
    /// <param name="message">Optional initial body text (caller-localized).</param>
    /// <param name="isPersistent">
    /// When <see langword="true"/> the notification does not auto-dismiss on its
    /// own; a scheduled auto-dismiss may still be applied later via the handle.
    /// </param>
    public NotificationViewModel(
        Guid id,
        NotificationSeverity severity,
        string title,
        string? message,
        bool isPersistent)
    {
        Id = id;
        _severity = severity;
        _title = title;
        _message = message;
        IsPersistent = isPersistent;
        CreatedUtc = DateTimeOffset.UtcNow;
        Actions = new ObservableCollection<NotificationActionViewModel>();
        Actions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasActions));
        CloseCommand = new RelayCommand(RequestClose);
        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    /// <summary>Stable identity, shared with the owning handle.</summary>
    public Guid Id { get; }

    /// <summary>When the notification was created (UTC).</summary>
    public DateTimeOffset CreatedUtc { get; }

    /// <summary>
    /// When the notification was dismissed (UTC); set as it moves to history.
    /// </summary>
    public DateTimeOffset? DismissedUtc { get; set; }

    /// <summary>
    /// <see langword="true"/> when the notification was created as persistent
    /// (no implicit auto-dismiss). Informational; the service tracks the live
    /// auto-dismiss schedule separately.
    /// </summary>
    public bool IsPersistent { get; }

    /// <summary>Severity, driving accent colour, icon, and accessibility label.</summary>
    public NotificationSeverity Severity
    {
        get => _severity;
        set
        {
            if (SetProperty(ref _severity, value))
            {
                OnPropertyChanged(nameof(IsInfo));
                OnPropertyChanged(nameof(IsSuccess));
                OnPropertyChanged(nameof(IsWarning));
                OnPropertyChanged(nameof(IsError));
            }
        }
    }

    /// <summary>The notification title (caller-localized).</summary>
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    /// <summary>Optional body text (caller-localized).</summary>
    public string? Message
    {
        get => _message;
        set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
                // Re-evaluate on mutation: collapse and let the host recompute
                // truncation against the new text on the next layout pass.
                IsExpanded = false;
                OnPropertyChanged(nameof(ShowExpandToggle));
            }
        }
    }

    /// <summary><see langword="true"/> when <see cref="Message"/> is non-empty.</summary>
    public bool HasMessage => !string.IsNullOrWhiteSpace(_message);

    /// <summary>
    /// When <see langword="true"/> the full body text is shown (wrapped); when
    /// <see langword="false"/> the body is capped at two lines with an ellipsis.
    /// Toggled by the "Show more" / "Show less" link via
    /// <see cref="ToggleExpandCommand"/>.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(BodyMaxLines));
                OnPropertyChanged(nameof(ToggleExpandLabel));
                OnPropertyChanged(nameof(ShowExpandToggle));
            }
        }
    }

    /// <summary>
    /// Set by the host when the collapsed body text is clipped (more than two
    /// lines). Drives visibility of the "Show more" link.
    /// </summary>
    public bool IsMessageTruncated
    {
        get => _isMessageTruncated;
        set
        {
            if (SetProperty(ref _isMessageTruncated, value))
                OnPropertyChanged(nameof(ShowExpandToggle));
        }
    }

    /// <summary>Maximum body lines: two while collapsed, unlimited while expanded.</summary>
    public int BodyMaxLines => _isExpanded ? 0 : 2;

    /// <summary>
    /// <see langword="true"/> when the expand/collapse link should be shown —
    /// either the collapsed text is clipped, or it is currently expanded (so
    /// the user can collapse it again).
    /// </summary>
    public bool ShowExpandToggle => HasMessage && (_isMessageTruncated || _isExpanded);

    /// <summary>Label for the expand/collapse link.</summary>
    public string ToggleExpandLabel =>
        _isExpanded ? Strings.Notification_ShowLess : Strings.Notification_ShowMore;

    /// <summary>Toggles <see cref="IsExpanded"/>.</summary>
    public ICommand ToggleExpandCommand { get; }

    /// <summary><see langword="true"/> when the progress region is shown.</summary>
    public bool HasProgress
    {
        get => _hasProgress;
        set => SetProperty(ref _hasProgress, value);
    }

    /// <summary>Progress value in the range 0..1 (ignored while indeterminate).</summary>
    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, Math.Clamp(value, 0d, 1d));
    }

    /// <summary>When <see langword="true"/> the progress bar animates without a value.</summary>
    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set => SetProperty(ref _isIndeterminate, value);
    }

    /// <summary>Action buttons rendered on the card; empty when there are none.</summary>
    public ObservableCollection<NotificationActionViewModel> Actions { get; }

    /// <summary><see langword="true"/> when there is at least one action button.</summary>
    public bool HasActions => Actions.Count > 0;

    /// <summary>Caller-supplied custom content hosted by the card, if any.</summary>
    public object? CustomContent
    {
        get => _customContent;
        set
        {
            if (SetProperty(ref _customContent, value))
                OnPropertyChanged(nameof(HasCustomContent));
        }
    }

    /// <summary><see langword="true"/> when <see cref="CustomContent"/> is set.</summary>
    public bool HasCustomContent => _customContent is not null;

    /// <summary><see langword="true"/> when <see cref="Severity"/> is <see cref="NotificationSeverity.Info"/>.</summary>
    public bool IsInfo => Severity == NotificationSeverity.Info;

    /// <summary><see langword="true"/> when <see cref="Severity"/> is <see cref="NotificationSeverity.Success"/>.</summary>
    public bool IsSuccess => Severity == NotificationSeverity.Success;

    /// <summary><see langword="true"/> when <see cref="Severity"/> is <see cref="NotificationSeverity.Warning"/>.</summary>
    public bool IsWarning => Severity == NotificationSeverity.Warning;

    /// <summary><see langword="true"/> when <see cref="Severity"/> is <see cref="NotificationSeverity.Error"/>.</summary>
    public bool IsError => Severity == NotificationSeverity.Error;

    /// <summary>Closes the notification (close-button / user dismissal).</summary>
    public ICommand CloseCommand { get; }

    /// <summary>
    /// Raised when the card requests its own dismissal (close button, or an
    /// action with <see cref="NotificationActionDescriptor.DismissOnInvoke"/>).
    /// The owning service handles removal and history bookkeeping.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>Replaces the current action buttons with a new set.</summary>
    /// <param name="actions">The new action descriptors (may be empty).</param>
    public void SetActions(IEnumerable<NotificationActionDescriptor> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        Actions.Clear();
        foreach (var action in actions)
            Actions.Add(new NotificationActionViewModel(action, RequestClose));
    }

    /// <summary>Raises <see cref="CloseRequested"/>.</summary>
    private void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
