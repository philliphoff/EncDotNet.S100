using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IFeedbackService"/>. Gathers diagnostics from the
/// live view-models/services, captures an application screenshot, writes
/// a feedback bundle (zip) to a temp folder, and opens a prefilled
/// GitHub new-issue page.
/// </summary>
internal sealed class FeedbackService : IFeedbackService
{
    /// <summary>
    /// Target repository for feedback issues, in <c>owner/repo</c> form.
    /// </summary>
    private const string GitHubRepository = "philliphoff/EncDotNet.S100";

    /// <summary>
    /// Soft cap on the diagnostics JSON embedded in the issue body. URLs
    /// over ~8 KB are rejected by some browsers/servers, so the full data
    /// always lives in the saved bundle and only a trimmed copy is inlined.
    /// </summary>
    private const int MaxInlineJsonChars = 4000;

    private readonly DatasetsViewModel _datasets;
    private readonly IMapViewportNotifier _viewport;
    private readonly ILastErrorTracker _errors;
    private readonly IThemeService _theme;
    private readonly SettingsViewModel _settings;
    private readonly IAppScreenshotProvider _screenshot;
    private readonly IMapHostAccessor _mapHostAccessor;

    public FeedbackService(
        DatasetsViewModel datasets,
        IMapViewportNotifier viewport,
        ILastErrorTracker errors,
        IThemeService theme,
        SettingsViewModel settings,
        IAppScreenshotProvider screenshot,
        IMapHostAccessor mapHostAccessor)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(screenshot);
        ArgumentNullException.ThrowIfNull(mapHostAccessor);

        _datasets = datasets;
        _viewport = viewport;
        _errors = errors;
        _theme = theme;
        _settings = settings;
        _screenshot = screenshot;
        _mapHostAccessor = mapHostAccessor;
    }

    /// <inheritdoc />
    public async Task<FeedbackCollectResult> CollectAsync(CancellationToken cancellationToken = default)
    {
        var report = BuildReport();
        var screenshot = await CaptureScreenshotAsync(cancellationToken).ConfigureAwait(false);
        return new FeedbackCollectResult(report, screenshot);
    }

    /// <summary>
    /// Builds the textual diagnostic snapshot. Pure and synchronous so it
    /// can be unit-tested and called on the UI thread (it only reads
    /// view-model state).
    /// </summary>
    public FeedbackReport BuildReport()
    {
        var assembly = typeof(FeedbackService).Assembly;
        var version =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        var app = new FeedbackAppInfo(
            Name: Strings.Window_Title,
            Version: version,
            Theme: _theme.Current.ToString(),
            Palette: _settings.SelectedPalette.ToString());

        var runtime = new FeedbackRuntimeInfo(
            OperatingSystem: RuntimeInformation.OSDescription.Trim(),
            Architecture: RuntimeInformation.ProcessArchitecture.ToString(),
            FrameworkDescription: RuntimeInformation.FrameworkDescription.Trim(),
            Culture: CultureInfo.CurrentUICulture.Name);

        FeedbackViewportInfo? viewport = null;
        if (_viewport.Current is { } v)
        {
            viewport = new FeedbackViewportInfo(
                MinLatitude: v.MinLatitude,
                MinLongitude: v.MinLongitude,
                MaxLatitude: v.MaxLatitude,
                MaxLongitude: v.MaxLongitude);
        }

        var datasets = new List<FeedbackDatasetInfo>();
        foreach (var entry in _datasets.Entries)
        {
            datasets.Add(new FeedbackDatasetInfo(
                DisplayName: entry.DisplayName,
                ProductSpec: entry.ProductSpec,
                IsVisible: entry.IsVisible,
                ValidationErrorCount: entry.ValidationErrorCount,
                ValidationWarningCount: entry.ValidationWarningCount));
        }

        FeedbackErrorInfo? lastError = null;
        if (_errors.Current is { } e)
        {
            lastError = new FeedbackErrorInfo(
                TimestampUtc: e.TimestampUtc,
                Source: e.Source,
                ExceptionType: e.ExceptionType,
                Message: e.Message,
                StackTrace: e.StackTrace);
        }

        return new FeedbackReport
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Application = app,
            Runtime = runtime,
            Viewport = viewport,
            Datasets = datasets,
            LastError = lastError,
        };
    }

    private async Task<byte[]?> CaptureScreenshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            var window = await _screenshot.CapturePngAsync(cancellationToken).ConfigureAwait(false);
            if (window is { Length: > 0 })
                return window;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Fall through to the map-only snapshot.
        }

        // Fallback: capture just the map if the window could not be rendered.
        var host = _mapHostAccessor.Current;
        if (host is null)
            return null;

        try
        {
            return await host.RenderCurrentViewToPngAsync(1280, 800, 1.0, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<FeedbackSubmitResult> SubmitAsync(
        FeedbackSubmitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var json = request.Report.ToJson();
        var userMessage = request.UserMessage?.Trim() ?? string.Empty;

        var hasScreenshot = request.ScreenshotPng is { Length: > 0 };

        var (bundlePath, screenshotPath) = await Task.Run(
            () => WriteArtifacts(json, userMessage, request.ScreenshotPng),
            cancellationToken).ConfigureAwait(false);

        // Place the screenshot on the clipboard so the user can paste it
        // straight into the GitHub issue (a URL cannot embed an image).
        // Pasting is convenient but flaky: GitHub's browser paste-to-upload
        // frequently reports "failed to upload image.png" even for a valid
        // PNG, whereas dragging the standalone file in is reliable. The
        // revealed screenshot.png below is the dependable fallback.
        var screenshotOnClipboard = false;
        if (hasScreenshot)
        {
            screenshotOnClipboard = await TryCopyImageToClipboardAsync(
                request.ScreenshotPng!, cancellationToken).ConfigureAwait(false);
        }

        var issueUrl = BuildIssueUrl(
            request.Report, userMessage, json, screenshotPath,
            hasScreenshot, screenshotOnClipboard);

        OpenInBrowser(issueUrl);

        // Reveal the standalone screenshot so it is ready to drag into the
        // issue; fall back to the bundle when there is no screenshot.
        RevealInFileManager(screenshotPath ?? bundlePath);

        return new FeedbackSubmitResult(bundlePath, issueUrl, screenshotOnClipboard, screenshotPath);
    }

    /// <summary>
    /// Writes the on-disk feedback artifacts: the diagnostics/message/image
    /// zip bundle and, when a screenshot is included, a standalone
    /// <c>screenshot.png</c> next to it. The standalone file exists because
    /// GitHub's clipboard paste upload is unreliable; a loose PNG can be
    /// dragged straight into the issue form, which always succeeds.
    /// </summary>
    /// <returns>The bundle path and the standalone screenshot path (or
    /// <see langword="null"/> when no screenshot was provided).</returns>
    internal static (string BundlePath, string? ScreenshotPath) WriteArtifacts(
        string json, string userMessage, byte[]? screenshotPng)
    {
        var bundlePath = WriteBundle(json, userMessage, screenshotPng);

        string? screenshotPath = null;
        if (screenshotPng is { Length: > 0 })
        {
            // Mirror the bundle's stamp so the two files sort together.
            var fileName = Path.GetFileNameWithoutExtension(bundlePath);
            screenshotPath = Path.Combine(
                Path.GetDirectoryName(bundlePath)!, $"{fileName}-screenshot.png");
            File.WriteAllBytes(screenshotPath, screenshotPng);
        }

        return (bundlePath, screenshotPath);
    }

    internal static string WriteBundle(string json, string userMessage, byte[]? screenshotPng)
    {
        var folder = Path.Combine(Path.GetTempPath(), "S100ViewerFeedback");
        Directory.CreateDirectory(folder);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var bundlePath = Path.Combine(folder, $"feedback-{stamp}.zip");

        using var archive = ZipFile.Open(bundlePath, ZipArchiveMode.Create);

        var jsonEntry = archive.CreateEntry("diagnostics.json", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(jsonEntry.Open(), new UTF8Encoding(false)))
            writer.Write(json);

        var messageEntry = archive.CreateEntry("feedback.txt", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(messageEntry.Open(), new UTF8Encoding(false)))
            writer.Write(string.IsNullOrWhiteSpace(userMessage)
                ? "(no message provided)"
                : userMessage);

        if (screenshotPng is { Length: > 0 })
        {
            var imageEntry = archive.CreateEntry("screenshot.png", CompressionLevel.Optimal);
            using var imageStream = imageEntry.Open();
            imageStream.Write(screenshotPng, 0, screenshotPng.Length);
        }

        return bundlePath;
    }

    /// <summary>
    /// Builds a GitHub URL that opens the repository's slim, user-friendly
    /// <c>feedback.yml</c> issue form with its fields pre-populated. Blank
    /// issues are disabled on the repo, so targeting a form by template name
    /// (and prefilling by field id) is the only way to land the user on a form
    /// that already contains the details. The full technical payload rides in
    /// the auto-collected "diagnostics" field rather than a developer-oriented
    /// bug-report layout.
    /// </summary>
    internal static string BuildIssueUrl(
        FeedbackReport report,
        string userMessage,
        string json,
        string? screenshotPath,
        bool hasScreenshot,
        bool screenshotOnClipboard)
    {
        _ = report;

        var inlineJson = json.Length > MaxInlineJsonChars
            ? json[..MaxInlineJsonChars] + "\n… (truncated — see diagnostics.json in the bundle)"
            : json;
        var diagnostics = "```json\n" + inlineJson + "\n```";

        // GitHub's clipboard paste-to-upload is flaky ("failed to upload
        // image.png") even for a valid PNG, so the prefilled prompt always
        // points the user at the standalone file revealed in their file
        // manager — dragging it in is the reliable path. When the clipboard
        // copy succeeded we still offer paste first as the quicker option.
        var screenshot = string.Empty;
        if (hasScreenshot)
        {
            var fileName = screenshotPath is null
                ? "screenshot.png"
                : Path.GetFileName(screenshotPath);

            var builder = new StringBuilder();
            if (screenshotOnClipboard)
                builder.Append("A screenshot is on your clipboard — try pasting it here with ⌘V / Ctrl+V. ");
            builder.Append("If the upload fails, drag ")
                .Append(fileName)
                .Append(" into this field instead");
            if (screenshotPath is not null)
                builder.Append(" (it was just revealed in your file manager: ").Append(screenshotPath).Append(')');
            builder.Append(" — uploading the file is more reliable than pasting.");
            screenshot = builder.ToString();
        }

        var fields = new Dictionary<string, string>
        {
            ["title"] = BuildIssueTitle(userMessage),
            ["feedback"] = string.IsNullOrWhiteSpace(userMessage) ? string.Empty : userMessage,
            ["screenshot"] = screenshot,
            ["diagnostics"] = diagnostics,
        };

        var query = new StringBuilder($"https://github.com/{GitHubRepository}/issues/new?template=feedback.yml");
        foreach (var (key, value) in fields)
        {
            if (string.IsNullOrEmpty(value))
                continue;
            query.Append('&').Append(key).Append('=').Append(Uri.EscapeDataString(value));
        }

        return query.ToString();
    }

    private static string BuildIssueTitle(string userMessage)
    {
        const string prefix = "[Feedback]: ";
        if (string.IsNullOrWhiteSpace(userMessage))
            return prefix;

        var firstLine = userMessage;
        var newline = userMessage.IndexOf('\n');
        if (newline >= 0)
            firstLine = userMessage[..newline];
        firstLine = firstLine.Trim();

        const int maxTitle = 80;
        if (firstLine.Length > maxTitle - prefix.Length)
            firstLine = firstLine[..(maxTitle - prefix.Length - 1)] + "…";

        return prefix + firstLine;
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Best-effort: the bundle path is still surfaced to the user.
        }
    }

    private static void RevealInFileManager(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", new[] { "-R", path });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                {
                    UseShellExecute = true,
                });
            }
            else
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Process.Start(new ProcessStartInfo("xdg-open", dir) { UseShellExecute = true });
            }
        }
        catch
        {
            // Best-effort.
        }
    }

    /// <summary>
    /// Best-effort copy of a PNG image onto the system clipboard using the
    /// platform's native tool, so the user can paste the screenshot directly
    /// into the GitHub issue. Returns <see langword="false"/> when the
    /// platform tool is unavailable or fails.
    /// </summary>
    private static async Task<bool> TryCopyImageToClipboardAsync(
        byte[] png,
        CancellationToken cancellationToken)
    {
        string? tempPng = null;
        try
        {
            tempPng = Path.Combine(Path.GetTempPath(), $"s100-feedback-{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(tempPng, png, cancellationToken).ConfigureAwait(false);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // AppleScript reads the file as PNG data onto the pasteboard.
                var script =
                    $"set the clipboard to (read (POSIX file \"{tempPng}\") as «class PNGf»)";
                return await RunAsync("osascript", new[] { "-e", script }, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var ps =
                    "Add-Type -AssemblyName System.Windows.Forms,System.Drawing; " +
                    $"$img = [System.Drawing.Image]::FromFile('{tempPng}'); " +
                    "[System.Windows.Forms.Clipboard]::SetImage($img); $img.Dispose()";
                return await RunAsync(
                    "powershell",
                    new[] { "-NoProfile", "-STA", "-Command", ps },
                    cancellationToken).ConfigureAwait(false);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Prefer Wayland's wl-copy, fall back to X11's xclip.
                if (await RunAsync("wl-copy", new[] { "--type", "image/png", tempPng }, cancellationToken)
                        .ConfigureAwait(false))
                    return true;
                return await RunAsync(
                    "xclip",
                    new[] { "-selection", "clipboard", "-t", "image/png", "-i", tempPng },
                    cancellationToken).ConfigureAwait(false);
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (tempPng is not null)
            {
                try { File.Delete(tempPng); } catch { /* best-effort */ }
            }
        }
    }

    private static async Task<bool> RunAsync(string fileName, string[] arguments, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            foreach (var arg in arguments)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process is null)
                return false;

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}
