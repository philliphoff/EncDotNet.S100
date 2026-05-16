using System;
using System.Reflection;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

public class LoadFailureClassifierTests
{
    private static S100DatasetSchemaException MakeSchema(string product = "S-104") =>
        new(product, file: "test.h5", groupPath: "/Group_F", attributeOrDataset: "code",
            specReference: "S-100 Part 10c §10.2.1", message: "missing");

    private static S100DatasetNotSupportedException MakeNotSupported(string product = "S-104") =>
        new(product, file: "test.h5", feature: "data coding format 8",
            specReference: "S-100 Part 10c §10.2.1", message: "not supported");

    [Fact]
    public void Unwrap_TargetInvocationException_ReturnsInnerSchema()
    {
        var schema = MakeSchema();
        var wrapped = new TargetInvocationException(schema);

        var result = LoadFailureClassifier.Unwrap(wrapped);

        Assert.Same(schema, result);
    }

    [Fact]
    public void Unwrap_NestedInvocation_ReturnsInnermostSchema()
    {
        var schema = MakeSchema();
        var inner = new TargetInvocationException(schema);
        var outer = new TargetInvocationException(inner);

        var result = LoadFailureClassifier.Unwrap(outer);

        Assert.Same(schema, result);
    }

    [Fact]
    public void Unwrap_NoStructuredException_ReturnsOriginal()
    {
        var original = new InvalidOperationException("boom",
            new ArgumentException("inner"));

        var result = LoadFailureClassifier.Unwrap(original);

        Assert.Same(original, result);
    }

    [Fact]
    public void Unwrap_BothSchemaAndNotSupported_TakesInnermost()
    {
        var schema = MakeSchema();
        // NotSupported wraps Schema → Schema is innermost.
        var ns = new S100DatasetNotSupportedException(
            "S-104", file: "test.h5", feature: "feature",
            specReference: null, message: "ns", innerException: schema);

        var result = LoadFailureClassifier.Unwrap(ns);

        Assert.Same(schema, result);
    }

    [Fact]
    public void Unwrap_OnlyNotSupported_ReturnsIt()
    {
        var ns = MakeNotSupported();
        var wrapped = new TargetInvocationException(ns);

        var result = LoadFailureClassifier.Unwrap(wrapped);

        Assert.Same(ns, result);
    }
}
