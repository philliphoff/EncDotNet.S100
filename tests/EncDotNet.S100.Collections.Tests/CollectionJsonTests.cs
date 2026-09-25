using System.Text.Json;
using EncDotNet.S100.Collections.Persistence;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Collections.Tests;

public class CollectionJsonTests
{
    [Fact]
    public void Store_round_trips_polymorphic_sources()
    {
        var document = new CollectionStoreDocument(CollectionStoreDocument.CurrentVersion,
        [
            new DatasetCollection(Guid.NewGuid(), "Alaska ENCs",
            [
                new LocalFolderSource(Guid.NewGuid(), "Charts", "/charts/AK", Recursive: false),
                new ExchangeSetSource(Guid.NewGuid(), null, "/charts/AK_ENCs.zip"),
                new S128CatalogueSource(Guid.NewGuid(), "Catalogue", "/charts/s128.gml"),
                new NoaaEncFeedSource(Guid.NewGuid(), "NOAA ENC — Alaska", NoaaEncFeedSource.DefaultCatalogUri,
                    new NoaaEncFilter { States = ["AK"], CoastGuardDistricts = [17] }),
            ], new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero)),
        ]);

        var json = CollectionJson.SerializeStore(document);
        var restored = CollectionJson.DeserializeStore(json);

        Assert.Contains("\"kind\": \"localFolder\"", json);
        Assert.Contains("\"kind\": \"noaaEncFeed\"", json);
        var collection = Assert.Single(restored.Collections);
        Assert.Equal(document.Collections[0].Name, collection.Name);
        Assert.Equal(document.Collections[0].Sources, collection.Sources);
    }

    [Fact]
    public void Store_from_a_newer_version_is_rejected()
    {
        Assert.Throws<NotSupportedException>(
            () => CollectionJson.DeserializeStore("{\"version\": 99, \"collections\": []}"));
    }

    [Fact]
    public void Index_round_trips_items_locations_and_coverage()
    {
        var ring = new[] { new GeoPosition(51, 178), new GeoPosition(52, -178), new GeoPosition(53, 179), new GeoPosition(51, 178) };
        var index = new SourceIndex(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "fp",
        [
            new CollectionItem
            {
                Key = "./US3AK7PM",
                ProductSpec = "S-57",
                Name = "US3AK7PM",
                Edition = 12,
                Update = 3,
                IssueDate = new DateOnly(2026, 3, 1),
                UsageBand = 3,
                Status = CollectionItemStatus.Active,
                Bounds = GeoBounds.FromPositions(ring),
                Coverage = new GeoCoverage([new GeoPolygon(ring)]),
                Location = new LocalItemLocation("/charts", "US3AK7PM/US3AK7PM.000", ["US3AK7PM/US3AK7PM.001"], "CATALOG.031"),
                Properties = new Dictionary<string, string> { ["producingAgency"] = "550" },
            },
            new CollectionItem
            {
                Key = "US4AK11M",
                ProductSpec = "S-57",
                Name = "US4AK11M",
                Location = new RemoteItemLocation(new Uri("https://charts.noaa.gov/ENCs/US4AK11M.zip"), 123_456),
            },
            new CollectionItem
            {
                Key = "GB123",
                ProductSpec = "S-101",
                Name = "GB123",
                Location = NoItemLocation.Instance,
            },
        ],
        [new IndexDiagnostic(IndexDiagnosticSeverity.Warning, "missing", "a/b.000")]);

        using var stream = new MemoryStream();
        CollectionJson.WriteIndex(stream, index);
        stream.Position = 0;
        var restored = CollectionJson.ReadIndex(stream);

        Assert.Equal(index.Fingerprint, restored.Fingerprint);
        Assert.Equal(3, restored.Items.Count);

        var local = restored.Items[0];
        Assert.Equal(index.Items[0].Bounds, local.Bounds);
        Assert.True(local.Bounds!.Value.CrossesAntimeridian);
        Assert.Equal(ring, local.Coverage!.Polygons[0].Exterior);
        Assert.Equal(CollectionItemStatus.Active, local.Status);
        var location = Assert.IsType<LocalItemLocation>(local.Location);
        Assert.Equal(["US3AK7PM/US3AK7PM.001"], location.UpdateRelativePaths);
        Assert.Equal("550", local.Properties["producingAgency"]);

        var remote = Assert.IsType<RemoteItemLocation>(restored.Items[1].Location);
        Assert.Equal(123_456, remote.SizeBytes);
        Assert.IsType<NoItemLocation>(restored.Items[2].Location);
        Assert.Equal(index.Diagnostics, restored.Diagnostics);
    }

    [Fact]
    public void Index_rejects_uncompressed_content()
    {
        using var stream = new MemoryStream("{}"u8.ToArray());

        Assert.ThrowsAny<Exception>(() => CollectionJson.ReadIndex(stream));
    }

    [Fact]
    public void Positions_are_encoded_as_pairs()
    {
        var json = JsonSerializer.Serialize(new GeoPolygon([new GeoPosition(1.5, -2.25)]), CollectionJson.IndexOptions);

        Assert.Contains("[[1.5,-2.25]]", json);
    }
}
