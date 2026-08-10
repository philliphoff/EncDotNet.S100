using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Mcp.Tools.Mutable;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Issue #566: the headless MCP session must parse each dataset exactly once —
/// the resident processor feeds both the query projection and every render, with
/// no per-render re-parse. These tests count processor construction through an
/// injected factory to prove the single-parse invariant.
/// </summary>
public sealed class HeadlessMutableCatalogParseOnceTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    [SkippableFact]
    public async Task Load_then_render_twice_builds_one_processor()
    {
        Skip.IfNot(File.Exists(FixturePath), $"Fixture not found: {FixturePath}");

        using var counting = new CountingProcessorFactory(BundledDatasetProcessorFactory.Create());
        using var catalog = new HeadlessMutableCatalog(transforms: null, factory: counting);

        var outcome = await catalog.LoadAsync(FixturePath);
        Skip.If(outcome.Added.Count == 0, "The cell could not be loaded into the catalog.");

        // One dataset loaded → exactly one processor built. The read model was
        // projected from that same processor, not from a second parse.
        Assert.Equal(1, counting.CreateCount);
        Assert.Single(catalog.Datasets);

        using var session = new HeadlessS100Session(catalog);
        var renderer = (IImageRenderer)session;

        Assert.NotNull(await renderer.RenderToPngAsync(128, 128, pixelDensity: 1.0));
        Assert.NotNull(await renderer.RenderToPngAsync(128, 128, pixelDensity: 1.0));

        // Two renders, still one processor: renders composite the resident
        // processor rather than re-creating one from the path.
        Assert.Equal(1, counting.CreateCount);
    }

    private sealed class CountingProcessorFactory(IDatasetProcessorFactory inner)
        : IDatasetProcessorFactory, IDisposable
    {
        private int _createCount;

        public int CreateCount => Volatile.Read(ref _createCount);

        public IDatasetProcessor CreateProcessor(string path)
        {
            Interlocked.Increment(ref _createCount);
            return inner.CreateProcessor(path);
        }

        // Forward the declared-spec overload to the wrapped factory so a decorator
        // does not silently drop the --spec / catalogue-spec capability.
        public IDatasetProcessor CreateProcessor(string path, string? declaredProductSpec)
        {
            Interlocked.Increment(ref _createCount);
            return inner.CreateProcessor(path, declaredProductSpec);
        }

        public IDatasetProcessor CreateProcessorWithFilesystemUpdates(string path)
        {
            Interlocked.Increment(ref _createCount);
            return inner.CreateProcessorWithFilesystemUpdates(path);
        }

        public void Dispose() => (inner as IDisposable)?.Dispose();
    }
}
