using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Warning numbers in a message that link to other listed warnings, so a
/// "warnings in force" bulletin works as an index (#932).
/// </summary>
public sealed class NavigationalWarningLinksTests
{
    private static readonly DatasetEntry Amsa = new("/tmp/amsa.gml", "S-124");
    private static readonly DatasetEntry Copy = new("/tmp/copy.gml", "S-124");

    private static NavigationalWarningInfo Info(string series, string number) => new() { Series = series, Number = number };

    private static NavigationalWarningEntry Entry(string series, string number, DatasetEntry? dataset = null, string? id = null) =>
        new(dataset ?? Amsa, id ?? $"{series}.{number}", 0, Info(series, number), S100GeometryType.Point, null);

    private static readonly NavigationalWarningInfo Bulletin = Info("NAVAREA X", "110/26");

    private static List<(string Text, string? Target)> Links(string text, NavigationalWarningInfo self, params NavigationalWarningEntry[] warnings) =>
        NavigationalWarningLinks.Split(text, self, Amsa.DisplayName, warnings)
            .Where(s => s.IsLink)
            .Select(s => (s.Text, (string?)s.Target!.FeatureRef))
            .ToList();

    [Fact]
    public void Segments_concatenate_to_the_text_and_keep_line_breaks()
    {
        const string text = "NAVAREA X WARNINGS IN FORCE:\n   098/26  106/26\n   112/26\nNNNN";
        var segments = NavigationalWarningLinks.Split(text, Bulletin, Amsa.DisplayName,
            [Entry("NAVAREA X", "098/26"), Entry("NAVAREA X", "106/26")]);

        Assert.Equal(text, string.Concat(segments.Select(s => s.Text)));
        Assert.Equal(["098/26", "106/26"], segments.Where(s => s.IsLink).Select(s => s.Text));
    }

    [Fact]
    public void Numbers_not_in_the_list_stay_plain()
    {
        var links = Links("IN FORCE: 098/26 099/26", Bulletin, Entry("NAVAREA X", "098/26"));

        Assert.Equal([("098/26", (string?)"NAVAREA X.098/26")], links);
    }

    [Fact]
    public void The_cards_own_number_stays_plain()
    {
        var links = Links("NAVAREA X 110/26\nIN FORCE: 106/26 110/26", Bulletin,
            Entry("NAVAREA X", "110/26"), Entry("NAVAREA X", "106/26"));

        Assert.Equal(["106/26"], links.Select(l => l.Text));
    }

    [Fact]
    public void Series_written_before_the_number_picks_that_series()
    {
        var links = Links("SEE AUSCOAST 314/26 AND NAVAREA X 314/26.", Bulletin,
            Entry("NAVAREA X", "314/26"), Entry("AUSCOAST", "314/26"));

        Assert.Equal(["AUSCOAST.314/26", "NAVAREA X.314/26"], links.Select(l => l.Target));
    }

    [Fact]
    public void Series_written_before_the_number_with_no_such_warning_stays_plain()
    {
        // NAVAREA X 314/26 exists, but the text names AUSCOAST 314/26, which doesn't.
        var links = Links("AUSCOAST 314/26", Bulletin, Entry("NAVAREA X", "314/26"), Entry("AUSCOAST", "001/26"));

        Assert.Empty(links);
    }

    [Fact]
    public void Heading_series_applies_to_the_numbers_below_it()
    {
        const string text = "NAVAREA X WARNINGS IN FORCE:\n 106/26\nAUSCOAST WARNINGS IN FORCE:\n 106/26 314/26";
        var links = Links(text, Bulletin,
            Entry("NAVAREA X", "106/26"), Entry("AUSCOAST", "106/26"), Entry("AUSCOAST", "314/26"));

        Assert.Equal(["NAVAREA X.106/26", "AUSCOAST.106/26", "AUSCOAST.314/26"], links.Select(l => l.Target));
    }

    [Fact]
    public void Without_a_series_in_the_text_the_cards_own_series_wins()
    {
        var links = Links("IN FORCE: 106/26", Bulletin, Entry("AUSCOAST", "106/26"), Entry("NAVAREA X", "106/26"));

        Assert.Equal(["NAVAREA X.106/26"], links.Select(l => l.Target));
    }

    [Fact]
    public void Otherwise_a_number_unique_across_the_list_links()
    {
        var links = Links("IN FORCE: 314/26 106/26", Bulletin,
            Entry("AUSCOAST", "314/26"), Entry("AUSCOAST", "106/26"), Entry("NAVAREA XI", "106/26"));

        // 314/26 is only AUSCOAST's; 106/26 is in two other series, so it is ambiguous.
        Assert.Equal(["AUSCOAST.314/26"], links.Select(l => l.Target));
    }

    [Fact]
    public void Longer_series_names_win_over_their_prefixes()
    {
        var links = Links("NAVAREA XI 106/26", Bulletin, Entry("NAVAREA X", "106/26"), Entry("NAVAREA XI", "106/26"));

        Assert.Equal(["NAVAREA XI.106/26"], links.Select(l => l.Target));
    }

    [Fact]
    public void Series_match_ignores_case_and_spacing()
    {
        var links = Links("navarea  x 106/26", Info("AUSCOAST", "001/26"), Entry("NAVAREA X", "106/26"), Entry("AUSCOAST", "106/26"));

        Assert.Equal(["NAVAREA X.106/26"], links.Select(l => l.Target));
    }

    [Fact]
    public void Dates_and_longer_numbers_are_not_warning_numbers()
    {
        var links = Links("UNTIL 12/10/26. REF 2106/265. 106/26", Bulletin, Entry("NAVAREA X", "106/26"), Entry("NAVAREA X", "10/26"));

        Assert.Equal(["106/26"], links.Select(l => l.Text));
    }

    [Fact]
    public void A_warning_in_two_datasets_links_to_the_cards_own_dataset_first()
    {
        var links = NavigationalWarningLinks.Split("106/26", Bulletin, Copy.DisplayName,
            [Entry("NAVAREA X", "106/26", Amsa, "a"), Entry("NAVAREA X", "106/26", Copy, "b")]);

        Assert.Equal("b", Assert.Single(links, s => s.IsLink).Target!.FeatureRef);
    }

    [Fact]
    public void No_listed_warnings_gives_one_plain_run()
    {
        var segments = NavigationalWarningLinks.Split("106/26", Bulletin, null, []);

        Assert.False(Assert.Single(segments).IsLink);
    }
}
