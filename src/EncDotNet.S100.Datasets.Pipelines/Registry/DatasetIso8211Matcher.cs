using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Decides, from the envelope of an ISO 8211-encoded dataset, whether the file
/// belongs to a particular product. Contributed per
/// <see cref="S100ProductRegistration"/> so an ISO 8211 product is
/// <em>recognized</em> in the same place it is <em>constructed</em>, rather than
/// in a central <c>switch</c> inside <see cref="DatasetPipelineFactory"/>. The
/// factory reads the envelope once (see <see cref="Iso8211RootInfo"/>) and
/// returns the spec of the first registered product whose matcher claims it.
/// Returns <see langword="true"/> when the file belongs to the registering
/// product.
/// <para>
/// This is the ISO 8211 sibling of <see cref="DatasetGmlMatcher"/>: the
/// <c>.000</c> extension is shared by legacy S-57, S-101, and S-401 (inland
/// ENC), so the extension alone cannot route the file.
/// </para>
/// <para>
/// Matchers must be <b>mutually exclusive</b>: S-57 claims the files carrying
/// the S-57-only <c>DSPM</c> field, while the S-100 products claim their own
/// declared <c>PRSP</c> product identifier. At most one matcher claims any
/// valid dataset, so the result does not depend on the order registrations are
/// enumerated (the registry is unordered).
/// </para>
/// </summary>
/// <param name="root">A cheap, parsed view of the dataset's ISO 8211 envelope.</param>
public delegate bool DatasetIso8211Matcher(Iso8211RootInfo root);

/// <summary>
/// A cheap, parse-once view of an ISO 8211 dataset's envelope, handed to each
/// product's <see cref="DatasetIso8211Matcher"/> so it can recognize its own
/// datasets without re-reading the file. S-100 ISO 8211 products (S-101, S-401)
/// declare themselves in the <c>DSID</c> field's <c>PRSP</c> subfield (e.g.
/// <c>INT.IHO.S-401.1.2</c>); legacy S-57 has no <c>PRSP</c> and is instead
/// identified by the presence of its <c>DSPM</c> field (S-57 Ed 3.1 Appendix
/// B.1; S-100 Edition 5.2.1 Part 10a).
/// </summary>
public readonly record struct Iso8211RootInfo
{
    /// <summary>
    /// The declared product specification (<c>DSID</c>/<c>PRSP</c>), e.g.
    /// <c>INT.IHO.S-101.1.0.2</c>. Empty when the dataset declares none (legacy
    /// S-57, or a non-conformant S-100 cell).
    /// </summary>
    public required string ProductSpecification { get; init; }

    /// <summary>
    /// The declared encoding specification (<c>DSID</c>/<c>ENSP</c>), e.g.
    /// <c>S-100 Part 10a</c>. Empty when the dataset declares none. Carried for
    /// matchers that need to distinguish encodings; the built-in products
    /// discriminate on <see cref="ProductSpecification"/> alone.
    /// </summary>
    public required string EncodingSpecification { get; init; }

    /// <summary>
    /// Whether the Data Descriptive Record defines the S-57 <c>DSPM</c> (Data
    /// Set Parameter) field, which S-100 ISO 8211 products never carry.
    /// </summary>
    public required bool HasDataSetParameterField { get; init; }

    /// <summary>
    /// Whether <see cref="ProductSpecification"/> declares
    /// <paramref name="productId"/>, comparing canonical <c>"S-NNN"</c> forms so
    /// the long-form identifier (<c>INT.IHO.S-401.1.2</c>) matches the product's
    /// canonical spec string (<c>S-401</c>).
    /// </summary>
    /// <param name="productId">The canonical product id to look for (e.g. <c>"S-401"</c>).</param>
    public bool DeclaresProduct(string productId)
    {
        ArgumentException.ThrowIfNullOrEmpty(productId);
        // ProductSpecification can be null on a default(Iso8211RootInfo); treat
        // that as "no match" rather than throwing — the struct is public and can
        // be default-constructed.
        if (string.IsNullOrEmpty(ProductSpecification)) return false;

        return SpecName.TryNormalize(ProductSpecification, out var declared)
            && SpecName.TryNormalize(productId, out var wanted)
            && string.Equals(declared, wanted, StringComparison.OrdinalIgnoreCase);
    }
}
