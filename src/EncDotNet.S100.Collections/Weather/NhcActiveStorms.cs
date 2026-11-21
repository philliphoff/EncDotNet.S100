using System.Globalization;
using System.Text.Json;

namespace EncDotNet.S100.Collections.Weather;

/// <summary>
/// One storm of NHC's active-storm list, <c>CurrentStorms.json</c> (design
/// note <c>vector-weather.md</c> §8.1): what the Library lists it by and the
/// GIS zips of its latest advisory.
/// </summary>
/// <param name="AtcfId">The ATCF id in lower case, as NHC writes it: <c>al142026</c>.</param>
/// <param name="Name">The storm's name, as NHC writes it ("Zelda"), or its number for an unnamed one.</param>
/// <param name="Classification">NHC's class code: <c>TD</c>, <c>TS</c>, <c>HU</c>, <c>PTC</c> and so on.</param>
/// <param name="IntensityKnots">The maximum sustained wind in knots, if given.</param>
/// <param name="PressureHectopascals">The minimum central pressure in hectopascals, if given.</param>
/// <param name="Latitude">The storm's latitude in degrees, if given.</param>
/// <param name="Longitude">The storm's longitude in degrees, if given.</param>
/// <param name="Advisory">The latest forecast advisory's number as NHC writes it in file names: <c>"017"</c> or <c>"017A"</c>.</param>
/// <param name="Issued">When that advisory was issued (UTC).</param>
/// <param name="ForecastZip">The advisory's forecast track and cone zip (<c>…_5day_017.zip</c>).</param>
/// <param name="WindRadiiZip">The advisory's forecast wind radii zip (<c>…_fcst_017.zip</c>), if NHC lists one.</param>
/// <param name="BestTrackZip">The storm's best track zip (<c>…_best_track.zip</c>), if NHC lists one.</param>
public sealed record NhcActiveStorm(
    string AtcfId,
    string Name,
    string Classification,
    int? IntensityKnots,
    int? PressureHectopascals,
    double? Latitude,
    double? Longitude,
    string Advisory,
    DateTimeOffset Issued,
    Uri ForecastZip,
    Uri? WindRadiiZip,
    Uri? BestTrackZip)
{
    /// <summary>The advisory number as people read it, without leading zeros: <c>"17"</c> or <c>"17A"</c>.</summary>
    public string AdvisoryNumber => NhcActiveStorms.TrimAdvisory(Advisory);

    /// <summary>
    /// The short id the Library shows beside the name, the basin and storm
    /// number in upper case: <c>AL14</c> for <c>al142026</c>.
    /// </summary>
    public string ShortId => AtcfId.Length >= 4 ? AtcfId[..4].ToUpperInvariant() : AtcfId.ToUpperInvariant();

    /// <summary>
    /// The basin, from the ATCF id's first two letters: "Atlantic", "East
    /// Pacific" or "Central Pacific"; null for another.
    /// </summary>
    public string? Basin => NhcActiveStorms.BasinOf(AtcfId);

    /// <summary>
    /// The GIS zips of the advisory, each with the name NHC gives it: the
    /// forecast zip first, then the wind radii and best track when listed.
    /// </summary>
    public IReadOnlyList<(Uri Uri, string FileName)> Files
    {
        get
        {
            var files = new List<(Uri, string)> { (ForecastZip, NhcActiveStorms.FileNameOf(ForecastZip)) };
            if (WindRadiiZip is { } radii)
                files.Add((radii, NhcActiveStorms.FileNameOf(radii)));
            if (BestTrackZip is { } best)
                files.Add((best, NhcActiveStorms.FileNameOf(best)));
            return files;
        }
    }
}

/// <summary>
/// Reads NHC's active-storm list, <c>CurrentStorms.json</c> (V27): one entry
/// per storm NHC is issuing advisories for in the Atlantic, East and Central
/// Pacific.
/// </summary>
/// <remarks>
/// <para>
/// Each entry of <c>activeStorms</c> carries the storm (<c>id</c>,
/// <c>name</c>, <c>classification</c>, <c>intensity</c> and <c>pressure</c>
/// as strings, <c>latitudeNumeric</c>, <c>longitudeNumeric</c>) and one
/// object per product, each with its advisory (<c>advNum</c>), issue time
/// (<c>issuance</c>) and files. The forecast track and cone zip is
/// <c>forecastTrack.zipFile</c> (else <c>trackCone.zipFile</c>), the wind
/// radii zip <c>forecastWindRadiiGIS.zipFile</c> and the best track
/// <c>bestTrackGIS.zipFile</c>; a product NHC doesn't issue for the storm
/// is <c>null</c>.
/// </para>
/// <para>
/// A storm without a forecast zip (a potential tropical cyclone whose GIS
/// files aren't out yet) is left out, as is one whose zip is on another host
/// or isn't named as NHC names its zips.
/// </para>
/// </remarks>
public static class NhcActiveStorms
{
    /// <summary>The ATCF basins NHC issues advisories for: Atlantic, East and Central Pacific.</summary>
    public static IReadOnlyList<string> Basins { get; } = ["al", "ep", "cp"];

    /// <summary>
    /// Reads the storms of a <c>CurrentStorms.json</c> document. Storms that
    /// are malformed, in another basin or without a forecast zip are skipped.
    /// </summary>
    /// <param name="json">The document.</param>
    /// <param name="baseUri">The address it was read from, which file links are resolved against and must share a host with.</param>
    /// <exception cref="InvalidDataException">The document isn't an active-storm list.</exception>
    public static IReadOnlyList<NhcActiveStorm> Parse(string json, Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(baseUri);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{baseUri.AbsoluteUri} isn't JSON: {ex.Message}", ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("activeStorms", out var storms)
                || storms.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"{baseUri.AbsoluteUri} has no activeStorms list; is it NHC's CurrentStorms.json?");
            }

            var result = new List<NhcActiveStorm>();
            foreach (var storm in storms.EnumerateArray())
            {
                if (TryRead(storm, baseUri) is { } read)
                    result.Add(read);
            }

            return result;
        }
    }

    /// <summary>The advisory number without leading zeros: <c>"017A"</c> → <c>"17A"</c>.</summary>
    /// <param name="advisory">The advisory as NHC writes it in file names.</param>
    public static string TrimAdvisory(string advisory)
    {
        ArgumentNullException.ThrowIfNull(advisory);
        var trimmed = advisory.TrimStart('0');
        return trimmed.Length == 0 || !char.IsAsciiDigit(trimmed[0]) ? "0" + trimmed : trimmed;
    }

    /// <summary>The basin of an ATCF id ("Atlantic", "East Pacific" or "Central Pacific"), or null for another.</summary>
    /// <param name="atcfId">The ATCF id, in either case.</param>
    public static string? BasinOf(string atcfId) =>
        atcfId is { Length: >= 2 } id
            ? id[..2].ToLowerInvariant() switch
            {
                "al" => "Atlantic",
                "ep" => "East Pacific",
                "cp" => "Central Pacific",
                _ => null,
            }
            : null;

    internal static string FileNameOf(Uri uri) => Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath));

    private static NhcActiveStorm? TryRead(JsonElement storm, Uri baseUri)
    {
        if (storm.ValueKind != JsonValueKind.Object || Text(storm, "id") is not { Length: 8 } id)
            return null;
        id = id.ToLowerInvariant();
        if (!Basins.Contains(id[..2]))
            return null;

        var track = Product(storm, "forecastTrack") ?? Product(storm, "trackCone");
        if (track is not { } forecast
            || Link(forecast, "zipFile", baseUri) is not { } forecastZip
            || !IsAdvisoryZip(forecastZip, id, "_5day_", out var advisory)
            || Time(forecast, "issuance") is not { } issued)
        {
            return null;
        }

        Uri? radiiZip = Product(storm, "forecastWindRadiiGIS") is { } radii
            && Link(radii, "zipFile", baseUri) is { } r && IsAdvisoryZip(r, id, "_fcst_", out var radiiAdvisory)
            && string.Equals(radiiAdvisory, advisory, StringComparison.OrdinalIgnoreCase)
                ? r
                : null;
        Uri? bestZip = Product(storm, "bestTrackGIS") is { } best
            && Link(best, "zipFile", baseUri) is { } b
            && string.Equals(FileNameOf(b), id + "_best_track.zip", StringComparison.OrdinalIgnoreCase)
                ? b
                : null;

        return new NhcActiveStorm(
            id,
            Text(storm, "name") is { Length: > 0 } name ? name : id.ToUpperInvariant(),
            Text(storm, "classification") ?? string.Empty,
            Integer(storm, "intensity"),
            Integer(storm, "pressure"),
            Number(storm, "latitudeNumeric"),
            Number(storm, "longitudeNumeric"),
            advisory,
            issued,
            forecastZip,
            radiiZip,
            bestZip);
    }

    // A zip named as NHC names an advisory's: {id}{kind}{adv}.zip, with a three-digit advisory and an optional letter.
    private static bool IsAdvisoryZip(Uri uri, string id, string kind, out string advisory)
    {
        advisory = string.Empty;
        var name = FileNameOf(uri);
        var prefix = id + kind;
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return false;
        var adv = name[prefix.Length..^4];
        if (adv.Length is < 3 or > 4 || !adv[..3].All(char.IsAsciiDigit) || (adv.Length == 4 && !char.IsAsciiLetter(adv[3])))
            return false;
        advisory = adv.ToUpperInvariant();
        return true;
    }

    private static JsonElement? Product(JsonElement storm, string name) =>
        storm.TryGetProperty(name, out var product) && product.ValueKind == JsonValueKind.Object ? product : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;

    // NHC writes intensity and pressure as strings ("115"); a number is read too.
    private static int? Integer(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        return value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
    }

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            && double.IsFinite(number)
                ? number
                : null;

    private static DateTimeOffset? Time(JsonElement element, string name) =>
        Text(element, name) is { } text
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)
                ? time
                : null;

    // A link on the list's own host: the Library never follows a file link elsewhere.
    private static Uri? Link(JsonElement product, string name, Uri baseUri) =>
        Text(product, name) is { Length: > 0 } text
            && Uri.TryCreate(baseUri, text, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)
                ? uri
                : null;
}
