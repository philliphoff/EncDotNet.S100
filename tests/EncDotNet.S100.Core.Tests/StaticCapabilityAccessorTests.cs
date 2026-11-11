using EncDotNet.S100.Hosting;

namespace EncDotNet.S100.Core.Tests;

public class StaticCapabilityAccessorTests
{
    private sealed class Capability;

    [Fact]
    public void Current_ReturnsTheSuppliedInstance()
    {
        var capability = new Capability();

        var accessor = new StaticCapabilityAccessor<Capability>(capability);

        Assert.Same(capability, accessor.Current);
    }

    [Fact]
    public void Constructor_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => new StaticCapabilityAccessor<Capability>(null!));
    }

    [Fact]
    public void SatisfiesCovariantAccessorOfAnInterface()
    {
        // A StaticCapabilityAccessor<Concrete> must bind where an
        // ICapabilityAccessor<IShape> is expected (covariance), which is how a
        // host hands a concrete capability to a tool typed on the interface.
        var accessor = new StaticCapabilityAccessor<DerivedCapability>(new DerivedCapability());

        ICapabilityAccessor<IShape> asInterface = accessor;

        Assert.NotNull(asInterface.Current);
    }

    private interface IShape;

    private sealed class DerivedCapability : IShape;
}
