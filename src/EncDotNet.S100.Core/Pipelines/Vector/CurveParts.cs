using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Groups the curves of a curve feature into its connected parts.
/// </summary>
/// <remarks>
/// A feature may reference several curves: an S-100 Part 10a feature record's
/// spatial association field repeats, and a GML feature may carry several curve
/// members. Consecutive curves that meet end to start (the usual encoding of
/// one line split at shared nodes) form one part. Where they do not meet, the
/// feature has several parts, which must be drawn and queried apart: joining
/// them into one coordinate list would draw a straight segment across the gap
/// (issue #643).
/// </remarks>
public static class CurveParts
{
    /// <summary>
    /// Joins consecutive <paramref name="curves"/> that meet end to start into
    /// one part each, dropping the repeated shared vertex. Empty curves are
    /// skipped.
    /// </summary>
    /// <param name="curves">The feature's curves, in order.</param>
    /// <returns>The feature's connected parts, in order.</returns>
    public static IReadOnlyList<IReadOnlyList<GeoPosition>> Join(IReadOnlyList<IReadOnlyList<GeoPosition>> curves)
    {
        ArgumentNullException.ThrowIfNull(curves);

        var parts = new List<IReadOnlyList<GeoPosition>>();
        List<GeoPosition>? current = null;
        foreach (var curve in curves)
        {
            if (curve.Count == 0)
                continue;

            if (current is not null && current[^1] == curve[0])
            {
                for (int i = 1; i < curve.Count; i++)
                    current.Add(curve[i]);
                continue;
            }

            current = [.. curve];
            parts.Add(current);
        }

        return parts;
    }
}
