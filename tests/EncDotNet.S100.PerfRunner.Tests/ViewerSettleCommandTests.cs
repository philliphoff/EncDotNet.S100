namespace EncDotNet.S100.PerfRunner.Tests;

/// <summary>
/// Checks the MCP transport timeout and the failure message of
/// <see cref="ViewerSettleCommand"/>: an HTTP request must outlast every tool
/// timeout the run passes, and an HTTP timeout must be reported, not read as
/// a cancellation.
/// </summary>
public sealed class ViewerSettleCommandTests
{
    [Theory]
    [InlineData(120_000)]
    [InlineData(30_000)]
    [InlineData(600_000)]
    public void RequestTimeout_OutlastsTheLongestToolTimeout(int settleTimeoutMs)
    {
        var options = new SettleRunOptions
        {
            Route = "jumps",
            Mode = SettleMode.Cold,
            Jumps = SettleRoute.DefaultJumps,
            Pan = SettleRoute.DefaultPan,
            SettleTimeoutMs = settleTimeoutMs,
        };

        var timeout = ViewerSettleCommand.RequestTimeout(options);

        Assert.True(timeout > TimeSpan.FromMilliseconds(settleTimeoutMs));
        Assert.True(timeout > TimeSpan.FromMilliseconds(ViewerSettleRunner.LibraryIdleTimeoutMs));
        Assert.True(timeout > TimeSpan.FromSeconds(100), "Longer than HttpClient's default timeout.");
    }

    [Fact]
    public async Task Describe_NamesAnHttpClientTimeout()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(200) };

        // The listener accepts the connection but never answers.
        var ex = await Assert.ThrowsAsync<TaskCanceledException>(
            () => http.GetAsync(new Uri($"http://127.0.0.1:{port}/"), TestContext.Current.CancellationToken));

        var message = ViewerSettleCommand.Describe(ex);
        Assert.Contains("timed out", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_IncludesTypeAndInnerException()
    {
        var ex = new InvalidOperationException("outer", new TimeoutException("inner"));

        var message = ViewerSettleCommand.Describe(ex);

        Assert.Equal("InvalidOperationException: outer (TimeoutException: inner)", message);
    }
}
