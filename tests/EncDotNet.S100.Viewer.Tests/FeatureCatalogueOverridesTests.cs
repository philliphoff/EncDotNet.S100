using System.IO;
using EncDotNet.S100.Features;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.Viewer.Services;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Tests for <see cref="FeatureCatalogueOverrides"/> covering the
/// PR-C follow-up wiring: with no transient/persisted overrides in
/// scope, <c>Open</c> must return <c>null</c> so that the bundled
/// fallback registered through
/// <c>FeatureCatalogueManager.SetSource</c> (using
/// <see cref="Specification.CreateFeatureCatalogueSource(string)"/>)
/// fires. Mirrors the bundled-source seeding done for Portrayal
/// Catalogues via <c>PortrayalCatalogueSeeder</c>.
/// </summary>
public class FeatureCatalogueOverridesTests
{
    private static ViewerSettings NewSettings()
    {
        return new ViewerSettings
        {
            SettingsFilePath = Path.Combine(Path.GetTempPath(), "fco-tests-" + System.Guid.NewGuid() + ".json"),
        };
    }

    [Fact]
    public void Open_NoOverrides_ReturnsNull_SoBundledSetSourceFires()
    {
        var settings = NewSettings();
        var overrides = new FeatureCatalogueOverrides(settings);

        // Pre PR-C-wiring this returned a fresh bundled stream every
        // call, which prevented FeatureCatalogueManager.SetSource (with
        // CachingAssetSource) from ever being consulted because the
        // resolver always won. After the wiring it must return null so
        // the SetSource path takes over for the bundled fallback.
        Assert.Null(overrides.Open("S-101"));
        Assert.Null(overrides.Open("S-131"));
    }

    [Fact]
    public void Open_TransientPath_Wins()
    {
        var settings = NewSettings();
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, "<fc/>");
            var overrides = new FeatureCatalogueOverrides(settings);
            overrides.SetTransientPaths(new System.Collections.Generic.Dictionary<string, string>
            {
                ["S-101"] = tmp,
            });

            using var s = overrides.Open("S-101");
            Assert.NotNull(s);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void BundledSetSource_AndEmptyOverrides_ResolvesEveryAvailableSpec()
    {
        // Replicates the App.ConfigureServices wiring: a manager whose
        // resolver consults FeatureCatalogueOverrides (which now returns
        // null on no override) plus a SetSource registration per
        // bundled spec. Every advertised spec must round-trip a parsed
        // catalogue through the bundled fallback path.
        var settings = NewSettings();
        var overrides = new FeatureCatalogueOverrides(settings);
        using var manager = new FeatureCatalogueManager((string spec) => overrides.Open(spec));

        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasFeatureCatalogue(spec))
            {
                manager.SetSource(spec, Specification.CreateFeatureCatalogueSource(spec));
            }
        }

        foreach (var spec in Specification.AvailableSpecs)
        {
            var fc = manager.GetCatalogue(spec);
            Assert.NotNull(fc);
        }
    }
}
