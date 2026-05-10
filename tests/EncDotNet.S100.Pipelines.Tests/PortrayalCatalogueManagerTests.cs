using System.IO;
using EncDotNet.S100.Core;
using EncDotNet.S100.Portrayals;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

public class PortrayalCatalogueManagerTests
{
    [Fact]
    public void StringAndSpecRefOverloads_ShareCacheSlot_ForDefaultEdition()
    {
        var mgr = new PortrayalCatalogueManager();
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            mgr.SetPath("S-101", tmp);

            // String getter
            Assert.Equal(tmp, mgr.GetPath("S-101"));
            // SpecRef getter using same default version
            Assert.Equal(tmp, mgr.GetPath(new SpecRef("S-101", default)));
            // Different edition → not registered.
            Assert.Null(mgr.GetPath(new SpecRef("S-101", new SpecVersion(2, 0, 0))));
        }
        finally
        {
            Directory.Delete(tmp);
        }
    }

    [Fact]
    public void DistinctEditions_AreRegisteredIndependently()
    {
        var mgr = new PortrayalCatalogueManager();
        var a = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var b = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        try
        {
            mgr.SetPath(new SpecRef("S-101", new SpecVersion(1, 2, 0)), a);
            mgr.SetPath(new SpecRef("S-101", new SpecVersion(2, 0, 0)), b);

            Assert.Equal(a, mgr.GetPath(new SpecRef("S-101", new SpecVersion(1, 2, 0))));
            Assert.Equal(b, mgr.GetPath(new SpecRef("S-101", new SpecVersion(2, 0, 0))));
            Assert.True(mgr.HasCatalogue(new SpecRef("S-101", new SpecVersion(1, 2, 0))));
            Assert.True(mgr.HasCatalogue(new SpecRef("S-101", new SpecVersion(2, 0, 0))));
            Assert.Equal(2, mgr.RegisteredCataloguesByRef.Count);
        }
        finally
        {
            Directory.Delete(a);
            Directory.Delete(b);
        }
    }

    [Fact]
    public void HasCatalogue_String_NormalisesName()
    {
        var mgr = new PortrayalCatalogueManager();
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            mgr.SetPath("S-101", tmp);
            Assert.True(mgr.HasCatalogue("S101"));
            Assert.True(mgr.HasCatalogue("s-101"));
            Assert.True(mgr.HasCatalogue("S-101"));
        }
        finally
        {
            Directory.Delete(tmp);
        }
    }
}
