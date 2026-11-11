using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EncDotNet.S100.Collections.Secom;

/// <summary>
/// Reads SECOM JSON responses tolerantly. Live services differ between
/// editions and implementations: product types come as <c>S124</c> or
/// <c>S-124</c>, the summary list as <c>summaryObject</c> or
/// <c>informationSummaryObject</c>, pagination may be absent, and dates may
/// be ISO 8601 or compact (<c>20261007T003625Z</c>).
/// </summary>
internal static partial class SecomJson
{
    /// <summary>Reads a <c>GetSummary</c> response.</summary>
    /// <exception cref="InvalidDataException">The document is not a summary response.</exception>
    public static SecomSummaryPage ReadSummaryPage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("A SECOM summary response must be a JSON object.");

        var list = Property(root, "summaryObject") ?? Property(root, "informationSummaryObject");
        if (list is not { ValueKind: JsonValueKind.Array or JsonValueKind.Null })
            throw new InvalidDataException("The SECOM summary response has no summary list.");

        var items = new List<SecomSummary>();
        if (list.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in list.Value.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || String(entry, "dataReference") is not { Length: > 0 } reference)
                    continue;
                items.Add(new SecomSummary(
                    reference,
                    NormalizeProduct(String(entry, "dataProductType")),
                    String(entry, "info_productVersion"),
                    Container(Property(entry, "containerType")),
                    String(entry, "info_identifier"),
                    String(entry, "info_name"),
                    String(entry, "info_status"),
                    String(entry, "info_description"),
                    Date(String(entry, "info_lastModifiedDate")),
                    Int64(Property(entry, "info_size")),
                    Bool(Property(entry, "dataProtection")),
                    Bool(Property(entry, "dataCompression"))));
            }
        }

        var (total, perPage) = Pagination(root);
        return new SecomSummaryPage(items, total, perPage);
    }

    /// <summary>Reads a <c>Capability</c> response.</summary>
    /// <exception cref="InvalidDataException">The document is not a capability response.</exception>
    public static SecomCapability ReadCapability(JsonElement root, SecomApiVersion version)
    {
        if (root.ValueKind != JsonValueKind.Object || Property(root, "capability") is not { ValueKind: JsonValueKind.Array } list)
            throw new InvalidDataException("The SECOM capability response has no capability list.");

        var entries = new List<SecomCapabilityEntry>();
        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                continue;
            var interfaces = Property(entry, "implementedInterfaces");
            entries.Add(new SecomCapabilityEntry(
                Container(Property(entry, "containerType")),
                NormalizeProduct(String(entry, "dataProductType")),
                String(entry, "serviceVersion"),
                interfaces is { } get && Bool(Property(get, "get")),
                interfaces is { } summary && Bool(Property(summary, "getSummary"))));
        }

        return new SecomCapability(version, entries);
    }

    /// <summary>Reads the data objects of a <c>Get</c> response.</summary>
    /// <exception cref="InvalidDataException">The document is not a get response, or its data is not base64.</exception>
    public static IReadOnlyList<SecomDataObject> ReadDataObjects(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || Property(root, "dataResponseObject") is not { } list)
            throw new InvalidDataException("The SECOM get response has no data list.");

        // Some services return a single object rather than a list.
        IEnumerable<JsonElement> entries = list.ValueKind switch
        {
            JsonValueKind.Array => list.EnumerateArray(),
            JsonValueKind.Object => [list],
            _ => [],
        };

        var objects = new List<SecomDataObject>();
        foreach (var entry in entries)
        {
            if (entry.ValueKind != JsonValueKind.Object || String(entry, "data") is not { } data)
                continue;
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(data);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException("SECOM data is not base64.", ex);
            }

            objects.Add(new SecomDataObject(bytes, ExchangeMetadata(Property(entry, "exchangeMetadata"))));
        }

        return objects;
    }

    /// <summary>
    /// Returns the canonical short form of a SECOM data product type
    /// (<c>S124</c>, <c>s-124</c> → <c>S-124</c>); other values (<c>RTZ</c>,
    /// <c>OTHER</c>) are upper-cased; empty values give <see langword="null"/>.
    /// </summary>
    public static string? NormalizeProduct(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        var match = ProductPattern().Match(trimmed);
        return match.Success ? "S-" + match.Groups[1].Value : trimmed.ToUpperInvariant();
    }

    /// <summary>The SECOM form of a canonical product for the given interface version (<c>S-124</c> → <c>S124</c> on v1).</summary>
    public static string ProductParameter(string productSpec, SecomApiVersion version) =>
        version == SecomApiVersion.V1 ? productSpec.Replace("-", string.Empty, StringComparison.Ordinal) : productSpec;

    /// <summary>Parses an ISO 8601 or compact (<c>yyyyMMddTHHmmssZ</c>) date-time.</summary>
    public static DateTimeOffset? Date(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var iso))
            return iso;
        return DateTimeOffset.TryParseExact(
            value, ["yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmssK", "yyyyMMdd"], CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var compact)
            ? compact
            : null;
    }

    private static SecomExchangeMetadata? ExchangeMetadata(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } metadata)
            return null;

        var signature = Property(metadata, "digitalSignatureValue");
        var certificates = new List<string>();
        string? thumbprint = null;
        string? value = null;
        if (signature is { ValueKind: JsonValueKind.Object } s)
        {
            switch (Property(s, "publicCertificate"))
            {
                case { ValueKind: JsonValueKind.Array } array:
                    certificates.AddRange(array.EnumerateArray()
                        .Where(c => c.ValueKind == JsonValueKind.String)
                        .Select(c => c.GetString()!)
                        .Where(c => c.Length > 0));
                    break;
                case { ValueKind: JsonValueKind.String } single when single.GetString() is { Length: > 0 } text:
                    certificates.Add(text);
                    break;
            }

            thumbprint = String(s, "publicRootCertificateThumbprint");
            value = String(s, "digitalSignature");
        }

        return new SecomExchangeMetadata(
            Bool(Property(metadata, "dataProtection")),
            String(metadata, "protectionScheme"),
            String(metadata, "digitalSignatureReference"),
            Bool(Property(metadata, "compressionFlag")),
            certificates,
            thumbprint,
            value);
    }

    private static (int? Total, int? PerPage) Pagination(JsonElement root) =>
        Property(root, "pagination") is { ValueKind: JsonValueKind.Object } pagination
            ? ((int?)Int64(Property(pagination, "totalItems")), (int?)Int64(Property(pagination, "maxItemsPerPage")))
            : (null, null);

    private static SecomContainerType Container(JsonElement? element) => element switch
    {
        { ValueKind: JsonValueKind.Number } n when n.TryGetInt32(out var value) && value is >= 0 and <= 2 => (SecomContainerType)value,
        { ValueKind: JsonValueKind.String } s => s.GetString()?.Trim().ToUpperInvariant() switch
        {
            "0" or "S100_DATASET" => SecomContainerType.DataSet,
            "1" or "S100_EXCHANGESET" => SecomContainerType.ExchangeSet,
            _ => SecomContainerType.None,
        },
        _ => SecomContainerType.DataSet,
    };

    private static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        if (element.TryGetProperty(name, out var exact))
            return exact;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        return null;
    }

    private static string? String(JsonElement element, string name) => Property(element, name) switch
    {
        { ValueKind: JsonValueKind.String } s => s.GetString(),
        { ValueKind: JsonValueKind.Number } n => n.GetRawText(),
        _ => null,
    };

    private static long? Int64(JsonElement? element) => element switch
    {
        { ValueKind: JsonValueKind.Number } n when n.TryGetInt64(out var value) => value,
        { ValueKind: JsonValueKind.String } s when long.TryParse(s.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
        _ => null,
    };

    private static bool Bool(JsonElement? element) => element switch
    {
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.String } s => string.Equals(s.GetString(), "true", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    [GeneratedRegex(@"^[Ss]-?(\d{2,3})$", RegexOptions.CultureInvariant)]
    private static partial Regex ProductPattern();
}
