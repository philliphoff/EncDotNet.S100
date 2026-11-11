namespace EncDotNet.S100.Core.Tests;

public class DisplayScaleRangeTests
{
    [Fact]
    public void FromDeclared_well_ordered_pair_is_unchanged()
    {
        Assert.Equal(new DisplayScaleRange(90000, 22000), DisplayScaleRange.FromDeclared(90000, 22000));
    }

    [Fact]
    public void FromDeclared_inverted_pair_is_swapped()
    {
        // IHO S-101 test cells 101AA00DS0006/0007/0015 ship this inversion.
        Assert.Equal(new DisplayScaleRange(90000, 22000), DisplayScaleRange.FromDeclared(22000, 90000));
    }

    [Fact]
    public void FromDeclared_single_bound_is_kept()
    {
        Assert.Equal(new DisplayScaleRange(22000, null), DisplayScaleRange.FromDeclared(22000, null));
        Assert.Equal(new DisplayScaleRange(null, 90000), DisplayScaleRange.FromDeclared(null, 90000));
    }

    [Fact]
    public void FromDeclared_non_positive_bounds_are_absent()
    {
        Assert.Equal(new DisplayScaleRange(null, 22000), DisplayScaleRange.FromDeclared(0, 22000));
        Assert.Equal(new DisplayScaleRange(90000, null), DisplayScaleRange.FromDeclared(90000, -1));
        Assert.Equal(new DisplayScaleRange(null, null), DisplayScaleRange.FromDeclared(null, null));
    }
}
