using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace EncDotNet.S100.Nmea;

/// <summary>
/// Where NMEA 0183 lines come from: a serial port, a network multiplexer, a
/// replay file. A consumer enumerates <see cref="ReadLinesAsync"/> and parses
/// each line with <see cref="NmeaSentence.TryParse"/>.
/// </summary>
/// <remarks>
/// Each enumeration opens the input and closes it when the enumeration ends or
/// is disposed, so a consumer that stops reading releases the file or socket.
/// An enumeration that ends normally means the input ended (a replay file ran
/// out); an exception means it failed, and the consumer may enumerate again to
/// retry.
/// </remarks>
public interface INmeaLineSource
{
    /// <summary>A short description of the input for logs and status, e.g. <c>udp://0.0.0.0:10110</c>.</summary>
    string Description { get; }

    /// <summary>Opens the input and reads it line by line, without line endings.</summary>
    /// <param name="cancellationToken">Stops the reading and closes the input.</param>
    IAsyncEnumerable<string> ReadLinesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Splits a byte stream into NMEA lines.</summary>
public static class NmeaLineReader
{
    /// <summary>The longest run without a line ending that is kept: anything longer is noise and is dropped up to the next line ending.</summary>
    public const int MaxLineLength = 1024;

    /// <summary>
    /// Reads <paramref name="stream"/> line by line. Lines end at CR, LF or
    /// CR LF; empty lines are skipped; bytes are read as Latin-1, so a corrupt
    /// byte becomes one character the parser then rejects. A last line without
    /// an ending is returned too: the parser decides whether it is whole.
    /// </summary>
    /// <param name="stream">The stream. It is not disposed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async IAsyncEnumerable<string> ReadLinesAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var buffer = new byte[4096];
        var line = new StringBuilder(128);
        bool overflow = false;
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            for (int i = 0; i < read; i++)
            {
                byte b = buffer[i];
                if (b is (byte)'\r' or (byte)'\n')
                {
                    if (line.Length > 0 && !overflow)
                        yield return line.ToString();
                    line.Clear();
                    overflow = false;
                    continue;
                }

                if (overflow)
                    continue;
                if (line.Length >= MaxLineLength)
                {
                    overflow = true;
                    line.Clear();
                    continue;
                }
                line.Append((char)b);
            }
        }

        if (line.Length > 0 && !overflow)
            yield return line.ToString();
    }

    /// <summary>Splits one datagram or text block into its non-empty lines.</summary>
    /// <param name="text">The text.</param>
    public static IEnumerable<string> SplitLines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length <= MaxLineLength)
                yield return line;
        }
    }
}

/// <summary>
/// Lines from a stream the caller opens: a serial port's
/// <c>BaseStream</c>, a pipe, a <see cref="MemoryStream"/> in a test. The
/// stream is opened for each enumeration and disposed when it ends.
/// </summary>
public sealed class NmeaStreamLineSource : INmeaLineSource
{
    private readonly Func<CancellationToken, ValueTask<Stream>> _open;

    /// <summary>Creates a source that calls <paramref name="open"/> for each enumeration and disposes the stream after.</summary>
    /// <param name="open">Opens the stream.</param>
    /// <param name="description">The description, e.g. <c>COM3</c>.</param>
    public NmeaStreamLineSource(Func<CancellationToken, ValueTask<Stream>> open, string description)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentException.ThrowIfNullOrEmpty(description);
        _open = open;
        Description = description;
    }

    /// <inheritdoc />
    public string Description { get; }

    /// <summary>A source over text already in memory, for tests and samples.</summary>
    /// <param name="text">The NMEA text, lines separated by CR LF or LF.</param>
    public static NmeaStreamLineSource FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = Encoding.Latin1.GetBytes(text);
        return new NmeaStreamLineSource(_ => ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false)), "text");
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var stream = await _open(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            await foreach (var line in NmeaLineReader.ReadLinesAsync(stream, cancellationToken).ConfigureAwait(false))
                yield return line;
        }
    }
}

/// <summary>Options for <see cref="NmeaReplayFile"/>.</summary>
public sealed record NmeaReplayOptions
{
    /// <summary>
    /// The pause between lines, on <see cref="TimeProvider"/>. Zero (the
    /// default) replays the file as fast as it is read, which is what tests
    /// want; a recording made at 10 lines a second plays back in real time at
    /// 100 ms.
    /// </summary>
    public TimeSpan LineInterval { get; init; } = TimeSpan.Zero;

    /// <summary>Whether to start again at the end of the file. Defaults to <see langword="false"/>: the enumeration ends.</summary>
    public bool Loop { get; init; }

    /// <summary>The clock the pauses run on; <see langword="null"/> for <see cref="System.TimeProvider.System"/>.</summary>
    public TimeProvider? TimeProvider { get; init; }
}

/// <summary>
/// Replays an NMEA recording: a text file of sentences, one per line, as
/// logged by most multiplexers and chart plotters. Lines starting with
/// <c>#</c> are comments and are skipped.
/// </summary>
/// <remarks>
/// The file is opened for reading with <see cref="FileShare.Read"/> for each
/// enumeration and closed when the enumeration ends or is disposed.
/// </remarks>
public sealed class NmeaReplayFile : INmeaLineSource
{
    private readonly NmeaReplayOptions _options;

    /// <summary>Creates a replay of <paramref name="path"/>.</summary>
    /// <param name="path">The recording.</param>
    /// <param name="options">The options; <see langword="null"/> for the defaults (as fast as possible, once).</param>
    public NmeaReplayFile(string path, NmeaReplayOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _options = options ?? new NmeaReplayOptions();
        if (_options.LineInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), _options.LineInterval, "The line interval can't be negative.");
        Path = path;
    }

    /// <summary>The recording's path.</summary>
    public string Path { get; }

    /// <inheritdoc />
    public string Description => "file://" + Path;

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var time = _options.TimeProvider ?? TimeProvider.System;
        do
        {
            var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (stream.ConfigureAwait(false))
            {
                bool any = false;
                await foreach (var line in NmeaLineReader.ReadLinesAsync(stream, cancellationToken).ConfigureAwait(false))
                {
                    if (line.StartsWith('#'))
                        continue;
                    if (any && _options.LineInterval > TimeSpan.Zero)
                        await Task.Delay(_options.LineInterval, time, cancellationToken).ConfigureAwait(false);
                    any = true;
                    yield return line;
                }

                // An empty file would spin when looping.
                if (!any)
                    yield break;
            }
        }
        while (_options.Loop && !cancellationToken.IsCancellationRequested);
    }
}

/// <summary>
/// Lines from UDP datagrams, as most NMEA multiplexers, chart plotters and
/// Signal K servers broadcast them (by convention on port 10110). Each
/// datagram holds one or more whole lines.
/// </summary>
public sealed class NmeaUdpLineSource : INmeaLineSource
{
    /// <summary>The conventional NMEA 0183 UDP and TCP port: 10110.</summary>
    public const int DefaultPort = 10110;

    private readonly IPEndPoint _endPoint;

    /// <summary>Creates a source listening on <paramref name="port"/>.</summary>
    /// <param name="port">The UDP port; 0 picks a free one (see <see cref="BoundPort"/>).</param>
    /// <param name="address">The local address to listen on; <see langword="null"/> for every interface.</param>
    public NmeaUdpLineSource(int port = DefaultPort, IPAddress? address = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, IPEndPoint.MaxPort);
        _endPoint = new IPEndPoint(address ?? IPAddress.Any, port);
    }

    /// <inheritdoc />
    public string Description => $"udp://{_endPoint}";

    /// <summary>The port the current enumeration is bound to, once it has started; useful with port 0.</summary>
    public int? BoundPort { get; private set; }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var client = new UdpClient(_endPoint.AddressFamily);
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.Bind(_endPoint);
        BoundPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }

                foreach (var line in NmeaLineReader.SplitLines(Encoding.Latin1.GetString(result.Buffer)))
                    yield return line;
            }
        }
        finally
        {
            BoundPort = null;
        }
    }
}

/// <summary>
/// Lines from a TCP NMEA server, such as a multiplexer or a chart plotter's
/// NMEA output (often port 10110). A dropped or refused connection is retried
/// after <see cref="ReconnectDelay"/> until the enumeration is cancelled.
/// </summary>
public sealed class NmeaTcpLineSource : INmeaLineSource
{
    private readonly TimeProvider _time;

    /// <summary>Creates a source that connects to <paramref name="host"/>:<paramref name="port"/>.</summary>
    /// <param name="host">The server's host name or address.</param>
    /// <param name="port">The server's port.</param>
    /// <param name="reconnectDelay">The wait before reconnecting; <see langword="null"/> for 5 seconds.</param>
    /// <param name="timeProvider">The clock the wait runs on; <see langword="null"/> for <see cref="TimeProvider.System"/>.</param>
    public NmeaTcpLineSource(string host, int port = NmeaUdpLineSource.DefaultPort, TimeSpan? reconnectDelay = null, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, IPEndPoint.MaxPort);
        Host = host;
        Port = port;
        ReconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(5);
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The server's host.</summary>
    public string Host { get; }

    /// <summary>The server's port.</summary>
    public int Port { get; }

    /// <summary>The wait before reconnecting after the connection drops or is refused.</summary>
    public TimeSpan ReconnectDelay { get; }

    /// <inheritdoc />
    public string Description => $"tcp://{Host}:{Port}";

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var client = new TcpClient();
            try
            {
                Stream? stream = null;
                try
                {
                    await client.ConnectAsync(Host, Port, cancellationToken).ConfigureAwait(false);
                    stream = client.GetStream();
                }
                catch (SocketException)
                {
                    // Refused or unreachable: wait and retry below.
                }

                if (stream is not null)
                {
                    var lines = NmeaLineReader.ReadLinesAsync(stream, cancellationToken).GetAsyncEnumerator(cancellationToken);
                    try
                    {
                        while (true)
                        {
                            bool more;
                            try
                            {
                                more = await lines.MoveNextAsync().ConfigureAwait(false);
                            }
                            catch (IOException)
                            {
                                more = false; // The connection dropped.
                            }
                            if (!more)
                                break;
                            yield return lines.Current;
                        }
                    }
                    finally
                    {
                        await lines.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                client.Dispose();
            }

            try
            {
                await Task.Delay(ReconnectDelay, _time, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }
}
