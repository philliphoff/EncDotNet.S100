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

    /// <summary>An S-100 feed (<c>encdotnet-s100-feed</c> JSON); see <see cref="S100FeedSource"/>.</summary>
    S100Feed,

    /// <summary>
    /// A remote S-100 exchange catalogue (<c>CATALOG.XML</c>) with its datasets
    /// beside it, such as NOAA's S-102 on AWS (issue #685); see <see cref="S100CatalogueFeedSource"/>.
    /// </summary>
    S100ExchangeCatalogue,

    /// <summary>
    /// An S-100 forecast feed: one folder per forecast model, each with a
    /// catalogue of its latest run, such as NOAA's S-111 on AWS (issue #685);
    /// see <see cref="S100ForecastFeedSource"/>. The entry lists the models.
    /// </summary>
    S100ForecastModels,

    /// <summary>
    /// A SECOM (IEC 63173-2) service read anonymously (issue #804); the
    /// catalogue URL is the service's endpoint. See <see cref="SecomSource"/>.
    /// </summary>
    Secom,
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
/// <param name="Product">The one product specification the catalogue publishes (e.g. <c>S-102</c>), if it says.</param>
/// <param name="NotForNavigation">True when the provider marks all of the catalogue's data as not for navigation.</param>
/// <param name="Models">For a forecast feed, its models (curated: names, cadence and forecast horizon); otherwise empty.</param>
/// <param name="Pilot">True for a pilot service, which may cover little and lapse (shown with a "Pilot" chip).</param>
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
    string? Note = null,
    string? Product = null,
    bool NotForNavigation = false,
    IReadOnlyList<ForecastModel>? Models = null,
    bool Pilot = false)
{
    /// <summary>For a forecast feed, its models; otherwise empty.</summary>
    public IReadOnlyList<ForecastModel> Models { get; init; } = Models ?? [];
}

/// <summary>
/// The curated list of known online chart catalogues (issue #670), maintained
/// in this repository (<c>KnownSources/known-sources.json</c>) and embedded in
/// the library. Entries point at each provider's own catalogue; nothing is
/// derived from other projects' source lists.
/// </summary>
public static partial class KnownCatalogueSources
{
    private const string ResourceName = "EncDotNet.S100.Collections.KnownSources.known-sources.json";

    private static readonly DocumentJsonContext ReadJson = new(CreateOptions());

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

        var document = JsonSerializer.Deserialize(stream, ReadJson.Document)
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
                s.Note,
                string.IsNullOrWhiteSpace(s.Product) ? null : s.Product.Trim(),
                s.NotForNavigation,
                (s.Models ?? [])
                    .Where(m => m.Id is { Length: > 0 } && m.Name is { Length: > 0 } && m.CadenceHours > 0 && m.HorizonHours > 0)
                    .Select(m => new ForecastModel(m.Id!, m.Name!, m.CadenceHours, m.HorizonHours,
                        string.IsNullOrWhiteSpace(m.Catalogue) ? null : m.Catalogue.Trim()))
                    .ToArray(),
                s.Pilot))
            .Where(s => s.Format != KnownCatalogueFormat.S100ForecastModels || s.Models.Count > 0)
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
            s.Id, s.Name, s.Provider, s.Region, s.Format, s.CatalogUri, s.Homepage, s.Coverage, s.Editions, s.Sizes, s.Note,
            s.Product, s.NotForNavigation,
            s.Models.Count == 0 ? null : s.Models.Select(m => new ModelEntry(m.Id, m.Name, m.CadenceHours, m.HorizonHours, m.CataloguePath)).ToArray(),
            s.Pilot))
            .ToArray());
        JsonSerializer.Serialize(stream, document, WriteJson.Document);
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
            KnownCatalogueFormat.S100Feed => (KnownCatalogueCoverage.Polygons, true, true),
            KnownCatalogueFormat.S100ExchangeCatalogue => (KnownCatalogueCoverage.Polygons, true, false),
            KnownCatalogueFormat.Secom => (KnownCatalogueCoverage.None, false, true),
            _ => (KnownCatalogueCoverage.None, false, false),
        };
        if (format == KnownCatalogueFormat.Secom)
            catalogUri = Secom.SecomClient.NormalizeServiceUri(catalogUri);
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

    /// <summary>The region SECOM services from a service registry are listed under (#822).</summary>
    public const string SecomRegistryRegion = "SECOM service registry";

    /// <summary>The prefix of the ids <see cref="FromRegistry"/> gives.</summary>
    public const string SecomRegistryIdPrefix = "msr-";

    /// <summary>
    /// Describes a SECOM service listed in a service registry (#822) as a
    /// catalogue: its registered name and organisation, listed under
    /// <see cref="SecomRegistryRegion"/> by product, a provisional
    /// registration marked as a pilot.
    /// </summary>
    public static KnownCatalogueSource FromRegistry(Secom.SecomRegistryService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return new KnownCatalogueSource(
            SecomRegistryIdPrefix + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(service.InstanceId + "|" + service.EndpointUri.AbsoluteUri)))[..16].ToLowerInvariant(),
            service.Name,
            service.OrganizationName ?? service.EndpointUri.Host,
            [SecomRegistryRegion, service.ProductSpec],
            KnownCatalogueFormat.Secom,
            Secom.SecomClient.NormalizeServiceUri(service.EndpointUri),
            null,
            KnownCatalogueCoverage.None,
            Editions: false,
            Sizes: true,
            Note: service.Description,
            Product: service.IsS100Product ? service.ProductSpec : null,
            Pilot: service.Status != Secom.SecomRegistryStatus.Released);
    }

    private static readonly DocumentJsonContext WriteJson = new(new JsonSerializerOptions(CreateOptions()) { WriteIndented = true });

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new LenientEnumConverter<KnownCatalogueFormat>());
        options.Converters.Add(new LenientEnumConverter<KnownCatalogueCoverage>());
        return options;
    }

    private sealed record Document(int Version, IReadOnlyList<Entry>? Sources);

    [JsonSerializable(typeof(Document))]
    private sealed partial class DocumentJsonContext : JsonSerializerContext;

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
        string? Note,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Product = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool NotForNavigation = false,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ModelEntry>? Models = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Pilot = false);

    private sealed record ModelEntry(
        string? Id,
        string? Name,
        int CadenceHours,
        int HorizonHours,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Catalogue = null);

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
