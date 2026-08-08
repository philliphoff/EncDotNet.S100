namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Fluent registration helpers for <see cref="S100ProductRegistry"/>.
/// </summary>
public static class S100ProductRegistryExtensions
{
    /// <summary>
    /// Registers a single product and returns the registry for chaining.
    /// </summary>
    public static S100ProductRegistry AddS100Product(
        this S100ProductRegistry registry,
        S100ProductRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(registration);
        return registry;
    }

    /// <summary>
    /// Registers every built-in S-100 product (the batteries-included set) and
    /// returns the registry for chaining.
    /// </summary>
    public static S100ProductRegistry AddAllS100Products(this S100ProductRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        S100Products.RegisterAll(registry);
        return registry;
    }
}
