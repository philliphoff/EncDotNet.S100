using System.Globalization;
using System.Text.Json;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo;

/// <summary>
/// JSON contract for the small subset of aisstream.io messages we
/// consume. Expressed as static methods rather than POCOs to keep
/// the API surface small (every field is consumed in exactly one
/// place anyway).
/// </summary>
internal static class AisStreamIoJson
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Builds the subscribe frame per
    /// <c>https://aisstream.io/documentation</c>. Bounding boxes
    /// are sent as <c>[[ [lat, lon], [lat, lon] ]]</c> (south-west
    /// then north-east).
    /// </summary>
    public static string BuildSubscribeFrame(string apiKey, AisSubscriptionRequest request)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        ArgumentNullException.ThrowIfNull(request);

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteString("APIKey", apiKey);

            writer.WritePropertyName("BoundingBoxes");
            writer.WriteStartArray();
            writer.WriteStartArray();
            if (request.Area is { } box)
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(box.SouthLatitude);
                writer.WriteNumberValue(box.WestLongitude);
                writer.WriteEndArray();
                writer.WriteStartArray();
                writer.WriteNumberValue(box.NorthLatitude);
                writer.WriteNumberValue(box.EastLongitude);
                writer.WriteEndArray();
            }
            else
            {
                // Service requires at least one outer bbox; -90/-180/90/180 ≈ "global".
                writer.WriteStartArray();
                writer.WriteNumberValue(-90.0);
                writer.WriteNumberValue(-180.0);
                writer.WriteEndArray();
                writer.WriteStartArray();
                writer.WriteNumberValue(90.0);
                writer.WriteNumberValue(180.0);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
            writer.WriteEndArray();

            if (request.Mmsis is { Count: > 0 })
            {
                writer.WritePropertyName("FiltersShipMMSI");
                writer.WriteStartArray();
                foreach (var mmsi in request.Mmsis)
                {
                    // aisstream.io expects MMSIs as strings.
                    writer.WriteStringValue(mmsi.ToString(CultureInfo.InvariantCulture));
                }
                writer.WriteEndArray();
            }

            var include = request.Include;
            if (include != (AisMessageKinds.PositionReports | AisMessageKinds.StaticVoyageData))
            {
                writer.WritePropertyName("FilterMessageTypes");
                writer.WriteStartArray();
                if (include.HasFlag(AisMessageKinds.PositionReports))
                {
                    writer.WriteStringValue("PositionReport");
                }
                if (include.HasFlag(AisMessageKinds.StaticVoyageData))
                {
                    writer.WriteStringValue("ShipStaticData");
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Parses one inbound JSON frame. Returns <see langword="null"/>
    /// for unknown / malformed frames; the driver logs and drops
    /// those.
    /// </summary>
    public static AisMessage? ParseInbound(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("MessageType", out var messageTypeProp)
                || messageTypeProp.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var messageType = messageTypeProp.GetString();
            return messageType switch
            {
                "PositionReport" => ParsePositionReport(root),
                "ShipStaticData" => ParseShipStaticData(root),
                _ => null,
            };
        }
    }

    private static (uint Mmsi, DateTimeOffset Timestamp)? ReadMetadata(JsonElement root)
    {
        if (!root.TryGetProperty("MetaData", out var meta) || meta.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        if (!meta.TryGetProperty("MMSI", out var mmsiProp) || mmsiProp.ValueKind != JsonValueKind.Number)
        {
            return null;
        }
        if (!mmsiProp.TryGetUInt32(out var mmsi)) return null;

        var timestamp = DateTimeOffset.UtcNow;
        if (meta.TryGetProperty("time_utc", out var timeProp)
            && timeProp.ValueKind == JsonValueKind.String
            && timeProp.GetString() is { } timeString
            && TryParseAisStreamTimestamp(timeString, out var parsed))
        {
            timestamp = parsed;
        }
        return (mmsi, timestamp);
    }

    /// <summary>
    /// aisstream.io emits timestamps in Go's <c>"2006-01-02 15:04:05.000000000 -0700 MST"</c> form
    /// (e.g. <c>"2026-05-29 14:23:01.234567 +0000 UTC"</c>). Strip the trailing zone-name token
    /// before handing to <see cref="DateTimeOffset.TryParseExact"/>.
    /// </summary>
    internal static bool TryParseAisStreamTimestamp(string text, out DateTimeOffset value)
    {
        var trimmed = text.AsSpan().TrimEnd();
        var lastSpace = trimmed.LastIndexOf(' ');
        if (lastSpace > 0)
        {
            // If the trailing token isn't a numeric offset, drop it (Go's MST-style label).
            var tail = trimmed[(lastSpace + 1)..];
            if (tail.Length > 0 && tail[0] != '+' && tail[0] != '-')
            {
                trimmed = trimmed[..lastSpace];
            }
        }
        var candidate = trimmed.ToString();
        var formats = new[]
        {
            "yyyy-MM-dd HH:mm:ss.ffffff zzz",
            "yyyy-MM-dd HH:mm:ss.fffffff zzz",
            "yyyy-MM-dd HH:mm:ss.fff zzz",
            "yyyy-MM-dd HH:mm:ss zzz",
        };
        return DateTimeOffset.TryParseExact(candidate, formats,
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value)
            || DateTimeOffset.TryParse(candidate, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out value);
    }

    private static AisPositionReport? ParsePositionReport(JsonElement root)
    {
        var meta = ReadMetadata(root);
        if (meta is null) return null;
        if (!root.TryGetProperty("Message", out var message)
            || !message.TryGetProperty("PositionReport", out var pr))
        {
            return null;
        }

        // Position can come either from the inner PositionReport block
        // (Latitude / Longitude) or from the MetaData block — aisstream
        // is inconsistent across message families. Prefer the inner.
        var lat = TryGetDouble(pr, "Latitude")
                  ?? TryGetDouble(root.GetProperty("MetaData"), "latitude");
        var lon = TryGetDouble(pr, "Longitude")
                  ?? TryGetDouble(root.GetProperty("MetaData"), "longitude");
        if (lat is null || lon is null) return null;

        var cog = CollapseSentinel(TryGetDouble(pr, "Cog"), 360.0);
        var hdg = CollapseSentinel(TryGetDouble(pr, "TrueHeading"), 511.0);
        var sog = CollapseSentinel(TryGetDouble(pr, "Sog"), 102.3);
        var rot = CollapseSentinelSigned(TryGetDouble(pr, "RateOfTurn"), -128.0);

        AisNavigationStatus? nav = null;
        if (TryGetInt(pr, "NavigationalStatus") is { } navInt
            && navInt >= 0 && navInt <= 15)
        {
            nav = (AisNavigationStatus)navInt;
        }

        return new AisPositionReport
        {
            Mmsi = meta.Value.Mmsi,
            Timestamp = meta.Value.Timestamp,
            Latitude = lat.Value,
            Longitude = lon.Value,
            CourseOverGroundDeg = cog,
            HeadingDeg = hdg,
            SpeedOverGroundKn = sog,
            RateOfTurnDegPerMin = rot,
            NavigationStatus = nav,
        };
    }

    private static AisStaticVoyageData? ParseShipStaticData(JsonElement root)
    {
        var meta = ReadMetadata(root);
        if (meta is null) return null;
        if (!root.TryGetProperty("Message", out var message)
            || !message.TryGetProperty("ShipStaticData", out var ssd))
        {
            return null;
        }

        var typeCode = TryGetInt(ssd, "Type") ?? 0;
        AisDimensions? dims = null;
        if (ssd.TryGetProperty("Dimension", out var dim) && dim.ValueKind == JsonValueKind.Object)
        {
            var a = TryGetDouble(dim, "A") ?? 0;
            var b = TryGetDouble(dim, "B") ?? 0;
            var c = TryGetDouble(dim, "C") ?? 0;
            var d = TryGetDouble(dim, "D") ?? 0;
            var length = a + b;
            var beam = c + d;
            if (length > 0 && beam > 0)
            {
                dims = new AisDimensions
                {
                    LengthMetres = length,
                    BeamMetres = beam,
                    BowOffsetMetres = a,
                    PortOffsetMetres = c,
                };
            }
        }

        return new AisStaticVoyageData
        {
            Mmsi = meta.Value.Mmsi,
            Timestamp = meta.Value.Timestamp,
            ImoNumber = TryGetUInt(ssd, "ImoNumber"),
            CallSign = TryGetTrimmedString(ssd, "CallSign"),
            VesselName = TryGetTrimmedString(ssd, "Name")
                          ?? TryGetTrimmedString(root.GetProperty("MetaData"), "ShipName"),
            ShipType = (AisShipType)typeCode,
            ShipTypeClass = AisShipTypeClassExtensions.ToClass(typeCode),
            Dimensions = dims,
            DraughtMetres = TryGetDouble(ssd, "MaximumStaticDraught"),
            Destination = TryGetTrimmedString(ssd, "Destination"),
            Eta = TryReadEta(ssd),
        };
    }

    private static DateTimeOffset? TryReadEta(JsonElement ssd)
    {
        if (!ssd.TryGetProperty("Eta", out var eta) || eta.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var month = TryGetInt(eta, "Month");
        var day = TryGetInt(eta, "Day");
        var hour = TryGetInt(eta, "Hour");
        var minute = TryGetInt(eta, "Minute");
        if (month is null || day is null || hour is null || minute is null) return null;
        if (month is 0 || day is 0) return null;
        try
        {
            // AIS ETA has no year — pin to current UTC year.
            var year = DateTime.UtcNow.Year;
            return new DateTimeOffset(year, month.Value, day.Value, hour.Value, minute.Value, 0, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static double? CollapseSentinel(double? value, double sentinel)
    {
        if (value is null) return null;
        return Math.Abs(value.Value - sentinel) < 0.01 ? null : value.Value;
    }

    private static double? CollapseSentinelSigned(double? value, double sentinel)
    {
        if (value is null) return null;
        // AIS ROT sentinel is -128 / +128; both indicate "not available".
        return Math.Abs(value.Value) >= Math.Abs(sentinel) - 0.01 ? null : value.Value;
    }

    private static double? TryGetDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.TryGetDouble(out var d) ? d : null,
            _ => null,
        };
    }

    private static int? TryGetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var i) ? i : null;
    }

    private static uint? TryGetUInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.Number && prop.TryGetUInt32(out var u) ? u : null;
    }

    private static string? TryGetTrimmedString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop)) return null;
        if (prop.ValueKind != JsonValueKind.String) return null;
        var text = prop.GetString();
        if (string.IsNullOrWhiteSpace(text)) return null;
        return text.TrimEnd().TrimEnd('@', ' ');
    }

    /// <summary>
    /// Replaces the API key in a logged subscribe frame with a
    /// fixed redaction marker so the key never lands in logs.
    /// </summary>
    public static string RedactApiKey(string frame, string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey)) return frame;
        return frame.Replace(apiKey, "***REDACTED***", StringComparison.Ordinal);
    }
}
