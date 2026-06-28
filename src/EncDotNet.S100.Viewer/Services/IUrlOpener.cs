using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Opens an external URL in the user's default browser. Abstracted so
/// view-models that link out (e.g. the About dialog's "Update now") can be
/// unit-tested without launching a real browser.
/// </summary>
internal interface IUrlOpener
{
    /// <summary>Opens <paramref name="url"/> in the default browser.</summary>
    void Open(string url);
}

/// <summary>
/// Default <see cref="IUrlOpener"/> that shell-launches the URL. Best-effort:
/// a launch failure is logged and swallowed rather than surfaced to the user.
/// </summary>
internal sealed class ProcessUrlOpener : IUrlOpener
{
    private readonly ILogger<ProcessUrlOpener>? _logger;

    public ProcessUrlOpener(ILogger<ProcessUrlOpener>? logger = null) => _logger = logger;

    /// <inheritdoc />
    public void Open(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to open URL {Url}.", url);
        }
    }
}
