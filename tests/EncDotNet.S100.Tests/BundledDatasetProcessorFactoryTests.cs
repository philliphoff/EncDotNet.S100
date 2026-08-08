namespace EncDotNet.S100.Tests;

/// <summary>
/// Covers the one-call <see cref="BundledDatasetProcessorFactory"/> convenience
/// (issue #512 step 9): a host gets a ready-to-use
/// <c>IDatasetProcessorFactory</c> seeded with the bundled official catalogues,
/// with no hand-wiring of catalogue managers, Lua engine, CRS factory, or the
/// product registry.
/// </summary>
public sealed class BundledDatasetProcessorFactoryTests
{
    private static readonly string S124Surface =
        System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", "S124", "navwarn_surface.gml");

    [Fact]
    public void Create_ReturnsDisposableProcessorFactory()
    {
        using var factory = BundledDatasetProcessorFactory.Create();

        Assert.IsAssignableFrom<Datasets.Pipelines.IDatasetProcessorFactory>(factory);
        Assert.IsAssignableFrom<IDisposable>(factory);
    }

    [Fact]
    public void CreateProcessor_AfterDispose_Throws()
    {
        var factory = BundledDatasetProcessorFactory.Create();
        factory.Dispose();
        factory.Dispose(); // idempotent

        Assert.Throws<ObjectDisposedException>(() => factory.CreateProcessor("any.gml"));
        Assert.Throws<ObjectDisposedException>(
            () => factory.CreateProcessorWithFilesystemUpdates("any.gml"));
    }

    [SkippableFact]
    public void CreateProcessor_BuildsProcessorFromBundledCatalogues()
    {
        Skip.IfNot(File.Exists(S124Surface), "S-124 surface fixture not present.");

        using var factory = BundledDatasetProcessorFactory.Create();

        var processor = factory.CreateProcessor(S124Surface);
        try
        {
            Assert.Equal("S-124", processor.Spec.Name);
        }
        finally
        {
            (processor as IDisposable)?.Dispose();
        }
    }
}
