using System.Globalization;

namespace EncDotNet.S100.Datasets.Grib;

/// <summary>
/// Names the model that produced a GRIB2 file. GRIB2 carries no model name,
/// only the originating centre (Section 1) and a generating process number
/// (Section 4) whose meaning is the centre's own, so the name comes from:
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><description>
/// NCEP's generating process (NCEP ON388 table A): 81, 82 and 96 are the GFS
/// (analysis, and forecast), 11 is GFS-Wave (the global WAVEWATCH III run).
/// </description></item>
/// <item><description>
/// The file name, as the sources name their files: <c>gfs.t00z…</c> and
/// <c>filter_gfs_0p25.pl</c> (a NOMADS GRIB filter download) are the GFS;
/// <c>gfswave.t00z…</c> and <c>filter_gfswave.pl</c> are GFS-Wave; ECMWF
/// Open Data's <c>…-oper-fc</c> and <c>…-scda-fc</c> are the IFS,
/// <c>…-wave-fc</c> and <c>…-scwv-fc</c> its wave model (ECMWF WAM), and
/// <c>aifs</c> anywhere in the name is the AIFS.
/// </description></item>
/// <item><description>
/// The centre's short name (<see cref="GribCentres"/>), for example
/// <c>"ECMWF"</c>, or <c>"GRIB2 centre 74"</c>.
/// </description></item>
/// </list>
/// <para>
/// A file holding fields of several models (GFS and GFS-Wave appended
/// together, say) is named after all of them: <c>"GFS + GFS-Wave"</c>.
/// </para>
/// </remarks>
internal static class GribModelNames
{
    /// <summary>The model name of the fields, in the order the models first appear.</summary>
    /// <param name="fields">The fields used.</param>
    /// <param name="fileName">The file's name, or <see langword="null"/>.</param>
    public static string Describe(IEnumerable<GribField> fields, string? fileName)
    {
        var names = new List<string>();
        foreach (var field in fields)
        {
            var name = ForField(field, fileName);
            if (!names.Contains(name))
                names.Add(name);
        }

        return names.Count == 0 ? FromFileName(fileName, centre: null) ?? "GRIB2" : string.Join(" + ", names);
    }

    /// <summary>The model name of one field.</summary>
    /// <param name="field">The field.</param>
    /// <param name="fileName">The file's name, or <see langword="null"/>.</param>
    public static string ForField(GribField field, string? fileName)
    {
        var centre = field.Identification.Centre;
        if (centre == GribCentres.Ncep)
        {
            var process = field.Product.GeneratingProcessIdentifier switch
            {
                81 or 82 or 96 => "GFS",
                11 => "GFS-Wave",
                _ => null,
            };
            if (process is not null)
                return process;
        }

        return FromFileName(fileName, centre)
            ?? GribCentres.GetName(centre)
            ?? string.Create(CultureInfo.InvariantCulture, $"GRIB2 centre {centre}");
    }

    /// <summary>The model a source's file name names, or <see langword="null"/>.</summary>
    /// <param name="fileName">The file's name, or <see langword="null"/>.</param>
    /// <param name="centre">The originating centre, or <see langword="null"/> when unknown.</param>
    public static string? FromFileName(string? fileName, ushort? centre)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var tokens = Path.GetFileName(fileName)
            .ToLowerInvariant()
            .Split(['.', '-', '_', ' ', '+'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Any(t => t.StartsWith("aifs", StringComparison.Ordinal)))
            return "ECMWF AIFS";
        if (tokens.Contains("gfswave"))
            return "GFS-Wave";
        if (tokens.Contains("gfs"))
            return "GFS";
        if (centre is null or GribCentres.Ecmwf)
        {
            // ECMWF Open Data streams: <date>-<step>h-<stream>-<type>.grib2.
            if (tokens.Contains("fc") && (tokens.Contains("wave") || tokens.Contains("scwv")))
                return "ECMWF WAM";
            if (tokens.Contains("fc") && (tokens.Contains("oper") || tokens.Contains("scda")))
                return "ECMWF IFS";
        }

        return null;
    }
}
