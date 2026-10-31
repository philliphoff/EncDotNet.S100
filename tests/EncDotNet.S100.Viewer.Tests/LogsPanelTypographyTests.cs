using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.VisualTree;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Logs tab's typography (#1171): monospaced, regular-weight rows, short
/// level labels, and an exception box that neither wraps nor shows the home
/// directory.
/// </summary>
public sealed class LogsPanelTypographyTests
{
    private const string Category = "EncDotNet.S100.Viewer.Services.DatasetLoaderService";

    private static (LogBuffer Buffer, LogsViewModel Panel) Create()
    {
        var buffer = new LogBuffer();
        var panel = new LogsViewModel(buffer, showFeedback: null, post: action => action());
        return (buffer, panel);
    }

    [Theory]
    [InlineData(LogLevel.Trace, "Trace", "Trace")]
    [InlineData(LogLevel.Debug, "Debug", "Debug")]
    [InlineData(LogLevel.Information, "Info", "Info")]
    [InlineData(LogLevel.Warning, "Warn", "Warning")]
    [InlineData(LogLevel.Error, "Error", "Error")]
    [InlineData(LogLevel.Critical, "Crit", "Critical")]
    public void Rows_show_short_level_labels_and_keep_the_full_name_for_the_accessible_name(LogLevel level, string shortText, string fullText)
    {
        var buffer = new LogBuffer { MinimumLevel = LogLevel.Trace };
        buffer.Add(level, Category, "message");
        buffer.Flush();

        var row = new LogRowViewModel(Assert.Single(buffer.Snapshot()));
        Assert.Equal(shortText, row.LevelShortText);
        Assert.Equal(fullText, row.LevelText);
        Assert.StartsWith(fullText, row.AccessibleName, StringComparison.Ordinal);
        Assert.Equal(level == LogLevel.Information, row.IsInformation);
    }

    [Fact]
    public void The_exception_splits_into_its_first_line_and_frames_with_the_home_directory_redacted()
    {
        var (buffer, panel) = Create();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.SkipWhen(string.IsNullOrEmpty(home) || home.TrimEnd('/', '\\').Length < 3, "no home directory");
        var path = Path.Combine(home, "Charts", "US5MA1BO.000");
        buffer.Add(LogLevel.Error, Category, $"Could not open {path}", Thrown(new IOException($"Missing {path}")));
        buffer.Flush();

        var row = Assert.Single(panel.Entries);
        Assert.Equal($"System.IO.IOException: Missing {Path.Combine("~", "Charts", "US5MA1BO.000")}", row.ExceptionHeadline);
        Assert.StartsWith(Environment.NewLine + "   at ", row.ExceptionFrames, StringComparison.Ordinal);
        Assert.Contains("~", row.DetailsMessage);
        Assert.DoesNotContain(home, row.DetailsMessage);
        Assert.DoesNotContain(home, row.ExceptionHeadline + row.ExceptionFrames);
        // The buffered entry itself keeps the real path; only the pane redacts it.
        Assert.Contains(home, row.Message);
    }

    [Fact]
    public void An_entry_without_an_exception_has_no_exception_text()
    {
        var (buffer, panel) = Create();
        buffer.Add(LogLevel.Information, Category, "Loaded");
        buffer.Flush();

        var row = Assert.Single(panel.Entries);
        Assert.Empty(row.ExceptionHeadline);
        Assert.Empty(row.ExceptionFrames);
    }

    [AvaloniaFact]
    public void Row_cells_are_monospaced_at_regular_weight_and_the_message_is_full_ink()
    {
        var (buffer, panel) = Create();
        buffer.Add(LogLevel.Information, Category, "Loaded US5MA1BO in 697.9ms");
        buffer.Flush();

        using var host = ViewHost.Show(new LogsView { DataContext = panel }, width: 1100, height: 260);

        var item = host.Find<ListBoxItem>(r => ReferenceEquals(r.DataContext, panel.Entries[0]));
        var cells = item.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("logsCell"))
            .ToList();
        Assert.Equal(4, cells.Count);
        var mono = (FontFamily)Application.Current!.FindResource("MonoFontFamily")!;
        foreach (var cell in cells)
        {
            Assert.Equal(mono, cell.FontFamily);
            Assert.Equal(11, cell.FontSize);
            Assert.Equal(FontWeight.Normal, cell.FontWeight);
        }

        var message = cells.Single(t => t.Text == "Loaded US5MA1BO in 697.9ms");
        var time = cells.Single(t => t.Text == panel.Entries[0].TimeText);
        var level = cells.Single(t => t.Text == "Info");
        Assert.NotEqual(time.Foreground?.ToString(), message.Foreground?.ToString());
        Assert.Equal(time.Foreground?.ToString(), level.Foreground?.ToString());
    }

    [AvaloniaFact]
    public void The_exception_does_not_wrap_and_scrolls_sideways()
    {
        var (buffer, panel) = Create();
        buffer.Add(LogLevel.Error, Category, "Load failed", Thrown(new InvalidOperationException("boom")));
        buffer.Flush();

        using var host = ViewHost.Show(new LogsView { DataContext = panel }, width: 1100, height: 260);
        host.Click(host.Find<ListBoxItem>(r => ReferenceEquals(r.DataContext, panel.Entries[0])));

        var exception = host.Find<SelectableTextBlock>("Logs.Details.Exception");
        Assert.Equal(TextWrapping.NoWrap, exception.TextWrapping);
        var runs = exception.Inlines!.OfType<Run>().ToList();
        Assert.Equal("System.InvalidOperationException: boom", runs[0].Text);
        Assert.NotEqual(runs[0].Foreground?.ToString(), runs[1].Foreground?.ToString());
        var scroll = host.Find<ScrollViewer>("Logs.Details.ExceptionScroll");
        Assert.True(scroll.Extent.Width > scroll.Viewport.Width, "a stack frame is wider than the pane and scrolls");
        Assert.True(host.IsShown("Logs.Details.Category"));
    }

    private static Exception Thrown(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }
}
