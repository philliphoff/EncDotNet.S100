using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.Cap;

/// <summary>The form a CAP file came in.</summary>
public enum CapFormat
{
    /// <summary>One CAP 1.2 <c>&lt;alert&gt;</c> document.</summary>
    CapXml,

    /// <summary>An Atom feed whose entries are CAP alerts, whole or as NWS entries with <c>cap:</c> elements.</summary>
    Atom,

    /// <summary>The NWS GeoJSON form from <c>api.weather.gov/alerts</c>: one alert feature or a feature collection.</summary>
    NwsGeoJson,
}

/// <summary>A CAP <c>msgType</c>.</summary>
public enum CapMessageType
{
    /// <summary><c>Alert</c>: initial information.</summary>
    Alert,

    /// <summary><c>Update</c>: replaces the alerts it references.</summary>
    Update,

    /// <summary><c>Cancel</c>: cancels the alerts it references.</summary>
    Cancel,

    /// <summary><c>Ack</c>: acknowledges receipt.</summary>
    Ack,

    /// <summary><c>Error</c>: rejects a message.</summary>
    Error,
}

/// <summary>A CAP <c>severity</c>, in increasing order.</summary>
public enum CapSeverity
{
    /// <summary><c>Unknown</c>, or a value the reader doesn't know.</summary>
    Unknown,

    /// <summary><c>Minor</c>: minimal or no known threat to life or property.</summary>
    Minor,

    /// <summary><c>Moderate</c>: possible threat to life or property.</summary>
    Moderate,

    /// <summary><c>Severe</c>: significant threat to life or property.</summary>
    Severe,

    /// <summary><c>Extreme</c>: extraordinary threat to life or property.</summary>
    Extreme,
}

/// <summary>A name and value pair: a CAP <c>geocode</c>, <c>parameter</c> or <c>eventCode</c>.</summary>
/// <param name="Name">The <c>valueName</c>, e.g. <c>"UGC"</c>.</param>
/// <param name="Value">The <c>value</c>, e.g. <c>"ANZ535"</c>.</param>
public sealed record CapValue(string Name, string Value);

/// <summary>A CAP <c>circle</c>: a centre and a radius.</summary>
/// <param name="Centre">The centre, WGS-84.</param>
/// <param name="RadiusKilometres">The radius in kilometres; 0 for a point.</param>
public readonly record struct CapCircle(GeoPosition Centre, double RadiusKilometres);

/// <summary>A CAP <c>area</c>: what it says and the shapes it gives.</summary>
public sealed record CapArea
{
    /// <summary>The <c>areaDesc</c>.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The <c>polygon</c>s, each a closed ring of WGS-84 positions, in document order.</summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> Polygons { get; init; } = [];

    /// <summary>The <c>circle</c>s, in document order.</summary>
    public IReadOnlyList<CapCircle> Circles { get; init; } = [];

    /// <summary>The <c>geocode</c>s, in document order.</summary>
    public IReadOnlyList<CapValue> Geocodes { get; init; } = [];

    /// <summary>
    /// The NWS zone codes among <see cref="Geocodes"/> (<c>valueName</c> <c>UGC</c>),
    /// upper case, in document order, each once.
    /// </summary>
    public IReadOnlyList<string> ZoneCodes =>
        Geocodes.Where(g => string.Equals(g.Name, "UGC", StringComparison.OrdinalIgnoreCase))
            .Select(g => g.Value.Trim().ToUpperInvariant())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>Whether the area gives a shape of its own (a polygon or a circle).</summary>
    public bool HasShape => Polygons.Count > 0 || Circles.Count > 0;
}

/// <summary>A CAP <c>info</c> block: the event, its timing and what to do, in one language.</summary>
public sealed record CapInfo
{
    /// <summary>The <c>language</c>, e.g. <c>"en-US"</c>, or <see langword="null"/>.</summary>
    public string? Language { get; init; }

    /// <summary>The <c>category</c>s, e.g. <c>Met</c>.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>The <c>event</c>, e.g. <c>"Gale Warning"</c>.</summary>
    public string Event { get; init; } = string.Empty;

    /// <summary>The <c>responseType</c>s, e.g. <c>Avoid</c>.</summary>
    public IReadOnlyList<string> ResponseTypes { get; init; } = [];

    /// <summary>The <c>urgency</c> as written, e.g. <c>"Expected"</c>.</summary>
    public string Urgency { get; init; } = "Unknown";

    /// <summary>The <c>severity</c>.</summary>
    public CapSeverity Severity { get; init; }

    /// <summary>The <c>certainty</c> as written, e.g. <c>"Likely"</c>.</summary>
    public string Certainty { get; init; } = "Unknown";

    /// <summary>The <c>eventCode</c>s.</summary>
    public IReadOnlyList<CapValue> EventCodes { get; init; } = [];

    /// <summary>The <c>effective</c> time, or <see langword="null"/>.</summary>
    public DateTimeOffset? Effective { get; init; }

    /// <summary>The <c>onset</c> time, or <see langword="null"/>.</summary>
    public DateTimeOffset? Onset { get; init; }

    /// <summary>The <c>expires</c> time, or <see langword="null"/>.</summary>
    public DateTimeOffset? Expires { get; init; }

    /// <summary>
    /// When the hazard ends, as the NWS gives it apart from <see cref="Expires"/>
    /// (which is when the message should be replaced): the GeoJSON <c>ends</c>,
    /// or the CAP <c>eventEndingTime</c> parameter. <see langword="null"/> when not given.
    /// </summary>
    public DateTimeOffset? Ends { get; init; }

    /// <summary>The <c>senderName</c>, e.g. <c>"NWS San Francisco CA"</c>, or <see langword="null"/>.</summary>
    public string? SenderName { get; init; }

    /// <summary>The <c>headline</c>, or <see langword="null"/>.</summary>
    public string? Headline { get; init; }

    /// <summary>The <c>description</c>, or <see langword="null"/>.</summary>
    public string? Description { get; init; }

    /// <summary>The <c>instruction</c>, or <see langword="null"/>.</summary>
    public string? Instruction { get; init; }

    /// <summary>The <c>web</c> link, or <see langword="null"/>.</summary>
    public string? Web { get; init; }

    /// <summary>The <c>parameter</c>s.</summary>
    public IReadOnlyList<CapValue> Parameters { get; init; } = [];

    /// <summary>The <c>area</c>s.</summary>
    public IReadOnlyList<CapArea> Areas { get; init; } = [];
}

/// <summary>One CAP 1.2 alert message.</summary>
public sealed record CapAlert
{
    /// <summary>The <c>identifier</c>.</summary>
    public required string Identifier { get; init; }

    /// <summary>The <c>sender</c>, e.g. <c>"w-nws.webmaster@noaa.gov"</c>.</summary>
    public string Sender { get; init; } = string.Empty;

    /// <summary>The <c>sent</c> time.</summary>
    public DateTimeOffset Sent { get; init; }

    /// <summary>The <c>status</c> as written, e.g. <c>"Actual"</c>.</summary>
    public string Status { get; init; } = "Actual";

    /// <summary>The <c>msgType</c>.</summary>
    public CapMessageType MessageType { get; init; }

    /// <summary>The <c>scope</c> as written, e.g. <c>"Public"</c>, or <see langword="null"/>.</summary>
    public string? Scope { get; init; }

    /// <summary>
    /// The identifiers of the alerts this one updates or cancels (the middle
    /// part of each <c>sender,identifier,sent</c> in <c>references</c>), in
    /// document order.
    /// </summary>
    public IReadOnlyList<string> References { get; init; } = [];

    /// <summary>The <c>info</c> blocks.</summary>
    public IReadOnlyList<CapInfo> Infos { get; init; } = [];
}

/// <summary>What a CAP file holds: its alerts, in document order.</summary>
/// <param name="Format">The form the file came in.</param>
/// <param name="Alerts">The alerts.</param>
public sealed record CapDocument(CapFormat Format, IReadOnlyList<CapAlert> Alerts);

/// <summary>A file that isn't CAP, or CAP the reader can't use.</summary>
public sealed class CapFormatException : FormatException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong.</param>
    public CapFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with its cause.</summary>
    /// <param name="message">What is wrong.</param>
    /// <param name="innerException">The cause.</param>
    public CapFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
