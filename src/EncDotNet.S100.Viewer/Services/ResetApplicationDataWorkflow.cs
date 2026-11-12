using EncDotNet.S100.Viewer.Services.Secom;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>What <see cref="ResetApplicationDataWorkflow"/> clears.</summary>
internal enum ResetApplicationData
{
    /// <summary>Every setting, imported identity, crash marker and cache: a clean slate.</summary>
    ResetAll,

    /// <summary>The on-disk caches; settings are kept.</summary>
    ClearCaches,
}

/// <summary>
/// Settings → Advanced's Reset all settings and Clear caches (#1398): removes
/// the imported identities' keys (for a reset), resets or clears, then
/// restarts the viewer so nothing stale stays in memory. The page confirms
/// first.
/// </summary>
internal sealed class ResetApplicationDataWorkflow
{
    private readonly IDataMaintenanceService _dataMaintenance;
    private readonly IApplicationControlService _applicationControl;
    private readonly SecomIdentityStore _identities;

    /// <summary>Creates the workflow.</summary>
    /// <param name="dataMaintenance">Resets the settings and clears the caches.</param>
    /// <param name="applicationControl">Restarts the viewer.</param>
    /// <param name="identities">The imported identities, whose keys live outside the settings file (#845).</param>
    public ResetApplicationDataWorkflow(
        IDataMaintenanceService dataMaintenance,
        IApplicationControlService applicationControl,
        SecomIdentityStore identities)
    {
        ArgumentNullException.ThrowIfNull(dataMaintenance);
        ArgumentNullException.ThrowIfNull(applicationControl);
        ArgumentNullException.ThrowIfNull(identities);
        _dataMaintenance = dataMaintenance;
        _applicationControl = applicationControl;
        _identities = identities;
    }

    /// <summary>Resets or clears <paramref name="what"/>, then restarts the viewer.</summary>
    /// <param name="what">What to clear.</param>
    public void Run(ResetApplicationData what)
    {
        if (what == ResetApplicationData.ResetAll)
        {
            _identities.RemoveAll();
            _dataMaintenance.ResetAll();
        }
        else
        {
            _dataMaintenance.ClearCaches();
        }

        _applicationControl.Restart();
    }
}
