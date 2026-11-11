using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// Locks the stable <see cref="ToolError.Code"/> and message shape of the
/// renderer-neutral errors added for the mutable MCP tool set (#560). Codes
/// are part of the agent-facing contract and must not drift silently.
/// </summary>
public class MutableToolErrorTests
{
    [Fact]
    public void HostNotReady_HasStableCodeAndMessage()
    {
        var error = new HostNotReady("the render surface");

        Assert.Equal("host_not_ready", error.Code);
        Assert.Equal("Not ready: the render surface.", error.Message);
        Assert.Equal("the render surface", error.What);
        Assert.IsAssignableFrom<ToolError>(error);
    }

    [Fact]
    public void DatasetLoadFailed_HasStableCodeAndMessage()
    {
        var error = new DatasetLoadFailed("the exchange set contained no portrayable datasets");

        Assert.Equal("dataset_load_failed", error.Code);
        Assert.Equal(
            "Load failed: the exchange set contained no portrayable datasets.",
            error.Message);
        Assert.Equal("the exchange set contained no portrayable datasets", error.Reason);
        Assert.IsAssignableFrom<ToolError>(error);
    }

    [Theory]
    [InlineData("must be between -90 and 90", "Invalid argument 'latitude': must be between -90 and 90.")]
    [InlineData("The identity could not be read: wrong password.", "Invalid argument 'latitude': The identity could not be read: wrong password.")]
    public void InvalidArgument_EndsWithOnePeriod(string reason, string expected)
    {
        // A reason that is an exception message already ends with a period (set_secom_identity).
        var error = new InvalidArgument("latitude", reason);

        Assert.Equal("invalid_argument", error.Code);
        Assert.Equal(expected, error.Message);
        Assert.Equal(reason, error.Reason);
    }
}
