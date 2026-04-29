using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Compile-time and reflection-based assertions about the capability-interface
/// shape of <see cref="IVectorPortrayalCatalogue"/>. These tests exist to
/// prevent regressions in the segmentation introduced by Phase 4 of the
/// refactor (S-100 Part 9 §6.4 / §11): the kitchen-sink catalogue interface
/// was split into <see cref="IXsltRuleSource"/>, <see cref="ILuaRuleSource"/>,
/// and <see cref="IPortrayalAssetSource"/> so future <c>ILuaRuleExecutor</c> /
/// coverage / synthetic-catalogue consumers can depend on the smallest viable
/// capability set.
/// </summary>
public class VectorCatalogueCapabilityTests
{
    [Fact]
    public void VectorPortrayalCatalogue_ExtendsAllThreeCapabilityInterfaces()
    {
        Assert.True(typeof(IXsltRuleSource).IsAssignableFrom(typeof(IVectorPortrayalCatalogue)));
        Assert.True(typeof(ILuaRuleSource).IsAssignableFrom(typeof(IVectorPortrayalCatalogue)));
        Assert.True(typeof(IPortrayalAssetSource).IsAssignableFrom(typeof(IVectorPortrayalCatalogue)));
    }

    [Fact]
    public void CapabilityInterfaces_AreIndependent()
    {
        // None of the capability interfaces should depend on one another.
        // This guards against accidental re-coupling in future edits.
        Assert.False(typeof(IXsltRuleSource).IsAssignableFrom(typeof(ILuaRuleSource)));
        Assert.False(typeof(ILuaRuleSource).IsAssignableFrom(typeof(IXsltRuleSource)));
        Assert.False(typeof(IPortrayalAssetSource).IsAssignableFrom(typeof(IXsltRuleSource)));
        Assert.False(typeof(IPortrayalAssetSource).IsAssignableFrom(typeof(ILuaRuleSource)));
    }

    [Fact]
    public void VectorPortrayalCatalogue_SurfaceIsTrimToRulesAndViewingGroups()
    {
        // The kitchen-sink interface should expose only the two members that
        // genuinely belong to the rule-driven pipeline. Asset and per-rule
        // lookup members live on the capability interfaces.
        var ownMembers = typeof(IVectorPortrayalCatalogue)
            .GetMembers(System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToHashSet();

        // Property accessors come through as get_Rules / get_ViewingGroups.
        Assert.Contains("get_Rules", ownMembers);
        Assert.Contains("get_ViewingGroups", ownMembers);
        Assert.DoesNotContain("GetSymbol", ownMembers);
        Assert.DoesNotContain("GetLineStyle", ownMembers);
        Assert.DoesNotContain("GetAreaFill", ownMembers);
        Assert.DoesNotContain("GetCompiledRule", ownMembers);
        Assert.DoesNotContain("GetLuaScript", ownMembers);
    }
}
