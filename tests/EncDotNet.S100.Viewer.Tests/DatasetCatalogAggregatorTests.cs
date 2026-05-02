using System.Collections.Generic;
using EncDotNet.S100.Viewer.Catalogs;

namespace EncDotNet.S100.Viewer.Tests;

public class DatasetCatalogAggregatorTests
{
    private sealed class FakeSource : IDatasetCatalogSource
    {
        private List<DatasetCatalogEntry> _entries = new();
        public string Id { get; }
        public string DisplayName { get; }
        public IReadOnlyList<DatasetCatalogEntry> Entries => _entries;
        public event System.EventHandler<DatasetCatalogChangedEventArgs>? Changed;

        public FakeSource(string id, params DatasetCatalogEntry[] entries)
        {
            Id = id;
            DisplayName = id;
            _entries = new List<DatasetCatalogEntry>(entries);
        }

        public void SetEntries(IEnumerable<DatasetCatalogEntry> entries)
        {
            _entries = new List<DatasetCatalogEntry>(entries);
            Changed?.Invoke(this, new DatasetCatalogChangedEventArgs(this));
        }
    }

    private static DatasetCatalogEntry Entry(string id, string sourceId) =>
        new()
        {
            Id = id,
            SourceId = sourceId,
            Title = id,
        };

    [Fact]
    public void Entries_IsUnion_OfChildSources()
    {
        var aggregator = new DatasetCatalogAggregator();
        aggregator.Add(new FakeSource("a", Entry("a-1", "a"), Entry("a-2", "a")));
        aggregator.Add(new FakeSource("b", Entry("b-1", "b")));

        var ids = aggregator.Entries.Select(e => e.Id).ToArray();

        Assert.Equal(3, ids.Length);
        Assert.Contains("a-1", ids);
        Assert.Contains("a-2", ids);
        Assert.Contains("b-1", ids);
    }

    [Fact]
    public void Add_RaisesChanged()
    {
        var aggregator = new DatasetCatalogAggregator();
        var raised = 0;
        aggregator.Changed += (_, _) => raised++;

        aggregator.Add(new FakeSource("a"));

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Remove_RaisesChanged_OnlyWhenSourceWasRegistered()
    {
        var aggregator = new DatasetCatalogAggregator();
        var src = new FakeSource("a");
        aggregator.Add(new FakeSource("kept"));
        aggregator.Add(src);

        var raised = 0;
        aggregator.Changed += (_, _) => raised++;

        Assert.True(aggregator.Remove(src));
        Assert.Equal(1, raised);

        // Removing again should be a no-op.
        Assert.False(aggregator.Remove(src));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ChildChange_PropagatesAsAggregatorChange()
    {
        var aggregator = new DatasetCatalogAggregator();
        var src = new FakeSource("a", Entry("a-1", "a"));
        aggregator.Add(src);

        var raised = 0;
        aggregator.Changed += (_, _) => raised++;

        src.SetEntries(new[] { Entry("a-1", "a"), Entry("a-2", "a") });

        Assert.Equal(1, raised);
        Assert.Equal(2, aggregator.Entries.Count);
    }

    [Fact]
    public void Remove_StopsListeningToChildChanges()
    {
        var aggregator = new DatasetCatalogAggregator();
        var src = new FakeSource("a");
        aggregator.Add(src);
        Assert.True(aggregator.Remove(src));

        var raised = 0;
        aggregator.Changed += (_, _) => raised++;

        // Aggregator should no longer react to this source.
        src.SetEntries(new[] { Entry("a-1", "a") });

        Assert.Equal(0, raised);
    }
}
