using System.Runtime.InteropServices;
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
        var dsid = Writable(document.DataRecords.SelectMany(r => r.Fields).First(f => f.Tag == "DSID"));
        dsid[ProductSpecificationOffset(dsid)] = productSpecification;

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
                && BitConverter.ToUInt16(frid.Data.Span[7..]) == depareObjl)
            .Take(2)
            .Select(r => r.Fields.First(f => f.Tag == "FOID"))
            .ToList();
        // FOID (binary): AGEN b12, FIDN b14, FIDS b12.
        depthAreas[0].Data.Span[..8].CopyTo(Writable(depthAreas[1]));

        if (zeroCompilationScale)
        {
            // DSPM (binary): RCNM b11, RCID b14, HDAT b11, VDAT b11, SDAT b11, CSCL b14, …
            var dspm = Writable(document.DataRecords.SelectMany(r => r.Fields).First(f => f.Tag == "DSPM"));
            dspm.Slice(8, 4).Clear();
        }

        var path = Path.Combine(directory, fileName);
        Iso8211DocumentWriter.WriteToFile(path, document);
        return path;
    }

    /// <summary>
    /// Writes a copy of the NOAA fixture into <paramref name="directory"/> whose
    /// DSPM declares <paramref name="compilationScale"/> as its compilation
    /// scale (CSCL), for tests that rank overlapping cells. Returns the cell's
    /// full path.
    /// </summary>
    public static string WriteWithCompilationScale(string directory, string fileName, uint compilationScale)
    {
        var document = Iso8211DocumentReader.ReadFromFile(FixturePath());
        // DSPM (binary): RCNM b11, RCID b14, HDAT b11, VDAT b11, SDAT b11, CSCL b14, …
        var dspm = Writable(document.DataRecords.SelectMany(r => r.Fields).First(f => f.Tag == "DSPM"));
        BitConverter.TryWriteBytes(dspm.Slice(8, 4), compilationScale);

        var path = Path.Combine(directory, fileName);
        Iso8211DocumentWriter.WriteToFile(path, document);
        return path;
    }

    /// <summary>
    /// Writes a copy of the NOAA fixture into <paramref name="directory"/> in
    /// which the first point, line and area features (soundings and meta
    /// objects aside) are given the object classes 500, 501 and 502, which the
    /// S-57 Object Catalogue doesn't define, as S-64 3.2.1 does. Returns the
    /// cell's full path.
    /// </summary>
    public static string WriteWithUnknownClasses(string directory, string fileName)
    {
        const ushort soundingObjl = 129;
        const ushort firstMetaObjl = 300;
        var document = Iso8211DocumentReader.ReadFromFile(FixturePath());

        // FRID (binary): RCNM b11, RCID b14, PRIM b11, GRUP b11, OBJL b12, …
        for (byte prim = 1; prim <= 3; prim++)
        {
            var frid = Writable(document.DataRecords
                .Select(r => r.Fields.FirstOrDefault(f => f.Tag == "FRID"))
                .First(f => f is not null
                    && f.Data.Span[5] == prim
                    && BitConverter.ToUInt16(f.Data.Span[7..]) is var objl
                    && objl != soundingObjl && objl < firstMetaObjl)!);
            BitConverter.TryWriteBytes(frid[7..], (ushort)(499 + prim));
        }

        var path = Path.Combine(directory, fileName);
        Iso8211DocumentWriter.WriteToFile(path, document);
        return path;
    }

    /// <summary>
    /// Writes a copy of the NOAA fixture into <paramref name="directory"/> whose
    /// wreck (<c>WRECKS</c>, object class 159) is a red stake lateral beacon
    /// (<c>BCNLAT</c>, 7) marking the port side: <c>COLOUR</c> 3,
    /// <c>BCNSHP</c> 1 and <c>CATLAM</c> 1, named "<c>Bn 1</c>". The cell has no
    /// beacon of its own. Its attributes are rewritten in place, so its
    /// <c>OBJNAM</c> is padded with spaces to the wreck's attribute field's
    /// length. Returns the cell's full path.
    /// </summary>
    public static string WriteWithLateralBeacon(string directory, string fileName)
    {
        const ushort wrecks = 159;
        const ushort lateralBeacon = 7;
        const ushort colour = 75, beaconShape = 4, categoryOfLateralMark = 36, objectName = 116;
        const byte fieldTerminator = 0x1E;
        var document = Iso8211DocumentReader.ReadFromFile(FixturePath());

        // FRID (binary): RCNM b11, RCID b14, PRIM b11, GRUP b11, OBJL b12, …
        var record = document.DataRecords.First(r =>
            r.Fields.FirstOrDefault(f => f.Tag == "FRID") is { } frid && BitConverter.ToUInt16(frid.Data.Span[7..]) == wrecks);
        var fridData = Writable(record.Fields.First(f => f.Tag == "FRID"));
        BitConverter.TryWriteBytes(fridData[7..], lateralBeacon);

        // ATTF (binary): repeated ATTL b12 and ATVL, each value closed by a
        // unit terminator, then the field terminator.
        var attf = Writable(record.Fields.First(f => f.Tag == "ATTF"));
        var bytes = new List<byte>();
        void Add(ushort code, string value)
        {
            bytes.AddRange(BitConverter.GetBytes(code));
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(value));
            bytes.Add(UnitTerminator);
        }

        Add(colour, "3");
        Add(beaconShape, "1");
        Add(categoryOfLateralMark, "1");
        // The field's data may or may not hold its terminator.
        var terminated = attf[^1] == fieldTerminator;
        var padding = attf.Length - bytes.Count - 2 - 1 - (terminated ? 1 : 0);
        if (padding < "Bn 1".Length)
            throw new InvalidOperationException($"The wreck's attributes are too short ({attf.Length} bytes) to hold a beacon's.");
        Add(objectName, "Bn 1".PadRight(padding));
        if (terminated)
            bytes.Add(fieldTerminator);
        bytes.ToArray().CopyTo(attf);

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
    private static int ProductSpecificationOffset(Span<byte> dsid)
    {
        var offset = 1 + 4 + 1 + 1;
        for (var i = 0; i < 3; i++)
            offset += dsid[offset..].IndexOf(UnitTerminator) + 1;
        return offset + 8 + 8 + 4;
    }

    // A field's data is a read-only slice of the copy of the file the reader made, so
    // editing it in place changes only this document.
    private static Span<byte> Writable(Iso8211Field field) => MemoryMarshal.AsMemory(field.Data).Span;

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
