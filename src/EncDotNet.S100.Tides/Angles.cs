namespace EncDotNet.S100.Tides;

/// <summary>Degree helpers.</summary>
internal static class Angles
{
    public const double DegreesToRadians = Math.PI / 180.0;

    /// <summary>Normalizes an angle to [0, 360) degrees.</summary>
    public static double Normalize(double degrees)
    {
        var result = degrees % 360.0;
        return result < 0 ? result + 360.0 : result;
    }
}
