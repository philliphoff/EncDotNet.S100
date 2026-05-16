using System;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

public class LoadFailureViewModelTests
{
    [Fact]
    public void FromException_Schema_UsesSchemaBodyFormat()
    {
        var ex = new S100DatasetSchemaException(
            product: "S-104",
            file: "test.h5",
            groupPath: "/WaterLevel/WaterLevel.01",
            attributeOrDataset: "values",
            specReference: "S-100 Part 10c §10.2.1.2",
            message: "missing");

        var vm = LoadFailureViewModel.FromException("My Dataset", "/tmp/test.h5", ex);

        var expected = string.Format(
            Strings.LoadFailureDialog_SchemaBody,
            "S-104", "values", "/WaterLevel/WaterLevel.01");
        Assert.Equal(expected, vm.PrimaryMessage);
        Assert.True(vm.HasSpecReference);
        Assert.Contains("S-100 Part 10c §10.2.1.2", vm.SpecReference);
    }

    [Fact]
    public void FromException_NotSupported_UsesNotSupportedBodyFormat()
    {
        var ex = new S100DatasetNotSupportedException(
            product: "S-104",
            file: "test.h5",
            feature: "data coding format 8 (time series at fixed stations)",
            specReference: "S-100 Part 10c §10.2.1",
            message: "not supported");

        var vm = LoadFailureViewModel.FromException("My Dataset", "/tmp/test.h5", ex);

        var expected = string.Format(
            Strings.LoadFailureDialog_NotSupportedBody,
            "S-104", "data coding format 8 (time series at fixed stations)");
        Assert.Equal(expected, vm.PrimaryMessage);
        Assert.True(vm.HasSpecReference);
    }

    [Fact]
    public void FromException_Generic_UsesExceptionMessage()
    {
        var ex = new InvalidOperationException("Some other failure.");

        var vm = LoadFailureViewModel.FromException("My Dataset", "/tmp/test.dat", ex);

        var expected = string.Format(Strings.LoadFailureDialog_GenericBody, "Some other failure.");
        Assert.Equal(expected, vm.PrimaryMessage);
        Assert.False(vm.HasSpecReference);
        Assert.Null(vm.SpecReference);
    }

    [Fact]
    public void FromException_NoSpecReference_HidesSpecLine()
    {
        var ex = new S100DatasetSchemaException(
            product: "S-104",
            file: "test.h5",
            groupPath: "/g",
            attributeOrDataset: "a",
            specReference: null,
            message: "msg");

        var vm = LoadFailureViewModel.FromException("Dataset", "/tmp/x.h5", ex);

        Assert.Null(vm.SpecReference);
        Assert.False(vm.HasSpecReference);
    }

    [Fact]
    public void FromException_DetailsContainsOriginalToString()
    {
        // Wrap a schema exception so we can verify that Details
        // carries the *original* (outer) toString, not the unwrapped
        // innermost exception. The user needs the whole chain.
        var schema = new S100DatasetSchemaException(
            product: "S-104", file: "test.h5", groupPath: "/g",
            attributeOrDataset: "a", specReference: null,
            message: "schema-msg");
        var original = new InvalidOperationException("OUTER", schema);

        var vm = LoadFailureViewModel.FromException("Dataset", "/tmp/x.h5", original);

        Assert.Equal(original.ToString(), vm.Details);
        Assert.Contains("OUTER", vm.Details);
        Assert.Contains("schema-msg", vm.Details);
    }
}
