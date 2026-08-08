using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit coverage for the product registry seam that replaced
/// <see cref="DatasetPipelineFactory"/>'s former hard-coded product
/// <c>switch</c> (issue #512 step 9): a host can now enable a subset of products
/// or add its own. These tests exercise the registry mechanics directly; the
/// delegates are trivial (<c>null!</c>) because construction of real processors
/// is covered by the factory/product tests.
/// </summary>
public class S100ProductRegistryTests
{
    private static S100ProductRegistration Fake(string spec) => new()
    {
        Spec = spec,
        CreateFromPath = (_, _) => null!,
        CreateFromSource = (_, _) => null!,
    };

    [Fact]
    public void Register_ThenResolve_ReturnsSameRegistration()
    {
        var registry = new S100ProductRegistry();
        var registration = Fake("S-101");

        registry.Register(registration);

        Assert.True(registry.IsRegistered("S-101"));
        Assert.Same(registration, registry.Resolve("S-101"));
        Assert.True(registry.TryResolve("S-101", out var resolved));
        Assert.Same(registration, resolved);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        var registry = new S100ProductRegistry();
        var registration = Fake("S-101");
        registry.Register(registration);

        Assert.Same(registration, registry.Resolve("s-101"));
        Assert.True(registry.IsRegistered("s-101"));
    }

    [Fact]
    public void Register_NormalizesNonCanonicalSpec_ResolvableByCanonical()
    {
        var registry = new S100ProductRegistry();
        var registration = Fake("S101");

        registry.Register(registration);

        Assert.Same(registration, registry.Resolve("S-101"));
        Assert.True(registry.IsRegistered("S-101"));
        Assert.Contains("S-101", registry.RegisteredSpecs);
    }

    [Fact]
    public void Register_ReplacesExistingBySpec()
    {
        var registry = new S100ProductRegistry();
        var first = Fake("S-101");
        var second = Fake("S-101");

        registry.Register(first);
        registry.Register(second);

        Assert.Same(second, registry.Resolve("S-101"));
        Assert.Single(registry.RegisteredSpecs);
    }

    [Fact]
    public void Resolve_ForUnregisteredSpec_Throws()
    {
        var registry = new S100ProductRegistry();

        Assert.False(registry.IsRegistered("S-999"));
        Assert.False(registry.TryResolve("S-999", out _));
        Assert.Throws<NotSupportedException>(() => registry.Resolve("S-999"));
    }

    [Fact]
    public void AddS100Product_ChainsAndRegisters()
    {
        var registry = new S100ProductRegistry()
            .AddS100Product(Fake("S-101"))
            .AddS100Product(Fake("S-102"));

        Assert.True(registry.IsRegistered("S-101"));
        Assert.True(registry.IsRegistered("S-102"));
    }

    [Fact]
    public void CreateDefaultRegistry_RegistersEveryBuiltInProduct()
    {
        string[] expected =
        [
            "S-57", "S-101", "S-102", "S-104", "S-111", "S-122", "S-124",
            "S-125", "S-127", "S-128", "S-129", "S-131", "S-201", "S-411", "S-421",
        ];

        var registry = S100Products.CreateDefaultRegistry();

        Assert.Equal(expected.Length, registry.RegisteredSpecs.Count);
        foreach (var spec in expected)
            Assert.True(registry.IsRegistered(spec), $"expected {spec} to be registered");
    }

    [Fact]
    public void AddAllS100Products_MatchesDefaultRegistry()
    {
        var registry = new S100ProductRegistry().AddAllS100Products();

        Assert.Equal(
            S100Products.CreateDefaultRegistry().RegisteredSpecs.OrderBy(s => s),
            registry.RegisteredSpecs.OrderBy(s => s));
    }
}
