using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using EncDotNet.Iso8211;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// Reads an S-57 (Edition 3.1) ENC base cell from an ISO 8211 stream into an
/// <see cref="S57Document"/>. Update files (<c>.001</c>, <c>.002</c>, …) are
/// rejected.
/// </summary>
internal static class S57DocumentReader
{
    // S-57 record names (RCNM) for vector records.
    public const byte RcnmIsolatedNode = 110;
    public const byte RcnmConnectedNode = 120;
    public const byte RcnmEdge = 130;
    public const byte RcnmFace = 140;

    public static S57Document ReadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var iso = Iso8211DocumentReader.ReadFromFile(path);
        return Parse(iso);
    }

    public static S57Document ReadFromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var iso = Iso8211DocumentReader.Read(stream);
        return Parse(iso);
    }

    private static S57Document Parse(Iso8211Document iso)
    {
        var ddr = iso.DataDescriptiveRecord is not null
            ? Iso8211DataDescriptiveRecordReader.Read(iso.DataDescriptiveRecord)
            : throw new InvalidOperationException("ISO 8211 document has no Data Descriptive Record.");

        S57DatasetIdentification? dsid = null;
        S57DatasetParameters? dspm = null;
        var vectorRecords = ImmutableDictionary.CreateBuilder<S57Name, S57VectorRecord>();
        var features = ImmutableArray.CreateBuilder<S57FeatureRecord>();

        foreach (var record in iso.DataRecords)
        {
            // ISO 8211 data records often begin with a `0001` leader field;
            // identify the record type by which S-57 tag is present.
            if (record.GetFieldByTag("DSID") is not null)
            {
                dsid = ParseDsid(record, ddr);
                if (record.GetFieldByTag("DSPM") is not null)
                    dspm = ParseDspm(record, ddr);
            }
            else if (record.GetFieldByTag("DSPM") is not null)
            {
                dspm = ParseDspm(record, ddr);
            }
            else if (record.GetFieldByTag("VRID") is not null)
            {
                var vr = ParseVectorRecord(record, ddr);
                if (vr is not null)
                    vectorRecords[new S57Name(vr.RecordName, vr.RecordId)] = vr;
            }
            else if (record.GetFieldByTag("FRID") is not null)
            {
                var fr = ParseFeatureRecord(record, ddr);
                if (fr is not null)
                    features.Add(fr);
            }
        }

        if (dsid is null)
            throw new InvalidOperationException("S-57 dataset is missing the DSID record.");

        if (!string.IsNullOrEmpty(dsid.UpdateNumber) && dsid.UpdateNumber != "0")
        {
            throw new NotSupportedException(
                $"S-57 update files (UPDN={dsid.UpdateNumber}) are not supported. " +
                "Open the .000 base cell instead; updates must be applied externally.");
        }

        return new S57Document
        {
            Identification = dsid,
            Parameters = dspm ?? new S57DatasetParameters(),
            VectorRecords = vectorRecords.ToImmutable(),
            Features = features.ToImmutable(),
        };
    }

    // ── DSID ────────────────────────────────────────────────────────────

    private static S57DatasetIdentification ParseDsid(Iso8211Record record, Iso8211DataDescriptiveRecord ddr)
    {
        var field = record.GetFieldByTag("DSID")
            ?? throw new InvalidOperationException("DSID field missing from DSID record.");
        var def = ddr.GetFieldDefinition("DSID")!;
        var reader = new Iso8211FieldReader(def, field.Data);

        reader.TryGetSubfield<byte>("EXPP", out var expp);
        reader.TryGetSubfield<byte>("INTU", out var intu);
        reader.TryGetSubfield<string>("DSNM", out var dsnm);
        reader.TryGetSubfield<string>("EDTN", out var edtn);
        reader.TryGetSubfield<string>("UPDN", out var updn);
        reader.TryGetSubfield<string>("ISDT", out var isdt);
        reader.TryGetSubfield<string>("STED", out var sted);
        reader.TryGetSubfield<string>("PRSP", out var prsp);
        reader.TryGetSubfield<ushort>("AGEN", out var agen);

        return new S57DatasetIdentification
        {
            ExchangePurpose = expp,
            IntendedUsage = intu,
            DatasetName = dsnm ?? "",
            Edition = edtn ?? "",
            UpdateNumber = updn ?? "",
            IssueDate = isdt ?? "",
            StandardEdition = sted ?? "",
            ProductSpecification = prsp ?? "",
            ProducingAgency = agen,
        };
    }

    // ── DSPM ────────────────────────────────────────────────────────────

    private static S57DatasetParameters ParseDspm(Iso8211Record record, Iso8211DataDescriptiveRecord ddr)
    {
        var field = record.GetFieldByTag("DSPM")
            ?? throw new InvalidOperationException("DSPM field missing from DSPM record.");
        var def = ddr.GetFieldDefinition("DSPM")!;
        var reader = new Iso8211FieldReader(def, field.Data);

        reader.TryGetSubfield<byte>("HDAT", out var hdat);
        reader.TryGetSubfield<byte>("VDAT", out var vdat);
        reader.TryGetSubfield<byte>("SDAT", out var sdat);
        reader.TryGetSubfield<uint>("CSCL", out var cscl);
        reader.TryGetSubfield<uint>("COMF", out var comf);
        reader.TryGetSubfield<uint>("SOMF", out var somf);

        return new S57DatasetParameters
        {
            HorizontalDatum = hdat,
            VerticalDatum = vdat,
            SoundingDatum = sdat,
            CompilationScale = cscl,
            CoordinateMultiplicationFactor = comf,
            SoundingMultiplicationFactor = somf,
        };
    }

    // ── Vector records (VRID + ATTV + VRPT + SG2D/SG3D) ─────────────────

    private static S57VectorRecord? ParseVectorRecord(Iso8211Record record, Iso8211DataDescriptiveRecord ddr)
    {
        var vridField = record.GetFieldByTag("VRID");
        if (vridField is null) return null;
        var vridDef = ddr.GetFieldDefinition("VRID")!;
        var vridReader = new Iso8211FieldReader(vridDef, vridField.Data);
        var rcnm = vridReader.GetSubfield<byte>("RCNM");
        var rcid = vridReader.GetSubfield<uint>("RCID");

        // VRPT — Vector Record Pointers (links to other vectors)
        var pointers = ImmutableArray.CreateBuilder<S57VectorPointer>();
        foreach (var vrptField in record.GetFieldsByTag("VRPT"))
        {
            var vrptDef = ddr.GetFieldDefinition("VRPT");
            if (vrptDef is null) break;
            var vrptReader = new Iso8211FieldReader(vrptDef, vrptField.Data);
            foreach (var group in vrptReader.GetSubfieldGroups())
            {
                if (!TryReadName(group, out var pname)) continue;
                var ornt = TryGetByte(group, "ORNT");
                var usag = TryGetByte(group, "USAG");
                var topi = TryGetByte(group, "TOPI");
                var mask = TryGetByte(group, "MASK");
                pointers.Add(new S57VectorPointer(pname.RecordName, pname.RecordId, ornt, usag, topi, mask));
            }
        }

        // SG2D — 2D coordinates
        var coords2d = ImmutableArray.CreateBuilder<(int Y, int X)>();
        foreach (var sg2dField in record.GetFieldsByTag("SG2D"))
        {
            var def = ddr.GetFieldDefinition("SG2D");
            if (def is null) break;
            var rdr = new Iso8211FieldReader(def, sg2dField.Data);
            foreach (var group in rdr.GetSubfieldGroups())
            {
                var y = group.GetSubfield<int>("YCOO");
                var x = group.GetSubfield<int>("XCOO");
                coords2d.Add((y, x));
            }
        }

        // SG3D — 3D coordinates (soundings)
        var coords3d = ImmutableArray.CreateBuilder<(int Y, int X, int Z)>();
        foreach (var sg3dField in record.GetFieldsByTag("SG3D"))
        {
            var def = ddr.GetFieldDefinition("SG3D");
            if (def is null) break;
            var rdr = new Iso8211FieldReader(def, sg3dField.Data);
            foreach (var group in rdr.GetSubfieldGroups())
            {
                var y = group.GetSubfield<int>("YCOO");
                var x = group.GetSubfield<int>("XCOO");
                var z = group.GetSubfield<int>("VE3D");
                coords3d.Add((y, x, z));
            }
        }

        // ATTV — Attributes on vector records (rare).
        var attrs = ImmutableArray.CreateBuilder<S57Attribute>();
        foreach (var attvField in record.GetFieldsByTag("ATTV"))
        {
            var def = ddr.GetFieldDefinition("ATTV");
            if (def is null) break;
            var rdr = new Iso8211FieldReader(def, attvField.Data);
            foreach (var group in rdr.GetSubfieldGroups())
            {
                var attl = group.GetSubfield<ushort>("ATTL");
                string atvl;
                try { atvl = group.GetSubfield<string>("ATVL"); }
                catch (KeyNotFoundException) { atvl = ""; }
                attrs.Add(new S57Attribute(attl, atvl));
            }
        }

        return new S57VectorRecord
        {
            RecordName = rcnm,
            RecordId = rcid,
            Pointers = pointers.ToImmutable(),
            Coordinates2D = coords2d.ToImmutable(),
            Coordinates3D = coords3d.ToImmutable(),
            Attributes = attrs.ToImmutable(),
        };
    }

    // ── Feature records (FRID + FOID + ATTF + FSPT) ─────────────────────

    private static S57FeatureRecord? ParseFeatureRecord(Iso8211Record record, Iso8211DataDescriptiveRecord ddr)
    {
        var fridField = record.GetFieldByTag("FRID");
        if (fridField is null) return null;
        var fridDef = ddr.GetFieldDefinition("FRID")!;
        var fridReader = new Iso8211FieldReader(fridDef, fridField.Data);
        var rcid = fridReader.GetSubfield<uint>("RCID");
        fridReader.TryGetSubfield<byte>("PRIM", out var prim);
        fridReader.TryGetSubfield<byte>("GRUP", out var grup);
        fridReader.TryGetSubfield<ushort>("OBJL", out var objl);

        // FOID
        ushort agen = 0;
        uint fidn = 0;
        ushort fids = 0;
        var foidField = record.GetFieldByTag("FOID");
        if (foidField is not null)
        {
            var foidDef = ddr.GetFieldDefinition("FOID")!;
            var foidReader = new Iso8211FieldReader(foidDef, foidField.Data);
            foidReader.TryGetSubfield<ushort>("AGEN", out agen);
            foidReader.TryGetSubfield<uint>("FIDN", out fidn);
            foidReader.TryGetSubfield<ushort>("FIDS", out fids);
        }

        // ATTF
        var attrs = ImmutableArray.CreateBuilder<S57Attribute>();
        foreach (var attfField in record.GetFieldsByTag("ATTF"))
        {
            var def = ddr.GetFieldDefinition("ATTF");
            if (def is null) break;
            var rdr = new Iso8211FieldReader(def, attfField.Data);
            foreach (var group in rdr.GetSubfieldGroups())
            {
                var attl = group.GetSubfield<ushort>("ATTL");
                string atvl;
                try { atvl = group.GetSubfield<string>("ATVL"); }
                catch (KeyNotFoundException) { atvl = ""; }
                attrs.Add(new S57Attribute(attl, atvl));
            }
        }

        // FSPT
        var spatials = ImmutableArray.CreateBuilder<S57FeatureSpatialPointer>();
        foreach (var fsptField in record.GetFieldsByTag("FSPT"))
        {
            var def = ddr.GetFieldDefinition("FSPT");
            if (def is null) break;
            var rdr = new Iso8211FieldReader(def, fsptField.Data);
            foreach (var group in rdr.GetSubfieldGroups())
            {
                if (!TryReadName(group, out var pname)) continue;
                var ornt = TryGetByte(group, "ORNT");
                var usag = TryGetByte(group, "USAG");
                var mask = TryGetByte(group, "MASK");
                spatials.Add(new S57FeatureSpatialPointer(pname.RecordName, pname.RecordId, ornt, usag, mask));
            }
        }

        return new S57FeatureRecord
        {
            RecordId = rcid,
            Primitive = prim,
            Group = grup,
            ObjectClass = objl,
            ProducingAgency = agen,
            FeatureIdentificationNumber = fidn,
            FeatureIdentificationSubdivision = fids,
            Attributes = attrs.ToImmutable(),
            SpatialPointers = spatials.ToImmutable(),
        };
    }

    // ── NAME helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Reads the S-57 NAME composite (5 bytes binary little-endian: RCNM | RCID).
    /// Falls back to separate <c>RCNM</c>/<c>RCID</c> subfields if the producer
    /// chose to split them in the DDR.
    /// </summary>
    private static bool TryReadName(Iso8211SubfieldGroup group, out S57Name name)
    {
        try
        {
            var bytes = group.GetSubfieldBytes("NAME");
            if (bytes.Length >= 5)
            {
                var rcnm = bytes[0];
                var rcid = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(1, 4));
                name = new S57Name(rcnm, rcid);
                return true;
            }
        }
        catch (KeyNotFoundException)
        {
            // Fall through to split subfields.
        }

        try
        {
            var rcnm = group.GetSubfield<byte>("RCNM");
            var rcid = group.GetSubfield<uint>("RCID");
            name = new S57Name(rcnm, rcid);
            return true;
        }
        catch (KeyNotFoundException)
        {
            name = default;
            return false;
        }
    }

    private static byte TryGetByte(Iso8211SubfieldGroup group, string subfield)
    {
        try { return group.GetSubfield<byte>(subfield); }
        catch (KeyNotFoundException) { return 0; }
    }
}
