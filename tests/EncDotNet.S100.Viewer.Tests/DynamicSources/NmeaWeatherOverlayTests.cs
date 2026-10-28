using EncDotNet.S100.DataModel;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.DynamicSources.Nmea;
using EncDotNet.S100.Nmea;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using EncDotNet.S100.Viewer.Services.DynamicSources.Nmea;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;
using EncDotNet.S100.Viewer.Tests.DynamicSources.OwnShip;
using Microsoft.Extensions.DependencyInjection;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources;

/// <summary>#1073: the viewer's own-ship NMEA weather overlay: its input spec and own-ship binding.</summary>
public sealed class NmeaWeatherOverlayTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 14, 50, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("udp:notaport")]
    [InlineData("udp:70000")]
    [InlineData("http://example.org/nmea")]
    public void No_or_an_unknown_input_means_no_overlay(string? spec)
    {
        Assert.Null(NmeaWeatherOverlay.ParseInput(spec));

        var services = new ServiceCollection().AddNmeaWeather(spec);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IDynamicFeatureSource));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(NmeaWeatherOverlay));
        // The renderer is registered either way.
        Assert.Contains(services, d => d.ServiceType == typeof(IDynamicFeatureRenderer) && Equals(d.ServiceKey, NmeaWeatherSource.FeatureKind));
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
        Assert.Equal(description, NmeaWeatherOverlay.ParseInput(spec)!.Description);
    }

    [Fact]
    public void A_path_is_a_looping_replay()
    {
        var path = Path.Combine(Path.GetTempPath(), "voyage.nmea");

        var plain = Assert.IsType<NmeaReplayFile>(NmeaWeatherOverlay.ParseInput(path));
        var uri = Assert.IsType<NmeaReplayFile>(NmeaWeatherOverlay.ParseInput(new Uri(path).AbsoluteUri));

        Assert.Equal(Path.GetFullPath(path), plain.Path);
        Assert.Equal(Path.GetFullPath(path), uri.Path);
    }

    [Fact]
    public async Task A_configured_input_registers_the_source_with_its_layer_stack_id()
    {
        var services = new ServiceCollection().AddNmeaWeather("udp:2002");
        await using var provider = services.BuildServiceProvider();

        var source = Assert.IsType<NmeaWeatherSource>(provider.GetRequiredService<IDynamicFeatureSource>());
        Assert.Equal(NmeaWeatherServiceCollectionExtensions.SourceId, source.Id);
        Assert.Same(source, provider.GetRequiredService<NmeaWeatherOverlay>().Source);
    }

    [Fact]
    public async Task The_own_ship_fix_places_the_feature_while_own_ship_is_shown()
    {
        var ownShip = new StubOwnShipPositionProvider();
        bool shown = true;
        var source = new NmeaWeatherSource(NmeaStreamLineSource.FromText(string.Empty), new NmeaWeatherSourceOptions { PublishInterval = TimeSpan.Zero });
        await using var overlay = new NmeaWeatherOverlay(source, ownShip, () => shown);
        ownShip.Push(new OwnShipPosition(42.36, -70.70, Angle.FromDegrees(95), Speed.FromKnots(6.5), Now, Angle.FromDegrees(90)));
        overlay.Attach();

        source.ProcessLine(NmeaSentence.Format("IIMWV,083.0,R,13.8,N,A"));

        var feature = Assert.Single(source.CurrentFeatures);
        Assert.Equal(new GeoPosition(42.36, -70.70), feature.Coordinates[0]);
        // The simulated own ship's heading and motion stand in for the stream's.
        Assert.Equal(TrueWindSource.DerivedOverGround, NmeaWeatherSource.GetWeather(feature)!.TrueWindSource);

        ownShip.Push(new OwnShipPosition(42.37, -70.71, null, null, Now));
        Assert.Equal(new GeoPosition(42.37, -70.71), Assert.Single(source.CurrentFeatures).Coordinates[0]);

        // Hidden own ship: no position, so no feature until the stream sends RMC.
        shown = false;
        overlay.RefreshOwnShip();
        Assert.Empty(source.CurrentFeatures);

        source.ProcessLine(NmeaSentence.Format("GPRMC,145000.00,A,4221.000,N,07039.000,W,6.5,95.0,271026,14.5,W,A"));
        Assert.Equal(new GeoPosition(42.35, -70.65), Assert.Single(source.CurrentFeatures).Coordinates[0]);
    }
}
