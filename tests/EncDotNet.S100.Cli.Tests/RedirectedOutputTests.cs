using System.Diagnostics;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.TestSupport;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Issue #1692: when standard output is not a terminal, text output is
/// complete and wraps at a usable width instead of collapsing to "…".
/// </summary>
/// <remarks>
/// The process tests run the <c>s100</c> host with standard output
/// redirected and <c>TERM=linux</c>: on Linux, .NET then reports a console
/// width of -1, which is what truncated every line before the fix.
/// </remarks>
public sealed class RedirectedOutputTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("redirected-cli-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task Version_is_printed_when_output_is_redirected()
    {
        var (exit, stdout) = await RunHostAsync(["--version"], term: "linux", columns: null);

        Assert.Equal(0, exit);
        Assert.False(string.IsNullOrWhiteSpace(stdout), "s100 --version printed nothing.");
        Assert.DoesNotContain('…', stdout);
    }

    [Fact]
    public async Task Help_is_complete_when_output_is_redirected()
    {
        var (exit, stdout) = await RunHostAsync(["--help"], term: "linux", columns: null);

        Assert.Equal(0, exit);
        Assert.Contains("USAGE:", stdout);
        Assert.Contains("s100 [OPTIONS] <COMMAND>", stdout);
        Assert.Contains("--skill", stdout);
    }

    [Fact]
    public async Task Info_table_is_complete_and_wraps_at_the_default_width()
    {
        var dataset = TropicalCycloneFixtures.CopyIan(_dir);

        var (exit, stdout) = await RunHostAsync(["info", dataset], term: "linux", columns: null);

        Assert.Equal(0, exit);
        Assert.Contains("Major Hurricane Ian (AL092022), North Atlantic", stdout);
        Assert.Contains("20, issued 2022-09-28 03:00 UTC", stdout);
        Assert.Contains("Wind radii", stdout);
        Assert.Equal(CliConsoleWidth.DefaultWidth, WidestLine(stdout));

        // The property table fits; only the wide forecast-point table may
        // abbreviate its column headers, as in an 80-column terminal.
        var properties = stdout[..stdout.IndexOf("Tau", StringComparison.Ordinal)];
        Assert.DoesNotContain('…', properties);
    }

    [Fact]
    public async Task Info_output_is_the_same_whatever_term_is_set_to()
    {
        var dataset = TropicalCycloneFixtures.CopyIan(_dir);

        var (_, linux) = await RunHostAsync(["info", dataset], term: "linux", columns: null);
        var (_, unset) = await RunHostAsync(["info", dataset], term: null, columns: null);

        Assert.Equal(unset, linux);
    }

    [Fact]
    public async Task Columns_sets_the_width_when_the_console_reports_none()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "TERM=linux yields no console width only on Linux.");
        var dataset = TropicalCycloneFixtures.CopyIan(_dir);

        var (exit, stdout) = await RunHostAsync(["info", dataset], term: "linux", columns: "120");

        Assert.Equal(0, exit);
        Assert.Contains("Tropical cyclone forecast, not for navigation. Follow official NHC advisories.", stdout);
        Assert.InRange(WidestLine(stdout), CliConsoleWidth.DefaultWidth + 1, 120);
    }

    [Theory]
    [InlineData(-1, null, CliConsoleWidth.DefaultWidth)]
    [InlineData(0, null, CliConsoleWidth.DefaultWidth)]
    [InlineData(-1, "120", 120)]
    [InlineData(-1, "0", CliConsoleWidth.DefaultWidth)]
    [InlineData(-1, "-40", CliConsoleWidth.DefaultWidth)]
    [InlineData(-1, "wide", CliConsoleWidth.DefaultWidth)]
    [InlineData(132, "120", 132)]
    public void Resolve_keeps_a_detected_width_and_falls_back_to_columns_then_the_default(
        int detected, string? columns, int expected)
    {
        Assert.Equal(expected, CliConsoleWidth.Resolve(detected, columns));
    }

    [Fact]
    public void Ensure_keeps_a_usable_width()
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(new StringWriter()) });
        console.Profile.Width = 100;

        CliConsoleWidth.Ensure(console, columns: "120");

        Assert.Equal(100, console.Profile.Width);
    }

    private static int WidestLine(string text) =>
        text.Split('\n').Max(line => line.TrimEnd('\r').Length);

    private static async Task<(int Exit, string Stdout)> RunHostAsync(string[] args, string? term, string? columns)
    {
        var host = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "s100.exe" : "s100");
        Assert.SkipUnless(File.Exists(host), $"CLI host not found: {host}");

        var start = new ProcessStartInfo(host)
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var argument in args)
            start.ArgumentList.Add(argument);
        start.Environment["S100_NO_UPDATE_CHECK"] = "1";
        SetOrRemove(start, "TERM", term);
        SetOrRemove(start, CliConsoleWidth.ColumnsVariable, columns);
        start.Environment.Remove("LINES");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(90));
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderr = process.StandardError.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);
        await stderr;
        return (process.ExitCode, await stdout);
    }

    private static void SetOrRemove(ProcessStartInfo start, string name, string? value)
    {
        if (value is null)
            start.Environment.Remove(name);
        else
            start.Environment[name] = value;
    }
}
