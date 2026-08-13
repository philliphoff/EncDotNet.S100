namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Decides, from the root element of a GML-encoded dataset, whether the
/// document belongs to a particular product. Contributed per
/// <see cref="S100ProductRegistration"/> so a GML product is <em>recognized</em>
/// in the same place it is <em>constructed</em>, rather than in a central
/// <c>switch</c> inside <see cref="DatasetPipelineFactory"/>. The factory reads
/// the document's root once (see <see cref="GmlRootInfo"/>) and returns the spec
/// of the first registered product whose matcher claims it. Returns
/// <see langword="true"/> when the document belongs to the registering product.
/// </summary>
/// <param name="root">A cheap, parsed view of the document's root element.</param>
public delegate bool DatasetGmlMatcher(GmlRootInfo root);

/// <summary>
/// A cheap, parse-once view of a GML dataset's root element, handed to each
/// product's <see cref="DatasetGmlMatcher"/> so it can recognize its own
/// documents without re-parsing. Real-world S-100 GML datasets declare their
/// product through the root element's namespace, the root's local name, an
/// S-100 <c>productIdentifier</c> element, or — for generically-rooted
/// <c>&lt;DataSet&gt;</c> documents — a namespace declared on the root's
/// attributes.
/// </summary>
public readonly record struct GmlRootInfo
{
    /// <summary>The namespace URI of the root element.</summary>
    public required string NamespaceUri { get; init; }

    /// <summary>The local (unprefixed) name of the root element.</summary>
    public required string LocalName { get; init; }

    /// <summary>
    /// The values of the root element's attributes (typically the declared
    /// <c>xmlns:*</c> namespaces). Used to recognize a generic
    /// <c>&lt;DataSet&gt;</c> root that names its product only through a
    /// declared namespace rather than the root element's own namespace.
    /// </summary>
    public required IReadOnlyList<string> AttributeValues { get; init; }

    /// <summary>
    /// A bounded text prefix of the document, used by
    /// <see cref="ContainsProductIdentifier"/> to sniff an S-100
    /// <c>productIdentifier</c> element for products (e.g. S-411 1.2.1 samples,
    /// the mislabelled S-122 2.0.0 sample) that omit an application-schema
    /// namespace on the root.
    /// </summary>
    public required string Xml { get; init; }

    /// <summary>Whether the root element is a generic GML <c>&lt;DataSet&gt;</c>.</summary>
    public bool IsDataSetRoot => LocalName.Equals("DataSet", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Sniffs the document prefix for an S-100
    /// <c>&lt;productIdentifier&gt;{productId}&lt;/productIdentifier&gt;</c>
    /// element. Used for products (e.g. S-411 1.2.1 samples) that don't declare
    /// an application-schema namespace on the dataset root.
    /// </summary>
    /// <param name="productId">The canonical product id to look for (e.g. <c>"S-411"</c>).</param>
    public bool ContainsProductIdentifier(string productId)
    {
        var span = Xml.AsSpan(0, Math.Min(Xml.Length, 8192));
        var marker = "productIdentifier".AsSpan();
        var idx = span.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;
        var rest = span[(idx + marker.Length)..];
        return rest.IndexOf(productId.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
