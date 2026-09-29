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
        Assert.True(row.CanLoadAfterDownload);
        Assert.False(row.CanLoad);
    }

    private sealed class StubDownloader(bool outdated) : ILibraryDownloader
    {
        public event EventHandler? Changed { add { } remove { } }

        public CollectionItem Localize(CollectionItem item) => item;

        public bool IsOutdated(CollectionItem item) => outdated;

        public bool CanDownload(CollectionItem item) => true;

        public Task<LibraryDownloadResult> DownloadAsync(IReadOnlyList<CollectionItem> items, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LibraryDownloadResult(0, 0, false));
    }
}
