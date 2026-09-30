using EncDotNet.S100.Collections;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Library row's primary state (the swatch that mirrors the map outline)
/// and secondary tags (UX refinement §6).
/// </summary>
public sealed class LibraryItemViewModelTests
{
    private static readonly LibrarySource Source = new(
        new NoaaEncFeedSource(Guid.NewGuid(), null, NoaaEncFeedSource.DefaultCatalogUri, NoaaEncFilter.All),
        null, LibrarySourceState.Ready);

    private static CollectionItem Online(int edition = 46, int update = 0) =>
        LibraryDownloadServiceTests.Cell(update) with
        {
            Edition = edition,
            Location = new RemoteItemLocation(new Uri("https://example.test/US4OH1MK.zip"), 3_040_870),
        };

    private static LibraryItemViewModel Row(CollectionItem item, LibraryLoadState state = LibraryLoadState.None, bool outdated = false) =>
        new(item, Source, _ => state, new StubDownloader(outdated));

    [Theory]
    [InlineData("Local", "Local")]
    [InlineData("Loaded", "Local")]
    [InlineData("Deferred", "Local")]
    [InlineData("Outdated", "Local")]
    [InlineData("Online", "Online")]
    [InlineData("Listed", "Listed")]
    [InlineData("Missing", "Missing")]
    public void Every_availability_has_exactly_one_primary_state(string availability, string primary)
    {
        Assert.Equal(
            Enum.Parse<LibraryPrimaryAvailability>(primary),
            LibraryOutlineStyles.Primary(Enum.Parse<LibraryAvailability>(availability)));
    }

    [Fact]
    public void The_swatch_and_the_map_share_one_style_per_primary_state()
    {
        var styles = Enum.GetValues<LibraryPrimaryAvailability>().Select(LibraryOutlineStyles.For).ToArray();

        Assert.Equal(styles.Length, styles.Distinct().Count());
        Assert.Null(LibraryOutlineStyles.Local.DashArray);  // solid
        Assert.NotNull(LibraryOutlineStyles.Online.DashArray);
    }

    [Fact]
    public void Outlines_are_thin_with_butt_caps_except_the_listed_dots()
    {
        Assert.Equal(1.0, LibraryOutlineStyles.Local.Width);
        Assert.Equal(1.0, LibraryOutlineStyles.Online.Width);
        Assert.Equal(1.2, LibraryOutlineStyles.Listed.Width);
        Assert.Equal(1.2, LibraryOutlineStyles.Missing.Width);
        Assert.Equal([5f, 3f], LibraryOutlineStyles.Online.DashArray);
        Assert.Equal([0f, 3f], LibraryOutlineStyles.Listed.DashArray);
        Assert.Equal([3f, 2f], LibraryOutlineStyles.Missing.DashArray);

        // A zero-length dash with round caps draws true dots; round caps elsewhere would lengthen each dash.
        Assert.True(LibraryOutlineStyles.Listed.RoundCap);
        Assert.False(LibraryOutlineStyles.Local.RoundCap);
        Assert.False(LibraryOutlineStyles.Online.RoundCap);
        Assert.False(LibraryOutlineStyles.Missing.RoundCap);
    }

    [Fact]
    public void An_online_row_has_no_tags_and_shows_its_size()
    {
        var row = Row(Online());

        Assert.Equal(LibraryPrimaryAvailability.Online, row.PrimaryAvailability);
        Assert.Empty(row.Tags);
        Assert.EndsWith("2.9 MB", row.Summary.Replace(',', '.'));
    }

    [Theory]
    [InlineData(0, "Ed 46 available")]
    [InlineData(3, "Ed 46 Upd 3 available")]
    public void An_outdated_row_is_local_and_names_the_available_edition(int update, string tag)
    {
        var row = Row(Online(46, update), outdated: true);

        Assert.Equal(LibraryAvailability.Outdated, row.Availability);
        Assert.Equal(LibraryPrimaryAvailability.Local, row.PrimaryAvailability);
        var only = Assert.Single(row.Tags);
        Assert.Equal((tag, LibraryItemTagKind.Update), (only.Text, only.Kind));
    }

    [Theory]
    [InlineData("Loaded", "Loaded", "Loaded")]
    [InlineData("Deferred", "On pan", "OnPan")]
    public void Loaded_and_on_pan_are_tags(string state, string text, string kind)
    {
        var tag = Assert.Single(Row(Online(), Enum.Parse<LibraryLoadState>(state)).Tags);

        Assert.Equal((text, Enum.Parse<LibraryItemTagKind>(kind)), (tag.Text, tag.Kind));
    }

    [Fact]
    public void Details_are_grouped_with_readable_labels_and_a_short_download_link()
    {
        var item = Online(46, 2) with
        {
            Bounds = new GeoBounds(41.2, -88.5, 41.5, -87.9),
            Properties = new Dictionary<string, string>
            {
                ["riverMiles"] = "257–285",
                ["someOtherKey"] = "x",
                ["notForNavigation"] = "true",
            },
        };
        var row = new LibraryItemViewModel(item, Source, _ => LibraryLoadState.None, new StubDownloader(false), "USACE rivers");

        Assert.Equal(["Product", "Coverage", "Source"], row.Details.Select(g => g.Title));
        Assert.Equal("PRODUCT", row.Details[0].Header);
        var fields = row.Details.SelectMany(g => g.Fields).ToDictionary(f => f.Label);
        Assert.Equal("S-57 · ENC cell", fields["Product"].Value);
        Assert.Equal("Ed 46 · Update 2", fields["Edition"].Value);
        Assert.True(fields["Edition"].IsMono);
        Assert.True(fields["North-east"].IsMono);
        Assert.Equal("USACE rivers", fields["Collection"].Value);
        Assert.Equal("example.test · US4OH1MK.zip", fields["Download"].Value);
        Assert.Equal("https://example.test/US4OH1MK.zip", fields["Download"].CopyValue);
        Assert.Equal("257–285", fields["River miles"].Value);
        Assert.Equal("x", fields["Some other key"].Value);
        Assert.DoesNotContain("notForNavigation", fields.Keys);
        Assert.True(row.NotForNavigation);
    }

    [Fact]
    public void The_header_names_the_primary_state_and_offers_load_after_download()
    {
        var row = Row(Online());

        Assert.Equal("Online · 2.9 MB", row.PrimaryStateText.Replace(',', '.'));

        // A local copy names its size too.
        var dir = Directory.CreateTempSubdirectory("library-item-");
        try
        {
            File.WriteAllBytes(Path.Combine(dir.FullName, "x.000"), new byte[2048]);
            var local = new LibraryItemViewModel(Online() with { Location = new LocalItemLocation(dir.FullName, "x.000", []) }, Source,
                _ => LibraryLoadState.None, new StubDownloader(false));
            Assert.Equal("Local · 2 KB", local.PrimaryStateText);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
        Assert.True(row.CanLoadAfterDownload);
        Assert.False(row.CanLoad);
        Assert.Equal("Load after download", row.LoadTooltip);
        Assert.Equal("Download (2.9 MB)", row.DownloadTooltip.Replace(',', '.'));
    }

    [Fact]
    public void Download_states_show_as_tags_and_progress()
    {
        var downloader = new StubDownloader(false);
        var retried = 0;
        var row = new LibraryItemViewModel(Online(), Source, _ => LibraryLoadState.None, downloader, null, _ => { retried++; return Task.CompletedTask; });

        downloader.Status = new LibraryDownloadItemStatus(LibraryDownloadItemState.Queued, 0, 3_040_870);
        row.RefreshDownload();
        Assert.Equal(LibraryItemTagKind.Queued, Assert.Single(row.Tags).Kind);
        Assert.False(row.IsDownloading);

        downloader.Status = new LibraryDownloadItemStatus(LibraryDownloadItemState.Running, 1_900_000, 3_040_870);
        row.RefreshDownload();
        Assert.True(row.IsDownloading);
        Assert.Equal(0.62, row.DownloadProgress, 2);
        Assert.Equal("1.8 MB / 2.9 MB", row.DownloadProgressText!.Replace(',', '.'));
        Assert.Empty(row.Tags);

        downloader.Status = new LibraryDownloadItemStatus(LibraryDownloadItemState.Failed, 0, null, "404");
        row.RefreshDownload();
        var failed = Assert.Single(row.Tags);
        Assert.Equal(("Failed · retry", LibraryItemTagKind.Failed), (failed.Text, failed.Kind));
        failed.Command!.Execute(null);
        Assert.Equal(1, retried);
        Assert.Equal("Failed: 404", row.Details.SelectMany(g => g.Fields).Single(f => f.Label == "Last download").Value);
    }

    private sealed class StubDownloader(bool outdated) : ILibraryDownloader
    {
        public LibraryDownloadItemStatus? Status { get; set; }

        public LibraryDownloadItemStatus? StatusOf(CollectionItem item) => Status;

        public event EventHandler? Changed { add { } remove { } }

        public CollectionItem Localize(CollectionItem item) => item;

        public bool IsOutdated(CollectionItem item) => outdated;

        public bool CanDownload(CollectionItem item) => true;

        public Task<LibraryDownloadResult> DownloadAsync(IReadOnlyList<CollectionItem> items, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LibraryDownloadResult(0, 0, false));
    }
}
