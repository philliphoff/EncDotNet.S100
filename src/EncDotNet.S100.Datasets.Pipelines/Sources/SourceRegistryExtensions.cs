namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Fluent registration helpers for <see cref="SourceRegistry"/> (data source
/// design note D5, D9), the sibling of <see cref="ProductRegistryExtensions"/>.
/// </summary>
public static class SourceRegistryExtensions
{
    /// <summary>Registers a source, such as a plug-in's, and returns the registry for chaining.</summary>
    /// <param name="registry">The host's registry.</param>
    /// <param name="registration">The source.</param>
    /// <param name="replace">
    /// Whether to put it in place of a source with the same id, as a host does
    /// to wrap a built-in one; otherwise a duplicate id is refused.
    /// </param>
    public static SourceRegistry AddDataSource(this SourceRegistry registry, SourceRegistration registration, bool replace = false)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(registration, replace);
        return registry;
    }
}
