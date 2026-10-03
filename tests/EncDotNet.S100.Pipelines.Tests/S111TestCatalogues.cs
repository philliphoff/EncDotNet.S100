using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Catalogue manager with the bundled S-111 portrayal catalogue registered —
/// every S-111 DCF is portrayed with its SCAROW arrows, so the processor
/// requires it.
/// </summary>
internal static class S111TestCatalogues
{
    public static PortrayalCatalogueManager Create()
    {
        var manager = new PortrayalCatalogueManager();
        manager.SetSource("S-111", Specification.CreatePortrayalCatalogueSource("S-111"));
        return manager;
    }
}
