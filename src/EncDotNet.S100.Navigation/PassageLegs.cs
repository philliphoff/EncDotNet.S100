namespace EncDotNet.S100.Navigation;

/// <summary>Comparing legs for the checks' caches.</summary>
internal static class PassageLegs
{
    /// <summary>Whether two lists of legs have the same values, leg by leg.</summary>
    public static bool Same(IReadOnlyList<PassageLeg> a, IReadOnlyList<PassageLeg> b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!Same(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Same(PassageLeg a, PassageLeg b) =>
        ReferenceEquals(a, b)
        || (a.Geometry == b.Geometry
            && a.PortLimit == b.PortLimit
            && a.StarboardLimit == b.StarboardLimit
            && a.Speed == b.Speed
            && a.Draft == b.Draft
            && a.Squat == b.Squat
            && a.RequiredUkc == b.RequiredUkc
            && a.Name == b.Name
            && a.Points.SequenceEqual(b.Points));
}
