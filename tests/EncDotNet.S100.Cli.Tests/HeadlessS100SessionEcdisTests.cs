using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Mcp.Tools.Mutable;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Acceptance tests for issue #567: the headless composite session must honour
/// the ECDIS display <em>category</em> (and, by the same seam, per-spec hidden
/// viewing groups / display planes) carried on
/// <see cref="MapPresentationState.EcdisDisplay"/>. Before the fix the composite
/// render options carried a reduced presentation model, so
/// <c>set_display_category</c> updated session state but never reached the
/// render.
/// </summary>
public sealed class HeadlessS100SessionEcdisTests
{
    // A real ENC cell carrying non-base content; the same fixture the S-57 ECDIS
    // filter unit test uses to prove DisplayBase drops instructions relative to
    // All. Copied to TestData by the test project (see the .csproj Content item).
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    [SkippableFact]
    public async Task SetDisplayCategory_ChangesHeadlessCompositeRender()
    {
        Skip.IfNot(File.Exists(FixturePath), $"Fixture not found: {FixturePath}");

        using var catalog = new HeadlessMutableCatalog();
        var outcome = await catalog.LoadAsync(FixturePath);
        Skip.If(outcome.Added.Count == 0, "The S-57 cell could not be loaded into the catalog.");

        using var session = new HeadlessS100Session(catalog);
        var presentation = (IPresentationController)session;
        var renderer = (IImageRenderer)session;

        // "All" — no viewing-group filter — is the maximal render.
        await session.SetPresentationAsync(
            presentation.Current.WithEcdisDisplay(
                new EcdisDisplaySettings { Category = EcdisDisplayCategory.All }));
        var all = await renderer.RenderToPngAsync(256, 256, pixelDensity: 1.0);

        // "Display Base" drops every viewing group above the base minimum. For a
        // cell with non-base content this must change the pixels.
        await session.SetPresentationAsync(
            presentation.Current.WithEcdisDisplay(
                new EcdisDisplaySettings { Category = EcdisDisplayCategory.DisplayBase }));
        var displayBase = await renderer.RenderToPngAsync(256, 256, pixelDensity: 1.0);

        Assert.NotNull(all);
        Assert.NotNull(displayBase);
        Assert.False(all!.SequenceEqual(displayBase!),
            "Narrowing the ECDIS display category from All to DisplayBase should change the "
            + "headless composite render; it did not, so the category is being ignored.");
    }
}
