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

    [Fact]
    public void Describe_Schema_NamesTheProductAttributeAndGroup()
    {
        var reason = LoadFailureClassifier.Describe(new TargetInvocationException(MakeSchema()));

        Assert.Equal("This S-104 file is missing or has an invalid 'code' at '/Group_F'.", reason);
    }

    [Fact]
    public void Describe_NotSupported_NamesTheProductAndFeature()
    {
        var reason = LoadFailureClassifier.Describe(MakeNotSupported());

        Assert.Equal("S-104 dataset uses data coding format 8, which is not yet supported.", reason);
    }

    [Fact]
    public void Describe_Unstructured_UsesTheExceptionMessage()
    {
        Assert.Equal("disk on fire", LoadFailureClassifier.Describe(new IOException("disk on fire")));
    }

    [Fact]
    public void Describe_DamagedFile_SaysSoAndSpellsOutControlCharacters()
    {
        // A random file named .000 fails in the ISO 8211 reader like this.
        var ex = new InvalidOperationException(
            "load failed", new InvalidDataException("Invalid numeric character '\u000B' at position 0."));

        Assert.Equal(
            "The file is damaged or is not in the format its name suggests (Invalid numeric character 'U+000B' at position 0.).",
            LoadFailureClassifier.Describe(ex));
    }

    [Fact]
    public void FindDamagedFileException_FindsTruncatedFilesButNotStructuredFailures()
    {
        var truncated = new EndOfStreamException("Unexpected end of record.");

        Assert.Same(truncated, LoadFailureClassifier.FindDamagedFileException(new TargetInvocationException(truncated)));
        Assert.Null(LoadFailureClassifier.FindDamagedFileException(new IOException("disk on fire")));
        Assert.Null(LoadFailureClassifier.FindDamagedFileException(
            new S100DatasetSchemaException("S-102", file: "x.h5", groupPath: "/", attributeOrDataset: "code",
                specReference: null, message: "bad", innerException: new InvalidDataException("bad bytes"))));
    }

    [Fact]
    public void Printable_LeavesOrdinaryTextAlone() =>
        Assert.Equal("Invalid numeric character 's' at position 0.",
            LoadFailureClassifier.Printable("Invalid numeric character 's' at position 0."));

    [Fact]
    public void Toast_DamagedFile_UsesTheFriendlyBodyAndKeepsTheReaderMessageInDetails()
    {
        var ex = new InvalidDataException("Invalid numeric character '\u000B' at position 0.");

        var failure = LoadFailureViewModel.FromException("US5GARBG.000", "/charts/US5GARBG.000", ex);

        Assert.Equal(Viewer.Resources.Strings.LoadFailureToast_DamagedBody, failure.PrimaryMessage);
        Assert.Contains("Invalid numeric character", failure.Details);
    }
}
