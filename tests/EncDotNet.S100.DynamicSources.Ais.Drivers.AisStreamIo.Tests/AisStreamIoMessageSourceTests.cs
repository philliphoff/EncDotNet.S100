using System.Threading.Channels;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo.Tests;

// The subscription's receive loop runs on a thread-pool thread, so these
// tests never poll state it writes. They await signals from the fake
// transport/factory (or a channel fed by the subscription's events), each
// bounded by FakeAisStreamIoTransport.WaitTimeout.
public class AisStreamIoMessageSourceTests
{
    private static AisStreamIoOptions Options() => new()
    {
        ApiKey = "TEST-KEY",
        Endpoint = new Uri("ws://test.invalid/v0/stream"),
        SubscribeDeadline = TimeSpan.FromSeconds(5),
        InitialReconnectBackoff = TimeSpan.FromMilliseconds(10),
        MaxReconnectBackoff = TimeSpan.FromMilliseconds(50),
    };

    private static ChannelReader<AisPositionReport> CapturePositions(IAisSubscription sub)
    {
        var positions = Channel.CreateUnbounded<AisPositionReport>();
        sub.PositionReportReceived += (_, p) => positions.Writer.TryWrite(p);
        return positions.Reader;
    }

    private static Task<AisPositionReport> NextAsync(ChannelReader<AisPositionReport> reader) =>
        reader.ReadAsync().AsTask().WaitAsync(FakeAisStreamIoTransport.WaitTimeout);

    [Fact]
    public async Task Subscribe_sends_subscribe_frame_and_dispatches_position()
    {
        var factory = new FakeAisStreamIoTransportFactory();
        var source = new AisStreamIoMessageSource(Options(), factory.Create);

        await using var sub = source.Subscribe(new AisSubscriptionRequest
        {
            Area = new BoundingBox(30, -120, 40, -110),
        });
        var positions = CapturePositions(sub);

        var transport = await factory.NextAsync();
        var frames = await transport.WaitForOutboundFramesAsync(1);
        Assert.Contains("\"APIKey\":\"TEST-KEY\"", frames[0]);

        transport.EnqueueInbound("""
        {"MessageType":"PositionReport","MetaData":{"MMSI":1,"time_utc":"2026-01-01 00:00:00 +0000 UTC"},
        "Message":{"PositionReport":{"Latitude":35,"Longitude":-115,"Cog":90,"TrueHeading":92,"Sog":10,"NavigationalStatus":0}}}
        """);

        var position = await NextAsync(positions);
        Assert.Equal(1u, position.Mmsi);
    }

    [Fact]
    public async Task TryUpdateArea_resends_subscribe_with_new_bbox()
    {
        var factory = new FakeAisStreamIoTransportFactory();
        var source = new AisStreamIoMessageSource(Options(), factory.Create);

        await using var sub = source.Subscribe(new AisSubscriptionRequest
        {
            Area = new BoundingBox(0, 0, 1, 1),
        });
        var transport = await factory.NextAsync();
        await transport.WaitForOutboundFramesAsync(1);

        Assert.True(sub.TryUpdateArea(new BoundingBox(10, 20, 30, 40)));

        // The receive loop sends the new subscribe frame just before its
        // next receive. Trigger one inbound message so the loop iterates.
        transport.EnqueueInbound("{}");
        var frames = await transport.WaitForOutboundFramesAsync(2);
        var second = frames[1];
        Assert.Contains("\"APIKey\":\"TEST-KEY\"", second);
        Assert.Contains("10", second);
        Assert.Contains("40", second);
    }

    [Fact]
    public async Task Driver_reconnects_after_peer_close()
    {
        var factory = new FakeAisStreamIoTransportFactory();
        var source = new AisStreamIoMessageSource(Options(), factory.Create);

        await using var sub = source.Subscribe(new AisSubscriptionRequest());
        var first = await factory.NextAsync();
        await first.WaitForOutboundFramesAsync(1);

        // First transport closes; loop should dispose it and reconnect via
        // the factory, re-sending the subscribe frame on the new transport.
        first.ScriptClose();
        await first.WaitForDisposedAsync();

        var second = await factory.NextAsync();
        var frames = await second.WaitForOutboundFramesAsync(1);
        Assert.Contains("\"APIKey\":\"TEST-KEY\"", frames[0]);
        Assert.False(second.Disposed);
    }

    [Fact]
    public async Task Subscribe_filters_messages_by_mmsi_allowlist()
    {
        var factory = new FakeAisStreamIoTransportFactory();
        var source = new AisStreamIoMessageSource(Options(), factory.Create);
        await using var sub = source.Subscribe(new AisSubscriptionRequest
        {
            Mmsis = new uint[] { 7 },
        });
        var positions = CapturePositions(sub);

        var transport = await factory.NextAsync();
        await transport.WaitForOutboundFramesAsync(1);

        transport.EnqueueInbound("""
        {"MessageType":"PositionReport","MetaData":{"MMSI":1,"time_utc":"2026-01-01 00:00:00 +0000 UTC"},
        "Message":{"PositionReport":{"Latitude":1,"Longitude":1,"Cog":0,"TrueHeading":0,"Sog":0,"NavigationalStatus":0}}}
        """);
        transport.EnqueueInbound("""
        {"MessageType":"PositionReport","MetaData":{"MMSI":7,"time_utc":"2026-01-01 00:00:00 +0000 UTC"},
        "Message":{"PositionReport":{"Latitude":2,"Longitude":2,"Cog":0,"TrueHeading":0,"Sog":0,"NavigationalStatus":0}}}
        """);

        // Frames dispatch in order, so the first report through the filter
        // must be MMSI 7 — MMSI 1 would have arrived before it otherwise.
        var position = await NextAsync(positions);
        Assert.Equal(7u, position.Mmsi);
    }

    [Fact]
    public async Task DisposeAsync_stops_loop_and_cleans_up_transport()
    {
        var factory = new FakeAisStreamIoTransportFactory();
        var source = new AisStreamIoMessageSource(Options(), factory.Create);
        var sub = source.Subscribe(new AisSubscriptionRequest());
        var transport = await factory.NextAsync();
        await transport.WaitForOutboundFramesAsync(1);

        await sub.DisposeAsync();

        Assert.True(transport.Disposed);
    }

    [Fact]
    public void Constructor_rejects_missing_api_key()
    {
        Assert.Throws<ArgumentException>(() => new AisStreamIoMessageSource(new AisStreamIoOptions
        {
            ApiKey = "",
        }));
    }

    [Fact]
    public async Task Outgoing_frames_never_log_api_key_in_redacted_form()
    {
        // Sanity test paralleling docs/design/ais-source.md §15: the redactor must hide the key.
        var factory = new FakeAisStreamIoTransportFactory();
        var options = Options();
        var source = new AisStreamIoMessageSource(options, factory.Create);
        await using var sub = source.Subscribe(new AisSubscriptionRequest());
        var transport = await factory.NextAsync();
        var frames = await transport.WaitForOutboundFramesAsync(1);

        var raw = frames[0];
        Assert.Contains(options.ApiKey, raw, StringComparison.Ordinal);

        var redacted = AisStreamIoJson.RedactApiKey(raw, options.ApiKey);
        Assert.DoesNotContain(options.ApiKey, redacted, StringComparison.Ordinal);
    }
}
