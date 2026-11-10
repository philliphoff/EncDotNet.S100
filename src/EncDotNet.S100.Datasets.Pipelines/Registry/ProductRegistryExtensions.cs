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

    /// <summary>
    /// Registers NATO Additional Military Layers (<see cref="S100Products.Aml"/>)
    /// and returns the registry for chaining. AML isn't an S-100 product, so
    /// <see cref="AddAllS100Products"/> leaves it out.
    /// </summary>
    public static ProductRegistry AddAmlProduct(this ProductRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(S100Products.Aml);
        return registry;
    }

    /// <summary>
    /// Registers the built-in weather formats (<see cref="WeatherProducts"/>:
    /// NetCDF-CF) and returns the registry for chaining. They aren't S-100
    /// products, so <see cref="AddAllS100Products"/> leaves them out.
    /// </summary>
    public static ProductRegistry AddWeatherProducts(this ProductRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        WeatherProducts.RegisterAll(registry);
        return registry;
    }
}
