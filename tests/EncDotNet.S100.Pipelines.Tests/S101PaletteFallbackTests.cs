using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Regression coverage for issue #321: when a portrayal catalogue's colour
/// profile is present but yields no usable palette (e.g. a colour profile that
/// only declares named colours with no sRGB values, or a format the reader does
/// not support), <see cref="S101PortrayalCatalogue.SwitchPaletteAsync"/> must
/// degrade gracefully to a usable palette rather than throwing a
/// <see cref="KeyNotFoundException"/> that aborts the whole dataset load.
/// </summary>
public class S101PaletteFallbackTests
{
    // A minimal S-101 portrayal catalogue that references a single colour
    // profile. The profile file (below) contains only named colours with no
    // sRGB values, so ColorProfileReader produces an empty palette for Day,
    // Dusk, and Night — the exact condition reported in #321.
    private const string CatalogueXml =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <portrayalCatalogue productId="S-101" version="1.0.0">
          <colorProfiles>
            <colorProfile id="1">
              <description>
                <name>Color Profile</name>
                <language>eng</language>
              </description>
              <fileName>colorProfile.xml</fileName>
              <fileType>ColorProfile</fileType>
              <fileFormat>XML</fileFormat>
            </colorProfile>
          </colorProfiles>
        </portrayalCatalogue>
        """;

    private const string EmptyColorProfileXml =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <colorProfile>
          <color token="NODTA" name="grey"><description>No data</description></color>
          <color token="CHBLK" name="black"><description>Chart black</description></color>
        </colorProfile>
        """;

    private static async Task<S101PortrayalCatalogue> CreateCatalogueAsync(string dir)
    {
        Directory.CreateDirectory(Path.Combine(dir, "ColorProfiles"));
        await File.WriteAllTextAsync(Path.Combine(dir, "portrayal_catalogue.xml"), CatalogueXml);
        await File.WriteAllTextAsync(Path.Combine(dir, "ColorProfiles", "colorProfile.xml"), EmptyColorProfileXml);

        var source = FileSystemAssetSource.Create(dir);
        var provider = await PortrayalCatalogueProvider.OpenAsync(source);
        return new S101PortrayalCatalogue(provider);
    }

    [Fact]
    public async Task SwitchPalette_WhenProfileYieldsNoColors_FallsBackInsteadOfThrowing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "encdotnet-issue321-" + Guid.NewGuid().ToString("N"));
        try
        {
            var catalogue = await CreateCatalogueAsync(dir);

            // Must not throw KeyNotFoundException (the #321 symptom).
            await catalogue.SwitchPaletteAsync(PaletteType.Day);

            Assert.NotNull(catalogue.ActivePalette);
            Assert.Same(ColorPalette.Default, catalogue.ActivePalette);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SwitchPalette_ToEveryType_NeverThrows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "encdotnet-issue321-" + Guid.NewGuid().ToString("N"));
        try
        {
            var catalogue = await CreateCatalogueAsync(dir);

            await catalogue.SwitchPaletteAsync(PaletteType.Day);
            await catalogue.SwitchPaletteAsync(PaletteType.Dusk);
            await catalogue.SwitchPaletteAsync(PaletteType.Night);

            Assert.NotNull(catalogue.ActivePalette);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// A palette load that is cancelled part-way must not poison the shared
    /// per-spec asset cache. Before the fix, the cache was marked
    /// <c>PalettesLoaded</c> up-front, so a cancelled load left it flagged
    /// loaded-but-empty and every subsequent <c>SwitchPaletteAsync('Day')</c>
    /// threw <see cref="KeyNotFoundException"/> — the #321 symptom, reproducible
    /// with the bundled catalogue and no custom configuration. A later,
    /// non-cancelled call must successfully load the real Day palette.
    /// </summary>
    [Fact]
    public async Task CancelledLoad_DoesNotPoisonCache_LaterLoadSucceeds()
    {
        using var pcSource = Specification.CreatePortrayalCatalogueSource("S-101");
        var provider = await PortrayalCatalogueProvider.OpenAsync(pcSource);
        var catalogue = new S101PortrayalCatalogue(provider);

        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await catalogue.SwitchPaletteAsync(PaletteType.Day, cts.Token));
        }

        // The cache must not have been committed by the cancelled attempt, so a
        // fresh load resolves the real, populated Day palette.
        await catalogue.SwitchPaletteAsync(PaletteType.Day);
        Assert.Equal("Day", catalogue.ActivePalette.Name);
        Assert.NotEmpty(catalogue.ActivePalette.Colors);
    }

    /// <summary>
    /// Two catalogues sharing one per-spec cache (as happens for every cell of
    /// a single S-101 exchange set) may load palettes concurrently on separate
    /// threads. The shared load gate must serialize the one-shot load so neither
    /// observes a half-populated dictionary nor throws — the multi-cell variant
    /// of the #321 failure.
    /// </summary>
    [Fact]
    public async Task ConcurrentLoads_SharingCache_BothSucceed()
    {
        using var pcSource = Specification.CreatePortrayalCatalogueSource("S-101");
        var provider = await PortrayalCatalogueProvider.OpenAsync(pcSource);

        // Both catalogues read provider.AssetCache, so they share one cache.
        var catalogueA = new S101PortrayalCatalogue(provider);
        var catalogueB = new S101PortrayalCatalogue(provider);

        await Task.WhenAll(
            Task.Run(async () => await catalogueA.SwitchPaletteAsync(PaletteType.Day)),
            Task.Run(async () => await catalogueB.SwitchPaletteAsync(PaletteType.Day)));

        Assert.Equal("Day", catalogueA.ActivePalette.Name);
        Assert.NotEmpty(catalogueA.ActivePalette.Colors);
        Assert.Equal("Day", catalogueB.ActivePalette.Name);
        Assert.NotEmpty(catalogueB.ActivePalette.Colors);
    }
}
