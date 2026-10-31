using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Logs tab in the bottom dock (#1080): the view model's incremental
/// refresh from the buffer, filtering, following the tail (#1173), the level
/// filter's Debug capture and the problem counts (#1172), the unread badge, the keys, and the view driven through its automation ids.
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

        panel.SelectedLevelFilter = panel.LevelFilters.Single(f => f.Level == LogLevel.Debug);
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
    public void Level_filter_offers_four_levels_and_Everything_owns_debug_capture()
    {
        var (buffer, panel) = Create();
        Assert.Equal(
            [LogLevel.Error, LogLevel.Warning, LogLevel.Information, LogLevel.Debug],
            panel.LevelFilters.Select(f => f.Level));
        Assert.Equal(LogLevel.Information, panel.SelectedLevelFilter.Level);
        Assert.False(panel.IsCapturingDebug);

        var changed = new List<string?>();
        panel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        panel.SelectedLevelFilter = panel.LevelFilters.Single(f => f.Level == LogLevel.Debug);
        Assert.Equal(LogLevel.Debug, buffer.MinimumLevel);
        Assert.True(panel.IsCapturingDebug);
        Assert.Contains(nameof(LogsViewModel.IsCapturingDebug), changed);

        // Debug is captured from that moment, and shown.
        buffer.Add(LogLevel.Debug, Category, "debug detail");
        buffer.Flush();
        Assert.Equal(["debug detail"], panel.Entries.Select(r => r.Summary));

        panel.SelectedLevelFilter = panel.LevelFilters.Single(f => f.Level == LogLevel.Warning);
        Assert.Equal(LogLevel.Information, buffer.MinimumLevel);
        Assert.False(panel.IsCapturingDebug);
    }

    [Fact]
    public void A_buffer_already_capturing_debug_starts_on_Everything()
    {
        var buffer = new LogBuffer(minimumLevel: LogLevel.Debug);
        var panel = new LogsViewModel(buffer, post: action => action());

        Assert.Equal(LogLevel.Debug, panel.SelectedLevelFilter.Level);
        Assert.True(panel.IsCapturingDebug);
    }

    [Fact]
    public void Count_chips_count_the_buffer_and_stay_right_as_it_evicts()
    {
        var (buffer, panel) = Create(capacity: 4);
        Assert.False(panel.HasErrors);
        Assert.False(panel.HasWarnings);

        buffer.Add(LogLevel.Error, Category, "e1");
        buffer.Add(LogLevel.Warning, Category, "w1");
        buffer.Add(LogLevel.Warning, Category, "w2");
        buffer.Add(LogLevel.Critical, Category, "c1");
        buffer.Flush();
        Assert.Equal((2, 2), (panel.ErrorCount, panel.WarningCount));

        // Filtered-out entries still count: the chips describe the buffer.
        panel.SelectedLevelFilter = panel.LevelFilters.Single(f => f.Level == LogLevel.Error);
        Assert.Equal((2, 2), (panel.ErrorCount, panel.WarningCount));

        // Three infos push e1, w1 and w2 out of the 4-entry buffer.
        buffer.Add(LogLevel.Information, Category, "i1");
        buffer.Add(LogLevel.Information, Category, "i2");
        buffer.Add(LogLevel.Information, Category, "i3");
        buffer.Flush();
        Assert.Equal((1, 0), (panel.ErrorCount, panel.WarningCount));
        Assert.True(panel.HasErrors);
        Assert.False(panel.HasWarnings);

        // A collapsed repeat is one entry, and it pushes c1 out.
        buffer.Add(LogLevel.Warning, Category, "again");
        buffer.Add(LogLevel.Warning, Category, "again");
        buffer.Flush();
        Assert.Equal((0, 1), (panel.ErrorCount, panel.WarningCount));
        Assert.False(panel.HasErrors);

        panel.ClearCommand.Execute(null);
        Assert.Equal((0, 0), (panel.ErrorCount, panel.WarningCount));
        Assert.False(panel.HasErrors);
    }

    [Fact]
    public void A_chip_filters_to_its_level_and_a_second_click_restores_the_previous_one()
    {
        var (buffer, panel) = Create();
        buffer.Add(LogLevel.Information, Category, "info");
        buffer.Add(LogLevel.Warning, Category, "warn");
        buffer.Add(LogLevel.Error, Category, "err");
        buffer.Flush();

        panel.ShowErrorsCommand.Execute(null);
        Assert.Equal(LogLevel.Error, panel.SelectedLevelFilter.Level);
        Assert.True(panel.IsErrorsChipActive);
        Assert.Equal(["err"], panel.Entries.Select(r => r.Summary));

        // Errors → warnings chip → back still returns to the level before the chips.
        panel.ShowWarningsCommand.Execute(null);
        Assert.Equal(LogLevel.Warning, panel.SelectedLevelFilter.Level);
        Assert.True(panel.IsWarningsChipActive);
        Assert.False(panel.IsErrorsChipActive);
        panel.ShowWarningsCommand.Execute(null);
        Assert.Equal(LogLevel.Information, panel.SelectedLevelFilter.Level);
        Assert.False(panel.IsWarningsChipActive);
        Assert.Equal(3, panel.Entries.Count);

        // A level picked in the combo box replaces what a chip would restore.
        panel.ShowErrorsCommand.Execute(null);
        panel.SelectedLevelFilter = panel.LevelFilters.Single(f => f.Level == LogLevel.Warning);
        panel.SelectedLevelFilter = panel.LevelFilters.Single(f => f.Level == LogLevel.Error);
        Assert.False(panel.IsErrorsChipActive);
        panel.ShowErrorsCommand.Execute(null);
        Assert.Equal(LogLevel.Error, panel.SelectedLevelFilter.Level);
    }

    [Fact]
    public void A_chip_narrows_an_area_search_and_the_counts_ignore_it()
    {
        var (buffer, panel) = Create();
        buffer.Add(LogLevel.Error, "EncDotNet.S100.Viewer.Services.Secom.SecomClient", "secom failed");
        buffer.Add(LogLevel.Warning, "EncDotNet.S100.Viewer.Services.Secom.SecomClient", "secom slow");
        buffer.Add(LogLevel.Error, Category, "load failed");
        buffer.Flush();
        var secom = panel.Entries[0].AreaText;
        Assert.NotEqual(secom, panel.Entries[2].AreaText);

        panel.SearchText = $"area:\"{secom}\"";
        panel.ShowErrorsCommand.Execute(null);
        Assert.Equal(["secom failed"], panel.Entries.Select(r => r.Summary));
        // The chips describe the buffer, not what the search shows.
        Assert.Equal((2, 1), (panel.ErrorCount, panel.WarningCount));

        panel.ShowErrorsCommand.Execute(null);
        Assert.Equal(["secom failed", "secom slow"], panel.Entries.Select(r => r.Summary));
    }

    [Fact]
    public void A_chip_keeps_debug_capture_until_it_restores_Everything()
    {
        var (buffer, panel) = Create();
        panel.SelectedLevelFilter = panel.LevelFilters.Single(f => f.Level == LogLevel.Debug);

        panel.ShowErrorsCommand.Execute(null);
        Assert.Equal(LogLevel.Error, panel.SelectedLevelFilter.Level);
        Assert.Equal(LogLevel.Debug, buffer.MinimumLevel);

        panel.ShowErrorsCommand.Execute(null);
        Assert.Equal(LogLevel.Debug, panel.SelectedLevelFilter.Level);
        Assert.Equal(LogLevel.Debug, buffer.MinimumLevel);
    }

    [Fact]
    public void Rows_put_errors_and_exceptions_in_the_error_gutter()
    {
        var (buffer, panel) = Create();
        buffer.Add(LogLevel.Information, Category, "info");
        buffer.Add(LogLevel.Warning, Category, "warn");
        buffer.Add(LogLevel.Information, Category, "info with exception", new IOException("disk"));
        buffer.Add(LogLevel.Warning, Category, "warn with exception", new IOException("disk"));
        buffer.Add(LogLevel.Critical, Category, "crit");
        buffer.Flush();

        Assert.Equal([false, false, true, true, true], panel.Entries.Select(r => r.ShowsErrorGlyph));
        Assert.Equal([false, true, false, false, false], panel.Entries.Select(r => r.ShowsWarningGlyph));
    }

    [Fact]
    public void Follows_the_tail_by_default_and_asks_the_view_to_scroll()
    {
        var (buffer, panel) = Create();
        var scrolls = 0;
        panel.ScrollToNewestRequested += (_, _) => scrolls++;

        buffer.Add(LogLevel.Information, Category, "one");
        buffer.Flush();

        Assert.True(panel.IsFollowing);
        Assert.Equal(1, scrolls);
        Assert.Equal(0, panel.NewEntriesCount);
        Assert.False(panel.HasNewEntries);
    }

    [Fact]
    public void Scrolling_away_stops_following_and_counts_the_rows_that_arrive_below()
    {
        var (buffer, panel) = Create();
        buffer.Add(LogLevel.Information, Category, "read me");
        buffer.Flush();
        var scrolls = 0;
        panel.ScrollToNewestRequested += (_, _) => scrolls++;

        panel.OnUserScrolled(atBottom: false);
        buffer.Add(LogLevel.Information, Category, "new 1");
        buffer.Add(LogLevel.Information, Category, "new 2");
        buffer.Flush();

        Assert.False(panel.IsFollowing);
        Assert.Equal(["read me", "new 1", "new 2"], panel.Entries.Select(r => r.Summary));
        Assert.Equal(0, scrolls);
        Assert.Equal(2, panel.NewEntriesCount);
        Assert.True(panel.HasNewEntries);
        Assert.Equal(string.Format(Strings.Logs_NewEntriesFormat, 2), panel.NewEntriesText);
    }

    [Fact]
    public void Only_shown_rows_count_as_new()
    {
        var (buffer, panel) = Create();
        panel.SelectedLevelFilter = panel.LevelFilters.Single(f => f.Level == LogLevel.Warning);
        panel.StopFollowing();

        buffer.Add(LogLevel.Information, Category, "hidden");
        buffer.Add(LogLevel.Warning, Category, "shown");
        buffer.Flush();

        Assert.Equal(1, panel.NewEntriesCount);
    }

    [Fact]
    public void Selecting_a_row_stops_following_but_keeping_it_through_a_rebuild_does_not_restart()
    {
        var (buffer, panel) = Create();
        buffer.Add(LogLevel.Error, Category, "failed");
        buffer.Flush();

        panel.SelectedEntry = panel.Entries[0];
        Assert.False(panel.IsFollowing);

        // Scrolling back to the bottom doesn't resume while the selection pins the view.
        panel.OnUserScrolled(atBottom: true);
        Assert.False(panel.IsFollowing);

        panel.SelectedEntry = null;
        panel.OnUserScrolled(atBottom: true);
        Assert.True(panel.IsFollowing);
    }

    [Fact]
    public void Eviction_while_away_drops_old_rows_and_keeps_the_new_count()
    {
        var (buffer, panel) = Create(capacity: 4);
        buffer.Add(LogLevel.Information, Category, "one");
        buffer.Add(LogLevel.Information, Category, "two");
        buffer.Add(LogLevel.Information, Category, "three");
        buffer.Flush();
        panel.SelectedEntry = panel.Entries[2];

        for (var i = 1; i <= 3; i++)
            buffer.Add(LogLevel.Information, Category, $"new {i}");
        buffer.Flush();

        Assert.Equal(["three", "new 1", "new 2", "new 3"], panel.Entries.Select(r => r.Summary));
        Assert.Equal("three", panel.SelectedEntry?.Summary);
        Assert.False(panel.IsFollowing);
        Assert.Equal(3, panel.NewEntriesCount);

        // The buffer can evict the new rows too; the count never exceeds what's listed.
        for (var i = 4; i <= 9; i++)
            buffer.Add(LogLevel.Information, Category, $"new {i}");
        buffer.Flush();
        Assert.Null(panel.SelectedEntry);
        Assert.Equal(4, panel.NewEntriesCount);
    }

    [Fact]
    public void Resuming_scrolls_to_the_newest_and_clears_the_new_count()
    {
        var (buffer, panel) = Create();
        buffer.Add(LogLevel.Information, Category, "one");
        buffer.Flush();
        panel.SelectedEntry = panel.Entries[0];
        buffer.Add(LogLevel.Information, Category, "two");
        buffer.Flush();
        var scrolls = 0;
        panel.ScrollToNewestRequested += (_, _) => scrolls++;

        panel.ResumeFollowingCommand.Execute(null);

        Assert.True(panel.IsFollowing);
        Assert.Equal(1, scrolls);
        Assert.Equal(0, panel.NewEntriesCount);
        Assert.False(panel.HasNewEntries);
        // The selection stays; following no longer moves it out of view by surprise.
        Assert.Equal("one", panel.SelectedEntry?.Summary);

        buffer.Add(LogLevel.Information, Category, "three");
        buffer.Flush();
        Assert.Equal(2, scrolls);
    }

    [Fact]
    public void Clear_resumes_following()
    {
        var (buffer, panel) = Create();
        buffer.Add(LogLevel.Information, Category, "one");
        buffer.Flush();
        panel.StopFollowing();

        panel.ClearCommand.Execute(null);

        Assert.True(panel.IsFollowing);
    }

    [Fact]
    public void Keys_move_the_selection_and_esc_clears_the_selection_then_the_search()
    {
        var (buffer, panel) = Create();
        foreach (var message in new[] { "one", "two", "three" })
            buffer.Add(LogLevel.Information, Category, message);
        buffer.Flush();

        Assert.True(panel.MoveSelection(-1));
        Assert.Equal("three", panel.SelectedEntry?.Summary);
        panel.MoveSelection(-1);
        panel.MoveSelection(-1);
        panel.MoveSelection(-1);
        Assert.Equal("one", panel.SelectedEntry?.Summary);
        panel.MoveSelection(1);
        Assert.Equal("two", panel.SelectedEntry?.Summary);

        panel.SearchText = "t";
        Assert.True(panel.ClearSelectionOrSearch());
        Assert.Null(panel.SelectedEntry);
        Assert.Equal("t", panel.SearchText);
        Assert.True(panel.ClearSelectionOrSearch());
        Assert.Equal(string.Empty, panel.SearchText);
        Assert.False(panel.ClearSelectionOrSearch());
    }

    [Fact]
    public void Copy_entry_copies_the_selected_entry_redacted()
    {
        var (buffer, panel) = Create();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.SkipWhen(string.IsNullOrEmpty(home) || home.TrimEnd('/', '\\').Length < 3, "no home directory");
        buffer.Add(LogLevel.Information, Category, "other");
        buffer.Add(LogLevel.Warning, Category, $"Could not open {Path.Combine(home, "Charts", "US5MA1BO.000")}");
        buffer.Flush();
        string? copied = null;
        panel.CopyRequested += (_, text) => copied = text;
        Assert.False(panel.CopySelectedEntryCommand.CanExecute(null));

        panel.SelectedEntry = panel.Entries[1];
        panel.CopySelectedEntryCommand.Execute(null);

        Assert.NotNull(copied);
        Assert.Contains("Could not open ~", copied);
        Assert.DoesNotContain(home, copied);
        Assert.DoesNotContain("other", copied);
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
        Assert.Contains("System.InvalidOperationException: boom", string.Concat(exception.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)));

        host.Click(host.Find<Button>("Logs.CloseDetails"));
        Assert.Null(panel.SelectedEntry);
    }

    [AvaloniaFact]
    public void View_toolbar_drives_the_count_chips_and_clear()
    {
        var (buffer, panel) = Create();
        buffer.Add(LogLevel.Information, Category, "one");
        buffer.Add(LogLevel.Warning, Category, "two");
        buffer.Flush();
        using var host = ViewHost.Show(new LogsView { DataContext = panel }, width: 1100, height: 260);

        Assert.False(host.IsShown("Logs.CapturingDebug"));
        Assert.False(host.IsShown("Logs.ErrorsChip"));
        Assert.True(host.IsShown("Logs.WarningsChip"));
        host.Click(host.Find<Button>("Logs.WarningsChip"));
        Assert.Equal(LogLevel.Warning, panel.SelectedLevelFilter.Level);
        host.Click(host.Find<Button>("Logs.WarningsChip"));
        Assert.Equal(LogLevel.Information, panel.SelectedLevelFilter.Level);

        panel.SelectedLevelFilter = panel.LevelFilters.Single(f => f.Level == LogLevel.Debug);
        host.Settle();
        Assert.True(host.IsShown("Logs.CapturingDebug"));

        host.Click(host.Find<Button>("Logs.Clear"));
        host.Settle();
        Assert.False(host.IsShown("Logs.WarningsChip"));
        Assert.Empty(panel.Entries);
        Assert.True(host.IsShown("Logs.Search"));
        Assert.True(host.IsShown("Logs.LevelFilter"));
    }

    [AvaloniaFact]
    public void View_toolbar_never_overlaps_and_wraps_the_actions_when_narrow()
    {
        // At 1100 px with the left dock open the bottom dock is about 730 px
        // wide; the search box used to be drawn under the toolbar's toggles.
        // Every toolbar item shows: both chips and the Capturing Debug hint.
        var (buffer, panel) = Create();
        buffer.Add(LogLevel.Error, Category, "err");
        buffer.Add(LogLevel.Warning, Category, "warn");
        buffer.Flush();
        panel.SelectedLevelFilter = panel.LevelFilters.Single(f => f.Level == LogLevel.Debug);
        using var host = ViewHost.Show(new LogsView { DataContext = panel }, width: 1100, height: 260);
        string[] ids =
        [
            "Logs.LevelFilter", "Logs.CapturingDebug", "Logs.Search", "Logs.ErrorsChip", "Logs.WarningsChip",
            "Logs.Copy", "Logs.Clear", "Logs.SendFeedback",
        ];
        foreach (var id in ids.Where(id => id != "Logs.CapturingDebug"))
            Assert.Equal(28, host.Find<Control>(id).Bounds.Height, precision: 1);
        // ShadUI's ComboBox draws its box inside the control; it must not be clipped.
        var levelBox = host.Find<ComboBox>("Logs.LevelFilter").GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Border");
        Assert.Equal(28, levelBox.Bounds.Height, precision: 1);
        var toolbar = host.Find<LogsToolbarPanel>(_ => true);
        var wrapped = new List<bool>();

        foreach (var width in new[] { 1100d, 900, 730, 600, 520, 440 })
        {
            host.Window.Width = width;
            host.Settle();

            // A narrow dock drops the Capturing Debug hint to keep the filters on their line.
            Assert.Equal(width > 560, host.IsShown("Logs.CapturingDebug"));
            var bounds = ids.Where(host.IsShown).Select(id => (id, rect: BoundsInWindow(host, host.Find<Control>(id)))).ToList();
            foreach (var (id, rect) in bounds)
            {
                Assert.True(rect.Width > 0 && rect.Right <= width + 0.5, $"{id} {rect} doesn't fit at {width} px.");
            }
            for (var i = 0; i < bounds.Count; i++)
            {
                for (var j = i + 1; j < bounds.Count; j++)
                {
                    Assert.False(
                        bounds[i].rect.Intersects(bounds[j].rect),
                        $"{bounds[i].id} {bounds[i].rect} overlaps {bounds[j].id} {bounds[j].rect} at {width} px.");
                }
            }

            // The level filter sits fully above the column headers.
            var header = host.Find<TextBlock>(t => t.Text == Strings.Logs_Column_Time);
            Assert.True(bounds[0].rect.Bottom <= BoundsInWindow(host, header).Top + 0.5);
            wrapped.Add(toolbar.IsActionsWrapped);
        }

        Assert.False(wrapped[0], "A wide dock keeps the toolbar on one line.");
        Assert.True(wrapped[^1], "A narrow dock moves the actions to a second line.");
    }

    [AvaloniaFact]
    public void View_keeps_a_selected_row_in_place_while_rows_arrive_and_the_pill_resumes()
    {
        var (buffer, panel) = Create(capacity: 130);
        for (var i = 0; i < 120; i++)
            buffer.Add(LogLevel.Information, Category, $"row {i}");
        buffer.Flush();
        using var host = ViewHost.Show(new LogsView { DataContext = panel }, width: 1100, height: 300);
        Assert.True(IsInView(host, panel.Entries[^1]), "Following shows the newest row.");
        Assert.False(host.IsShown("Logs.NewEntries"));

        var selected = panel.Entries[^3];
        host.Click(host.Find<ListBoxItem>(r => ReferenceEquals(r.DataContext, selected)));
        Assert.False(panel.IsFollowing);
        var before = RowTop(host, selected);

        // 10 rows append below the selection.
        for (var i = 120; i < 130; i++)
            buffer.Add(LogLevel.Information, Category, $"row {i}");
        buffer.Flush();
        host.Settle();
        host.Settle();
        Assert.Equal("row 0", panel.Entries[0].Summary);
        Assert.Equal(before, RowTop(host, selected), precision: 0);

        // 20 more: the buffer evicts 20 from the front.
        for (var i = 130; i < 150; i++)
            buffer.Add(LogLevel.Information, Category, $"row {i}");
        buffer.Flush();
        host.Settle();
        host.Settle();

        Assert.Equal("row 20", panel.Entries[0].Summary);
        Assert.Same(selected, panel.SelectedEntry);
        Assert.Equal(before, RowTop(host, selected), precision: 0);
        Assert.True(host.IsShown("Logs.NewEntries"));
        Assert.Equal(string.Format(Strings.Logs_NewEntriesFormat, 30), host.Find<Button>("Logs.NewEntries").Content);

        host.Click(host.Find<Button>("Logs.NewEntries"));
        host.Settle();

        Assert.True(panel.IsFollowing);
        Assert.False(host.IsShown("Logs.NewEntries"));
        Assert.True(IsInView(host, panel.Entries[^1]), "The pill goes back to the newest row.");
    }

    [AvaloniaFact]
    public void View_keys_are_scoped_to_the_panel()
    {
        var (buffer, panel) = Create();
        for (var i = 0; i < 40; i++)
            buffer.Add(LogLevel.Information, Category, $"row {i}");
        buffer.Flush();
        using var host = ViewHost.Show(new LogsView { DataContext = panel }, width: 1100, height: 300);
        host.Click(host.Find<ListBoxItem>(r => ReferenceEquals(r.DataContext, panel.Entries[^1])));
        Assert.Equal("row 39", panel.SelectedEntry?.Summary);

        host.Press(PhysicalKey.ArrowUp);
        host.Press(PhysicalKey.ArrowUp);
        Assert.Equal("row 37", panel.SelectedEntry?.Summary);
        host.Press(PhysicalKey.ArrowDown);
        Assert.Equal("row 38", panel.SelectedEntry?.Summary);
        Assert.False(panel.IsFollowing);

        string? copied = null;
        panel.CopyRequested += (_, text) => copied = text;
        var command = Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers == KeyModifiers.Meta
            ? RawInputModifiers.Meta
            : RawInputModifiers.Control;
        host.Press(PhysicalKey.C, command);
        Assert.Contains("row 38", copied);
        Assert.DoesNotContain("row 39", copied);

        host.Press(PhysicalKey.End);
        Assert.True(panel.IsFollowing);

        host.Press(PhysicalKey.Escape);
        Assert.Null(panel.SelectedEntry);

        host.Press(PhysicalKey.F, command);
        Assert.True(host.Find<TextBox>("Logs.Search").IsFocused);
        Assert.False(panel.IsFollowing);
        host.Type("row 3");
        Assert.Equal("row 3", panel.SearchText);
        host.Press(PhysicalKey.Escape);
        Assert.Equal(string.Empty, panel.SearchText);
    }

    private static bool IsInView(ViewHost host, LogRowViewModel row)
    {
        var list = host.Find<ListBox>("Logs.List");
        if (list.ContainerFromItem(row) is not Control container || !container.IsEffectivelyVisible)
            return false;
        var top = container.TranslatePoint(default, list)?.Y ?? double.NaN;
        return top >= -0.5 && top + container.Bounds.Height <= list.Bounds.Height + 0.5;
    }

    private static double RowTop(ViewHost host, LogRowViewModel row) =>
        BoundsInWindow(host, host.Find<ListBoxItem>(r => ReferenceEquals(r.DataContext, row))).Top;

    private static Rect BoundsInWindow(ViewHost host, Control control)
    {
        var origin = control.TranslatePoint(default, host.Window) ?? default;
        return new Rect(origin, control.Bounds.Size);
    }
}
