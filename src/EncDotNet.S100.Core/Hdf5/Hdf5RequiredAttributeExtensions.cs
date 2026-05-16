namespace EncDotNet.S100.Hdf5;

/// <summary>
/// Extension methods that translate PureHDF's
/// <c>Could not find attribute '{name}'</c> failure (and equivalent
/// "missing" exceptions from other backends) into typed
/// <see cref="S100DatasetSchemaException"/>s carrying spec context.
/// Reader implementations call these for attributes that are required
/// by the spec; optional attributes should still be guarded with
/// <see cref="IHdf5Group.AttributeExists(string)"/>.
/// </summary>
public static class Hdf5RequiredAttributeExtensions
{
    /// <summary>
    /// Reads a required floating-point attribute from
    /// <paramref name="group"/>. Throws
    /// <see cref="S100DatasetSchemaException"/> if the attribute is
    /// missing, preserving the underlying exception as
    /// <see cref="Exception.InnerException"/>.
    /// </summary>
    public static double ReadRequiredDoubleAttribute(
        this IHdf5Group group,
        string name,
        string product,
        string? file,
        string groupPath,
        string? specReference)
    {
        ArgumentNullException.ThrowIfNull(group);
        try
        {
            return group.ReadDoubleAttribute(name);
        }
        catch (Exception ex) when (LooksLikeMissingAttribute(ex, name))
        {
            throw MakeSchemaException(product, file, groupPath, name, specReference, ex);
        }
    }

    /// <summary>
    /// Reads a required fixed-point attribute from
    /// <paramref name="group"/>. Throws
    /// <see cref="S100DatasetSchemaException"/> if the attribute is
    /// missing.
    /// </summary>
    public static long ReadRequiredInt64Attribute(
        this IHdf5Group group,
        string name,
        string product,
        string? file,
        string groupPath,
        string? specReference)
    {
        ArgumentNullException.ThrowIfNull(group);
        try
        {
            return group.ReadInt64Attribute(name);
        }
        catch (Exception ex) when (LooksLikeMissingAttribute(ex, name))
        {
            throw MakeSchemaException(product, file, groupPath, name, specReference, ex);
        }
    }

    /// <summary>
    /// Reads a required string attribute from <paramref name="group"/>.
    /// Throws <see cref="S100DatasetSchemaException"/> if the attribute
    /// is missing.
    /// </summary>
    public static string ReadRequiredStringAttribute(
        this IHdf5Group group,
        string name,
        string product,
        string? file,
        string groupPath,
        string? specReference)
    {
        ArgumentNullException.ThrowIfNull(group);
        try
        {
            return group.ReadStringAttribute(name);
        }
        catch (Exception ex) when (LooksLikeMissingAttribute(ex, name))
        {
            throw MakeSchemaException(product, file, groupPath, name, specReference, ex);
        }
    }

    /// <summary>
    /// Pattern-matches the textual signature of a "missing HDF5
    /// attribute" failure. We don't depend on a specific PureHDF
    /// exception subclass (the backend is plug-replaceable), so we
    /// match the message defensively. The signature observed in
    /// PureHDF is <c>Could not find attribute '{name}'</c>; we require
    /// both the <c>Could not find</c> prefix and the attribute name
    /// itself to appear in the message so unrelated failures do not
    /// get re-skinned as schema exceptions.
    /// </summary>
    internal static bool LooksLikeMissingAttribute(Exception ex, string name)
    {
        var message = ex.Message;
        if (string.IsNullOrEmpty(message))
            return false;

        bool prefixMatches =
            message.Contains("Could not find", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("not found", StringComparison.OrdinalIgnoreCase);

        return prefixMatches && message.Contains(name, StringComparison.Ordinal);
    }

    private static S100DatasetSchemaException MakeSchemaException(
        string product,
        string? file,
        string groupPath,
        string name,
        string? specReference,
        Exception inner)
    {
        var message = ExceptionMessageFormatter.FormatSchema(product, file, groupPath, name, specReference);
        return new S100DatasetSchemaException(product, file, groupPath, name, specReference, message, inner);
    }
}
