using System.Text.Json;
using System.Text.Json.Serialization;

namespace EncDotNet.S100.Collections.KnownSources;

/// <summary>The catalogue format of a <see cref="KnownCatalogueSource"/>.</summary>
public enum KnownCatalogueFormat
{
    /// <summary>A NOAA ENC product catalogue (<c>EncProductCatalog</c>); see <see cref="NoaaEncFeedSource"/>.</summary>
    NoaaEnc,

    /// <summary>A USACE Inland ENC product catalogue (<c>IENC…ProductCatalog</c>); see <see cref="UsaceIencFeedSource"/>.</summary>
    UsaceIenc,

    /// <summary>A community chart list (<c>RncProductCatalogChartCatalogs</c>); see <see cref="ChartCatalogsFeedSource"/>.</summary>
    ChartCatalogs,
}

/// <summary>What coverage a catalogue publishes for its cells.</summary>
public enum KnownCatalogueCoverage
{
    /// <summary>No coverage: cells cannot be drawn until downloaded.</summary>
    None,

    /// <summary>A bounding box per cell.</summary>
    BoundingBoxes,

    /// <summary>Coverage polygons per cell.</summary>
    Polygons,
}

/// <summary>
/// One online chart catalogue in the curated list of known sources (issue
/// #670): where it is, who publishes it, and what it provides.
/// </summary>
/// <param name="Id">A stable identifier (e.g. <c>noaa-enc</c>).</param>
/// <param name="Name">The display name.</param>
/// <param name="Provider">Who publishes the catalogue.</param>
/// <param name="Region">Where it applies, broadest first (e.g. <c>North America › United States</c>).</param>
/// <param name="Format">The catalogue format.</param>
/// <param name="CatalogUri">The catalogue's URL.</param>
/// <param name="Homepage">The provider's page for the data (terms, notices), if any.</param>
/// <param name="Coverage">What coverage the catalogue publishes.</param>
/// <param name="Editions">True when the catalogue lists editions and updates (so UPDATE can be detected).</param>
/// <param name="Sizes">True when the catalogue lists download sizes.</param>
/// <param name="Note">A short description shown with the entry, if any.</param>
public sealed record KnownCatalogueSource(
    string Id,
    string Name,
    string Provider,
    IReadOnlyList<string> Region,
    KnownCatalogueFormat Format,
    Uri CatalogUri,
    Uri? Homepage,
    KnownCatalogueCoverage Coverage,
    bool Editions,
    bool Sizes,
    string? Note = null);

/// <summary>
/// The curated list of known online chart catalogues (issue #670), maintained
/// in this repository (<c>KnownSources/known-sources.json</c>) and embedded in
/// the library. Entries point at each provider's own catalogue; nothing is
/// derived from other projects' source lists.
/// </summary>
public static class KnownCatalogueSources
{
    private const string ResourceName = "EncDotNet.S100.Collections.KnownSources.known-sources.json";

    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static readonly Lazy<IReadOnlyList<KnownCatalogueSource>> BuiltIn = new(() =>
    {
        using var stream = typeof(KnownCatalogueSources).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");
        return Read(stream);
    });

    /// <summary>The built-in list, in file order.</summary>
    public static IReadOnlyList<KnownCatalogueSource> All => BuiltIn.Value;

    /// <summary>Finds a built-in source by <see cref="KnownCatalogueSource.Id"/>.</summary>
    public static KnownCatalogueSource? Find(string id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads a known-sources document (the embedded list, or a newer copy).
    /// Entries whose format this build does not understand are skipped.
    /// </summary>
    /// <exception cref="JsonException">The document is malformed.</exception>
    public static IReadOnlyList<KnownCatalogueSource> Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var document = JsonSerializer.Deserialize<Document>(stream, Options)
            ?? throw new JsonException("Known-sources document is empty.");
        return (document.Sources ?? [])
            .Where(s => s.Format is { } && s.Id is { Length: > 0 } && s.Name is { Length: > 0 } && s.CatalogUri is { IsAbsoluteUri: true })
            .Select(s => new KnownCatalogueSource(
                s.Id!,
                s.Name!,
                s.Provider ?? string.Empty,
                s.Region ?? [],
                s.Format!.Value,
                s.CatalogUri!,
                s.Homepage,
                s.Coverage ?? KnownCatalogueCoverage.None,
                s.Editions,
                s.Sizes,
                s.Note))
            .ToArray();
    }

    /// <summary>
    /// Writes <paramref name="sources"/> as a known-sources document, for
    /// example the user's own catalogues; <see cref="Read"/> reads it back.
    /// </summary>
    public static void Write(Stream stream, IEnumerable<KnownCatalogueSource> sources)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(sources);

        var document = new Document(1, sources.Select(s => new Entry(
            s.Id, s.Name, s.Provider, s.Region, s.Format, s.CatalogUri, s.Homepage, s.Coverage, s.Editions, s.Sizes, s.Note))
            .ToArray());
        JsonSerializer.Serialize(stream, document, WriteOptions);
    }

    /// <summary>
    /// Describes a catalogue the user added by URL (issue #670), with what
    /// its format provides: its title (or the host) as the name, the host as
    /// the provider, and the region "Custom".
    /// </summary>
    public static KnownCatalogueSource FromUrl(Uri catalogUri, KnownCatalogueFormat format, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(catalogUri);

        var (coverage, editions, sizes) = format switch
        {
            KnownCatalogueFormat.NoaaEnc => (KnownCatalogueCoverage.Polygons, true, true),
            KnownCatalogueFormat.UsaceIenc => (KnownCatalogueCoverage.BoundingBoxes, true, true),
            _ => (KnownCatalogueCoverage.None, false, false),
        };
        return new KnownCatalogueSource(
            "user-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(catalogUri.AbsoluteUri)))[..16].ToLowerInvariant(),
            string.IsNullOrWhiteSpace(title) ? catalogUri.Host : title.Trim(),
            catalogUri.Host,
            [CustomRegion],
            format,
            catalogUri,
            null,
            coverage,
            editions,
            sizes,
            null);
    }

    /// <summary>The region user-added catalogues are listed under.</summary>
    public const string CustomRegion = "Custom";

    private static readonly JsonSerializerOptions WriteOptions = new(Options) { WriteIndented = true };

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new LenientEnumConverter<KnownCatalogueFormat>());
        options.Converters.Add(new LenientEnumConverter<KnownCatalogueCoverage>());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private sealed record Document(int Version, IReadOnlyList<Entry>? Sources);

    private sealed record Entry(
        string? Id,
        string? Name,
        string? Provider,
        IReadOnlyList<string>? Region,
        KnownCatalogueFormat? Format,
        Uri? CatalogUri,
        Uri? Homepage,
        KnownCatalogueCoverage? Coverage,
        bool Editions,
        bool Sizes,
        string? Note);

    /// <summary>Reads camelCase enum names; an unknown name reads as <see langword="null"/> so the entry can be skipped.</summary>
    private sealed class LenientEnumConverter<T> : JsonConverter<T?>
        where T : struct, Enum
    {
        public override bool HandleNull => true;

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var value)
                ? value
                : null;

        public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
        {
            if (value is { } v)
                writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(v.ToString()));
            else
                writer.WriteNullValue();
        }
    }
}
