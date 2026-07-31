namespace EncDotNet.S100.Core;

/// <summary>
/// Resolves the declared product specification of an HDF5 dataset (the root
/// <c>productSpecification</c> attribute, S-100 Part 10c §10.2.1) into a
/// strongly-typed <see cref="SpecRef"/>.
/// </summary>
/// <remarks>
/// Real-world values take the long-form <c>"INT.IHO.S-NNN.x.y.z"</c> shape
/// (including pre-release drafts such as <c>"INT.IHO.S-104.0.8"</c>), which
/// <see cref="SpecRef.TryParse"/> understands. When the attribute is absent
/// or carries only a product code without a parseable edition, the
/// <paramref name="fallbackName"/> is used with a default (unknown) edition so
/// the dataset's product is still identified.
/// </remarks>
public static class HdfDeclaredSpec
{
    /// <summary>
    /// Resolves <paramref name="productSpecification"/> to a
    /// <see cref="SpecRef"/>, falling back to
    /// <paramref name="fallbackName"/> (with a default edition) when the
    /// string is missing or only partly parseable.
    /// </summary>
    /// <param name="productSpecification">
    /// The raw root <c>productSpecification</c> attribute value, or <c>null</c>.
    /// </param>
    /// <param name="fallbackName">
    /// The product the reader knows it is processing (e.g. <c>"S-104"</c>),
    /// used when the declared string cannot be fully parsed.
    /// </param>
    public static SpecRef Resolve(string? productSpecification, string fallbackName)
    {
        if (!string.IsNullOrWhiteSpace(productSpecification)
            && SpecRef.TryParse(productSpecification, out var declared))
        {
            return declared;
        }

        return new SpecRef(fallbackName, default);
    }
}
