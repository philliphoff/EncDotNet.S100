using EncDotNet.S100.Hdf5;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// PR-B: verifies the .WithFile(...) processor-layer attachment used
/// by S-102/S-104/S-111 dataset processors when the underlying reader
/// threw a typed exception with File == null (because the reader only
/// had a Stream). Processors catch and rethrow via .WithFile() to
/// attach the source file name.
/// </summary>
public class Hdf5DatasetExceptionWithFileTests
{
    [Fact]
    public void SchemaException_WithFile_AttachesFileNameAndPreservesContext()
    {
        var inner = new InvalidOperationException("Could not find attribute 'gridOriginLatitude'.");
        var original = new S100DatasetSchemaException(
            product: "S-104",
            file: null,
            groupPath: "/WaterLevel/WaterLevel.01",
            attributeOrDataset: "gridOriginLatitude",
            specReference: "S-100 Part 10c §10.2.1.2",
            message: "S-104 dataset is missing required attribute 'gridOriginLatitude' on group '/WaterLevel/WaterLevel.01' (S-100 Part 10c §10.2.1.2). The file appears to be non-conforming.",
            innerException: inner);

        Assert.Null(original.File);

        var wrapped = original.WithFile("104UK_TEST.h5");

        Assert.Equal("104UK_TEST.h5", wrapped.File);
        Assert.Equal(original.Product, wrapped.Product);
        Assert.Equal(original.GroupPath, wrapped.GroupPath);
        Assert.Equal(original.AttributeOrDataset, wrapped.AttributeOrDataset);
        Assert.Equal(original.SpecReference, wrapped.SpecReference);
        Assert.Same(original.InnerException, wrapped.InnerException);

        Assert.Contains("104UK_TEST.h5", wrapped.Message);
        Assert.Contains("gridOriginLatitude", wrapped.Message);
        Assert.Contains("/WaterLevel/WaterLevel.01", wrapped.Message);
        Assert.Contains("S-100 Part 10c §10.2.1.2", wrapped.Message);
    }

    [Fact]
    public void NotSupportedException_WithFile_AttachesFileNameAndPreservesContext()
    {
        var original = new S100DatasetNotSupportedException(
            product: "S-104",
            file: null,
            feature: "data coding format 8 (time series at fixed stations)",
            specReference: "S-100 Part 10c §10.2.1",
            message: "S-104 dataset uses data coding format 8 (time series at fixed stations), which is not yet supported (S-100 Part 10c §10.2.1).");

        var wrapped = original.WithFile("104UK_DCF8.h5");

        Assert.Equal("104UK_DCF8.h5", wrapped.File);
        Assert.Equal(original.Product, wrapped.Product);
        Assert.Equal(original.Feature, wrapped.Feature);
        Assert.Equal(original.SpecReference, wrapped.SpecReference);

        Assert.Contains("104UK_DCF8.h5", wrapped.Message);
        Assert.Contains("data coding format 8", wrapped.Message);
        Assert.Contains("not yet supported", wrapped.Message);
    }

    [Fact]
    public void WithFile_NullArgument_ReturnsEquivalentExceptionWhenAlreadyNull()
    {
        var original = new S100DatasetSchemaException(
            "S-104", null, "/WaterLevel", "dataCodingFormat", null,
            "S-104 dataset is missing required attribute 'dataCodingFormat' on group '/WaterLevel'. The file appears to be non-conforming.");

        var same = original.WithFile(null);

        Assert.Same(original, same);
    }
}
