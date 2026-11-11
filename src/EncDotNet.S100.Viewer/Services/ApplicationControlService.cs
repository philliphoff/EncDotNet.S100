using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Controls the viewer process lifetime — currently a single
/// <see cref="Restart"/> operation used by the Settings panel's
/// "reset all" flow.
/// </summary>
internal interface IApplicationControlService
{
    /// <summary>
    /// Relaunches the viewer with the same command-line arguments, then
    /// shuts the current instance down. Used after a "reset all" so the
    /// fresh process loads default settings and rebuilds caches.
    /// </summary>
    void Restart();
}

/// <inheritdoc />
internal sealed class ApplicationControlService : IApplicationControlService
{
    private readonly ILogger<ApplicationControlService> _logger;

    public ApplicationControlService(ILogger<ApplicationControlService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Restart()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                var args = Environment.GetCommandLineArgs();
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                };

                // Preserve the original arguments (e.g. --data-dir) so the
                // restarted instance keeps the same isolation; skip index 0
                // (the executable/assembly path).
                for (var i = 1; i < args.Length; i++)
                {
                    psi.ArgumentList.Add(args[i]);
                }

                Process.Start(psi);
            }
            else
            {
                _logger.LogError("Cannot restart: the process path is unknown.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch the replacement viewer process.");
        }
        finally
        {
            Shutdown();
        }
    }

    private static void Shutdown()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            Environment.Exit(0);
        }
    }
}
