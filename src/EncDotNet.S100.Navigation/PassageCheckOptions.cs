namespace EncDotNet.S100.Navigation;

/// <summary>Tuning for <see cref="PassageCheckEngine"/>.</summary>
public sealed record PassageCheckOptions
{
    /// <summary>The default options.</summary>
    public static PassageCheckOptions Default { get; } = new();

    /// <summary>
    /// The longest stretch one reported sample stands for, in metres. Each
    /// reported sample carries the shoalest depth found in its stretch, so a
    /// longer spacing never hides a shoal; it only coarsens where along the
    /// stretch the shoal is placed. Default 50 m.
    /// </summary>
    public double SampleSpacingMeters { get; init; } = 50;

    /// <summary>
    /// The step along a leg as a fraction of the finest overlapping S-102
    /// cell size, so no grid cell the leg crosses is stepped over. Default
    /// 0,5 (two steps per cell).
    /// </summary>
    public double GridStepFactor { get; init; } = 0.5;

    /// <summary>The smallest step along a leg, in metres. Default 0,5 m.</summary>
    public double MinimumStepMeters { get; init; } = 0.5;

    /// <summary>
    /// The most steps taken along one leg. A longer leg over a very fine grid
    /// steps more coarsely rather than exceeding this. Default 250 000.
    /// </summary>
    public int MaxStepsPerLeg { get; init; } = 250_000;

    /// <summary>
    /// How close to the required UKC counts as tight, in metres. Default
    /// 0,5 m ("Tight (within 0,5 m of required)").
    /// </summary>
    public double TightMarginMeters { get; init; } = 0.5;

    /// <summary>
    /// When <c>true</c>, the S-102 uncertainty is subtracted from the charted
    /// depth before the clearance is computed (S-102 Ed 3.0.0 §6.5.1 says the
    /// uncertainty must be considered when classifying depths). Default
    /// <c>false</c>: the uncertainty is reported, not applied.
    /// </summary>
    public bool SubtractDepthUncertainty { get; init; }

    /// <summary>
    /// Stretches of one kind (in <see cref="PassageCheckResult.Gaps"/> and
    /// <see cref="PassageCheckResult.LowConfidence"/>) separated by less than
    /// this, in metres, are reported as one, so a ragged S-102 edge reads as
    /// one fallback stretch. Default 100 m.
    /// </summary>
    public double StretchMergeGapMeters { get; init; } = 100;

    /// <summary>
    /// Low-confidence stretches shorter than this, in metres, after merging,
    /// are left out of <see cref="PassageCheckResult.LowConfidence"/>; the
    /// samples still carry their <see cref="ConfidenceReasons"/>. A passage
    /// shorter than this keeps its stretches. Gaps are
    /// always reported, however short, since they explain an incomplete-data
    /// verdict. Default 100 m.
    /// </summary>
    public double MinimumLowConfidenceStretchMeters { get; init; } = 100;
}
