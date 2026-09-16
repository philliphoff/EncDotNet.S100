using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// <see cref="DatasetEntry.PortrayalSpec"/> starts from the conventional
/// mapping of the entry's product spec and follows the loaded processor, which
/// for an S-57 cell depends on whether it is a maritime or an inland ENC
/// (issue #608).
/// </summary>
public class DatasetEntryPortrayalSpecTests
{
    [Theory]
    [InlineData("S-57", "S-101")]
    [InlineData("S-101", "S-101")]
    [InlineData("S-401", "S-401")]
    [InlineData("S-124", "S-124")]
    public void PortrayalSpec_BeforeLoad_IsConventionalMapping(string productSpec, string expected)
    {
        var entry = new DatasetEntry("/tmp/cell", productSpec);

        Assert.Equal(expected, entry.PortrayalSpec);
    }

    [Fact]
    public void SetPortrayalSpec_Changed_UpdatesAndNotifiesOnce()
    {
        var entry = new DatasetEntry("/tmp/U37IL005.000", "S-57");
        var observed = new List<string?>();
        entry.PropertyChanged += (_, e) => observed.Add(e.PropertyName);

        entry.SetPortrayalSpec("S-401");
        entry.SetPortrayalSpec("S-401");

        Assert.Equal("S-401", entry.PortrayalSpec);
        Assert.Equal("S-57", entry.ProductSpec);
        Assert.Equal([nameof(DatasetEntry.PortrayalSpec)], observed);
    }

    [Fact]
    public void SetPortrayalSpec_SameAsConventional_DoesNotNotify()
    {
        var entry = new DatasetEntry("/tmp/US5MA1BO.000", "S-57");
        var observed = new List<string?>();
        entry.PropertyChanged += (_, e) => observed.Add(e.PropertyName);

        entry.SetPortrayalSpec("S-101");

        Assert.Equal("S-101", entry.PortrayalSpec);
        Assert.Empty(observed);
    }

    [Fact]
    public void SetPortrayalSpec_Blank_Throws()
    {
        var entry = new DatasetEntry("/tmp/cell", "S-57");

        Assert.ThrowsAny<ArgumentException>(() => entry.SetPortrayalSpec(" "));
    }
}
