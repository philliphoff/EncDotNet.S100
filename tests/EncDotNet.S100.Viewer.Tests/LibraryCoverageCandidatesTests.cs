using EncDotNet.S100.Collections;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Which listed datasets the Library coverage overlay outlines at a scale
/// (<see cref="LibraryCoverageOverlayController.Candidates"/>).
/// </summary>
public sealed class LibraryCoverageCandidatesTests
{
    private static readonly LibrarySource Source = new(
        new LocalFolderSource(Guid.NewGuid(), null, "/charts"), null, LibrarySourceState.Ready);

    private static LibraryItemViewModel Row(string name, int coarsest, int finest) =>
        new(new CollectionItem
        {
            Key = name,
            ProductSpec = "S-101",
            Name = name,
            MinimumDisplayScale = coarsest,
            MaximumDisplayScale = finest,
            Bounds = new GeoBounds(50.5, -1.5, 50.9, -1.0),
            Location = new LocalItemLocation("/charts", name + ".000", []),
        }, Source, _ => LibraryLoadState.None, null, null);

    private static string[] Outlined(IEnumerable<LibraryItemViewModel> rows, double scale) =>
        LibraryCoverageOverlayController.Candidates(rows, scale).Select(r => r.Name).ToArray();

    private static readonly LibraryItemViewModel[] Solent =
    [
        Row("coastal", 350_000, 180_000),
        Row("approach", 180_000, 90_000),
        Row("harbour", 22_000, 12_000),
    ];

    [Fact]
    public void Zoomed_out_every_listed_dataset_is_outlined()
    {
        // As the UK S-101 group viewed at about 1:1,000,000.
        Assert.Equal(["coastal", "approach", "harbour"], Outlined(Solent, 1_000_000));
    }

    [Fact]
    public void Zoomed_in_datasets_well_past_their_finest_scale_drop_away()
    {
        Assert.Equal(["approach", "harbour"], Outlined(Solent, 25_000));  // coastal stops at 1:45,000
        Assert.Equal(["harbour"], Outlined(Solent, 5_000));
    }
}
