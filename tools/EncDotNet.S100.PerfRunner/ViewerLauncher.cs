using System.Diagnostics;
using Spectre.Console;

namespace EncDotNet.S100.PerfRunner;

/// <summary>
/// Starts a SoundCharts viewer for one settle run: MCP on, a port file, its
/// render caches in their own folder (<c>--cache-dir</c>) and the Lua engine
/// pinned with <c>S100_LUA_ENGINE</c>. Stops it afterwards.
/// </summary>
internal sealed class ViewerLauncher : IAsyncDisposable
{
    private readonly Process _process;

    private ViewerLauncher(Process process, string portFile)
    {
        _process = process;
        PortFile = portFile;
    }

    /// <summary>The file the viewer writes its MCP endpoint to.</summary>
    public string PortFile { get; }

    /// <summary>
    /// The command line for <paramref name="viewer"/>: run directly, or through
    /// <c>dotnet</c> for a <c>.dll</c>.
    /// </summary>
    internal static (string FileName, List<string> Arguments) CommandFor(
        string viewer, string portFile, string cacheDirectory, IEnumerable<string> extraArguments)
    {
        var arguments = new List<string>();
        var fileName = viewer;
        if (viewer.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            fileName = "dotnet";
            arguments.Add(viewer);
        }

        arguments.AddRange(["--mcp", "--mcp-port-file", portFile, "--cache-dir", cacheDirectory]);
        arguments.AddRange(extraArguments);
        return (fileName, arguments);
    }

    /// <summary>Starts the viewer and waits for its port file.</summary>
    public static async Task<ViewerLauncher> StartAsync(
        string viewer,
        string engine,
        string cacheDirectory,
        string workDirectory,
        IEnumerable<string> extraArguments,
        string? telemetryFile,
        TimeSpan startupTimeout,
        CancellationToken ct)
    {
        Directory.CreateDirectory(cacheDirectory);
        Directory.CreateDirectory(workDirectory);
        var portFile = Path.Combine(workDirectory, $"mcp-{Guid.NewGuid():N}.url");
        var (fileName, arguments) = CommandFor(viewer, portFile, cacheDirectory, extraArguments);
        var start = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        start.Environment["S100_LUA_ENGINE"] = engine;
        if (telemetryFile is not null)
            start.Environment["ENC_DOTNET_OTEL_FILE"] = telemetryFile;

        AnsiConsole.MarkupLine($"[bold]Launch:[/]   {Markup.Escape(fileName)} {Markup.Escape(string.Join(' ', arguments))} (S100_LUA_ENGINE={Markup.Escape(engine)})");
        var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {viewer}.");
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var launcher = new ViewerLauncher(process, portFile);
        var sw = Stopwatch.StartNew();
        while (!File.Exists(portFile) || new FileInfo(portFile).Length == 0)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException($"The viewer exited with code {process.ExitCode} before it published its MCP endpoint.");
            }

            if (sw.Elapsed > startupTimeout)
            {
                await launcher.DisposeAsync();
                throw new TimeoutException($"The viewer didn't publish its MCP endpoint within {startupTimeout.TotalSeconds:F0} s.");
            }

            await Task.Delay(250, ct);
        }

        return launcher;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.CloseMainWindow();
                using var exited = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await _process.WaitForExitAsync(exited.Token);
                }
                catch (OperationCanceledException)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        finally
        {
            _process.Dispose();
            try
            {
                File.Delete(PortFile);
            }
            catch (IOException)
            {
            }
        }
    }
}
