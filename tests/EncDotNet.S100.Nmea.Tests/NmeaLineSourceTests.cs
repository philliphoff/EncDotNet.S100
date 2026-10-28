using System.Net;
using System.Net.Sockets;
using System.Text;

namespace EncDotNet.S100.Nmea.Tests;

public sealed class NmeaLineSourceTests
{
    internal static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "NMEA", name);

    private static async Task<List<string>> ReadAll(INmeaLineSource source, int max = int.MaxValue)
    {
        var lines = new List<string>();
        await foreach (var line in source.ReadLinesAsync(TestContext.Current.CancellationToken))
        {
            lines.Add(line);
            if (lines.Count >= max)
                break;
        }
        return lines;
    }

    [Fact]
    public async Task Lines_split_at_CR_LF_or_CR_LF_and_empty_lines_are_skipped()
    {
        var source = NmeaStreamLineSource.FromText("$A*00\r\n$B*00\n$C*00\r\r\n\n$D*00");

        Assert.Equal(["$A*00", "$B*00", "$C*00", "$D*00"], await ReadAll(source));
    }

    [Fact]
    public async Task A_run_too_long_to_be_a_line_is_dropped_up_to_the_next_line_ending()
    {
        var noise = new string('x', NmeaLineReader.MaxLineLength + 10);
        var source = NmeaStreamLineSource.FromText($"$A*00\r\n{noise}\r\n$B*00\r\n");

        Assert.Equal(["$A*00", "$B*00"], await ReadAll(source));
    }

    [Fact]
    public async Task Lines_split_across_reads_are_joined()
    {
        // A stream that returns one byte per read, like a slow serial port.
        var bytes = Encoding.ASCII.GetBytes("$IIMTW,12.4,C*14\r\n$HEHDT,90.0,T*16\r\n");
        var source = new NmeaStreamLineSource(_ => ValueTask.FromResult<Stream>(new TrickleStream(bytes)), "trickle");

        Assert.Equal(["$IIMTW,12.4,C*14", "$HEHDT,90.0,T*16"], await ReadAll(source));
    }

    [Fact]
    public async Task A_replay_skips_comments_and_closes_the_file()
    {
        var replay = new NmeaReplayFile(FixturePath("relative-wind.nmea"));

        var lines = await ReadAll(replay);

        Assert.Equal(5, lines.Count);
        Assert.All(lines, l => Assert.StartsWith("$", l, StringComparison.Ordinal));
        Assert.Equal("file://" + FixturePath("relative-wind.nmea"), replay.Description);

        // Closed: it can be opened exclusively.
        using (new FileStream(FixturePath("relative-wind.nmea"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }

    [Fact]
    public async Task A_replay_stopped_early_closes_the_file()
    {
        var replay = new NmeaReplayFile(FixturePath("ownship-weather.nmea"));

        Assert.Equal(2, (await ReadAll(replay, max: 2)).Count);

        using (new FileStream(FixturePath("ownship-weather.nmea"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }

    [Fact]
    public async Task A_looping_replay_starts_again()
    {
        var replay = new NmeaReplayFile(FixturePath("relative-wind.nmea"), new NmeaReplayOptions { Loop = true });

        var lines = await ReadAll(replay, max: 12);

        Assert.Equal(lines[0], lines[5]);
        Assert.Equal(lines[1], lines[11]);
    }

    [Fact]
    public void A_negative_line_interval_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NmeaReplayFile("x.nmea", new NmeaReplayOptions { LineInterval = TimeSpan.FromSeconds(-1) }));
    }

    [Fact]
    public async Task Udp_datagrams_become_lines()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = new NmeaUdpLineSource(port: 0, address: IPAddress.Loopback);
        await using var lines = source.ReadLinesAsync(ct).GetAsyncEnumerator(ct);

        // MoveNextAsync binds the socket before it waits for a datagram.
        var first = lines.MoveNextAsync();
        Assert.NotNull(source.BoundPort);

        using var sender = new UdpClient(AddressFamily.InterNetwork);
        var datagram = Encoding.ASCII.GetBytes("$IIMTW,12.4,C*14\r\n$HEHDT,90.0,T*16\r\n");
        await sender.SendAsync(datagram, new IPEndPoint(IPAddress.Loopback, source.BoundPort!.Value), ct);

        Assert.True(await first);
        Assert.Equal("$IIMTW,12.4,C*14", lines.Current);
        Assert.True(await lines.MoveNextAsync());
        Assert.Equal("$HEHDT,90.0,T*16", lines.Current);
    }

    [Fact]
    public async Task Tcp_lines_are_read_and_a_dropped_connection_is_reconnected()
    {
        var ct = TestContext.Current.CancellationToken;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var server = Task.Run(async () =>
        {
            for (int connection = 0; connection < 2; connection++)
            {
                using var client = await listener.AcceptTcpClientAsync(ct);
                var stream = client.GetStream();
                var text = Encoding.ASCII.GetBytes($"$IIMTW,1{connection}.0,C*hh\r\n");
                await stream.WriteAsync(text, ct);
                await stream.FlushAsync(ct);
            }
        }, ct);

        var source = new NmeaTcpLineSource("127.0.0.1", port, reconnectDelay: TimeSpan.FromMilliseconds(50));
        Assert.Equal($"tcp://127.0.0.1:{port}", source.Description);

        var lines = await ReadAll(source, max: 2);

        Assert.Equal(["$IIMTW,10.0,C*hh", "$IIMTW,11.0,C*hh"], lines);
        await server;
    }

    private sealed class TrickleStream(byte[] data) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= data.Length || count == 0)
                return 0;
            buffer[offset] = data[_position++];
            return 1;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
