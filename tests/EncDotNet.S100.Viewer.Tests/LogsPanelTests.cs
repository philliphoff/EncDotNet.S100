using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Logs tab in the bottom dock (#1080): the view model's incremental
/// refresh from the buffer, filtering, pause, Verbose, the unread badge, and
/// the view driven through its automation ids.
/// </summary>
public sealed class LogsPanelTests
{
    private const string Category = "EncDotNet.S100.Viewer.Services.DatasetLoaderService";

    private static (LogBuffer Buffer, LogsViewModel Panel) Create(int capacity = LogBuffer.DefaultCapacity, Action? showFeedback = null)
    {
        var buffer = new LogBuffer(capacity);
        // Inline "UI thread": a flush applies the buffer's changes at once.
        var panel = new LogsViewModel(buffer, showFeedback, post: action => action());
        return (buffer, panel);
    }

    [Fact]
    public void Shows_buffered_entries_and_follows_new_ones()
    {
        var (buffer, panel) = Create();
        buffer.Add(LogLevel.Information, Category, "first");
        buffer.Flush();
        buffer.Add(LogLevel.Warning, Category, "second");
        buffer.Flush();

        Assert.Equal(["first", "second"], panel.Entries.Select(r => r.Summary));
        Assert.Equal("Datasets", panel.Entries[0].AreaText);
        Assert.True(panel.Entries[1].IsWarning);
        Assert.False(panel.IsEmpty);
    }

    [Fact]
    public void Appends_and_evicts_row_by_row_without_replacing_existing_rows()
    {
        var (buffer, panel) = Create(capacity: 3);
        buffer.Add(LogLevel.Information, Category, "one");
        buffer.Add(LogLevel.Information, Category, "two");
        buffer.Flush();
        var two = panel.Entries[1];
        var changes = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        panel.Entries.CollectionChanged += (_, e) => changes.Add(e.Action);

        buffer.Add(LogLevel.Information, Category, "three");
        buffer.Add(LogLevel.Information, Category, "four");
        buffer.Flush();

        Assert.Equal(["two", "three", "four"], panel.Entries.Select(r => r.Summary));
        Assert.Same(two, panel.Entries[0]);
        Assert.DoesNotContain(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, changes);
    }

    [Fact]
    public void A_collapsed_repeat_updates_its_row_count()
    {
        var (buffer, panel) = Create();
        buffer.Add(LogLevel.Warning, Category, "Tile fetch failed");
        buffer.Flush();
        var row = Assert.Single(panel.Entries);

        buffer.Add(LogLevel.Warning, Category, "Tile fetch failed");
        buffer.Add(LogLevel.Warning, Category, "Tile fetch failed");
        buffer.Flush();

        Assert.Same(row, Assert.Single(panel.Entries));
        Assert.Equal(3, row.Count);
        Assert.Equal("×3", row.CountText);
        Assert.True(row.IsRepeated);
    }

    [Fact]
    public void Debug_is_hidden_by_default_and_the_level_filter_and_search_narrow_the_list()
    {
        var (buffer, panel) = Create();
        buffer.MinimumLevel = LogLevel.Debug;
        buffer.Add(LogLevel.Debug, Category, "debug detail");
        buffer.Add(LogLevel.Information, "EncDotNet.S100.Viewer.Services.Secom.SecomClient", "Fetched SECOM list");
        buffer.Add(LogLevel.Warning, Category, "Slow parse");
        buffer.Add(LogLevel.Error, Category, "Load failed", new IOException("disk"));
        buffer.Flush();

        Assert.Equal(LogLevel.Information, panel.SelectedLevelFilter.Level);
        Assert.Equal(["Fetched SECOM list", "Slow parse", "Load failed"], panel.Entries.Select(r => r.Summary));

        panel.SelectedLevelFilter = panel.LevelFilters.Single(f => f.Level == LogLevel.Warning);
        Assert.Equal(["Slow parse", "Load failed"], panel.Entries.Select(r => r.Summary));

        panel.SelectedLevelFilter = panel.LevelFilters[0];
        Assert.Equal(4, panel.Entries.Count);

        panel.SearchText = "secom";
        Assert.Equal(["Fetched SECOM list"], panel.Entries.Select(r => r.Summary));

        panel.SearchText = "disk";
        Assert.Equal(["Load failed"], panel.Entries.Select(r => r.Summary));

        panel.SearchText = "nothing like this";
        Assert.True(panel.IsEmpty);
        Assert.Equal(Resources.Strings.Logs_EmptyFiltered, panel.EmptyText);
    }

    [Fact]
    public void Verbose_changes_only_the_buffer_level_and_shows_debug()
    {
        var (buffer, panel) = Create();
        Assert.False(panel.IsVerbose);

        panel.IsVerbose = true;
        Assert.Equal(LogLevel.Debug, buffer.MinimumLevel);
        Assert.Equal(LogLevel.Debug, panel.SelectedLevelFilter.Level);

        panel.IsVerbose = false;
        Assert.Equal(LogLevel.Information, buffer.MinimumLevel);
        Assert.Equal(LogLevel.Information, panel.SelectedLevelFilter.Level);
    }

    [Fact]
    public void Pause_freezes_the_list_and_resume_catches_up()
    {
        var (buffer, panel) = Create();
        buffer.Add(LogLevel.Information, Category, "before");
        buffer.Flush();

        panel.IsPaused = true;
        buffer.Add(LogLevel.Information, Category, "during 1");
        buffer.Add(LogLevel.Information, Category, "during 2");
        buffer.Flush();

        Assert.Equal(["before"], panel.Entries.Select(r => r.Summary));
        Assert.Equal(string.Format(Resources.Strings.Logs_PausedFormat, 2), panel.PausedText);

        panel.IsPaused = false;
        Assert.Equal(["before", "during 1", "during 2"], panel.Entries.Select(r => r.Summary));
        Assert.Equal(string.Empty, panel.PausedText);
    }

    [Fact]
    public void Clear_empties_the_buffer_and_the_list()
    {
        var (buffer, panel) = Create();
        buffer.Add(LogLevel.Information, Category, "one");
        buffer.Flush();

        panel.ClearCommand.Execute(null);

        Assert.Empty(buffer.Snapshot());
        Assert.Empty(panel.Entries);
        Assert.Equal(Resources.Strings.Logs_Empty, panel.EmptyText);
        Assert.False(panel.CopyCommand.CanExecute(null));
    }

    [Fact]
    public void Copy_puts_the_shown_entries_on_the_clipboard_with_the_home_directory_redacted()
    {
        var (buffer, panel) = Create();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.SkipWhen(string.IsNullOrEmpty(home) || home.TrimEnd('/', '\\').Length < 3, "no home directory");
        buffer.Add(LogLevel.Warning, Category, $"Could not open {Path.Combine(home, "Charts", "US5MA1BO.000")}");
        buffer.Flush();
        string? copied = null;
        panel.CopyRequested += (_, text) => copied = text;

        panel.CopyCommand.Execute(null);

        Assert.NotNull(copied);
        Assert.Contains("Could not open ~", copied);
        Assert.DoesNotContain(home, copied);
    }

    [Fact]
    public void Badge_counts_new_warnings_and_errors_while_the_tab_is_hidden()
    {
        // Warnings logged at startup, before the panel existed, count too.
        var buffer = new LogBuffer();
        buffer.Add(LogLevel.Warning, Category, "early");
        var panel = new LogsViewModel(buffer, post: action => action());
        Assert.Equal(1, panel.BadgeCount);

        buffer.Add(LogLevel.Information, Category, "fine");
        buffer.Add(LogLevel.Warning, Category, "slow");
        buffer.Add(LogLevel.Error, Category, "failed");
        buffer.Add(LogLevel.Error, Category, "failed");
        buffer.Flush();
        Assert.Equal(4, panel.BadgeCount);
        Assert.Equal(string.Format(Resources.Strings.Logs_BadgeName, 4), panel.BadgeName);

        panel.IsTabShown = true;
        Assert.Equal(0, panel.BadgeCount);
        buffer.Add(LogLevel.Error, Category, "while shown");
        buffer.Flush();
        Assert.Equal(0, panel.BadgeCount);

        panel.IsTabShown = false;
        Assert.Equal(0, panel.BadgeCount);
        buffer.Add(LogLevel.Error, Category, "after");
        buffer.Flush();
        Assert.Equal(1, panel.BadgeCount);
    }

    [Fact]
    public void Send_feedback_opens_the_feedback_dialog()
    {
        var opened = 0;
        var (_, panel) = Create(showFeedback: () => opened++);

        panel.SendFeedbackCommand.Execute(null);

        Assert.Equal(1, opened);
        Assert.False(Create().Panel.SendFeedbackCommand.CanExecute(null));
    }

    [Fact]
    public void A_large_burst_rebuilds_once_and_keeps_the_selection()
    {
        var (buffer, panel) = Create();
        buffer.Add(LogLevel.Information, Category, "selected");
        buffer.Flush();
        panel.SelectedEntry = panel.Entries[0];
        var resets = 0;
        panel.Entries.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                resets++;
        };

        for (var i = 0; i < LogsViewModel.IncrementalLimit + 50; i++)
            buffer.Add(LogLevel.Information, Category, $"burst {i}");
        buffer.Flush();

        Assert.Equal(LogsViewModel.IncrementalLimit + 51, panel.Entries.Count);
        Assert.InRange(resets, 1, 2);
        Assert.Equal("selected", panel.SelectedEntry?.Summary);
    }

    [AvaloniaFact]
    public void View_shows_rows_and_selecting_one_shows_its_exception()
    {
        var (buffer, panel) = Create();
        buffer.Add(LogLevel.Information, Category, "Loaded US5MA1BO");
        buffer.Add(LogLevel.Error, Category, "Load failed", new InvalidOperationException("boom"));
        buffer.Flush();

        using var host = ViewHost.Show(new LogsView { DataContext = panel }, width: 1100, height: 260);

        var list = host.Find<ListBox>("Logs.List");
        Assert.Equal(2, list.ItemCount);
        Assert.False(host.IsShown("Logs.Details.Exception"));

        host.Click(host.Find<ListBoxItem>(r => ReferenceEquals(r.DataContext, panel.Entries[1])));

        Assert.Same(panel.Entries[1], panel.SelectedEntry);
        var exception = host.Find<SelectableTextBlock>("Logs.Details.Exception");
        Assert.Contains("System.InvalidOperationException: boom", exception.Text);

        host.Click(host.Find<Button>("Logs.CloseDetails"));
        Assert.Null(panel.SelectedEntry);
    }

    [AvaloniaFact]
    public void View_toolbar_drives_pause_verbose_and_clear()
    {
        var (buffer, panel) = Create();
        buffer.Add(LogLevel.Information, Category, "one");
        buffer.Flush();
        using var host = ViewHost.Show(new LogsView { DataContext = panel }, width: 1100, height: 260);

        host.Click(host.Find<ToggleButton>("Logs.Pause"));
        Assert.True(panel.IsPaused);
        host.Click(host.Find<ToggleButton>("Logs.Verbose"));
        Assert.True(panel.IsVerbose);
        host.Click(host.Find<Button>("Logs.Clear"));
        Assert.Empty(panel.Entries);
        Assert.True(host.IsShown("Logs.Search"));
        Assert.True(host.IsShown("Logs.LevelFilter"));
    }
}
