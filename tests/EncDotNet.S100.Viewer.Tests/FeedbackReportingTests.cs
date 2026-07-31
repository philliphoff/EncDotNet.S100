using System.IO.Compression;
using System.Text;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers the feedback reporter's pure logic: the diagnostic report
/// serialisation, the GitHub issue-URL builder, and the on-disk bundle
/// writer. UI presentation (the dialog) is exercised manually.
/// </summary>
public class FeedbackReportingTests
{
    private static FeedbackReport SampleReport(bool withViewport = true, bool withError = true) =>
        new()
        {
            GeneratedUtc = new DateTimeOffset(2026, 6, 14, 12, 0, 0, TimeSpan.Zero),
            Application = new FeedbackAppInfo("S-100 Viewer", "1.2.3", "Dark", "Day"),
            Runtime = new FeedbackRuntimeInfo("macOS 15", "Arm64", ".NET 10.0", "en-US"),
            Viewport = withViewport
                ? new FeedbackViewportInfo(50.0, -5.0, 51.0, -4.0)
                : null,
            Datasets = new[]
            {
                new FeedbackDatasetInfo("US5MA1BO", "S-101", true, 0, 2),
            },
            LastError = withError
                ? new FeedbackErrorInfo(
                    new DateTimeOffset(2026, 6, 14, 11, 59, 0, TimeSpan.Zero),
                    "UIThread.UnhandledException", "System.InvalidOperationException",
                    "boom", "System.InvalidOperationException: boom\n   at X()")
                : null,
        };

    [Fact]
    public void ToJson_IncludesKeyFields()
    {
        var json = SampleReport().ToJson();

        Assert.Contains("\"Version\": \"1.2.3\"", json);
        Assert.Contains("\"Palette\": \"Day\"", json);
        Assert.Contains("US5MA1BO", json);
        Assert.Contains("\"ProductSpec\": \"S-101\"", json);
        Assert.Contains("boom", json);
    }

    [Fact]
    public void ToJson_IncludesPreviousCrashes()
    {
        var report = SampleReport() with
        {
            PreviousCrashes = new[]
            {
                new FeedbackCrashInfo(
                    new DateTimeOffset(2026, 6, 14, 10, 0, 0, TimeSpan.Zero), 4242, "1.2.2"),
                new FeedbackCrashInfo(
                    new DateTimeOffset(2026, 6, 14, 11, 0, 0, TimeSpan.Zero), 4343, "1.2.3"),
            },
            CrashLogTail = "[UIThread.UnhandledException] kaboom",
        };

        var json = report.ToJson();

        Assert.Contains("PreviousCrashes", json);
        Assert.Contains("\"Pid\": 4242", json);
        Assert.Contains("\"Pid\": 4343", json);
        Assert.Contains("CrashLogTail", json);
        Assert.Contains("kaboom", json);
    }

    [Fact]
    public void ToJson_OmitsCrashLogTailWhenNoCrash()
    {
        // Default report has an empty crash list and a null tail.
        var json = SampleReport().ToJson();

        Assert.DoesNotContain("CrashLogTail", json);
    }

    [Fact]
    public void ToJson_OmitsNullSections()
    {
        var json = SampleReport(withViewport: false, withError: false).ToJson();

        Assert.DoesNotContain("Viewport", json);
        Assert.DoesNotContain("LastError", json);
    }

    [Fact]
    public void BuildIssueUrl_TargetsFeedbackFormAndPrefillsFields()
    {
        var report = SampleReport();
        var json = report.ToJson();
        var url = FeedbackService.BuildIssueUrl(
            report, "Depth labels overlap", json,
            "/tmp/S100ViewerFeedback/feedback-x-screenshot.png",
            hasScreenshot: true, screenshotOnClipboard: true);

        Assert.StartsWith("https://github.com/philliphoff/EncDotNet.S100/issues/new?", url);
        // Targets the slim, user-friendly feedback form (blank issues are disabled).
        Assert.Contains("template=feedback.yml", url);
        // The user's words land in the feedback field, URL-encoded.
        Assert.Contains("feedback=", url);
        Assert.Contains(Uri.EscapeDataString("Depth labels overlap"), url);
        // Diagnostics JSON rides in the auto-collected field.
        Assert.Contains("diagnostics=", url);
        Assert.Contains(Uri.EscapeDataString("US5MA1BO"), url);
        // Title uses the feedback prefix.
        Assert.Contains(Uri.EscapeDataString("[Feedback]: Depth labels overlap"), url);
    }

    [Fact]
    public void BuildIssueUrl_OffersClipboardPasteAndDragFallback()
    {
        var report = SampleReport();
        var json = report.ToJson();
        var url = FeedbackService.BuildIssueUrl(
            report, "hi", json, "/tmp/S100ViewerFeedback/feedback-y-screenshot.png",
            hasScreenshot: true, screenshotOnClipboard: true);

        Assert.Contains("screenshot=", url);
        // Mentions paste (clipboard succeeded) and the drag-drop fallback file.
        Assert.Contains(Uri.EscapeDataString("pasting"), url);
        Assert.Contains(Uri.EscapeDataString("feedback-y-screenshot.png"), url);
    }

    [Fact]
    public void BuildIssueUrl_PointsToFileWhenScreenshotNotOnClipboard()
    {
        var report = SampleReport();
        var json = report.ToJson();
        var url = FeedbackService.BuildIssueUrl(
            report, "hi", json, "/tmp/S100ViewerFeedback/feedback-z-screenshot.png",
            hasScreenshot: true, screenshotOnClipboard: false);

        Assert.Contains("screenshot=", url);
        Assert.Contains(Uri.EscapeDataString("feedback-z-screenshot.png"), url);
        // Always steers the user to the reliable drag-and-drop path.
        Assert.Contains(Uri.EscapeDataString("drag"), url);
    }

    [Fact]
    public void BuildIssueUrl_TruncatesLongDiagnostics()
    {
        var report = SampleReport();
        var bigJson = new string('x', 20_000);
        var url = FeedbackService.BuildIssueUrl(
            report, "hi", bigJson, "/tmp/b.zip", hasScreenshot: false, screenshotOnClipboard: false);

        Assert.Contains(Uri.EscapeDataString("truncated"), url);
        // Even with a huge report, the URL stays within a sane bound.
        Assert.True(url.Length < 16_000, $"URL unexpectedly long: {url.Length}");
    }

    [Fact]
    public void WriteArtifacts_WritesBundleAndStandaloneScreenshot()
    {
        var json = SampleReport().ToJson();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        var (bundlePath, screenshotPath) = FeedbackService.WriteArtifacts(json, "please fix", png);
        try
        {
            Assert.True(File.Exists(bundlePath));
            Assert.NotNull(screenshotPath);
            Assert.True(File.Exists(screenshotPath));
            // The loose PNG carries the raw bytes (the reliable drag source).
            Assert.Equal(png, File.ReadAllBytes(screenshotPath!));
            Assert.EndsWith("-screenshot.png", screenshotPath);
        }
        finally
        {
            File.Delete(bundlePath);
            if (screenshotPath is not null)
                File.Delete(screenshotPath);
        }
    }

    [Fact]
    public void WriteArtifacts_OmitsStandaloneScreenshotWhenNull()
    {
        var (bundlePath, screenshotPath) = FeedbackService.WriteArtifacts("{}", "", screenshotPng: null);
        try
        {
            Assert.True(File.Exists(bundlePath));
            Assert.Null(screenshotPath);
        }
        finally
        {
            File.Delete(bundlePath);
        }
    }

    [Fact]
    public void WriteBundle_WritesJsonMessageAndScreenshot()
    {
        var json = SampleReport().ToJson();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        var path = FeedbackService.WriteBundle(json, "please fix", png);
        try
        {
            Assert.True(File.Exists(path));
            using var archive = ZipFile.OpenRead(path);

            var names = archive.Entries.Select(e => e.FullName).ToArray();
            Assert.Contains("diagnostics.json", names);
            Assert.Contains("feedback.txt", names);
            Assert.Contains("screenshot.png", names);

            using var reader = new StreamReader(
                archive.GetEntry("diagnostics.json")!.Open(), Encoding.UTF8);
            Assert.Contains("US5MA1BO", reader.ReadToEnd());

            using var msg = new StreamReader(
                archive.GetEntry("feedback.txt")!.Open(), Encoding.UTF8);
            Assert.Equal("please fix", msg.ReadToEnd());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteBundle_OmitsScreenshotWhenNull()
    {
        var path = FeedbackService.WriteBundle("{}", "", screenshotPng: null);
        try
        {
            using var archive = ZipFile.OpenRead(path);
            Assert.DoesNotContain("screenshot.png", archive.Entries.Select(e => e.FullName));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
