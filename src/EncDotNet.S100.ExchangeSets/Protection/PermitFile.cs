using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// A parsed PERMIT.XML file: the licensing document a Data Server issues to a
/// Data Client conveying the (encrypted) cell keys for the products it has
/// licensed.
/// </summary>
/// <remarks>
/// <para>
/// S-100 Edition 5.2.1 Part 15 §15-7.4. A permit file is a sequence of one or
/// more header/products pairs (each pair, modelled here as a
/// <see cref="PermitGroup"/>, may address a different end-user system). Each
/// <c>products</c> section groups <c>datasetPermit</c> records by product
/// specification id (for example <c>S-101</c>).
/// </para>
/// <para>
/// Parsing is namespace-tolerant: it reads by local element name so both the
/// <c>http://www.iho.int/s100/se/5.0</c> and <c>5.1</c> schema namespaces are
/// accepted. The accompanying <c>PERMIT.SIGN</c> signature file (§15-7.4.5) is
/// not processed by this reader.
/// </para>
/// </remarks>
public sealed class PermitFile
{
    private PermitFile(IReadOnlyList<PermitGroup> groups, bool isAuthenticated)
    {
        Groups = groups;
        IsAuthenticated = isAuthenticated;
    }

    /// <summary>The header/products groups contained in the permit file.</summary>
    public IReadOnlyList<PermitGroup> Groups { get; }

    /// <summary>
    /// Whether the permit was authenticated using its mandatory
    /// <c>PERMIT.SIGN</c> file.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-7.4.5.</remarks>
    public bool IsAuthenticated { get; }

    /// <summary>
    /// Reads and parses a permit file from a stream.
    /// </summary>
    /// <param name="stream">A stream positioned at the start of the PERMIT.XML content.</param>
    /// <returns>The parsed permit file.</returns>
    /// <remarks>
    /// This parses permit metadata for inspection only. The returned permit is
    /// not authenticated and cannot be used with <see cref="PermitKeyProvider"/>.
    /// Use <see cref="PermitSignatureVerifier.AuthenticateAsync"/> before
    /// decrypting permit keys.
    /// </remarks>
    public static PermitFile Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var doc = XDocument.Load(stream);
        return Parse(doc, isAuthenticated: false);
    }

    /// <summary>
    /// Reads and parses a permit file from a path on disk.
    /// </summary>
    /// <param name="path">The path to the PERMIT.XML file.</param>
    /// <returns>The parsed permit file.</returns>
    /// <remarks>
    /// This parses permit metadata for inspection only. The returned permit is
    /// not authenticated and cannot be used with <see cref="PermitKeyProvider"/>.
    /// Use <see cref="PermitSignatureVerifier.AuthenticateAsync"/> before
    /// decrypting permit keys.
    /// </remarks>
    public static PermitFile Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var doc = XDocument.Load(path);
        return Parse(doc, isAuthenticated: false);
    }

    internal static PermitFile ReadAuthenticated(Stream stream)
    {
        var doc = XDocument.Load(stream);
        return Parse(doc, isAuthenticated: true);
    }

    private static PermitFile Parse(XDocument doc, bool isAuthenticated)
    {
        XElement root = doc.Root ?? throw new XmlException("Missing root element.");
        if (!string.Equals(root.Name.LocalName, "Permit", StringComparison.Ordinal))
        {
            throw new XmlException($"Unexpected permit root element '{root.Name.LocalName}'.");
        }

        var groups = new List<PermitGroup>();
        PermitHeader? pendingHeader = null;

        foreach (XElement child in root.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "header":
                    pendingHeader = ParseHeader(child);
                    break;

                case "products":
                    var products = ParseProducts(child);
                    groups.Add(new PermitGroup(pendingHeader ?? new PermitHeader(), products));
                    pendingHeader = null;
                    break;
            }
        }

        return new PermitFile(groups, isAuthenticated);
    }

    private static PermitHeader ParseHeader(XElement header)
    {
        return new PermitHeader
        {
            IssueDate = ParseDate(ChildValue(header, "issueDate")),
            DataServerName = ChildValue(header, "dataServerName"),
            DataServerIdentifier = ChildValue(header, "dataServerIdentifier"),
            Version = ChildValue(header, "version"),
            UserPermit = ChildValue(header, "userpermit"),
        };
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<DataPermit>> ParseProducts(XElement products)
    {
        var byProduct = new Dictionary<string, IReadOnlyList<DataPermit>>(StringComparer.OrdinalIgnoreCase);

        foreach (XElement product in products.Elements().Where(e => e.Name.LocalName == "product"))
        {
            string id = (string?)product.Attribute("id") ?? string.Empty;
            var permits = new List<DataPermit>();

            foreach (XElement permit in product.Elements()
                         .Where(e => e.Name.LocalName is "datasetPermit" or "permit"))
            {
                permits.Add(ParsePermit(permit));
            }

            if (byProduct.TryGetValue(id, out IReadOnlyList<DataPermit>? existing))
            {
                ((List<DataPermit>)existing).AddRange(permits);
            }
            else
            {
                byProduct[id] = permits;
            }
        }

        return byProduct;
    }

    private static DataPermit ParsePermit(XElement permit)
    {
        string? fileName = ChildValue(permit, "filename");
        if (string.IsNullOrEmpty(fileName))
        {
            throw new XmlException("A datasetPermit is missing its filename element.");
        }

        string? encryptedKeyHex = ChildValue(permit, "encryptedKey");
        if (string.IsNullOrEmpty(encryptedKeyHex))
        {
            throw new XmlException($"datasetPermit for '{fileName}' is missing its encryptedKey element.");
        }

        byte[] encryptedKey;
        try
        {
            encryptedKey = Convert.FromHexString(encryptedKeyHex.Trim());
        }
        catch (FormatException ex)
        {
            throw new XmlException(
                $"datasetPermit for '{fileName}' has a non-hexadecimal encryptedKey.", ex);
        }

        var editionText = ChildValue(permit, "editionNumber");
        int? editionNumber = null;
        if (editionText is not null)
        {
            if (!int.TryParse(
                    editionText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var edition) ||
                edition <= 0)
            {
                throw new XmlException(
                    $"datasetPermit for '{fileName}' has an invalid editionNumber.");
            }

            editionNumber = edition;
        }

        var issueDate = ParseOptionalDate(
            ChildValue(permit, "issueDate"),
            fileName,
            "issueDate");
        var expiryText = ChildValue(permit, "expiry");
        if (expiryText is null)
        {
            throw new XmlException($"datasetPermit for '{fileName}' is missing its expiry element.");
        }

        var expiry = ParseOptionalDate(expiryText, fileName, "expiry")
            ?? throw new XmlException($"datasetPermit for '{fileName}' has an invalid expiry.");

        return new DataPermit(
            fileName.Trim(),
            encryptedKey,
            expiry,
            editionNumber,
            issueDate);
    }

    private static string? ChildValue(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value?.Trim();

    internal static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var dateText = ExtractXmlSchemaDate(trimmed);
        return dateText is not null &&
               DateOnly.TryParseExact(
                   dateText,
                   "yyyy-MM-dd",
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out var date)
            ? date
            : null;
    }

    private static string? ExtractXmlSchemaDate(string value)
    {
        if (value.Length == 10)
        {
            return value;
        }

        if (value.Length == 11 && value[10] == 'Z')
        {
            return value[..10];
        }

        if (value.Length != 16 || value[10] is not ('+' or '-'))
        {
            return null;
        }

        if (!int.TryParse(value.AsSpan(11, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(value.AsSpan(14, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            hours > 14 ||
            minutes > 59 ||
            hours == 14 && minutes != 0)
        {
            return null;
        }

        return value[..10];
    }

    private static DateOnly? ParseOptionalDate(
        string? value,
        string fileName,
        string elementName)
    {
        if (value is null)
        {
            return null;
        }

        return ParseDate(value) ??
            throw new XmlException(
                $"datasetPermit for '{fileName}' has an invalid {elementName}.");
    }

    /// <summary>
    /// Finds the permit that applies to the given dataset file name, optionally
    /// restricting the search to a single product specification id.
    /// </summary>
    /// <param name="datasetFileName">The dataset file name (with or without extension).</param>
    /// <param name="permit">The matching permit, if found.</param>
    /// <param name="productId">An optional product id (e.g. <c>S-101</c>) to restrict the search.</param>
    /// <returns><c>true</c> if a matching permit was found.</returns>
    public bool TryGetPermit(string datasetFileName, out DataPermit? permit, string? productId = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(datasetFileName);

        foreach (PermitGroup group in Groups)
        {
            foreach ((string id, IReadOnlyList<DataPermit> permits) in group.Products)
            {
                if (productId is not null && !string.Equals(id, productId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (DataPermit candidate in permits)
                {
                    if (candidate.AppliesTo(datasetFileName))
                    {
                        permit = candidate;
                        return true;
                    }
                }
            }
        }

        permit = null;
        return false;
    }
}

/// <summary>
/// One header/products pair within a <see cref="PermitFile"/> (§15-7.4.3).
/// </summary>
public sealed class PermitGroup
{
    internal PermitGroup(PermitHeader header, IReadOnlyDictionary<string, IReadOnlyList<DataPermit>> products)
    {
        Header = header;
        Products = products;
    }

    /// <summary>The header that applies to this group's permits.</summary>
    public PermitHeader Header { get; }

    /// <summary>The permits in this group, keyed by product specification id.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<DataPermit>> Products { get; }
}
