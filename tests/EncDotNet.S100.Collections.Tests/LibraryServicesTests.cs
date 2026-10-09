using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>A Library and its services built without dependency injection (#792).</summary>
public sealed class LibraryServicesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "library-services-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task The_services_keep_the_library_in_the_data_directory_and_read_every_catalogue_kind()
    {
        var paths = new LibraryDataPaths(_root);
        var folder = Path.Combine(_root, "charts");
        Directory.CreateDirectory(folder);

        using (var services = LibraryServices.Create(paths))
        {
            services.Library.Initialize();
            var readers = services.Readers;
            Assert.All(
                new object?[] { readers.NoaaEnc, readers.UsaceIenc, readers.S100Feed, readers.CommunityList, readers.S100Catalogue, readers.ForecastModels, readers.Secom },
                Assert.NotNull);
            foreach (var known in KnownCatalogueSources.All)
                Assert.NotNull(LibrarySourceDraft.ForCatalogue(known, readers));

            LibrarySourceDraft.ForPath(LibrarySourceKind.Folder, folder).AddTo(services.Library);
            await services.Library.WhenIdle().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(paths.CollectionsFile));
        Assert.True(Directory.Exists(paths.IndexCacheDirectory));

        using var again = LibraryServices.Create(paths);
        again.Library.Initialize();
        Assert.Equal("charts", Assert.Single(again.Library.Collections).Definition.Name);
    }
}
