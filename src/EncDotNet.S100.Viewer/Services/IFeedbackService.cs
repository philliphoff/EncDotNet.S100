namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Collects viewer diagnostics for a user feedback report and submits a
/// completed report (local bundle + prefilled GitHub issue).
/// </summary>
internal interface IFeedbackService
{
    /// <summary>
    /// Gathers the current diagnostic snapshot and an optional
    /// application screenshot. Never throws for routine "data missing"
    /// conditions — absent pieces are simply left out.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<FeedbackCollectResult> CollectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a feedback bundle to disk (diagnostics JSON plus the
    /// screenshot when included) and opens a prefilled GitHub new-issue
    /// page in the default browser.
    /// </summary>
    /// <param name="request">The completed report, the user's message, and
    /// the screenshot to include (or <see langword="null"/> to exclude it).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The saved bundle path and the opened issue URL.</returns>
    Task<FeedbackSubmitResult> SubmitAsync(
        FeedbackSubmitRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of <see cref="IFeedbackService.CollectAsync"/>.</summary>
/// <param name="Report">The textual diagnostic snapshot.</param>
/// <param name="ScreenshotPng">PNG bytes of the application/map, or
/// <see langword="null"/> when no screenshot could be captured.</param>
internal sealed record FeedbackCollectResult(
    FeedbackReport Report,
    byte[]? ScreenshotPng);

/// <summary>Input to <see cref="IFeedbackService.SubmitAsync"/>.</summary>
/// <param name="Report">The diagnostic snapshot to include.</param>
/// <param name="UserMessage">The free-form text the user entered.</param>
/// <param name="ScreenshotPng">The screenshot to include, or
/// <see langword="null"/> to exclude it.</param>
internal sealed record FeedbackSubmitRequest(
    FeedbackReport Report,
    string UserMessage,
    byte[]? ScreenshotPng);

/// <summary>Result of <see cref="IFeedbackService.SubmitAsync"/>.</summary>
/// <param name="BundlePath">Absolute path of the saved feedback zip.</param>
/// <param name="IssueUrl">The GitHub new-issue URL that was opened.</param>
/// <param name="ScreenshotOnClipboard">Whether the screenshot PNG was
/// successfully placed on the system clipboard for pasting into the issue.</param>
/// <param name="ScreenshotPath">Absolute path of the standalone
/// <c>screenshot.png</c> written alongside the bundle and revealed in the
/// file manager for drag-and-drop, or <see langword="null"/> when no
/// screenshot was included.</param>
internal sealed record FeedbackSubmitResult(
    string BundlePath,
    string IssueUrl,
    bool ScreenshotOnClipboard,
    string? ScreenshotPath);
