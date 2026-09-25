using System.Net;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Noaa;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Collections.Tests;

public sealed class NoaaEncFeedIndexerTests : IDisposable
{
    private readonly TempDirectory _cache = new();
    private readonly FakeFeedServer _server = new(File.ReadAllBytes(TestPaths.Fixture("noaa-enc-prodcat.xml")));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly NoaaEncFeedIndexer _feeds;
    private readonly CollectionIndexer _indexer;

    public NoaaEncFeedIndexerTests()
    {
        _feeds = new NoaaEncFeedIndexer(new HttpClient(_server), _cache.Path, timeProvider: _time);
        _indexer = CollectionIndexer.CreateDefault(feeds: [_feeds]);
    }

    public void Dispose() => _cache.Dispose();

    private static NoaaEncFeedSource Feed(NoaaEncFilter? filter = null) =>
        new(Guid.NewGuid(), null, NoaaEncFeedSource.DefaultCatalogUri, filter ?? NoaaEncFilter.All);

    private static string[] Names(SourceIndex index) => index.Items.Select(i => i.Name).ToArray();

    [Fact]
    public async Task Unscoped_feed_indexes_every_active_cell_as_an_online_item()
    {
        var index = await _indexer.IndexAsync(Feed());

        Assert.Equal(["US1GLBBA", "US1GLBDA", "US1GLBDS", "US2PACTX", "US3TC300", "US5MI62M"], Names(index));
        Assert.Empty(index.Diagnostics);
        Assert.StartsWith("noaa-v1:", index.Fingerprint);

        var item = index.Items.Single(i => i.Name == "US2PACTX");
        Assert.Equal("S-57", item.ProductSpec);
        Assert.Equal(2, item.UsageBand);
        Assert.Equal(CollectionItemStatus.Active, item.Status);
        Assert.Equal("CA", item.Properties["states"]);
        var location = Assert.IsType<RemoteItemLocation>(item.Location);
        Assert.Equal(new Uri("https://www.charts.noaa.gov/ENCs/US2PACTX.zip"), location.Uri);
        Assert.Equal(30_318, location.SizeBytes);
        Assert.NotNull(location.LastModified);
    }

    [Theory]
    [InlineData("US3TC300", 140.29, 143.1063893)]  // continuous longitudes past −180 (Micronesia)
    [InlineData("US1GLBDS", 165.666667, 180.0)]    // ends exactly on the antimeridian
    [InlineData("US1GLBDA", -180.0, -153.6)]       // starts exactly on the antimeridian
    public void Bounds_normalise_continuous_longitudes(string name, double west, double east)
    {
        var cell = NoaaEncProductCatalogReader.Read(TestPaths.Fixture("noaa-enc-prodcat.xml"))
            .Cells.Single(c => c.Name == name);

        var bounds = NoaaEncFeedIndexer.Map(cell).Bounds!.Value;

        Assert.Equal(west, bounds.West, 5);
        Assert.Equal(east, bounds.East, 5);
        Assert.False(bounds.CrossesAntimeridian);
    }

    [Fact]
    public void Interior_panels_become_holes_of_their_exterior()
    {
        var cell = NoaaEncProductCatalogReader.Read(TestPaths.Fixture("noaa-enc-prodcat.xml"))
            .Cells.Single(c => c.Name == "US5MI62M");

        var polygon = Assert.Single(NoaaEncFeedIndexer.Map(cell).Coverage!.Polygons);

        Assert.Single(polygon.Holes);
    }

    [Fact]
    public async Task Filters_select_by_state_district_or_region_and_exclude_cancelled_cells()
    {
        var alaska = await _indexer.IndexAsync(Feed(new NoaaEncFilter { States = ["ak"] }));
        Assert.Equal(["US1GLBDA", "US1GLBDS"], Names(alaska));

        var withCancelled = await _indexer.IndexAsync(Feed(new NoaaEncFilter { States = ["AK"], IncludeCancelled = true }));
        Assert.Equal(["US1EEZ1M", "US1GLBDA", "US1GLBDS"], Names(withCancelled));
        Assert.Equal(CollectionItemStatus.Cancelled, withCancelled.Items[0].Status);
        Assert.Null(withCancelled.Items[0].Bounds);

        var union = await _indexer.IndexAsync(Feed(new NoaaEncFilter { States = ["HI"], Regions = [12], CoastGuardDistricts = [9] }));
        Assert.Equal(["US1GLBBA", "US2PACTX", "US5MI62M"], Names(union));

        // One download serves every filter.
        Assert.Equal(1, _server.Requests);
    }

    [Fact]
    public async Task Refresh_reuses_the_index_until_the_server_reports_a_change()
    {
        var source = Feed();
        var first = await _indexer.IndexAsync(source);
        Assert.Equal(1, _server.Requests);

        // Within the revalidation interval: no request at all.
        Assert.Same(first, await _indexer.IndexAsync(source, first));
        Assert.Equal(1, _server.Requests);

        // After it: a conditional request, answered 304.
        _time.Advance(TimeSpan.FromMinutes(20));
        Assert.Same(first, await _indexer.IndexAsync(source, first));
        Assert.Equal(2, _server.Requests);
        Assert.Equal(1, _server.NotModifiedResponses);

        // The server publishes a new catalogue.
        _time.Advance(TimeSpan.FromMinutes(20));
        _server.Publish(File.ReadAllBytes(TestPaths.Fixture("noaa-enc-prodcat.xml")), "\"v2\"");
        var rebuilt = await _indexer.IndexAsync(source, first);
        Assert.NotSame(first, rebuilt);
        Assert.NotEqual(first.Fingerprint, rebuilt.Fingerprint);
    }

    [Fact]
    public async Task Changing_the_filter_rebuilds_without_downloading_again()
    {
        var source = Feed();
        var first = await _indexer.IndexAsync(source);

        var narrowed = source with { Filter = new NoaaEncFilter { States = ["CA"] } };
        var second = await _indexer.IndexAsync(narrowed, first);

        Assert.NotSame(first, second);
        Assert.Equal(["US2PACTX"], Names(second));
        Assert.Equal(1, _server.Requests);
    }

    [Fact]
    public async Task Offline_refresh_serves_the_cached_catalogue_with_a_warning()
    {
        var source = Feed();
        var first = await _indexer.IndexAsync(source);

        _time.Advance(TimeSpan.FromHours(1));
        _server.Offline = true;

        // The fingerprint still matches, so an existing index is kept...
        Assert.Same(first, await _indexer.IndexAsync(source, first));

        // ...and a fresh index is built from the cached copy.
        var offline = await _feeds.IndexAsync(source, null, CancellationToken.None);
        Assert.Equal(Names(first), Names(offline));
        var warning = Assert.Single(offline.Diagnostics);
        Assert.Equal(IndexDiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(first.Fingerprint, offline.Fingerprint);
    }

    [Fact]
    public async Task Offline_without_a_cached_copy_reports_an_error()
    {
        _server.Offline = true;

        var index = await _indexer.IndexAsync(Feed());

        Assert.Empty(index.Items);
        Assert.Null(index.Fingerprint);
        Assert.Equal(IndexDiagnosticSeverity.Error, Assert.Single(index.Diagnostics).Severity);
    }

    [Fact]
    public async Task Interrupted_download_keeps_the_previous_copy()
    {
        var source = Feed();
        var first = await _indexer.IndexAsync(source);

        _time.Advance(TimeSpan.FromHours(1));
        _server.Publish(File.ReadAllBytes(TestPaths.Fixture("noaa-enc-prodcat.xml")), "\"v2\"");
        _server.FailMidBody = true;

        var index = await _feeds.IndexAsync(source, null, CancellationToken.None);

        Assert.Equal(Names(first), Names(index));
        Assert.Equal(IndexDiagnosticSeverity.Warning, Assert.Single(index.Diagnostics).Severity);
        Assert.Empty(Directory.EnumerateFiles(_cache.Path, "*.partial"));
    }

    [Fact]
    public async Task Facets_count_active_cells_once_per_value()
    {
        var catalog = await _feeds.GetCatalogAsync(NoaaEncFeedSource.DefaultCatalogUri);

        var facets = NoaaEncFacets.Compute(catalog);

        var alaska = facets.States.Single(f => f.Value == "AK");
        Assert.Equal(2, alaska.CellCount);
        Assert.Equal(1_624_954 + 496_603, alaska.TotalBytes);
        Assert.DoesNotContain(facets.States, f => f.Value == "WA");  // only the cancelled US1EEZ1M is in WA
        Assert.Equal(2, facets.CoastGuardDistricts.Single(f => f.Value == "17").CellCount);

        // Alaska and district 17 overlap: the same two cells, counted once.
        var (count, bytes) = NoaaEncFacets.Summarize(catalog, new NoaaEncFilter { States = ["AK"], CoastGuardDistricts = [17] });
        Assert.Equal(2, count);
        Assert.Equal(alaska.TotalBytes, bytes);
    }

    [Fact]
    public void Filter_equality_ignores_order_and_case()
    {
        var a = new NoaaEncFilter { States = ["AK", "wa"], Regions = [36, 34] };
        var b = new NoaaEncFilter { States = ["WA", "ak"], Regions = [34, 36] };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, b with { IncludeCancelled = true });
    }

    /// <summary>An in-memory HTTP server for one catalogue, honouring If-None-Match.</summary>
    private sealed class FakeFeedServer(byte[] content) : HttpMessageHandler
    {
        private byte[] _content = content;
        private string _etag = "\"v1\"";

        public int Requests { get; private set; }

        public int NotModifiedResponses { get; private set; }

        public bool Offline { get; set; }

        public bool FailMidBody { get; set; }

        public void Publish(byte[] content, string etag)
        {
            _content = content;
            _etag = etag;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Offline)
                throw new HttpRequestException("Network unreachable.");

            if (request.Headers.IfNoneMatch.Any(t => t.Tag == _etag))
            {
                NotModifiedResponses++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }

            Stream body = FailMidBody ? new FailingStream(_content) : new MemoryStream(_content);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) };
            response.Headers.ETag = System.Net.Http.Headers.EntityTagHeaderValue.Parse(_etag);
            return Task.FromResult(response);
        }
    }

    /// <summary>A body stream that fails after its first chunk.</summary>
    private sealed class FailingStream(byte[] content) : MemoryStream(content)
    {
        private bool _served;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_served)
                throw new IOException("Connection reset.");
            _served = true;
            return base.Read(buffer, offset, Math.Min(count, 100));
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_served)
                throw new IOException("Connection reset.");
            _served = true;
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, 100)], cancellationToken);
        }
    }
}
