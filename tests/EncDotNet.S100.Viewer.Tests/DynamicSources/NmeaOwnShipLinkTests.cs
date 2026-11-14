using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.DynamicSources;
using EncDotNet.S100.DynamicSources.Nmea;
using EncDotNet.S100.Nmea;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Viewer.Services.DynamicSources.Nmea;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;
using EncDotNet.S100.Viewer.Tests.DynamicSources.OwnShip;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources;

/// <summary>
/// #1073: own-ship weather from NMEA as a data source: its input spec, and
/// the link that forwards the own-ship fix while its entry is loaded.
/// </summary>
public sealed class NmeaOwnShipLinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 14, 50, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("udp:notaport")]
    [InlineData("udp:70000")]
    [InlineData("http://example.org/nmea")]
    public void No_or_an_unknown_input_parses_to_nothing(string? spec)
    {
        Assert.Null(NmeaInput.Parse(spec));
    }

    [Theory]
    [InlineData("udp", "udp://0.0.0.0:10110")]
    [InlineData("UDP", "udp://0.0.0.0:10110")]
    [InlineData("udp:2000", "udp://0.0.0.0:2000")]
    [InlineData("udp://127.0.0.1:2001", "udp://127.0.0.1:2001")]
    [InlineData("tcp://plotter.local:10110", "tcp://plotter.local:10110")]
    [InlineData("tcp://192.168.1.10", "tcp://192.168.1.10:10110")]
    public void Network_inputs(string spec, string description)
    {
        Assert.Equal(description, NmeaInput.Parse(spec)!.Description);
    }

    [Fact]
    public void A_path_is_a_looping_replay()
    {
        var path = Path.Combine(Path.GetTempPath(), "voyage.nmea");

        var plain = Assert.IsType<NmeaReplayFile>(NmeaInput.Parse(path));
        var uri = Assert.IsType<NmeaReplayFile>(NmeaInput.Parse(new Uri(path).AbsoluteUri));

        Assert.Equal(Path.GetFullPath(path), plain.Path);
        Assert.Equal(Path.GetFullPath(path), uri.Path);
    }

    [Fact]
    public async Task The_own_ship_fix_places_the_feature_while_own_ship_is_shown()
    {
        var ownShip = new StubOwnShipPositionProvider();
        bool shown = true;
        var source = new NmeaWeatherSource(input: NmeaStreamLineSource.FromText(string.Empty), options: new NmeaWeatherSourceOptions { PublishInterval = TimeSpan.Zero }, timeProvider: TimeProvider.System);
        await using var processor = new DynamicSourceProcessor(source, OwnShipWeatherPortrayal.Instance, new(SpecName.Nmea, default), ownsSource: true);
        var link = new NmeaOwnShipLink(ownShip, () => shown);
        ownShip.Push(new OwnShipPosition(42.36, -70.70, Angle.FromDegrees(95), Speed.FromKnots(6.5), Now, Angle.FromDegrees(90)));
        using var attachment = link.Attach(DatasetEntry.ForSource(S100Sources.NmeaWeather), processor);
        Assert.NotNull(attachment);

        source.ProcessLine(NmeaSentence.Format("IIMWV,083.0,R,13.8,N,A"));

        var feature = Assert.Single(source.CurrentFeatures);
        Assert.Equal(new GeoPosition(42.36, -70.70), feature.Coordinates[0]);
        // The simulated own ship's heading and motion stand in for the stream's.
        Assert.Equal(TrueWindSource.DerivedOverGround, NmeaWeatherSource.GetWeather(feature)!.TrueWindSource);

        ownShip.Push(new OwnShipPosition(42.37, -70.71, null, null, Now));
        Assert.Equal(new GeoPosition(42.37, -70.71), Assert.Single(source.CurrentFeatures).Coordinates[0]);

        // Hidden own ship: no position, so no feature until the stream sends RMC.
        shown = false;
        link.Refresh();
        Assert.Empty(source.CurrentFeatures);

        source.ProcessLine(NmeaSentence.Format("GPRMC,145000.00,A,4221.000,N,07039.000,W,6.5,95.0,271026,14.5,W,A"));
        Assert.Equal(new GeoPosition(42.35, -70.65), Assert.Single(source.CurrentFeatures).Coordinates[0]);
    }

    [Fact]
    public async Task A_detached_source_no_longer_follows_the_own_ship()
    {
        var ownShip = new StubOwnShipPositionProvider();
        var source = new NmeaWeatherSource(input: NmeaStreamLineSource.FromText(string.Empty), options: new NmeaWeatherSourceOptions { PublishInterval = TimeSpan.Zero }, timeProvider: TimeProvider.System);
        await using var processor = new DynamicSourceProcessor(source, OwnShipWeatherPortrayal.Instance, new(SpecName.Nmea, default), ownsSource: true);
        var link = new NmeaOwnShipLink(ownShip, followOwnShip: null);
        ownShip.Push(new OwnShipPosition(42.36, -70.70, null, null, Now));
        var attachment = link.Attach(DatasetEntry.ForSource(S100Sources.NmeaWeather), processor)!;
        source.ProcessLine(NmeaSentence.Format("IIMWV,083.0,R,13.8,N,A"));

        attachment.Dispose();
        ownShip.Push(new OwnShipPosition(43.0, -71.0, null, null, Now));

        Assert.Equal(new GeoPosition(42.36, -70.70), Assert.Single(source.CurrentFeatures).Coordinates[0]);
    }

    [Fact]
    public void A_link_without_an_own_ship_or_for_another_processor_attaches_nothing()
    {
        var entry = DatasetEntry.ForSource(S100Sources.NmeaWeather);
        Assert.Null(new NmeaOwnShipLink(ownShip: null, followOwnShip: null).Attach(entry, new FakeProcessor()));
        Assert.Null(new NmeaOwnShipLink(new StubOwnShipPositionProvider(), followOwnShip: null).Attach(entry, new FakeProcessor()));
    }

    private sealed class FakeProcessor : IDatasetProcessor
    {
        public SpecRef Spec => new(SpecName.Nmea, default);

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
    }
}
