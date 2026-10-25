using EncDotNet.S100.Hdf5;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Decides, from the root group of an HDF5 file, whether the file belongs to a
/// particular product. Contributed per <see cref="ProductRegistration"/> so an
/// HDF5 product is recognized in the same place it is constructed — the HDF5
/// sibling of <see cref="DatasetIso8211Matcher"/> and
/// <see cref="DatasetGmlMatcher"/>. The factory opens the file once (see
/// <see cref="Hdf5RootInfo"/>) and returns the spec of the first registered
/// product whose matcher claims it; an HDF5 file nobody claims is unknown.
/// <para>
/// Matchers must be <b>mutually exclusive</b>: the S-100 coverage products each
/// claim the files whose root <c>productSpecification</c> attribute declares
/// them (S-100 Part 10c), so the result does not depend on the order
/// registrations are enumerated.
/// </para>
/// </summary>
/// <param name="root">A view of the file's root group, valid only during the call.</param>
public delegate bool DatasetHdf5Matcher(Hdf5RootInfo root);

/// <summary>
/// A view of an HDF5 file's root group, handed to each product's
/// <see cref="DatasetHdf5Matcher"/>. The root <c>productSpecification</c>
/// attribute is read once up front; any other root attribute can be read with
/// <see cref="ReadStringAttribute"/>, which is only valid while the matcher
/// runs (the factory closes the file afterwards), so a matcher must not keep
/// the value.
/// </summary>
public readonly record struct Hdf5RootInfo
{
    /// <summary>
    /// The root <c>productSpecification</c> attribute (S-100 Part 10c), e.g.
    /// <c>INT.IHO.S-102.2.2</c>. Empty when the file declares none or it can't
    /// be read.
    /// </summary>
    public required string ProductSpecification { get; init; }

    /// <summary>The open root group, or <see langword="null"/> for a default value.</summary>
    internal IHdf5Group? Root { get; init; }

    /// <summary>
    /// Whether <see cref="ProductSpecification"/> names
    /// <paramref name="productId"/> (e.g. <c>"S-104"</c>). Matches by substring,
    /// case-insensitively, as the factory always has, so a producer that writes
    /// <c>S-104</c> or <c>INT.IHO.S-104.2.0</c> is recognized either way.
    /// </summary>
    /// <param name="productId">The canonical product id to look for.</param>
    public bool DeclaresProduct(string productId)
    {
        ArgumentException.ThrowIfNullOrEmpty(productId);
        return !string.IsNullOrEmpty(ProductSpecification)
            && ProductSpecification.Contains(productId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the root string attribute <paramref name="name"/>, or returns
    /// <see langword="null"/> when it is absent or unreadable. Valid only while
    /// the matcher runs.
    /// </summary>
    /// <param name="name">The attribute name, e.g. <c>"Conventions"</c>.</param>
    public string? ReadStringAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (Root is not { } root)
            return null;

        try
        {
            return root.AttributeExists(name) ? root.ReadStringAttribute(name) : null;
        }
        catch
        {
            // Unreadable attribute (wrong type, closed file): treat as absent.
            return null;
        }
    }
}
