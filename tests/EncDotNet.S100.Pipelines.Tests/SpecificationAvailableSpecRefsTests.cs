using EncDotNet.S100.Core;
using EncDotNet.S100.Specifications;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

public class SpecificationAvailableSpecRefsTests
{
    [Fact]
    public void AvailableSpecRefs_AreNonEmpty_AndCanonicallyNamed()
    {
        var refs = Specification.AvailableSpecRefs;
        Assert.NotEmpty(refs);
        Assert.All(refs, r => Assert.StartsWith("S-", r.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void AvailableSpecRefs_ContainOneEntryPerAvailableSpec()
    {
        Assert.Equal(Specification.AvailableSpecs.Count, Specification.AvailableSpecRefs.Count);
        foreach (var name in Specification.AvailableSpecs)
        {
            Assert.Contains(Specification.AvailableSpecRefs, r => r.Name == name);
        }
    }

    [Fact]
    public void AvailableSpecRefs_UseDefaultVersion()
    {
        // Without a per-bundle manifest declaring the spec edition, refs use
        // SpecVersion default (0.0.0) as a sentinel for "edition unspecified".
        Assert.All(Specification.AvailableSpecRefs, r => Assert.Equal(default(SpecVersion), r.Edition));
    }
}
