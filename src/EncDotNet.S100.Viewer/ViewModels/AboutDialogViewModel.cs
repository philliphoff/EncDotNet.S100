using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Updates;
using ShadUI;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View-model backing the "About" modal dialog. Surfaces the running
/// application version and drives the GitHub-release update check, including
/// the "skip this version" and "stop checking" choices (issue #379).
/// </summary>
/// <remarks>
/// The dialog is hosted by ShadUI's <see cref="DialogManager"/>; the
/// view-model closes itself through that manager. The update check is kicked
/// off (non-forced) by <see cref="InitializeAsync"/> when the dialog opens.
/// </remarks>
internal sealed class AboutDialogViewModel : ViewModelBase
{
    private readonly DialogManager _dialogManager;
    private readonly IUpdateService _updateService;
    private readonly IAppVersionProvider _versionProvider;
    private readonly IUrlOpener _urlOpener;
    private readonly TimeProvider _timeProvider;

    public AboutDialogViewModel(
        DialogManager dialogManager,
        IUpdateService updateService,
        IAppVersionProvider versionProvider,
        IUrlOpener urlOpener,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(dialogManager);
        ArgumentNullException.ThrowIfNull(updateService);
        ArgumentNullException.ThrowIfNull(versionProvider);
        ArgumentNullException.ThrowIfNull(urlOpener);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _dialogManager = dialogManager;
        _updateService = updateService;
        _versionProvider = versionProvider;
        _urlOpener = urlOpener;
        _timeProvider = timeProvider;

        CheckNowCommand = new AsyncRelayCommand(() => RunCheckAsync(force: true), () => !IsChecking);
        UpdateNowCommand = new RelayCommand(UpdateNow);
        ReleaseNotesCommand = new RelayCommand(OpenReleaseNotes);
        SkipCommand = new RelayCommand(Skip);
        DisableChecksCommand = new RelayCommand(DisableChecks);
        LicenseCommand = new RelayCommand(() => _urlOpener.Open(GitHubReleaseClient.LicenseUrl));
        ThirdPartyNoticesCommand = new RelayCommand(() => _urlOpener.Open(GitHubReleaseClient.ThirdPartyNoticesUrl));
        CloseCommand = new RelayCommand(() => _dialogManager.Close(this));
    }

    // ---- Branding / static product info -----------------------------------

    /// <summary>The product display name shown next to the app icon.</summary>
    public string ProductName => Strings.About_ProductName;

    /// <summary>The one-line product subtitle under the name.</summary>
    public string ProductSubtitle => Strings.About_ProductSubtitle;

    /// <summary>The footer copyright line.</summary>
    public string Copyright =>
        string.Format(CultureInfo.CurrentCulture, Strings.About_CopyrightFormat, _timeProvider.GetUtcNow().Year);

    /// <summary>The "Built with…" attribution paragraph.</summary>
    public string BuiltWith => Strings.About_BuiltWith;

    // ---- Version ----------------------------------------------------------

    /// <summary>The headline "Version X.Y.Z" line.</summary>
    public string VersionLine =>
        string.Format(CultureInfo.CurrentCulture, Strings.About_VersionFormat, _versionProvider.Current.Version);

    /// <summary>
    /// The secondary build line: full informational version plus the build
    /// date when one is embedded (e.g. <c>"build 2.4.1+a1f9c20 · 2026-06-18"</c>).
    /// </summary>
    public string BuildLine
    {
        get
        {
            var info = _versionProvider.Current;
            var core = info.CommitSha is { Length: > 0 } sha
                ? $"{info.Version}+{sha}"
                : info.Version;
            var line = string.Format(CultureInfo.CurrentCulture, Strings.About_BuildFormat, core);
            if (info.BuildDate is { } date)
                line += $" · {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
            return line;
        }
    }

    // ---- Update-check state ----------------------------------------------

    private bool _isChecking;
    /// <summary>True while an update check is in flight (shows a spinner).</summary>
    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (SetProperty(ref _isChecking, value))
            {
                OnPropertyChanged(nameof(ShowUpToDate));
                OnPropertyChanged(nameof(ShowUpdateAvailable));
                OnPropertyChanged(nameof(ShowDisabled));
                OnPropertyChanged(nameof(ShowCheckFailed));
                OnPropertyChanged(nameof(ShowStatusRow));
                OnPropertyChanged(nameof(CanSkip));
                ((AsyncRelayCommand)CheckNowCommand).NotifyCanExecuteChanged();
            }
        }
    }

    private UpdateStatus _status = UpdateStatus.Disabled;
    private UpdateStatus Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged(nameof(ShowUpToDate));
            OnPropertyChanged(nameof(ShowUpdateAvailable));
            OnPropertyChanged(nameof(ShowDisabled));
            OnPropertyChanged(nameof(ShowCheckFailed));
            OnPropertyChanged(nameof(ShowStatusRow));
            OnPropertyChanged(nameof(UpToDateLastChecked));
            OnPropertyChanged(nameof(UpdateAvailableHeader));
            OnPropertyChanged(nameof(UpdateReleasedLine));
            OnPropertyChanged(nameof(IsUpdateSkipped));
            OnPropertyChanged(nameof(CanSkip));
        }
    }

    /// <summary>True when the available update was skipped (notifications muted).</summary>
    public bool IsUpdateSkipped => Status.IsSkipped;

    /// <summary>Whether the Skip action is offered (only for unskipped updates).</summary>
    public bool CanSkip => ShowUpdateAvailable && !Status.IsSkipped;

    /// <summary>Whether the green "up to date" panel is shown.</summary>
    public bool ShowUpToDate => !IsChecking && Status.Availability == UpdateAvailability.UpToDate;

    /// <summary>Whether the blue "update available" panel is shown.</summary>
    public bool ShowUpdateAvailable => !IsChecking && Status.Availability == UpdateAvailability.UpdateAvailable;

    /// <summary>Whether the neutral "checks disabled / dev build" panel is shown.</summary>
    public bool ShowDisabled => !IsChecking && Status.Availability == UpdateAvailability.Disabled;

    /// <summary>Whether the "couldn't check" panel is shown.</summary>
    public bool ShowCheckFailed => !IsChecking && Status.Availability == UpdateAvailability.CheckFailed;

    /// <summary>Whether any status row (spinner or a result panel) is shown.</summary>
    public bool ShowStatusRow => true;

    /// <summary>The "Last checked …" caption under the up-to-date heading.</summary>
    public string UpToDateLastChecked
    {
        get
        {
            if (Status.CheckedAtUtc is { } checkedAt)
                return string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.About_LastCheckedFormat,
                    RelativeTime(checkedAt));
            return Strings.About_LastCheckedNever;
        }
    }

    /// <summary>The "Update available — X.Y.Z" heading.</summary>
    public string UpdateAvailableHeader =>
        string.Format(
            CultureInfo.CurrentCulture,
            Strings.About_UpdateAvailableFormat,
            Status.LatestVersion ?? string.Empty);

    /// <summary>
    /// The "Released … · NN MB" sub-line for the update panel. The size is
    /// omitted when no asset size is known.
    /// </summary>
    public string UpdateReleasedLine
    {
        get
        {
            var release = Status.LatestRelease;
            if (release is null)
                return string.Empty;

            var released = release.PublishedAt is { } published
                ? string.Format(CultureInfo.CurrentCulture, Strings.About_ReleasedFormat, RelativeTime(published))
                : string.Empty;

            if (release.LargestAssetBytes is { } bytes and > 0)
            {
                var size = FormatSize(bytes);
                return released.Length > 0 ? $"{released} · {size}" : size;
            }

            return released;
        }
    }

    // ---- Commands ---------------------------------------------------------

    /// <summary>Forces a fresh update check.</summary>
    public ICommand CheckNowCommand { get; }

    /// <summary>Opens the latest release page so the user can download it (Tier 1).</summary>
    public ICommand UpdateNowCommand { get; }

    /// <summary>Opens the latest release's notes page.</summary>
    public ICommand ReleaseNotesCommand { get; }

    /// <summary>Skips the current release while staying subscribed to later ones.</summary>
    public ICommand SkipCommand { get; }

    /// <summary>Turns automatic update checks off.</summary>
    public ICommand DisableChecksCommand { get; }

    /// <summary>Opens the license in the browser.</summary>
    public ICommand LicenseCommand { get; }

    /// <summary>Opens third-party notices in the browser.</summary>
    public ICommand ThirdPartyNoticesCommand { get; }

    /// <summary>Closes the dialog.</summary>
    public ICommand CloseCommand { get; }

    /// <summary>
    /// Kicks off the initial (non-forced) update check. Call once after the
    /// dialog is shown.
    /// </summary>
    public Task InitializeAsync() => RunCheckAsync(force: false);

    private async Task RunCheckAsync(bool force)
    {
        IsChecking = true;
        try
        {
            Status = await _updateService.CheckForUpdatesAsync(force).ConfigureAwait(true);
        }
        finally
        {
            IsChecking = false;
        }
    }

    private void UpdateNow()
    {
        var url = Status.LatestRelease?.HtmlUrl ?? GitHubReleaseClient.ReleasesPageUrl;
        _urlOpener.Open(url);
    }

    private void OpenReleaseNotes()
    {
        var url = Status.LatestRelease?.HtmlUrl ?? GitHubReleaseClient.ReleasesPageUrl;
        _urlOpener.Open(url);
    }

    private void Skip()
    {
        if (Status.LatestVersion is { Length: > 0 } version)
        {
            _updateService.SkipVersion(version);
            // Stay truthful: keep showing the update, just mark it skipped so
            // future proactive notifications are muted.
            Status = Status with { IsSkipped = true };
        }
    }

    private void DisableChecks()
    {
        _updateService.SetUpdateChecksEnabled(false);
        Status = UpdateStatus.Disabled;
    }

    /// <summary>
    /// Formats a past instant as a coarse, localized "… ago" string relative
    /// to now (just now / minutes / hours / days).
    /// </summary>
    private string RelativeTime(DateTimeOffset instant)
    {
        var delta = _timeProvider.GetUtcNow() - instant;
        if (delta < TimeSpan.Zero)
            delta = TimeSpan.Zero;

        if (delta.TotalMinutes < 1)
            return Strings.About_Time_JustNow;
        if (delta.TotalMinutes < 60)
            return string.Format(CultureInfo.CurrentCulture, Strings.About_Time_MinutesAgo, (int)delta.TotalMinutes);
        if (delta.TotalHours < 24)
            return string.Format(CultureInfo.CurrentCulture, Strings.About_Time_HoursAgo, (int)delta.TotalHours);
        return string.Format(CultureInfo.CurrentCulture, Strings.About_Time_DaysAgo, (int)delta.TotalDays);
    }

    /// <summary>Formats a byte count as a compact MB/KB string.</summary>
    private static string FormatSize(long bytes)
    {
        const double mb = 1024d * 1024d;
        const double kb = 1024d;
        if (bytes >= mb)
            return string.Format(CultureInfo.CurrentCulture, "{0:0.#} MB", bytes / mb);
        return string.Format(CultureInfo.CurrentCulture, "{0:0.#} KB", bytes / kb);
    }
}
