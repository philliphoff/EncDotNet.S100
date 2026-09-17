using EncDotNet.Iso8211;

namespace EncDotNet.S100.Pipelines.Tests;

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
