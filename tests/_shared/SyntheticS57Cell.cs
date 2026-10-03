using EncDotNet.Iso8211;

namespace EncDotNet.S100.TestSupport;

/// <summary>
/// Writes S-57 cells that declare a chosen product specification (<c>DSID</c>/
/// <c>PRSP</c>), for tests that need to tell a maritime ENC (<c>PRSP = 1</c>) from
/// an inland ENC (<c>PRSP = 10</c>). The cell is the committed NOAA fixture
/// <c>US5MA1BO.000</c> with only that one byte rewritten, so the tests need no
/// inland sample data.
/// </summary>
internal static class SyntheticS57Cell
{
    private const byte UnitTerminator = 0x1F;

    /// <summary>
    /// Writes a copy of the NOAA fixture named <paramref name="fileName"/> into
    /// <paramref name="directory"/> whose DSID declares
    /// <paramref name="productSpecification"/>, and returns its full path.
    /// </summary>
    public static string Write(string directory, string fileName, byte productSpecification)
    {
        var document = Iso8211DocumentReader.ReadFromFile(FixturePath());
        // The DDR defines a DSID field too; the value lives in the first data record.
        var dsid = document.DataRecords.SelectMany(r => r.Fields).First(f => f.Tag == "DSID");
        dsid.Data[ProductSpecificationOffset(dsid.Data)] = productSpecification;

        var path = Path.Combine(directory, fileName);
        Iso8211DocumentWriter.WriteToFile(path, document);
        return path;
    }

    /// <summary>
    /// Writes a copy of the NOAA fixture into <paramref name="directory"/> with
    /// source errors a validator must report: the second <c>DEPARE</c> feature
    /// is given the FOID of the first (a duplicate feature object identifier,
    /// <c>S101-R-2.1</c> once translated) and, when
    /// <paramref name="zeroCompilationScale"/> is set, the DSPM compilation
    /// scale is zeroed (<c>S57-R-1.1</c>). Returns the cell's full path.
    /// </summary>
    public static string WriteWithSourceErrors(string directory, string fileName, bool zeroCompilationScale = false)
    {
        const ushort depareObjl = 42;
        var document = Iso8211DocumentReader.ReadFromFile(FixturePath());

        // FRID (binary): RCNM b11, RCID b14, PRIM b11, GRUP b11, OBJL b12, …
        var depthAreas = document.DataRecords
            .Where(r => r.Fields.FirstOrDefault(f => f.Tag == "FRID") is { } frid
                && BitConverter.ToUInt16(frid.Data, 7) == depareObjl)
            .Take(2)
            .Select(r => r.Fields.First(f => f.Tag == "FOID"))
            .ToList();
        // FOID (binary): AGEN b12, FIDN b14, FIDS b12.
        Array.Copy(depthAreas[0].Data, depthAreas[1].Data, 8);

        if (zeroCompilationScale)
        {
            // DSPM (binary): RCNM b11, RCID b14, HDAT b11, VDAT b11, SDAT b11, CSCL b14, …
            var dspm = document.DataRecords.SelectMany(r => r.Fields).First(f => f.Tag == "DSPM");
            Array.Clear(dspm.Data, 8, 4);
        }

        var path = Path.Combine(directory, fileName);
        Iso8211DocumentWriter.WriteToFile(path, document);
        return path;
    }

    /// <summary>
    /// Locates <c>PRSP</c> in a binary-implementation DSID field (S-57 Edition 3.1
    /// Part 3 §7.3.1.1): RCNM (b11), RCID (b14), EXPP (b11), INTU (b11); the
    /// variable-length DSNM, EDTN and UPDN, each closed by a unit terminator;
    /// UADT (A(8)), ISDT (A(8)) and STED (R(4)); then PRSP (b11).
    /// </summary>
    private static int ProductSpecificationOffset(byte[] dsid)
    {
        var offset = 1 + 4 + 1 + 1;
        for (var i = 0; i < 3; i++)
            offset = Array.IndexOf(dsid, UnitTerminator, offset) + 1;
        return offset + 8 + 8 + 4;
    }

    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "datasets", "S57", "US5MA1BO", "US5MA1BO.000");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return Path.Combine("tests", "datasets", "S57", "US5MA1BO", "US5MA1BO.000");
    }
}
