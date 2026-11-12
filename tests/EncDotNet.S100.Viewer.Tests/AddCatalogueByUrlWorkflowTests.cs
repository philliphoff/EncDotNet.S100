using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Viewer.Library;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Adding a catalogue by URL (#1399): checked, deduplicated, recognised and kept.</summary>
public sealed class AddCatalogueByUrlWorkflowTests
{
    private static readonly KnownCatalogueSource Listed = KnownCatalogueSources.All[0];

    [Fact]
    public async Task A_recognised_url_is_kept_in_the_store()
    {
        var store = TestCatalogueDirectory.EmptyStore();
        var workflow = TestCatalogueDirectory.AddByUrl(
            (_, _) => Task.FromResult(new CatalogueProbe("RncProductCatalogChartCatalogs", KnownCatalogueFormat.ChartCatalogs, "Lists")), store);

        var outcome = await workflow.RunAsync(new AddCatalogueByUrlRequest(" https://example.test/c.xml ", [Listed]), TestContext.Current.CancellationToken);

        Assert.Null(outcome.Error);
        Assert.False(outcome.AlreadyListed);
        Assert.Equal("Lists", outcome.Source!.Name);
        Assert.Equal(KnownCatalogueFormat.ChartCatalogs, outcome.Source.Format);
        Assert.Equal([outcome.Source.Id], store.Sources.Select(s => s.Id));
        Assert.Same(store.Sources[0], workflow.Added.Single());
        Assert.True(workflow.Remove(outcome.Source.Id));
        Assert.Empty(workflow.Added);
    }

    [Fact]
    public async Task A_listed_url_is_returned_without_reading_it()
    {
        var reads = 0;
        var workflow = TestCatalogueDirectory.AddByUrl((_, _) =>
        {
            reads++;
            return Task.FromResult(new CatalogueProbe(null, null, null));
        });

        var outcome = await workflow.RunAsync(new AddCatalogueByUrlRequest(Listed.CatalogUri.AbsoluteUri, [Listed]), TestContext.Current.CancellationToken);

        Assert.True(outcome.AlreadyListed);
        Assert.Same(Listed, outcome.Source);
        Assert.Equal(0, reads);
        Assert.Empty(workflow.Added);
    }

    [Theory]
    [InlineData("not a url", "http or https")]
    [InlineData("https://example.test/down", "offline")]
    public async Task Failures_say_why(string url, string expected)
    {
        var workflow = TestCatalogueDirectory.AddByUrl((_, _) => throw new HttpRequestException("offline"));

        var outcome = await workflow.RunAsync(new AddCatalogueByUrlRequest(url, []), TestContext.Current.CancellationToken);

        Assert.Null(outcome.Source);
        Assert.Contains(expected, outcome.Error);
        Assert.Empty(workflow.Added);
    }
}
