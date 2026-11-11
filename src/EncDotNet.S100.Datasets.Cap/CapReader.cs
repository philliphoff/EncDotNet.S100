using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.Cap;

/// <summary>
/// Reads CAP 1.2 marine weather warnings in the three forms the NWS and other
/// alerting authorities publish (design note <c>docs/design/weather-sources.md</c> §6.1):
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// <b>CAP XML</b>: one <c>&lt;alert&gt;</c> in the CAP 1.2 namespace
/// (<c>urn:oasis:names:tc:emergency:cap:1.2</c>; 1.1 and 1.0 read the same).
/// </description></item>
/// <item><description>
/// <b>Atom</b>: a feed whose entries each hold a whole <c>&lt;alert&gt;</c>, or,
/// as <c>api.weather.gov/alerts.atom</c> writes them, carry the alert's
/// fields as <c>cap:</c> elements of the entry itself (one info, one area),
/// with the identifier in the entry's <c>id</c>, the headline in its
/// <c>title</c> and the description in its <c>summary</c>.
/// </description></item>
/// <item><description>
/// <b>NWS GeoJSON</b>: an alert feature, or a feature collection of them, as
/// <c>api.weather.gov/alerts</c> returns them. The feature's geometry is the
/// area's polygon; <c>ends</c> is <see cref="CapInfo.Ends"/>.
/// </description></item>
/// </list>
/// <para>
/// Elements are matched by local name, so a producer's namespace prefix
/// doesn't matter. Unknown elements are ignored. A <c>polygon</c> or
/// <c>circle</c> that can't be read is skipped rather than failing the file.
/// </para>
/// </remarks>
public static class CapReader
{
    /// <summary>The CAP 1.2 XML namespace.</summary>
    public const string Cap12Namespace = "urn:oasis:names:tc:emergency:cap:1.2";

    /// <summary>The start every CAP namespace shares (1.0, 1.1 and 1.2).</summary>
    public const string CapNamespacePrefix = "urn:oasis:names:tc:emergency:cap:";

    /// <summary>The Atom namespace.</summary>
    public const string AtomNamespace = "http://www.w3.org/2005/Atom";

    /// <summary>Reads a CAP file.</summary>
    /// <param name="path">The file's path.</param>
    /// <returns>The alerts.</returns>
    /// <exception cref="CapFormatException">The file isn't CAP XML, a CAP Atom feed or NWS alert GeoJSON.</exception>
    public static CapDocument Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>Reads CAP from <paramref name="stream"/>, which is left open.</summary>
    /// <param name="stream">The stream, read to its end.</param>
    /// <returns>The alerts.</returns>
    /// <exception cref="CapFormatException">The stream isn't CAP XML, a CAP Atom feed or NWS alert GeoJSON.</exception>
    public static CapDocument Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Read(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    /// <summary>Reads CAP from bytes.</summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <returns>The alerts.</returns>
    /// <exception cref="CapFormatException">The bytes aren't CAP XML, a CAP Atom feed or NWS alert GeoJSON.</exception>
    public static CapDocument Read(ReadOnlySpan<byte> bytes)
    {
        var start = SkipBomAndWhitespace(bytes);
        if (start >= bytes.Length)
            throw new CapFormatException("The file is empty.");

        return bytes[start] switch
        {
            (byte)'<' => ReadXml(bytes),
            (byte)'{' => NwsAlertGeoJson.Read(bytes),
            _ => throw new CapFormatException("The file is neither XML nor JSON."),
        };
    }

    /// <summary>
    /// Whether <paramref name="leadingBytes"/>, the start of a file, look like
    /// CAP: an XML document whose root is a CAP <c>alert</c>, or an Atom
    /// <c>feed</c> that declares the CAP namespace; or JSON from
    /// <c>api.weather.gov</c> that names an alert (<c>wx:Alert</c> or an
    /// <c>/alerts/</c> URL). Used to detect CAP files whatever their extension.
    /// </summary>
    /// <param name="leadingBytes">The file's first bytes; a kilobyte is enough for the NWS forms.</param>
    /// <returns><see langword="true"/> when the bytes look like CAP.</returns>
    /// <remarks>
    /// An S-100 GML dataset (S-124 and the others) has a <c>DataSet</c> root in
    /// an S-100 namespace, so it is never CAP; GeoJSON from anywhere other than
    /// <c>api.weather.gov</c> isn't claimed.
    /// </remarks>
    public static bool LooksLikeCap(ReadOnlySpan<byte> leadingBytes)
    {
        var start = SkipBomAndWhitespace(leadingBytes);
        if (start >= leadingBytes.Length)
            return false;

        var text = Encoding.UTF8.GetString(leadingBytes[start..]);
        if (text[0] == '{')
        {
            return text.Contains("api.weather.gov", StringComparison.OrdinalIgnoreCase)
                && (text.Contains("\"wx:Alert\"", StringComparison.Ordinal)
                    || text.Contains("api.weather.gov/alerts", StringComparison.OrdinalIgnoreCase));
        }

        if (text[0] != '<' || RootElementName(text) is not { } root)
            return false;
        var local = root[(root.IndexOf(':') + 1)..];
        var declaresCap = text.Contains(CapNamespacePrefix, StringComparison.Ordinal);
        return declaresCap && (local == "alert" || local == "feed");
    }

    /// <summary>
    /// Whether an XML root element (its namespace and local name, and the
    /// document's first characters) is CAP: an <c>alert</c> in a CAP namespace,
    /// or an Atom <c>feed</c> whose text declares the CAP namespace.
    /// </summary>
    /// <param name="namespaceUri">The root's namespace URI.</param>
    /// <param name="localName">The root's local name.</param>
    /// <param name="documentPrefix">The start of the document, or its whole text.</param>
    /// <returns><see langword="true"/> when the root is CAP.</returns>
    public static bool IsCapRoot(string namespaceUri, string localName, string documentPrefix)
    {
        if (string.Equals(localName, "alert", StringComparison.Ordinal)
            && namespaceUri.StartsWith(CapNamespacePrefix, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(localName, "feed", StringComparison.Ordinal)
            || !string.Equals(namespaceUri, AtomNamespace, StringComparison.Ordinal))
        {
            return false;
        }

        var prefix = documentPrefix.AsSpan(0, Math.Min(documentPrefix.Length, 64 * 1024));
        return prefix.IndexOf(CapNamespacePrefix.AsSpan(), StringComparison.Ordinal) >= 0;
    }

    // -----------------------------------------------------------------
    // XML: one alert, or an Atom feed
    // -----------------------------------------------------------------

    private static CapDocument ReadXml(ReadOnlySpan<byte> bytes)
    {
        XDocument document;
        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            });
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new CapFormatException($"The file isn't well-formed XML: {ex.Message}", ex);
        }

        var root = document.Root ?? throw new CapFormatException("The XML document has no root element.");
        if (root.Name.LocalName == "alert" && root.Name.NamespaceName.StartsWith(CapNamespacePrefix, StringComparison.Ordinal))
            return new CapDocument(CapFormat.CapXml, [ReadAlert(root)]);

        if (root.Name.LocalName == "feed" && root.Name.NamespaceName == AtomNamespace)
        {
            var alerts = new List<CapAlert>();
            foreach (var entry in root.Elements().Where(e => e.Name.LocalName == "entry"))
            {
                if (ReadEntry(entry) is { } alert)
                    alerts.Add(alert);
            }

            return new CapDocument(CapFormat.Atom, alerts);
        }

        throw new CapFormatException(
            $"The XML root is <{root.Name.LocalName}> in '{root.Name.NamespaceName}', not a CAP alert or an Atom feed.");
    }

    // An Atom entry: a whole alert inside it (in content or directly), or the
    // NWS entry with cap: fields.
    private static CapAlert? ReadEntry(XElement entry)
    {
        var whole = entry.Descendants().FirstOrDefault(e =>
            e.Name.LocalName == "alert" && e.Name.NamespaceName.StartsWith(CapNamespacePrefix, StringComparison.Ordinal));
        if (whole is not null)
            return ReadAlert(whole);

        bool IsCap(XElement e) => e.Name.NamespaceName.StartsWith(CapNamespacePrefix, StringComparison.Ordinal);
        var cap = entry.Elements().Where(IsCap).ToList();
        if (cap.Count == 0)
            return null;

        string? Cap(string name) => Text(cap.FirstOrDefault(e => e.Name.LocalName == name));
        string? Atom(string name) => Text(entry.Elements().FirstOrDefault(e => e.Name.LocalName == name && !IsCap(e)));

        var parameters = cap.Where(e => e.Name.LocalName == "parameter").Select(ReadValue).OfType<CapValue>().ToArray();
        var area = new CapArea
        {
            Description = Cap("areaDesc") ?? string.Empty,
            Polygons = cap.Where(e => e.Name.LocalName == "polygon").Select(e => ParsePolygon(e.Value)).OfType<IReadOnlyList<GeoPosition>>().ToArray(),
            Circles = cap.Where(e => e.Name.LocalName == "circle").Select(e => ParseCircle(e.Value)).OfType<CapCircle>().ToArray(),
            Geocodes = cap.Where(e => e.Name.LocalName == "geocode").Select(ReadValue).OfType<CapValue>().ToArray(),
        };

        var info = new CapInfo
        {
            Language = Cap("language"),
            Categories = cap.Where(e => e.Name.LocalName == "category").Select(e => e.Value.Trim()).ToArray(),
            Event = Cap("event") ?? string.Empty,
            ResponseTypes = cap.Where(e => e.Name.LocalName == "responseType").Select(e => e.Value.Trim()).ToArray(),
            Urgency = Cap("urgency") ?? "Unknown",
            Severity = ParseSeverity(Cap("severity")),
            Certainty = Cap("certainty") ?? "Unknown",
            EventCodes = cap.Where(e => e.Name.LocalName == "eventCode").Select(ReadValue).OfType<CapValue>().ToArray(),
            Effective = ParseTime(Cap("effective")),
            Onset = ParseTime(Cap("onset")),
            Expires = ParseTime(Cap("expires")),
            Ends = EventEndingTime(parameters),
            SenderName = Cap("senderName"),
            Headline = Cap("headline") ?? Atom("title"),
            Description = Cap("description") ?? Atom("summary"),
            Instruction = Cap("instruction"),
            Web = Cap("web"),
            Parameters = parameters,
            Areas = area.Description.Length > 0 || area.HasShape || area.Geocodes.Count > 0 ? [area] : [],
        };

        var id = Atom("id") ?? Cap("identifier") ?? string.Empty;
        return new CapAlert
        {
            Identifier = Cap("identifier") ?? IdentifierFromUrl(id),
            Sender = Text(entry.Elements().FirstOrDefault(e => e.Name.LocalName == "author")?.Elements().FirstOrDefault(e => e.Name.LocalName == "name")) ?? string.Empty,
            Sent = ParseTime(Cap("sent")) ?? ParseTime(Atom("published")) ?? ParseTime(Atom("updated")) ?? default,
            Status = Cap("status") ?? "Actual",
            MessageType = ParseMessageType(Cap("msgType")),
            Scope = Cap("scope"),
            References = ParseReferences(Cap("references")),
            Infos = [info],
        };
    }

    private static CapAlert ReadAlert(XElement alert)
    {
        string? Child(string name) => Text(alert.Elements().FirstOrDefault(e => e.Name.LocalName == name));

        var identifier = Child("identifier");
        if (string.IsNullOrWhiteSpace(identifier))
            throw new CapFormatException("A CAP alert has no identifier.");

        return new CapAlert
        {
            Identifier = identifier,
            Sender = Child("sender") ?? string.Empty,
            Sent = ParseTime(Child("sent")) ?? default,
            Status = Child("status") ?? "Actual",
            MessageType = ParseMessageType(Child("msgType")),
            Scope = Child("scope"),
            References = ParseReferences(Child("references")),
            Infos = alert.Elements().Where(e => e.Name.LocalName == "info").Select(ReadInfo).ToArray(),
        };
    }

    private static CapInfo ReadInfo(XElement info)
    {
        string? Child(string name) => Text(info.Elements().FirstOrDefault(e => e.Name.LocalName == name));
        IEnumerable<XElement> All(string name) => info.Elements().Where(e => e.Name.LocalName == name);

        var parameters = All("parameter").Select(ReadValue).OfType<CapValue>().ToArray();
        return new CapInfo
        {
            Language = Child("language"),
            Categories = All("category").Select(e => e.Value.Trim()).ToArray(),
            Event = Child("event") ?? string.Empty,
            ResponseTypes = All("responseType").Select(e => e.Value.Trim()).ToArray(),
            Urgency = Child("urgency") ?? "Unknown",
            Severity = ParseSeverity(Child("severity")),
            Certainty = Child("certainty") ?? "Unknown",
            EventCodes = All("eventCode").Select(ReadValue).OfType<CapValue>().ToArray(),
            Effective = ParseTime(Child("effective")),
            Onset = ParseTime(Child("onset")),
            Expires = ParseTime(Child("expires")),
            Ends = EventEndingTime(parameters),
            SenderName = Child("senderName"),
            Headline = Child("headline"),
            Description = Child("description"),
            Instruction = Child("instruction"),
            Web = Child("web"),
            Parameters = parameters,
            Areas = All("area").Select(ReadArea).ToArray(),
        };
    }

    private static CapArea ReadArea(XElement area)
    {
        IEnumerable<XElement> All(string name) => area.Elements().Where(e => e.Name.LocalName == name);
        return new CapArea
        {
            Description = Text(All("areaDesc").FirstOrDefault()) ?? string.Empty,
            Polygons = All("polygon").Select(e => ParsePolygon(e.Value)).OfType<IReadOnlyList<GeoPosition>>().ToArray(),
            Circles = All("circle").Select(e => ParseCircle(e.Value)).OfType<CapCircle>().ToArray(),
            Geocodes = All("geocode").Select(ReadValue).OfType<CapValue>().ToArray(),
        };
    }

    private static CapValue? ReadValue(XElement element)
    {
        var name = Text(element.Elements().FirstOrDefault(e => e.Name.LocalName == "valueName"));
        var value = Text(element.Elements().FirstOrDefault(e => e.Name.LocalName == "value"));
        return name is null || value is null ? null : new CapValue(name, value);
    }

    private static string? Text(XElement? element)
    {
        if (element is null)
            return null;
        var value = element.Value.Trim();
        return value.Length == 0 ? null : value;
    }

    // -----------------------------------------------------------------
    // Values shared by the XML and GeoJSON forms
    // -----------------------------------------------------------------

    /// <summary>
    /// Parses a CAP <c>polygon</c>: space-separated <c>latitude,longitude</c>
    /// pairs, the first and last the same. A ring that isn't closed is closed.
    /// </summary>
    /// <param name="text">The element's text.</param>
    /// <returns>The ring, or <see langword="null"/> when it has fewer than three distinct positions or can't be read.</returns>
    public static IReadOnlyList<GeoPosition>? ParsePolygon(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var ring = new List<GeoPosition>();
        foreach (var pair in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (ParsePosition(pair) is not { } position)
                return null;
            ring.Add(position);
        }

        return CloseRing(ring);
    }

    /// <summary>Parses a CAP <c>circle</c>: <c>latitude,longitude radius</c>, the radius in kilometres.</summary>
    /// <param name="text">The element's text.</param>
    /// <returns>The circle, or <see langword="null"/> when it can't be read.</returns>
    public static CapCircle? ParseCircle(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || ParsePosition(parts[0]) is not { } centre
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var radius)
            || !double.IsFinite(radius) || radius < 0)
        {
            return null;
        }

        return new CapCircle(centre, radius);
    }

    internal static IReadOnlyList<GeoPosition>? CloseRing(List<GeoPosition> ring)
    {
        if (ring.Count > 0 && ring[0] != ring[^1])
            ring.Add(ring[0]);
        return ring.Distinct().Count() < 3 ? null : ring;
    }

    private static GeoPosition? ParsePosition(string pair)
    {
        var comma = pair.IndexOf(',');
        if (comma <= 0
            || !double.TryParse(pair.AsSpan(0, comma), NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            || !double.TryParse(pair.AsSpan(comma + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
            || latitude is < -90 or > 90
            || longitude is < -180 or > 180)
        {
            return null;
        }

        return new GeoPosition(latitude, longitude);
    }

    internal static DateTimeOffset? ParseTime(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
            ? time
            : null;

    internal static CapSeverity ParseSeverity(string? text) => text?.Trim() switch
    {
        { } s when s.Equals("Extreme", StringComparison.OrdinalIgnoreCase) => CapSeverity.Extreme,
        { } s when s.Equals("Severe", StringComparison.OrdinalIgnoreCase) => CapSeverity.Severe,
        { } s when s.Equals("Moderate", StringComparison.OrdinalIgnoreCase) => CapSeverity.Moderate,
        { } s when s.Equals("Minor", StringComparison.OrdinalIgnoreCase) => CapSeverity.Minor,
        _ => CapSeverity.Unknown,
    };

    internal static CapMessageType ParseMessageType(string? text) => text?.Trim() switch
    {
        { } s when s.Equals("Update", StringComparison.OrdinalIgnoreCase) => CapMessageType.Update,
        { } s when s.Equals("Cancel", StringComparison.OrdinalIgnoreCase) => CapMessageType.Cancel,
        { } s when s.Equals("Ack", StringComparison.OrdinalIgnoreCase) => CapMessageType.Ack,
        { } s when s.Equals("Error", StringComparison.OrdinalIgnoreCase) => CapMessageType.Error,
        _ => CapMessageType.Alert,
    };

    // CAP references: space-separated "sender,identifier,sent" triples.
    internal static IReadOnlyList<string> ParseReferences(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        var identifiers = new List<string>();
        foreach (var triple in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = triple.Split(',');
            if (parts.Length >= 2 && parts[1].Length > 0)
                identifiers.Add(parts[1]);
        }

        return identifiers;
    }

    internal static DateTimeOffset? EventEndingTime(IReadOnlyList<CapValue> parameters) =>
        ParseTime(parameters.FirstOrDefault(p => string.Equals(p.Name, "eventEndingTime", StringComparison.OrdinalIgnoreCase))?.Value);

    // ".../alerts/urn:oid:2.49..." -> "urn:oid:2.49...".
    internal static string IdentifierFromUrl(string id)
    {
        var slash = id.LastIndexOf('/');
        return slash >= 0 && slash < id.Length - 1 ? id[(slash + 1)..] : id;
    }

    private static int SkipBomAndWhitespace(ReadOnlySpan<byte> bytes)
    {
        var start = bytes.StartsWith("﻿"u8) ? 3 : 0;
        while (start < bytes.Length && bytes[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            start++;
        return start;
    }

    // The root element's qualified name, skipping the XML declaration,
    // processing instructions and comments; null for a DOCTYPE or when the
    // root starts past the text.
    private static string? RootElementName(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var open = text.IndexOf('<', i);
            if (open < 0 || open + 1 >= text.Length)
                return null;
            if (text[open + 1] == '?')
            {
                var end = text.IndexOf("?>", open, StringComparison.Ordinal);
                if (end < 0) return null;
                i = end + 2;
                continue;
            }

            if (text.AsSpan(open).StartsWith("<!--"))
            {
                var end = text.IndexOf("-->", open, StringComparison.Ordinal);
                if (end < 0) return null;
                i = end + 3;
                continue;
            }

            if (text[open + 1] == '!')
                return null;

            var nameEnd = open + 1;
            while (nameEnd < text.Length && !char.IsWhiteSpace(text[nameEnd]) && text[nameEnd] is not ('>' or '/'))
                nameEnd++;
            return nameEnd >= text.Length ? null : text[(open + 1)..nameEnd];
        }

        return null;
    }
}
