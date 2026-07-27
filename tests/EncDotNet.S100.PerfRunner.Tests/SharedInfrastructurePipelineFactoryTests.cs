namespace EncDotNet.S100.PerfRunner.Tests;

/// <summary>
/// Regression guards for the reflection-based constructor probe in
/// <see cref="SharedInfrastructure"/>. Issue #491: when the live
/// <c>DatasetPipelineFactory</c> constructor gained a trailing optional
/// parameter, the old <c>Type.GetConstructor(Type[])</c> exact-arity probe
/// stopped matching and every non-trivial PerfRunner scenario failed with
/// <see cref="MissingMethodException"/> at warmup.
/// </summary>
public class SharedInfrastructurePipelineFactoryTests
{
    [Fact]
    public void CreatePipelineFactory_ResolvesLivePipelineFactoryConstructor()
    {
        // End-to-end regression guard: exercises the same reflection path
        // that every PerfRunner scenario hits at warmup. If any future
        // change to DatasetPipelineFactory's constructor breaks the probe,
        // this test — not the perf harness — is what should fail first.
        var factory = SharedInfrastructure.CreatePipelineFactory();

        Assert.NotNull(factory);
    }

    [Fact]
    public void FindConstructorMatchingPrefix_MatchesConstructorWithTrailingOptionalParameter()
    {
        var ctor = SharedInfrastructure.FindConstructorMatchingPrefix(
            typeof(TrailingOptionalCtorProbe),
            [typeof(string), typeof(int)]);

        Assert.NotNull(ctor);
        var parameters = ctor!.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.True(parameters[2].HasDefaultValue);
    }

    [Fact]
    public void FindConstructorMatchingPrefix_MatchesConstructorWithMultipleTrailingOptionals()
    {
        var ctor = SharedInfrastructure.FindConstructorMatchingPrefix(
            typeof(MultipleTrailingOptionalsProbe),
            [typeof(string)]);

        Assert.NotNull(ctor);
        Assert.Equal(3, ctor!.GetParameters().Length);
    }

    [Fact]
    public void FindConstructorMatchingPrefix_RejectsPrefixMismatch()
    {
        var ctor = SharedInfrastructure.FindConstructorMatchingPrefix(
            typeof(TrailingOptionalCtorProbe),
            [typeof(int), typeof(string)]);

        Assert.Null(ctor);
    }

    [Fact]
    public void FindConstructorMatchingPrefix_RejectsRequiredTrailingParameter()
    {
        var ctor = SharedInfrastructure.FindConstructorMatchingPrefix(
            typeof(RequiredTrailingParameterProbe),
            [typeof(string), typeof(int)]);

        Assert.Null(ctor);
    }

    [Fact]
    public void FindConstructorMatchingPrefix_MatchesExactArityConstructor()
    {
        var ctor = SharedInfrastructure.FindConstructorMatchingPrefix(
            typeof(TrailingOptionalCtorProbe),
            [typeof(string), typeof(int), typeof(bool)]);

        Assert.NotNull(ctor);
        Assert.Equal(3, ctor!.GetParameters().Length);
    }

    [Fact]
    public void InvokeWithDefaults_UsesCompileTimeDefaultForTrailingOptional()
    {
        var ctor = SharedInfrastructure.FindConstructorMatchingPrefix(
            typeof(TrailingOptionalCtorProbe),
            [typeof(string), typeof(int)]);
        Assert.NotNull(ctor);

        var instance = (TrailingOptionalCtorProbe)SharedInfrastructure.InvokeWithDefaults(
            ctor!,
            ["hello", 42]);

        Assert.Equal("hello", instance.Name);
        Assert.Equal(42, instance.Count);
        Assert.True(instance.Flag);
    }

    [Fact]
    public void InvokeWithDefaults_RejectsMoreArgsThanParameters()
    {
        var ctor = SharedInfrastructure.FindConstructorMatchingPrefix(
            typeof(TrailingOptionalCtorProbe),
            [typeof(string), typeof(int)]);
        Assert.NotNull(ctor);

        Assert.Throws<ArgumentException>(() =>
            SharedInfrastructure.InvokeWithDefaults(ctor!, ["a", 1, true, "extra"]));
    }

    private sealed class TrailingOptionalCtorProbe
    {
        public TrailingOptionalCtorProbe(string name, int count, bool flag = true)
        {
            Name = name;
            Count = count;
            Flag = flag;
        }

        public string Name { get; }

        public int Count { get; }

        public bool Flag { get; }
    }

    private sealed class MultipleTrailingOptionalsProbe
    {
        public MultipleTrailingOptionalsProbe(string name, int count = 0, bool flag = false)
        {
            Name = name;
            Count = count;
            Flag = flag;
        }

        public string Name { get; }

        public int Count { get; }

        public bool Flag { get; }
    }

    private sealed class RequiredTrailingParameterProbe
    {
        public RequiredTrailingParameterProbe(string name, int count, bool flag)
        {
            Name = name;
            Count = count;
            Flag = flag;
        }

        public string Name { get; }

        public int Count { get; }

        public bool Flag { get; }
    }
}
