using System.Globalization;
using System.Text;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Datasets.Pipelines.Navigation;

/// <summary>
/// Chart-wide water level adjustment over an <see cref="IWaterLevelField"/>:
/// the S-104 water level the route check and the pick report read, applied
/// to the charted depths the S-101 and S-102 processors portray.
/// </summary>
/// <remarks>
/// <para>
/// The datum rule follows <c>docs/design/chart-wide-water-level-adjustment.md</c>
/// §2.3 and §11 Q2. A water level on the same datum as the depth is applied,
/// and one on a different datum is always refused. When either datum isn't
/// declared (<see cref="DatumAgreement.Unknown"/>), the water level is
/// applied only if <see cref="ApplyUndeclaredDatum"/> is set, which is the
/// mariner's "Apply anyway when datum is not declared" override.
/// </para>
/// <para>
/// Instances are immutable and safe to share between render threads.
/// </para>
/// </remarks>
public sealed class WaterLevelDepthAdjustment : IDepthAdjustment
{
    private readonly IWaterLevelField _field;

    /// <summary>Creates the adjustment.</summary>
    /// <param name="field">The water level to apply.</param>
    /// <param name="applyUndeclaredDatum">Whether to apply water level where either datum isn't declared.</param>
    /// <param name="sourceKey">Identifies the water-level data the field was built from (for example a catalog snapshot), for <see cref="Key"/>.</param>
    public WaterLevelDepthAdjustment(IWaterLevelField field, bool applyUndeclaredDatum, string sourceKey)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(sourceKey);
        _field = field;
        ApplyUndeclaredDatum = applyUndeclaredDatum;
        Key = BuildKey(field.Basis, applyUndeclaredDatum, sourceKey);
    }

    /// <summary>The field this adjustment applies.</summary>
    public IWaterLevelField Field => _field;

    /// <summary>Whether water level is applied where either datum isn't declared.</summary>
    public bool ApplyUndeclaredDatum { get; }

    /// <inheritdoc />
    public string Key { get; }

    /// <inheritdoc />
    public IReadOnlyList<CoverageArea> Footprint => _field.Footprint;

    /// <inheritdoc />
    public DepthAdjustmentSample? At(double latitude, double longitude, int? depthDatum) =>
        Resolve(new GeoPosition(latitude, longitude), depthDatum) is { Status: WaterLevelStatus.Applied, Sample: { } sample }
            ? new DepthAdjustmentSample(sample.HeightMeters, sample.UncertaintyMeters)
            : null;

    /// <inheritdoc />
    public bool Accepts(int? depthDatum)
    {
        foreach (var source in _field.Basis.Sources)
        {
            if (Permits(VerticalDatumCheck.Compare(depthDatum, source.VerticalDatum)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The field's answer at a point with this adjustment's datum rule
    /// applied: an applied water level on an undeclared datum without the
    /// override comes back as <see cref="WaterLevelStatus.DatumMismatch"/>,
    /// the same status a different datum gets.
    /// </summary>
    /// <param name="position">The point.</param>
    /// <param name="depthDatum">The charted depth's datum code, or <see langword="null"/>.</param>
    /// <returns>The water level at the point and why it does or doesn't apply.</returns>
    public WaterLevelAtPoint Resolve(GeoPosition position, int? depthDatum)
    {
        var at = _field.Resolve(position, depthDatum);
        if (at.Status == WaterLevelStatus.Applied && at.Datum is { } datum && !Permits(datum))
        {
            return at with { Status = WaterLevelStatus.DatumMismatch };
        }

        return at;
    }

    /// <summary>Whether a datum agreement lets the water level apply under this adjustment's rule.</summary>
    /// <param name="agreement">The agreement between the depth's datum and the water level's.</param>
    /// <returns><see langword="true"/> when it applies.</returns>
    public bool Permits(DatumAgreement agreement) => agreement switch
    {
        DatumAgreement.Same => true,
        DatumAgreement.Unknown => ApplyUndeclaredDatum,
        _ => false,
    };

    private static string BuildKey(WaterLevelBasis basis, bool applyUndeclaredDatum, string sourceKey)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("wla;");
        sb.Append(basis.Kind == WaterLevelBasisKind.AtTime ? "at:" : "low:")
            .Append(basis.Start.Ticks.ToString(c));
        if (basis.Kind != WaterLevelBasisKind.AtTime)
        {
            sb.Append('-').Append(basis.End.Ticks.ToString(c));
        }

        sb.Append(";unk:").Append(applyUndeclaredDatum ? '1' : '0')
            .Append(";src:").Append(sourceKey);
        return sb.ToString();
    }
}
