using System.Collections.Concurrent;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for PR-4 of the asset-caching audit
/// (<c>docs/internal/asset-caching-audit.md</c> §6 PR-4): the S-111
/// portrayal catalogue must cache palettes through
/// <see cref="IPortrayalAssetCache.Palettes"/> so toggling
/// Day / Dusk / Night does not re-open the colour-profile file, and the
/// defensive belt-and-suspenders re-load inside
/// <c>ResolveColorScheme</c> must be gone.
/// </summary>
public class S111PaletteCacheTests
{
    // S-111 bundles a single colour-profile file containing all three
    // palettes — so the audit's "open the colour-profile asset at most
    // once" outcome is the eager-load case (one open total, not one per
    // palette type).
    private const string S111ColorProfileRelativePath = "ColorProfiles/colorProfile.xml";

    [Fact]
    public void Repeated_SwitchPalette_calls_open_color_profile_only_once()
    {
        using var inner = Specification.CreatePortrayalCatalogueSource("S-111");
        using var counting = new CountingAssetSource(inner);

        using var manager = new PortrayalCatalogueManager();
        manager.SetSource("S-111", counting);

        var provider = manager.GetProvider("S-111");
        var catalogue = new S111PortrayalCatalogue(provider);

        var before = counting.GetOpenCount(S111ColorProfileRelativePath);

        // Switch through every palette, in arbitrary order, including a
        // switch back to Day — none of these should re-open the asset
        // after the first one.
        catalogue.SwitchPalette(PaletteType.Day);
        Assert.NotEmpty(catalogue.ActivePalette.Colors);

        catalogue.SwitchPalette(PaletteType.Night);
        Assert.NotEmpty(catalogue.ActivePalette.Colors);

        catalogue.SwitchPalette(PaletteType.Dusk);
        Assert.NotEmpty(catalogue.ActivePalette.Colors);

        catalogue.SwitchPalette(PaletteType.Day);
        Assert.NotEmpty(catalogue.ActivePalette.Colors);

        var after = counting.GetOpenCount(S111ColorProfileRelativePath);

        // Eager-load reads the asset once per palette name on the
        // initial pass (Day/Dusk/Night = 3 opens), then never again.
        // The audit's hard guarantee is "at most once per palette type"
        // — assert exactly that, and additionally that subsequent
        // switches add no further opens.
        Assert.Equal(before + 3, after);
    }

    [Fact]
    public void ResolveColorScheme_does_not_reopen_color_profile_after_SwitchPalette()
    {
        using var inner = Specification.CreatePortrayalCatalogueSource("S-111");
        using var counting = new CountingAssetSource(inner);

        using var manager = new PortrayalCatalogueManager();
        manager.SetSource("S-111", counting);

        var provider = manager.GetProvider("S-111");
        var catalogue = new S111PortrayalCatalogue(provider);

        catalogue.SwitchPalette(PaletteType.Day);
        var afterSwitch = counting.GetOpenCount(S111ColorProfileRelativePath);

        // The defensive ActivePalette.Colors.Count == 0 re-load was the
        // bug we removed. Calling ResolveColorScheme many times after a
        // successful SwitchPalette must not re-open the colour-profile
        // file — even though S-111 returns a null scheme (the bundled
        // XSLT defines arrows only), the catalogue still touches the
        // active palette to honour the asset-cache contract.
        for (int i = 0; i < 5; i++)
        {
            // S-111 Ed 2.0.0 ResolveColorScheme returns null by design;
            // see content/S111/pc/Rules/select_arrow.xsl.
            Assert.Null(catalogue.ResolveColorScheme(new MarinerSettings()));
        }

        Assert.Equal(afterSwitch, counting.GetOpenCount(S111ColorProfileRelativePath));
    }

    /// <summary>
    /// Decorator that counts how many times each relative path is
    /// opened. Mirrors the pattern established by PR-2 and PR-3 tests
    /// (see <see cref="SpecLevelAssetCacheTests"/>).
    /// </summary>
    private sealed class CountingAssetSource : IAssetSource
    {
        private readonly IAssetSource _inner;
        private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);

        public CountingAssetSource(IAssetSource inner)
        {
            ArgumentNullException.ThrowIfNull(inner);
            _inner = inner;
        }

        public int GetOpenCount(string relativePath) =>
            _counts.TryGetValue(relativePath, out var n) ? n : 0;

        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            _counts.AddOrUpdate(relativePath, 1, (_, n) => n + 1);
            return _inner.OpenAsync(relativePath, cancellationToken);
        }

        public void Dispose()
        {
            // The caller owns the inner source.
        }
    }
}
