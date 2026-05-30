using EncDotNet.S100.DynamicSources.Ais;
using EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo.Tests;

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

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(20).ConfigureAwait(false);
        }
        throw new TimeoutException("Predicate did not become true.");
    }

    [Fact]
    public async Task Subscribe_sends_subscribe_frame_and_dispatches_position()
    {
        FakeAisStreamIoTransport? captured = null;
        var source = new AisStreamIoMessageSource(Options(), () => captured = new FakeAisStreamIoTransport());

        var positions = new List<AisPositionReport>();
        await using var sub = source.Subscribe(new AisSubscriptionRequest
        {
            Area = new BoundingBox(30, -120, 40, -110),
        });
        sub.PositionReportReceived += (_, p) => positions.Add(p);

        await WaitForAsync(() => captured?.OutboundFrames.Count == 1);
        Assert.Contains("\"APIKey\":\"TEST-KEY\"", captured!.OutboundFrames[0]);

        captured.EnqueueInbound("""
        {"MessageType":"PositionReport","MetaData":{"MMSI":1,"time_utc":"2026-01-01 00:00:00 +0000 UTC"},
        "Message":{"PositionReport":{"Latitude":35,"Longitude":-115,"Cog":90,"TrueHeading":92,"Sog":10,"NavigationalStatus":0}}}
        """);

        await WaitForAsync(() => positions.Count == 1);
        Assert.Equal(1u, positions[0].Mmsi);
    }

    [Fact]
    public async Task TryUpdateArea_resends_subscribe_with_new_bbox()
    {
        FakeAisStreamIoTransport? captured = null;
        var source = new AisStreamIoMessageSource(Options(), () => captured = new FakeAisStreamIoTransport());

        await using var sub = source.Subscribe(new AisSubscriptionRequest
        {
            Area = new BoundingBox(0, 0, 1, 1),
        });
        await WaitForAsync(() => captured?.OutboundFrames.Count == 1);

        Assert.True(sub.TryUpdateArea(new BoundingBox(10, 20, 30, 40)));

        // The receive loop sends the new subscribe frame just before its
        // next receive. Trigger one inbound message so the loop iterates.
        captured!.EnqueueInbound("{}");
        await WaitForAsync(() => captured.OutboundFrames.Count >= 2);
        var second = captured.OutboundFrames[1];
        Assert.Contains("\"APIKey\":\"TEST-KEY\"", second);
        Assert.Contains("10", second);
        Assert.Contains("40", second);
    }

    [Fact]
    public async Task Driver_reconnects_after_peer_close()
    {
        var transports = new List<FakeAisStreamIoTransport>();
        var source = new AisStreamIoMessageSource(Options(), () =>
        {
            var t = new FakeAisStreamIoTransport();
            transports.Add(t);
            return t;
        });

        await using var sub = source.Subscribe(new AisSubscriptionRequest());
        await WaitForAsync(() => transports.Count >= 1 && transports[0].OutboundFrames.Count >= 1);

        // First transport closes; loop should reconnect via factory.
        transports[0].ScriptClose();
        await WaitForAsync(() => transports.Count >= 2 && transports[1].OutboundFrames.Count >= 1);

        Assert.True(transports[0].Disposed);
        Assert.NotEmpty(transports[1].OutboundFrames);
    }

    [Fact]
    public async Task Subscribe_filters_messages_by_mmsi_allowlist()
    {
        FakeAisStreamIoTransport? captured = null;
        var source = new AisStreamIoMessageSource(Options(), () => captured = new FakeAisStreamIoTransport());
        await using var sub = source.Subscribe(new AisSubscriptionRequest
        {
            Mmsis = new uint[] { 7 },
        });
        var positions = new List<AisPositionReport>();
        sub.PositionReportReceived += (_, p) => positions.Add(p);

        await WaitForAsync(() => captured?.OutboundFrames.Count == 1);

        captured!.EnqueueInbound("""
        {"MessageType":"PositionReport","MetaData":{"MMSI":1,"time_utc":"2026-01-01 00:00:00 +0000 UTC"},
        "Message":{"PositionReport":{"Latitude":1,"Longitude":1,"Cog":0,"TrueHeading":0,"Sog":0,"NavigationalStatus":0}}}
        """);
        captured.EnqueueInbound("""
        {"MessageType":"PositionReport","MetaData":{"MMSI":7,"time_utc":"2026-01-01 00:00:00 +0000 UTC"},
        "Message":{"PositionReport":{"Latitude":2,"Longitude":2,"Cog":0,"TrueHeading":0,"Sog":0,"NavigationalStatus":0}}}
        """);

        await WaitForAsync(() => positions.Count == 1);
        Assert.Equal(7u, positions[0].Mmsi);
    }

    [Fact]
    public async Task DisposeAsync_stops_loop_and_cleans_up_transport()
    {
        FakeAisStreamIoTransport? captured = null;
        var source = new AisStreamIoMessageSource(Options(), () => captured = new FakeAisStreamIoTransport());
        var sub = source.Subscribe(new AisSubscriptionRequest());
        await WaitForAsync(() => captured?.OutboundFrames.Count == 1);

        await sub.DisposeAsync();

        Assert.True(captured!.Disposed);
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
        FakeAisStreamIoTransport? captured = null;
        var options = Options();
        var source = new AisStreamIoMessageSource(options, () => captured = new FakeAisStreamIoTransport());
        await using var sub = source.Subscribe(new AisSubscriptionRequest());
        await WaitForAsync(() => captured?.OutboundFrames.Count == 1);

        var raw = captured!.OutboundFrames[0];
        Assert.Contains(options.ApiKey, raw, StringComparison.Ordinal);

        var redacted = AisStreamIoJson.RedactApiKey(raw, options.ApiKey);
        Assert.DoesNotContain(options.ApiKey, redacted, StringComparison.Ordinal);
    }
}
