using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.LazyLoading;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// <see cref="CatalogueRegistry.Identity"/>, which the portrayal warm
/// signature and the stored cell scene key on (catalogue-panels CR2): a
/// session-only catalogue and a Reload after an edit change it, so lazily
/// loaded cells never reuse a scene drawn with the old catalogue; nothing
/// else does, so stored scenes survive a restart.
/// </summary>
public sealed class CatalogueIdentityTests : IDisposable
{
    private readonly CatalogueFixtures _files = new();
    private readonly ViewerSettings _settings;

    public CatalogueIdentityTests()
    {
        _settings = new ViewerSettings { SettingsFilePath = Path.Combine(_files.Root, "settings.json") };
    }

    public void Dispose() => _files.Dispose();

    private CatalogueRegistry Registry()
    {
        var registry = new CatalogueRegistry(_settings, new FeatureCatalogueManager(), new PortrayalCatalogueManager());
        registry.Initialize(null, null);
        return registry;
    }

    private static DetectedCatalogue Detect(string path) => CatalogueSpecDetection.Detect(path).Catalogue!;

    private string Signature(CatalogueRegistry registry) =>
        PortrayalWarmSignature.Of(MapPresentationState.Default, _settings, registry.Identity);

    [Fact]
    public void A_session_only_catalogue_changes_the_signature_and_revert_restores_it()
    {
        var registry = Registry();
        var builtIn = Signature(registry);

        registry.Use(Detect(_files.PortrayalFolder("9.1.0")), keepAfterRestart: false);

        Assert.Empty(_settings.PortrayalCataloguePaths);
        Assert.NotEqual(builtIn, Signature(registry));

        registry.Revert("S-102", CatalogueKind.Portrayal);
        Assert.Equal(builtIn, Signature(registry));
    }

    [Fact]
    public void A_reload_after_an_edit_changes_the_signature()
    {
        var registry = Registry();
        var file = _files.FeatureFile("9.0.0");
        registry.Use(Detect(file));
        var before = Signature(registry);

        registry.Reload("S-102", CatalogueKind.Feature);
        Assert.Equal(before, Signature(registry));

        File.WriteAllText(file, File.ReadAllText(file) + "\n<!-- edited -->");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));
        registry.Reload("S-102", CatalogueKind.Feature);

        Assert.NotEqual(before, Signature(registry));
    }

    [Fact]
    public void A_kept_catalogue_has_the_same_identity_after_a_restart()
    {
        Registry().Use(Detect(_files.PortrayalFolder("9.1.0")), keepAfterRestart: true);
        var first = Registry().Identity;

        Assert.NotEqual(string.Empty, first);
        Assert.Equal(first, Registry().Identity);
    }
}
