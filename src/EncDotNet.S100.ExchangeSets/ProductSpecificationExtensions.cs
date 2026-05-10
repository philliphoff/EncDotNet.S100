using EncDotNet.S100.Core;

namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Extensions that bridge the loosely-typed exchange-set
/// <see cref="ProductSpecification"/> DTO to the strongly-typed
/// <see cref="SpecRef"/> identity used elsewhere in the pipeline.
/// </summary>
public static class ProductSpecificationExtensions
{
    /// <summary>
    /// Attempts to derive a <see cref="SpecRef"/> from this product
    /// specification entry. Tries, in order:
    /// <list type="number">
    ///   <item><description><see cref="ProductSpecification.Name"/> + <see cref="ProductSpecification.Version"/>.</description></item>
    ///   <item><description><see cref="ProductSpecification.ProductIdentifier"/> in the long-form
    ///   <c>"INT.IHO.S-NNN.x.y.z"</c> shape (which carries both name and version).</description></item>
    /// </list>
    /// Returns <c>false</c> when neither shape yields a parseable
    /// <see cref="SpecRef"/>.
    /// </summary>
    public static bool TryToSpecRef(this ProductSpecification productSpecification, out SpecRef specRef)
    {
        ArgumentNullException.ThrowIfNull(productSpecification);

        if (!string.IsNullOrWhiteSpace(productSpecification.Name)
            && SpecName.TryNormalize(productSpecification.Name, out var name)
            && SpecVersion.TryParse(productSpecification.Version, out var edition))
        {
            specRef = new SpecRef(name, edition);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(productSpecification.ProductIdentifier)
            && SpecRef.TryParse(productSpecification.ProductIdentifier, out var fromIdentifier))
        {
            specRef = fromIdentifier;
            return true;
        }

        specRef = default;
        return false;
    }

    /// <summary>
    /// Strict variant of <see cref="TryToSpecRef"/>; throws
    /// <see cref="FormatException"/> when neither <see cref="ProductSpecification.Name"/>
    /// + <see cref="ProductSpecification.Version"/> nor
    /// <see cref="ProductSpecification.ProductIdentifier"/> can be resolved.
    /// </summary>
    public static SpecRef ToSpecRef(this ProductSpecification productSpecification)
    {
        if (!productSpecification.TryToSpecRef(out var specRef))
        {
            throw new FormatException(
                "ProductSpecification does not carry enough information to derive a SpecRef "
                + "(neither Name+Version nor ProductIdentifier resolved).");
        }

        return specRef;
    }
}
