using System.Net;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.DynamicSources.Nmea;
using EncDotNet.S100.Nmea;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;

namespace EncDotNet.S100.Viewer.Services.DynamicSources.Nmea;

/// <summary>
/// The viewer's side of the own-ship NMEA weather overlay (#1073): owns the
/// <see cref="NmeaWeatherSource"/>, feeds it the own-ship fix so the wind barb
/// is drawn next to the own-ship symbol, and starts it reading its input.
/// </summary>
/// <remarks>
/// <para>
/// The overlay exists only when <see cref="InputEnvironmentVariable"/> names
/// an NMEA input (see <see cref="ParseInput"/>): without instruments there is
/// nothing to show, so there is no Layer Stack row either.
/// </para>
/// <para>
/// The own-ship fix is forwarded only while the own-ship overlay is shown.
/// SoundCharts' own ship is still simulated, so while it is hidden the barb
/// goes where the stream's own <c>RMC</c> puts the vessel, not next to an
/// invisible simulated one.
/// </para>
/// </remarks>
internal sealed class NmeaWeatherOverlay : IAsyncDisposable
{
    /// <summary>
    /// The environment variable naming the NMEA input: <c>udp</c>,
    /// <c>udp:10110</c>, <c>udp://0.0.0.0:10110</c>, <c>tcp://host:10110</c>,
    /// or a recording's path (<c>file://</c> optional), replayed in a loop.
    /// </summary>
    public const string InputEnvironmentVariable = "SOUNDCHARTS_NMEA_INPUT";

    /// <summary>The pause between replayed lines: 100 ms, about an instrument bus's rate.</summary>
    public static readonly TimeSpan ReplayLineInterval = TimeSpan.FromMilliseconds(100);

    private readonly IOwnShipPositionProvider? _ownShip;
    private readonly Func<bool> _followOwnShip;
    private bool _attached;
    private int _disposed;

    /// <summary>Creates the overlay.</summary>
    /// <param name="source">The source.</param>
    /// <param name="ownShip">The own-ship provider whose fixes place the feature; <see langword="null"/> to rely on the stream's RMC.</param>
    /// <param name="followOwnShip">Whether to forward the fixes now, i.e. whether own ship is shown; <see langword="null"/> for always.</param>
    public NmeaWeatherOverlay(NmeaWeatherSource source, IOwnShipPositionProvider? ownShip, Func<bool>? followOwnShip = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        _ownShip = ownShip;
        _followOwnShip = followOwnShip ?? (static () => true);
    }

    /// <summary>The source.</summary>
    public NmeaWeatherSource Source { get; }

    /// <summary>Starts forwarding own-ship fixes and reading the input. Calling it again does nothing.</summary>
    public void Attach()
    {
        if (_attached || Volatile.Read(ref _disposed) != 0)
            return;
        _attached = true;

        if (_ownShip is not null)
        {
            _ownShip.Updated += OnOwnShipUpdated;
            RefreshOwnShip();
        }

        Source.Start();
    }

    /// <summary>
    /// Forwards the current own-ship fix, or clears it while own ship is
    /// hidden. Call when the own-ship overlay is shown or hidden.
    /// </summary>
    public void RefreshOwnShip()
    {
        if (_ownShip is null || Volatile.Read(ref _disposed) != 0)
            return;
        Forward(_ownShip.Current);
    }

    /// <summary>
    /// Parses an input spec: <c>udp</c>, <c>udp:PORT</c>, <c>udp://ADDRESS:PORT</c>,
    /// <c>tcp://HOST:PORT</c>, <c>file://PATH</c> or a plain path to a recording.
    /// </summary>
    /// <param name="spec">The spec.</param>
    /// <returns>The input, or <see langword="null"/> when the spec is empty or not understood.</returns>
    public static INmeaLineSource? ParseInput(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return null;
        spec = spec.Trim();

        if (spec.Equals("udp", StringComparison.OrdinalIgnoreCase))
            return new NmeaUdpLineSource();

        if (spec.StartsWith("udp:", StringComparison.OrdinalIgnoreCase) && !spec.StartsWith("udp://", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(spec.AsSpan(4), out int udpPort) && udpPort is > 0 and <= IPEndPoint.MaxPort
                ? new NmeaUdpLineSource(udpPort)
                : null;

        if (Uri.TryCreate(spec, UriKind.Absolute, out var uri))
        {
            switch (uri.Scheme)
            {
                case "udp":
                    return new NmeaUdpLineSource(
                        uri.IsDefaultPort || uri.Port <= 0 ? NmeaUdpLineSource.DefaultPort : uri.Port,
                        IPAddress.TryParse(uri.Host, out var address) ? address : null);
                case "tcp":
                    return string.IsNullOrEmpty(uri.Host)
                        ? null
                        : new NmeaTcpLineSource(uri.Host, uri.IsDefaultPort || uri.Port <= 0 ? NmeaUdpLineSource.DefaultPort : uri.Port);
                case "file":
                    return Replay(uri.LocalPath);
                default:
                    // A Windows path such as C:\logs\nmea.txt parses as scheme "c".
                    if (uri.Scheme.Length > 1)
                        return null;
                    break;
            }
        }

        return Replay(spec);
    }

    private static NmeaReplayFile Replay(string path) =>
        new(Path.GetFullPath(path), new NmeaReplayOptions { LineInterval = ReplayLineInterval, Loop = true });

    internal static OwnShipReference ToReference(OwnShipPosition fix) => new(
        new GeoPosition(fix.Latitude, fix.Longitude),
        fix.Heading,
        fix.CourseOverGround,
        fix.SpeedOverGround,
        fix.Timestamp);

    private void OnOwnShipUpdated(object? sender, OwnShipPosition fix)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        Forward(fix);
    }

    private void Forward(OwnShipPosition? fix)
    {
        try
        {
            Source.UpdateOwnShip(fix is not null && _followOwnShip() ? ToReference(fix) : null);
        }
        catch (ObjectDisposedException)
        {
            // Shutting down.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        if (_ownShip is not null)
            _ownShip.Updated -= OnOwnShipUpdated;
        await Source.DisposeAsync().ConfigureAwait(false);
    }
}
