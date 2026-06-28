using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EncDotNet.S100.Viewer.Services.Updates;

/// <summary>
/// Metadata for a single published GitHub release, as consumed by the
/// update checker. A trimmed projection of the GitHub Releases REST API
/// (<c>GET /repos/{owner}/{repo}/releases/latest</c>).
/// </summary>
/// <param name="TagName">The release tag (e.g. <c>"v2.5.0"</c>).</param>
/// <param name="Name">The release display name, when set.</param>
/// <param name="HtmlUrl">The browser URL of the release page.</param>
/// <param name="Body">The raw markdown release notes.</param>
/// <param name="PublishedAt">When the release was published, in UTC.</param>
/// <param name="IsPrerelease">Whether GitHub flagged this as a pre-release.</param>
/// <param name="LargestAssetBytes">
/// The size of the largest attached asset in bytes, used as an approximate
/// download size, or <see langword="null"/> when no assets are attached.
/// </param>
internal sealed record GitHubRelease(
    string TagName,
    string? Name,
    string? HtmlUrl,
    string? Body,
    DateTimeOffset? PublishedAt,
    bool IsPrerelease,
    long? LargestAssetBytes);

/// <summary>
/// Fetches release metadata from GitHub. Abstracted so the update logic can
/// be unit-tested without network access.
/// </summary>
internal interface IGitHubReleaseClient
{
    /// <summary>
    /// Gets the latest published, non-pre-release release for the configured
    /// repository, or <see langword="null"/> when none exists or the request
    /// fails (callers must treat failure as "no update information").
    /// </summary>
    Task<GitHubRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The outcome category of an update check.
/// </summary>
internal enum UpdateAvailability
{
    /// <summary>Checks are disabled (user setting) or this is a dev build.</summary>
    Disabled,

    /// <summary>The running version is the latest known release.</summary>
    UpToDate,

    /// <summary>A newer release is available (and not skipped).</summary>
    UpdateAvailable,

    /// <summary>The check could not complete (offline / API error).</summary>
    CheckFailed,
}

/// <summary>
/// The result of an update check, including the latest release when one was
/// found. Immutable so it can be handed straight to the About view-model.
/// </summary>
internal sealed record UpdateStatus
{
    /// <summary>The outcome category.</summary>
    public required UpdateAvailability Availability { get; init; }

    /// <summary>
    /// When the check completed, or <see langword="null"/> for a check that
    /// never ran (e.g. <see cref="UpdateAvailability.Disabled"/>).
    /// </summary>
    public DateTimeOffset? CheckedAtUtc { get; init; }

    /// <summary>The latest release, when one was retrieved.</summary>
    public GitHubRelease? LatestRelease { get; init; }

    /// <summary>
    /// The latest release version (tag without the leading <c>v</c>), when
    /// <see cref="Availability"/> is <see cref="UpdateAvailability.UpdateAvailable"/>.
    /// </summary>
    public string? LatestVersion { get; init; }

    /// <summary>
    /// True when the user chose to "skip" this version: the About dialog still
    /// shows the update truthfully, but proactive notifications (e.g. toasts)
    /// for this version are suppressed.
    /// </summary>
    public bool IsSkipped { get; init; }

    /// <summary>A disabled result (checks off or dev build).</summary>
    public static UpdateStatus Disabled { get; } =
        new() { Availability = UpdateAvailability.Disabled };
}

/// <summary>
/// Determines whether a newer release of the viewer is available, applying
/// the user's "skip this version" choice and de-duplicating checks within a
/// throttle window. Pure orchestration over <see cref="IGitHubReleaseClient"/>
/// and <see cref="IAppVersionProvider"/>; no UI or network concerns.
/// </summary>
internal interface IUpdateService
{
    /// <summary>
    /// Checks for a newer release. When <paramref name="force"/> is
    /// <see langword="false"/> the check is skipped (and the cached status
    /// returned) if a check ran within the throttle window. Network/parse
    /// failures resolve to <see cref="UpdateAvailability.CheckFailed"/> —
    /// the method never throws for those.
    /// </summary>
    Task<UpdateStatus> CheckForUpdatesAsync(bool force, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a "skip this version" choice so the user is not prompted for
    /// <paramref name="version"/> again, while still being notified about any
    /// later release.
    /// </summary>
    void SkipVersion(string version);

    /// <summary>Enables or disables automatic update checks.</summary>
    void SetUpdateChecksEnabled(bool enabled);

    /// <summary>Whether automatic update checks are currently enabled.</summary>
    bool UpdateChecksEnabled { get; }
}

/// <summary>
/// Default <see cref="IUpdateService"/>. Compares the running version against
/// the latest GitHub release, honours the persisted skip/enable settings, and
/// throttles network checks to at most once per <see cref="ThrottleWindow"/>.
/// </summary>
internal sealed class UpdateService : IUpdateService
{
    /// <summary>Minimum spacing between non-forced network checks.</summary>
    public static readonly TimeSpan ThrottleWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// When set to <c>1</c>, forces a live update check even for development
    /// builds, ignoring the <see cref="AppVersionInfo.IsDevelopmentBuild"/>
    /// gate. Intended only for manual/agent verification of the dialog against
    /// the real GitHub API; never set in shipped builds.
    /// </summary>
    private const string ForceCheckEnvVar = "S100_UPDATE_FORCE";

    private readonly IGitHubReleaseClient _releaseClient;
    private readonly IAppVersionProvider _versionProvider;
    private readonly ViewerSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly bool _forceCheck =
        Environment.GetEnvironmentVariable(ForceCheckEnvVar) == "1";
    private UpdateStatus? _lastStatus;

    public UpdateService(
        IGitHubReleaseClient releaseClient,
        IAppVersionProvider versionProvider,
        ViewerSettings settings,
        TimeProvider timeProvider)
    {
        _releaseClient = releaseClient ?? throw new ArgumentNullException(nameof(releaseClient));
        _versionProvider = versionProvider ?? throw new ArgumentNullException(nameof(versionProvider));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public bool UpdateChecksEnabled => _settings.UpdateCheckEnabled;

    /// <inheritdoc />
    public async Task<UpdateStatus> CheckForUpdatesAsync(bool force, CancellationToken cancellationToken = default)
    {
        var current = _versionProvider.Current;

        // No meaningful comparison for an unversioned local build, unless the
        // developer/agent opts in via S100_UPDATE_FORCE to exercise the live check.
        if ((current.IsDevelopmentBuild && !_forceCheck) || !_settings.UpdateCheckEnabled)
        {
            return _lastStatus = UpdateStatus.Disabled;
        }

        var now = _timeProvider.GetUtcNow();

        // Throttle: reuse the last computed status when a check ran recently,
        // unless the caller forces a fresh check (e.g. "Check now").
        if (!force
            && _lastStatus is { } cached
            && _settings.LastUpdateCheckUtc is { } last
            && now - last < ThrottleWindow)
        {
            return cached;
        }

        GitHubRelease? release;
        try
        {
            release = await _releaseClient.GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return _lastStatus = new UpdateStatus
            {
                Availability = UpdateAvailability.CheckFailed,
                CheckedAtUtc = now,
            };
        }

        _settings.LastUpdateCheckUtc = now;

        if (release is null)
        {
            TrySaveSettings();
            return _lastStatus = new UpdateStatus
            {
                Availability = UpdateAvailability.CheckFailed,
                CheckedAtUtc = now,
            };
        }

        var latestVersion = NormalizeTag(release.TagName);
        _settings.LastKnownLatestVersion = latestVersion;
        TrySaveSettings();

        // A newer release is always reported as "available" in the dialog. The
        // user's "skip" choice only mutes proactive notifications for it, so we
        // surface that as a flag rather than pretending we're up to date.
        var newerThanCurrent = ReleaseVersion.IsNewer(latestVersion, current.Version);
        var isSkipped = !string.IsNullOrEmpty(_settings.SkippedUpdateVersion)
            && !ReleaseVersion.IsNewer(latestVersion, _settings.SkippedUpdateVersion);

        if (newerThanCurrent)
        {
            return _lastStatus = new UpdateStatus
            {
                Availability = UpdateAvailability.UpdateAvailable,
                CheckedAtUtc = now,
                LatestRelease = release,
                LatestVersion = latestVersion,
                IsSkipped = isSkipped,
            };
        }

        return _lastStatus = new UpdateStatus
        {
            Availability = UpdateAvailability.UpToDate,
            CheckedAtUtc = now,
            LatestRelease = release,
            LatestVersion = latestVersion,
        };
    }

    /// <inheritdoc />
    public void SkipVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return;

        _settings.SkippedUpdateVersion = NormalizeTag(version);
        TrySaveSettings();

        // Keep showing the update; just mark it skipped so notifications mute.
        if (_lastStatus is { Availability: UpdateAvailability.UpdateAvailable } status)
        {
            _lastStatus = status with { IsSkipped = true };
        }
    }

    /// <inheritdoc />
    public void SetUpdateChecksEnabled(bool enabled)
    {
        _settings.UpdateCheckEnabled = enabled;
        TrySaveSettings();
        if (!enabled)
            _lastStatus = UpdateStatus.Disabled;
    }

    private static string NormalizeTag(string tag)
    {
        var trimmed = tag.Trim();
        return trimmed.StartsWith('v') || trimmed.StartsWith('V') ? trimmed[1..] : trimmed;
    }

    private void TrySaveSettings()
    {
        try
        {
            _settings.Save();
        }
        catch
        {
            // Best-effort persistence; a failed write must not break the check.
        }
    }
}
