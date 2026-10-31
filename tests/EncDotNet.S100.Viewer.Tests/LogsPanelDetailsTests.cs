using Avalonia.Controls;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Logs panel slice 4 (#1174): area tokens in the search and the row menu
/// that adds them (D16), times in the Local/UTC preference with day dividers
/// (D14), and the details pane's header and splitter (D11, D13).
/// </summary>
public sealed class LogsPanelDetailsTests
{
    private const string Datasets = "EncDotNet.S100.Viewer.Services.DatasetLoaderService";
    private const string Commands = "EncDotNet.S100.Viewer.Commands.ViewerCommandDispatcher";
    private const string OwnShip = "EncDotNet.S100.Viewer.Services.OwnShipService";

    // A fixed +10:00 "local" zone, so the tests don't depend on the machine's.
    private static readonly TimeZoneInfo Plus10 =
        TimeZoneInfo.CreateCustomTimeZone("Test+10", TimeSpan.FromHours(10), "Test+10", "Test+10");

    private sealed class Format(TimeFormat current) : ITimeFormatProvider
    {
        public TimeFormat Current { get; private set; } = current;

        public event Action<TimeFormat>? TimeFormatChanged;

        public void Set(TimeFormat value)
        {
            Current = value;
            TimeFormatChanged?.Invoke(value);
        }
    }

    private sealed class Fixture
    {
        public Fixture(TimeFormat format = TimeFormat.Local, int capacity = LogBuffer.DefaultCapacity)
        {
            Clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 6, 29, 52, 795, TimeSpan.Zero));
            Clock.SetLocalTimeZone(Plus10);
            Format = new Format(format);
            Buffer = new LogBuffer(capacity, timeProvider: Clock);
            Panel = new LogsViewModel(Buffer, post: action => action(), timeFormat: Format, time: Clock);
        }

        public FakeTimeProvider Clock { get; }
        public Format Format { get; }
        public LogBuffer Buffer { get; }
        public LogsViewModel Panel { get; }

        public void Add(string category, string message, LogLevel level = LogLevel.Information)
        {
            Buffer.Add(level, category, message);
            Buffer.Flush();
        }
    }

    private static IEnumerable<string> Shown(LogsViewModel panel) => panel.Entries.Select(r => r.Summary);

    [Fact]
    public void Area_tokens_show_only_or_hide_an_area_and_plain_words_still_match()
    {
        var f = new Fixture();
        f.Add(Datasets, "Loaded US5MA1BO");
        f.Add(Commands, "Viewer command completed");
        f.Add(OwnShip, "Fix lost");

        f.Panel.SearchText = "area:Datasets";
        Assert.Equal(["Loaded US5MA1BO"], Shown(f.Panel));

        f.Panel.SearchText = "-area:datasets";
        Assert.Equal(["Viewer command completed", "Fix lost"], Shown(f.Panel));

        f.Panel.SearchText = "area:\"Own ship\"";
        Assert.Equal(["Fix lost"], Shown(f.Panel));

        // An area's id works as well as its name, and tokens combine.
        f.Panel.SearchText = "area:OwnShip area:Commands";
        Assert.Equal(["Viewer command completed", "Fix lost"], Shown(f.Panel));

        f.Panel.SearchText = "-area:Commands lo";
        Assert.Equal(["Loaded US5MA1BO", "Fix lost"], Shown(f.Panel));

        // Plain words match as before, area names included.
        f.Panel.SearchText = "command completed";
        Assert.Equal(["Viewer command completed"], Shown(f.Panel));
        f.Panel.SearchText = "Datasets";
        Assert.Equal(["Loaded US5MA1BO"], Shown(f.Panel));

        // A token still being typed filters nothing.
        f.Panel.SearchText = "area:";
        Assert.Equal(3, f.Panel.Entries.Count);
    }

    [Theory]
    [InlineData("", "Datasets", false, "area:Datasets")]
    [InlineData("tile", "Datasets", true, "-area:Datasets tile")]
    [InlineData("area:Commands  tile", "Datasets", false, "area:Datasets tile")]
    [InlineData("-area:Commands", "Datasets", false, "-area:Commands area:Datasets")]
    [InlineData("area:Datasets", "Datasets", true, "-area:Datasets")]
    [InlineData("-area:Datasets x", "Datasets", false, "area:Datasets x")]
    [InlineData("area:Commands", "Own ship", true, "area:Commands -area:\"Own ship\"")]
    public void Adding_an_area_token_replaces_conflicting_ones(string search, string area, bool hide, string expected) =>
        Assert.Equal(expected, LogSearchQuery.WithAreaToken(search, area, hide));

    [Fact]
    public void Row_menu_copies_the_message_and_filters_by_the_selected_area()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var f = new Fixture();
        f.Add(Datasets, $"Could not open {home}/Charts/US5MA1BO.000");
        f.Add(Commands, "Viewer command completed");
        string? copied = null;
        f.Panel.CopyRequested += (_, text) => copied = text;

        Assert.False(f.Panel.CopySelectedMessageCommand.CanExecute(null));
        Assert.False(f.Panel.ShowOnlySelectedAreaCommand.CanExecute(null));

        f.Panel.SelectedEntry = f.Panel.Entries[0];
        Assert.Equal("Show only Datasets", f.Panel.ShowOnlySelectedAreaText);
        Assert.Equal("Hide Datasets", f.Panel.HideSelectedAreaText);

        f.Panel.CopySelectedMessageCommand.Execute(null);
        Assert.Equal("Could not open ~/Charts/US5MA1BO.000", copied);

        f.Panel.SearchText = "open";
        f.Panel.HideSelectedAreaCommand.Execute(null);
        Assert.Equal("-area:Datasets open", f.Panel.SearchText);
        Assert.Empty(f.Panel.Entries);

        f.Panel.SearchText = string.Empty;
        f.Panel.SelectedEntry = f.Panel.Entries[1];
        f.Panel.ShowOnlySelectedAreaCommand.Execute(null);
        Assert.Equal("area:Commands", f.Panel.SearchText);
        Assert.Equal(["Viewer command completed"], Shown(f.Panel));
    }

    [Fact]
    public void Times_follow_the_local_or_utc_preference_and_update_when_it_changes()
    {
        var f = new Fixture();
        f.Add(Datasets, "Loaded");
        f.Buffer.Add(LogLevel.Warning, Datasets, "Slow");
        f.Clock.Advance(TimeSpan.FromSeconds(2));
        f.Buffer.Add(LogLevel.Warning, Datasets, "Slow");
        f.Buffer.Flush();
        var row = f.Panel.Entries[0];
        var repeated = f.Panel.Entries[1];

        Assert.False(f.Panel.IsUtc);
        Assert.Equal("16:29:52.795", row.TimeText);
        Assert.Equal("2026-10-30 16:29:52.795", row.FullTimeText);
        Assert.Contains("16:29:54.795", repeated.RepeatedText);

        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        f.Format.Set(TimeFormat.Utc);

        Assert.True(f.Panel.IsUtc);
        Assert.Equal("06:29:52.795", row.TimeText);
        Assert.Equal("2026-10-30 06:29:52.795 UTC", row.FullTimeText);
        Assert.Contains("06:29:54.795", repeated.RepeatedText);
        Assert.Contains(nameof(LogRowViewModel.TimeText), changed);
        Assert.Contains(nameof(LogRowViewModel.FullTimeText), changed);

        // New rows arrive in the current preference.
        f.Add(Datasets, "Later");
        Assert.Equal("06:29:54.795", f.Panel.Entries[^1].TimeText);
    }

    [Fact]
    public void Day_dividers_mark_each_new_day_once_the_rows_span_more_than_one()
    {
        var f = new Fixture(capacity: 4);
        // 13:30 and 13:59 UTC are 23:30 and 23:59 on the 30th at +10:00.
        f.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 30, 13, 30, 0, TimeSpan.Zero));
        f.Add(Datasets, "a");
        f.Clock.Advance(TimeSpan.FromMinutes(29));
        f.Add(Commands, "b");
        Assert.All(f.Panel.Entries, r => Assert.False(r.ShowsDayDivider));

        // Past local midnight: the incremental append labels both days.
        f.Clock.Advance(TimeSpan.FromMinutes(2));
        f.Add(Datasets, "c");
        Assert.Equal([true, false, true], f.Panel.Entries.Select(r => r.ShowsDayDivider));
        Assert.Equal(new DateOnly(2026, 10, 31), f.Panel.Entries[2].Day);

        // In UTC all three are on the 30th.
        f.Format.Set(TimeFormat.Utc);
        Assert.All(f.Panel.Entries, r => Assert.False(r.ShowsDayDivider));
        f.Format.Set(TimeFormat.Local);
        Assert.Equal([true, false, true], f.Panel.Entries.Select(r => r.ShowsDayDivider));

        // Filtering to one day drops them; clearing the filter brings them back.
        f.Panel.SearchText = "area:Datasets";
        Assert.Equal([true, true], f.Panel.Entries.Select(r => r.ShowsDayDivider));
        f.Panel.SearchText = "-area:Datasets";
        Assert.Equal([false], f.Panel.Entries.Select(r => r.ShowsDayDivider));
        f.Panel.SearchText = string.Empty;
        Assert.Equal([true, false, true], f.Panel.Entries.Select(r => r.ShowsDayDivider));

        // Once the buffer evicts the first day, only one day is left.
        f.Add(Commands, "d");
        f.Add(Commands, "e");
        f.Add(Commands, "f");
        Assert.Equal(["c", "d", "e", "f"], Shown(f.Panel));
        Assert.All(f.Panel.Entries, r => Assert.False(r.ShowsDayDivider));
    }

    [AvaloniaFact]
    public void View_details_header_shows_the_full_time_and_copies_the_entry()
    {
        var f = new Fixture();
        f.Add(Datasets, "Load failed", LogLevel.Error);
        using var host = ViewHost.Show(new LogsView { DataContext = f.Panel }, width: 1100, height: 300);
        Assert.False(host.IsShown("Logs.Details.Splitter"));
        Assert.False(host.IsShown("Logs.Details"));

        f.Panel.SelectedEntry = f.Panel.Entries[0];
        host.Window.UpdateLayout();

        Assert.True(host.IsShown("Logs.Details.Splitter"));
        Assert.Equal("2026-10-30 16:29:52.795", host.Find<SelectableTextBlock>("Logs.Details.Time").Text);
        Assert.Equal(LogsView.DefaultDetailsWidth, host.Find<Border>("Logs.Details").Bounds.Width, 1);

        string? copied = null;
        f.Panel.CopyRequested += (_, text) => copied = text;
        host.Click(host.Find<Button>("Logs.Details.CopyEntry"));
        Assert.NotNull(copied);
        Assert.Contains("Load failed", copied);

        f.Format.Set(TimeFormat.Utc);
        host.Window.UpdateLayout();
        Assert.Equal("2026-10-30 06:29:52.795 UTC", host.Find<SelectableTextBlock>("Logs.Details.Time").Text);
    }

    [AvaloniaFact]
    public void View_details_pane_stays_within_its_width_limits()
    {
        var f = new Fixture();
        f.Add(Datasets, "one");
        using var host = ViewHost.Show(new LogsView { DataContext = f.Panel }, width: 560, height: 300);
        f.Panel.SelectedEntry = f.Panel.Entries[0];
        host.Window.UpdateLayout();

        // 60% of a 560 px panel is 336, so the default 380 is held to it.
        var body = host.Find<Grid>(g => g.Name == "LogsBody");
        var details = host.Find<Border>("Logs.Details");
        Assert.Equal(body.Bounds.Width * LogsView.MaxDetailsShare, details.Bounds.Width, 1);
    }
}
