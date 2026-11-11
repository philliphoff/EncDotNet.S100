using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Notifications;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Opening datasets from the window: Open Recent, which replays a dataset or
/// exchange set the user loaded before, and the in-flight exchange-set load
/// with its Cancel command.
/// </summary>
internal sealed class DatasetOpeningViewModel : ViewModelBase
{
    private readonly IRecentFilesService _recentFiles;
    private readonly INotificationService _notifications;
    private readonly DatasetsViewModel _datasets;
    private readonly ShellLayoutViewModel _layout;

    public DatasetOpeningViewModel(
        IRecentFilesService recentFiles,
        INotificationService notifications,
        DatasetsViewModel datasets,
        ShellLayoutViewModel layout)
    {
        ArgumentNullException.ThrowIfNull(recentFiles);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(layout);

        _recentFiles = recentFiles;
        _notifications = notifications;
        _datasets = datasets;
        _layout = layout;

        var cancel = new RelayCommand(
            () => _exchangeSetCts?.Cancel(),
            () => IsExchangeSetLoading);
        CancelExchangeSetCommand = cancel;

        // Re-evaluate Cancel command when the loading flag changes so the
        // overlay button enables/disables correctly.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsExchangeSetLoading))
                cancel.NotifyCanExecuteChanged();
        };

        OpenRecentCommand = new AsyncRelayCommand<string>(OpenRecentAsync);
    }

    private bool _isExchangeSetLoading;
    private System.Threading.CancellationTokenSource? _exchangeSetCts;

    /// <summary>
    /// True while an exchange-set load is in flight. Gates the
    /// <see cref="CancelExchangeSetCommand"/>; progress itself is surfaced
    /// through the notification system, not a modal overlay.
    /// </summary>
    public bool IsExchangeSetLoading
    {
        get => _isExchangeSetLoading;
        private set => SetProperty(ref _isExchangeSetLoading, value);
    }

    /// <summary>Cancels the in-flight exchange-set load. No-op if idle.</summary>
    public ICommand CancelExchangeSetCommand { get; }

    /// <summary>
    /// Called by <see cref="MainWindow"/> when an exchange-set load is
    /// about to start. Captures the cancellation source and marks the
    /// load as in flight.
    /// </summary>
    internal System.Threading.CancellationToken BeginExchangeSetLoad(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        _exchangeSetCts?.Dispose();
        _exchangeSetCts = new System.Threading.CancellationTokenSource();

        IsExchangeSetLoading = true;
        return _exchangeSetCts.Token;
    }

    /// <summary>
    /// Called by <see cref="MainWindow"/> when an exchange-set load
    /// completes (or is cancelled / fatally fails). Clears the in-flight
    /// flag. Progress and outcome (success / partial / failure) are
    /// surfaced by <see cref="Services.IExchangeSetService"/> through the
    /// notification service.
    /// </summary>
    internal void EndExchangeSetLoad(ExchangeSetOpenResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        IsExchangeSetLoading = false;
    }

    /// <summary>
    /// Opens a dataset that the user has previously loaded. If the file is
    /// no longer at the recorded path the entry is dropped from the recent
    /// list and a status message is shown.
    /// </summary>
    public IAsyncRelayCommand<string> OpenRecentCommand { get; }

    /// <summary>
    /// Opens an exchange set (folder, ZIP or <c>CATALOG.031</c>) with the
    /// window's progress UI; used by Open Recent to replay an exchange set.
    /// Set by the main window. Issue #655.
    /// </summary>
    internal Func<string, Task>? ExchangeSetOpener { get; set; }

    private async Task OpenRecentAsync(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            _notifications.Create(Strings.Toast_Warning)
                .WithSeverity(NotificationSeverity.Warning)
                .WithContent(string.Format(
                    Strings.Status_FileNoLongerExists,
                    Services.Notifications.NotificationFormat.ShortenPath(path)))
                .Show();
            // Drop the missing entry so the menu reflects reality.
            _recentFiles.Remove(path);
            return;
        }

        _layout.SelectDefaultTab();
        if (ExchangeSetOpener is { } openExchangeSet && IsExchangeSetPath(path))
        {
            await openExchangeSet(path);
            return;
        }

        await _datasets.LoadFromPathAsync(path);
    }

    /// <summary>
    /// True for a path Open Recent should replay as an exchange set: a folder,
    /// an exchange-set ZIP, or a <c>CATALOG.031</c>.
    /// </summary>
    internal static bool IsExchangeSetPath(string path) =>
        Directory.Exists(path)
        || (ExchangeSetDetection.IsZipPath(path) && ExchangeSetDetection.LooksLikeExchangeSetZip(path))
        || ExchangeSetDetection.IsS57CataloguePath(path);
}
