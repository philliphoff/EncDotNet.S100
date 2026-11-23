using System.Text;
using EncDotNet.S100.Tides.Stations.Mirror;
using EncDotNet.S100.Tides.Stations.Noaa;

namespace EncDotNet.S100.Tides.Stations.Tests;

/// <summary>The pieces a mirror publishes with: the comparison, the manifest and the publish check.</summary>
public sealed class MirrorPublishingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Compare_FindsNoChanges_AfterARoundTripThroughTheFile()
    {
        var catalogue = MirrorFixtures.Catalogue();
        var stream = new MemoryStream();
        await TideStationCatalogueFile.WriteAsync(stream, catalogue, Token);
        stream.Position = 0;
        var reread = await TideStationCatalogueFile.ReadAsync(stream, Token);

        var comparison = TideCatalogueComparison.Compare(catalogue, reread);

        Assert.False(comparison.HasChanges);
        Assert.Equal("1983-2001", comparison.DatumEpoch);
    }

    [Fact]
    public void Compare_FindsAddedRemovedAndChangedStations_OfBothKinds()
    {
        var previous = MirrorFixtures.Catalogue();
        var seattle = previous.Find(Fixtures.Seattle)!;
        var revised = new TideStationEntry
        {
            Id = seattle.Id,
            Name = seattle.Name,
            Position = seattle.Position,
            Kind = seattle.Kind,
            Region = seattle.Region,
            UtcOffset = seattle.UtcOffset,
            Constituents = [.. seattle.Constituents.Select(c => c.Name == "M2" ? c with { AmplitudeMeters = c.AmplitudeMeters + 0.001 } : c)],
            Datums = seattle.Datums,
        };
        var stations = previous.Stations
            .Where(s => s.Id != Fixtures.MarrowstonePoint)
            .Select(s => s.Id == Fixtures.Seattle ? revised : s)
            .Append(new TideStationEntry
            {
                Id = "9999999",
                Name = "New Station",
                Position = seattle.Position,
                Kind = TideStationKind.Subordinate,
                ReferenceStationId = Fixtures.Seattle,
                Offsets = previous.Find(Fixtures.TacomaNarrows)!.Offsets,
            })
            .ToList();
        var deception = previous.FindCurrentStation("PUG1701_33")!;
        var currents = previous.CurrentStations
            .Where(s => s.Id != "MOB1101_2")
            .Select(s => s.Id == deception.Id ? Renamed(s, "Deception Pass (renamed)") : s)
            .ToList();
        var current = new TideStationCatalogue(stations, currents, previous.Provider, previous.Retrieved);

        var comparison = TideCatalogueComparison.Compare(previous, current);

        Assert.True(comparison.HasChanges);
        Assert.Equal(["9999999"], comparison.AddedStationIds);
        Assert.Equal([Fixtures.MarrowstonePoint], comparison.RemovedStationIds);
        Assert.Equal([Fixtures.Seattle], comparison.ChangedStationIds);
        Assert.Empty(comparison.AddedCurrentStationIds);
        Assert.Equal(["MOB1101_2"], comparison.RemovedCurrentStationIds);
        Assert.Equal(["PUG1701_33"], comparison.ChangedCurrentStationIds);
    }

    [Fact]
    public void Compare_IgnoresTheRetrievalDate()
    {
        var comparison = TideCatalogueComparison.Compare(MirrorFixtures.Catalogue(), MirrorFixtures.Catalogue(MirrorFixtures.Retrieved.AddDays(30)));
        Assert.False(comparison.HasChanges);
    }

    [Fact]
    public async Task Manifest_RoundTrips_AndMatchesOnlyItsFile()
    {
        var catalogue = MirrorFixtures.Catalogue();
        var content = MirrorFixtures.Gzip(catalogue);
        var crawled = MirrorFixtures.Retrieved.AddDays(2);
        var @checked = MirrorFixtures.Retrieved.AddDays(9);
        var manifest = TideCatalogueManifest.Create(content, catalogue, crawled, @checked);

        var reread = await TideCatalogueManifest.ReadAsync(new MemoryStream(MirrorFixtures.ToJson(manifest)), Token);

        Assert.Equal(TideCatalogueManifest.DefaultCatalogueFileName, reread.File);
        Assert.Equal(TideStationCatalogueFile.FormatName, reread.CatalogueFormat);
        Assert.Equal(TideStationCatalogueFile.CurrentVersion, reread.CatalogueVersion);
        Assert.True(reread.IsSupported);
        Assert.Equal(content.Length, reread.Size);
        Assert.Matches("^[0-9a-f]{64}$", reread.Sha256);
        Assert.Equal(NoaaTideStationClient.ProviderName, reread.Provider);
        Assert.Equal(MirrorFixtures.Retrieved, reread.Retrieved);
        Assert.Equal(crawled, reread.Crawled);
        Assert.Equal(@checked, reread.Checked);
        Assert.Equal("1983-2001", reread.DatumEpoch);
        Assert.Equal(catalogue.Stations.Count, reread.StationCount);
        Assert.Equal(catalogue.Stations.Count(s => s.Kind == TideStationKind.Harmonic), reread.HarmonicStationCount);
        Assert.Equal(catalogue.Stations.Count(s => s.Kind == TideStationKind.Subordinate), reread.SubordinateStationCount);
        Assert.Equal(catalogue.CurrentStations.Count, reread.CurrentStationCount);

        Assert.True(reread.Matches(content));
        Assert.False(reread.Matches(content.AsSpan(1)));
        var changed = content.ToArray();
        changed[^1] ^= 1;
        Assert.False(reread.Matches(changed));
    }

    [Fact]
    public void Manifest_NeedsADatedCatalogue()
    {
        var catalogue = MirrorFixtures.Catalogue();
        var undated = new TideStationCatalogue(catalogue.Stations, catalogue.CurrentStations, catalogue.Provider, retrieved: null);
        Assert.Throws<ArgumentException>(() => TideCatalogueManifest.Create([1, 2], undated, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData("""{"format":"tide-stations","version":1}""", "format")]
    [InlineData("""{"format":"tide-stations-manifest","version":2}""", "version 2")]
    [InlineData("""{"format":"tide-stations-manifest","version":1,"file":"https://elsewhere.example/x.json.gz"}""", "relative")]
    [InlineData("""{"format":"tide-stations-manifest","version":1,"file":"x.json.gz"}""", "which format")]
    [InlineData("""{"format":"tide-stations-manifest","version":1,"file":"x.json.gz","catalogueFormat":"tide-stations","catalogueVersion":1,"size":10,"sha256":"ABC"}""", "SHA-256")]
    [InlineData("""{"format":"tide-stations-manifest","version":1,"file":"x.json.gz","catalogueFormat":"tide-stations","catalogueVersion":1,"size":10,"sha256":"0000000000000000000000000000000000000000000000000000000000000000"}""", "retrieved")]
    [InlineData("""[1,2]""", "Not a valid")]
    public async Task Manifest_RejectsAnInvalidFile_SayingWhy(string json, string reason)
    {
        var error = await Assert.ThrowsAsync<FormatException>(() => TideCatalogueManifest.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)), Token));
        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishCheck_PassesTheSameCatalogue_AndAFirstPublication()
    {
        var catalogue = MirrorFixtures.Catalogue();
        Assert.Empty(TideCataloguePublishCheck.FindProblems(null, catalogue, NoaaTideStationClient.ProviderName));
        Assert.Empty(TideCataloguePublishCheck.FindProblems(catalogue, MirrorFixtures.Catalogue(MirrorFixtures.Retrieved.AddDays(1)), NoaaTideStationClient.ProviderName));
    }

    [Fact]
    public void PublishCheck_Refuses_ACatalogueThatLostStations()
    {
        var previous = MirrorFixtures.Catalogue();

        // Seattle without its constituents: its subordinate stations can't be predicted either.
        var stations = previous.Stations
            .Select(s => s.Id == Fixtures.Seattle
                ? new TideStationEntry { Id = s.Id, Name = s.Name, Position = s.Position, Kind = s.Kind, Datums = s.Datums }
                : s)
            .ToList();
        var candidate = new TideStationCatalogue(stations, previous.CurrentStations.Skip(1).ToList(), previous.Provider, previous.Retrieved);

        var problems = TideCataloguePublishCheck.FindProblems(previous, candidate);

        Assert.Contains(problems, p => p.Contains("tide stations predictable on MLLW", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("current stations, down from", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("current stations are missing", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Contains("tide stations, down from", StringComparison.Ordinal));

        // A looser limit lets the same catalogue through.
        Assert.Empty(TideCataloguePublishCheck.FindProblems(previous, candidate, maxDropFraction: 1));
    }

    [Fact]
    public void PublishCheck_Refuses_AnUndatedOlderOrForeignCatalogue()
    {
        var previous = MirrorFixtures.Catalogue();
        var undated = new TideStationCatalogue(previous.Stations, previous.CurrentStations, previous.Provider, retrieved: null);
        var older = MirrorFixtures.Catalogue(MirrorFixtures.Retrieved.AddDays(-1));
        var foreign = new TideStationCatalogue(previous.Stations, previous.CurrentStations, "Example harbour authority", previous.Retrieved);
        var empty = new TideStationCatalogue([], previous.Provider, previous.Retrieved);

        Assert.Contains(TideCataloguePublishCheck.FindProblems(previous, undated), p => p.Contains("retrieved", StringComparison.Ordinal));
        Assert.Contains(TideCataloguePublishCheck.FindProblems(previous, older), p => p.Contains("before the published one", StringComparison.Ordinal));
        Assert.Contains(TideCataloguePublishCheck.FindProblems(previous, foreign, NoaaTideStationClient.ProviderName), p => p.Contains("provider", StringComparison.Ordinal));
        Assert.Contains(TideCataloguePublishCheck.FindProblems(null, empty), p => p.Contains("no tide stations", StringComparison.Ordinal));
    }

    private static CurrentStationEntry Renamed(CurrentStationEntry s, string name) => new()
    {
        Id = s.Id,
        Name = name,
        Position = s.Position,
        Kind = s.Kind,
        DepthMeters = s.DepthMeters,
        FloodDirectionDegrees = s.FloodDirectionDegrees,
        EbbDirectionDegrees = s.EbbDirectionDegrees,
        Constituents = s.Constituents,
        MajorAxisDirectionDegrees = s.MajorAxisDirectionDegrees,
        MajorMeanVelocity = s.MajorMeanVelocity,
        MinorMeanVelocity = s.MinorMeanVelocity,
        IsRotary = s.IsRotary,
        ReferenceStationId = s.ReferenceStationId,
        Offsets = s.Offsets,
    };
}
