using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Builds a <see cref="CatalogueRegistry"/> for tests.</summary>
internal static class TestCatalogues
{
    /// <summary>A registry over fresh managers (or <paramref name="portrayals"/>), not yet initialized.</summary>
    public static CatalogueRegistry Registry(ViewerSettings settings, PortrayalCatalogueManager? portrayals = null) =>
        new(settings, new FeatureCatalogueManager(), portrayals ?? new PortrayalCatalogueManager());
}
