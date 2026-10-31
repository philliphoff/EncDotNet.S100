using EncDotNet.S100.Collections;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// Whether chart-wide water level adjustment is on, and what it applied in a
/// view at a time. The seam the depth basis indicator (#1191) reads and
/// chart-wide adjustment (#1132) implements; until #1132 lands the only
/// implementation is <see cref="NoWaterLevelAdjustment"/>.
/// </summary>
/// <remarks>
/// Whether S-104 is loaded but not applied is <see cref="DepthBasisEvaluator"/>'s
/// decision, not this state's, so the two can't disagree. See
/// <c>docs/design/depth-basis-indicator.md</c> §2.4 and
/// <c>docs/design/chart-wide-water-level-adjustment.md</c> §7.
/// </remarks>
internal interface IWaterLevelAdjustmentState
{
    /// <summary>True when the mariner has turned chart adjustment on.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// What the adjustment applied in <paramref name="view"/> at
    /// <paramref name="viewTime"/>, or <see langword="null"/> when it is off.
    /// </summary>
    /// <param name="view">The map view in degrees, or <see langword="null"/> before the first view.</param>
    /// <param name="viewTime">The Timeline's view time.</param>
    /// <returns>The applied result, or <see langword="null"/>.</returns>
    WaterLevelAdjustmentResult? Evaluate(GeoBounds? view, DateTimeOffset viewTime);

    /// <summary>Raised when the setting or the applied result changes.</summary>
    event EventHandler? Changed;
}

/// <summary>What chart adjustment applied in a view (#1132 maps its <c>ChartAdjustmentState</c> onto this).</summary>
/// <param name="Status"><see cref="WaterLevelStatus.Applied"/> when the chart is adjusted somewhere in view; otherwise why not.</param>
/// <param name="StepTime">The S-104 time step the drawn layers are for, when applied.</param>
/// <param name="CoversWholeView">False when the adjusted area doesn't cover every charted depth in view.</param>
/// <param name="Datum">The depth and water-level datum agreement, when known.</param>
internal sealed record WaterLevelAdjustmentResult(
    WaterLevelStatus Status,
    DateTime? StepTime = null,
    bool CoversWholeView = true,
    DatumAgreement? Datum = null);

/// <summary>Chart adjustment that is always off: the only state until #1132.</summary>
internal sealed class NoWaterLevelAdjustment : IWaterLevelAdjustmentState
{
    /// <summary>The shared instance.</summary>
    public static NoWaterLevelAdjustment Instance { get; } = new();

    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public WaterLevelAdjustmentResult? Evaluate(GeoBounds? view, DateTimeOffset viewTime) => null;

    /// <inheritdoc />
    public event EventHandler? Changed { add { } remove { } }
}
