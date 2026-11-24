using System.Diagnostics;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Updates;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// The CLI's files go in the user's local application data even when that
/// folder doesn't exist yet, not in the folder the CLI runs from.
/// </summary>
public sealed class CliPathsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "s100-paths-" + Guid.NewGuid().ToString("N"));

    public CliPathsTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void The_default_files_are_absolute_paths_in_local_application_data()
    {
        Assert.True(Path.IsPathRooted(CliPaths.LocalApplicationData), CliPaths.LocalApplicationData);
        Assert.Equal(
            Path.Combine(CliPaths.LocalApplicationData, "EncDotNet.S100", "Tides", "noaa-tide-stations.json.gz"),
            TideCli.DefaultCataloguePath);
        Assert.Equal(
            Path.Combine(CliPaths.LocalApplicationData, "EncDotNet.S100.Cli", "update-cache.json"),
            JsonCliUpdateCache.DefaultPath);
    }

    [Fact]
    public async Task A_missing_data_folder_is_used_not_the_working_folder()
    {
        // XDG_DATA_HOME names the folder on Linux only.
        Assert.SkipUnless(OperatingSystem.IsLinux(), "XDG_DATA_HOME applies on Linux.");
        var host = Path.Combine(AppContext.BaseDirectory, "s100");
        Assert.SkipUnless(File.Exists(host), $"CLI host not found: {host}");

        var data = Path.Combine(_directory, "data-that-does-not-exist");
        var working = Path.Combine(_directory, "working");
        Directory.CreateDirectory(working);
        var start = new ProcessStartInfo(host)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = working,
        };
        foreach (var argument in new[] { "tides", "stations", "--near", "47.6,-122.34" })
            start.ArgumentList.Add(argument);
        start.Environment["XDG_DATA_HOME"] = data;
        start.Environment["S100_NO_UPDATE_CHECK"] = "1";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(90));
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderr = await process.StandardError.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);
        await stdout;

        // No catalogue there yet: the hint names where it would be.
        Assert.Equal(2, process.ExitCode);
        Assert.Contains(Path.Combine(data, "EncDotNet.S100", "Tides", "noaa-tide-stations.json.gz"), stderr);
        Assert.Empty(Directory.EnumerateFileSystemEntries(working));
    }
}
