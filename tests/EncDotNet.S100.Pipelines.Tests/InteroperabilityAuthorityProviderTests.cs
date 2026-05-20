using System;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

public class InteroperabilityAuthorityProviderTests
{
    [Fact]
    public void Default_holds_S98_authority()
    {
        var provider = new InteroperabilityAuthorityProvider();
        Assert.Same(InteroperabilityAuthority.Default, provider.Current);
    }

    [Fact]
    public void Ctor_accepts_initial_authority()
    {
        var lo = LoadOrderInteroperabilityAuthority.Default;
        var provider = new InteroperabilityAuthorityProvider(lo);
        Assert.Same(lo, provider.Current);
    }

    [Fact]
    public void Set_updates_Current_and_raises_event()
    {
        var provider = new InteroperabilityAuthorityProvider();
        var raised = 0;
        provider.CurrentChanged += () => raised++;

        provider.Set(LoadOrderInteroperabilityAuthority.Default);

        Assert.Same(LoadOrderInteroperabilityAuthority.Default, provider.Current);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Set_raises_event_even_when_authority_reference_is_unchanged()
    {
        // Hosts can force a re-sort by re-Setting the same instance
        // (e.g. after a catalogue reload tweaked the authority's
        // internal table). The contract is documented on the impl.
        var provider = new InteroperabilityAuthorityProvider();
        var raised = 0;
        provider.CurrentChanged += () => raised++;

        provider.Set(provider.Current);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Set_null_throws()
    {
        var provider = new InteroperabilityAuthorityProvider();
        Assert.Throws<ArgumentNullException>(() => provider.Set(null!));
    }
}
