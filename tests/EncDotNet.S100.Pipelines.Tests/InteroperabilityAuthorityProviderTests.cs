using System;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

public class InteroperabilityAuthorityProviderTests
{
    private static IInteroperabilityAuthority NewS98() => new InteroperabilityAuthority();
    private static LoadOrderInteroperabilityAuthority NewLoadOrder() =>
        new LoadOrderInteroperabilityAuthority(NewS98());

    [Fact]
    public void Ctor_stores_initial_authority()
    {
        var s98 = NewS98();
        var provider = new InteroperabilityAuthorityProvider(s98);
        Assert.Same(s98, provider.Current);
    }

    [Fact]
    public void Ctor_null_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new InteroperabilityAuthorityProvider(null!));
    }

    [Fact]
    public void Set_updates_Current_and_raises_event()
    {
        var provider = new InteroperabilityAuthorityProvider(NewS98());
        var lo = NewLoadOrder();
        var raised = 0;
        provider.CurrentChanged += () => raised++;

        provider.Set(lo);

        Assert.Same(lo, provider.Current);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Set_raises_event_even_when_authority_reference_is_unchanged()
    {
        var provider = new InteroperabilityAuthorityProvider(NewS98());
        var raised = 0;
        provider.CurrentChanged += () => raised++;

        provider.Set(provider.Current);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Set_null_throws()
    {
        var provider = new InteroperabilityAuthorityProvider(NewS98());
        Assert.Throws<ArgumentNullException>(() => provider.Set(null!));
    }
}
