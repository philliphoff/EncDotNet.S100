namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Fluent registration helpers for <see cref="ProductRegistry"/>.
/// </summary>
public static class ProductRegistryExtensions
{
    /// <summary>
    /// Registers a single product and returns the registry for chaining.
    /// </summary>
    public static ProductRegistry AddProduct(
        this ProductRegistry registry,
        ProductRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(registration);
        return registry;
    }

    /// <summary>
    /// Registers every built-in S-100 product (the batteries-included set) and
    /// returns the registry for chaining.
    /// </summary>
    public static ProductRegistry AddAllS100Products(this ProductRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        S100Products.RegisterAll(registry);
        return registry;
    }
}
