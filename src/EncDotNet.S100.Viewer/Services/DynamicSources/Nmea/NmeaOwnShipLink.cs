using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.DynamicSources;
using EncDotNet.S100.DynamicSources.Nmea;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;
using EncDotNet.S100.Viewer.Services.Sources;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.DynamicSources.Nmea;

/// <summary>
/// Forwards the own-ship fix to a loaded own-ship weather source (#1073), so
/// the wind barb sits on SoundCharts' own ship. The fix is forwarded only
/// while the own ship is shown: SoundCharts' own ship is still simulated, so
/// while it's hidden the barb goes where the stream's own <c>RMC</c> puts the
/// vessel, not next to an invisible simulated one.
/// </summary>
internal sealed class NmeaOwnShipLink : ISourceHostExtension
{
    /// <summary>The environment variable naming the NMEA input, as before NMEA weather became a data source.</summary>
    public const string InputEnvironmentVariable = "SOUNDCHARTS_NMEA_INPUT";

    private readonly IOwnShipPositionProvider? _ownShip;
    private readonly Func<bool> _followOwnShip;
    private readonly List<Attachment> _attachments = [];

    /// <summary>Creates the link.</summary>
    /// <param name="ownShip">The own-ship fix, or <see langword="null"/> when there's no own ship.</param>
    /// <param name="followOwnShip">Whether the own ship is shown now; always when <see langword="null"/>.</param>
    public NmeaOwnShipLink(IOwnShipPositionProvider? ownShip, Func<bool>? followOwnShip)
    {
        _ownShip = ownShip;
        _followOwnShip = followOwnShip ?? (static () => true);
    }

    /// <inheritdoc />
    public string SourceId => S100Sources.NmeaWeatherId;

    /// <summary>The input named by <see cref="InputEnvironmentVariable"/>, or <see langword="null"/> when it's unset.</summary>
    public static string? InputFromEnvironment() =>
        Environment.GetEnvironmentVariable(InputEnvironmentVariable) is { } value && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    /// <inheritdoc />
    public IDisposable? Attach(DatasetEntry entry, IDatasetProcessor processor)
    {
        if (_ownShip is null
            || processor is not DynamicSourceProcessor { Source: NmeaWeatherSource source })
        {
            return null;
        }
        var attachment = new Attachment(this, source);
        lock (_attachments)
            _attachments.Add(attachment);
        _ownShip.Updated += attachment.OnUpdated;
        attachment.Forward(_ownShip.Current);
        return attachment;
    }

    /// <summary>Forwards the fix again, after the own ship is shown or hidden.</summary>
    public void Refresh()
    {
        Attachment[] attachments;
        lock (_attachments)
            attachments = [.. _attachments];
        foreach (var attachment in attachments)
            attachment.Forward(_ownShip?.Current);
    }

    internal static OwnShipReference ToReference(OwnShipPosition fix) => new(
        new GeoPosition(fix.Latitude, fix.Longitude),
        fix.Heading,
        fix.CourseOverGround,
        fix.SpeedOverGround,
        fix.Timestamp);

    private sealed class Attachment(NmeaOwnShipLink link, NmeaWeatherSource source) : IDisposable
    {
        private int _disposed;

        public void OnUpdated(object? sender, OwnShipPosition fix) => Forward(fix);

        public void Forward(OwnShipPosition? fix)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;
            try
            {
                source.UpdateOwnShip(fix is not null && link._followOwnShip() ? ToReference(fix) : null);
            }
            catch (ObjectDisposedException)
            {
                // The entry was removed meanwhile.
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            if (link._ownShip is not null)
                link._ownShip.Updated -= OnUpdated;
            lock (link._attachments)
                link._attachments.Remove(this);
        }
    }
}
