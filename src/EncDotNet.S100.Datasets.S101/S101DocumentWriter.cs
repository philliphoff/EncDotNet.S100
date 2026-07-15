using EncDotNet.Iso8211;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// Serializes an <see cref="S101Document"/> to an S-101 ISO/IEC 8211 encoded
/// dataset (a <c>.000</c> cell), the symmetric inverse of
/// <see cref="S101DocumentReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// The writer emits a Data Descriptive Record (DDR) describing every field it
/// uses, followed by one data record per dataset / spatial / feature /
/// information object, using the field and subfield structure defined by S-101
/// (S-100 Part 10a). Field tags, subfield names, and binary formats mirror the
/// canonical S-101 encoding so the output round-trips through
/// <see cref="S101DocumentReader"/>.
/// </para>
/// <para>
/// This is the encoder consumed by the S-57 → S-101 conversion pipeline: an
/// <see cref="S101Document"/> produced by the translator is serialized here to a
/// standalone base cell (application profile <c>1</c>). Feature-to-feature
/// associations (<c>FASC</c>) are serialized when present, although the S-57
/// translator does not currently produce any.
/// </para>
/// </remarks>
public static class S101DocumentWriter
{
    // Record names (RCNM) per S-101 / S-100 Part 10a.
    private const byte RcnmDataset = 10;
    private const byte RcnmFeature = 100;
    private const byte RcnmPoint = 110;
    private const byte RcnmMultiPoint = 115;
    private const byte RcnmCurve = 120;
    private const byte RcnmCompositeCurve = 125;
    private const byte RcnmSurface = 130;
    private const byte RcnmInformation = 150;

    private static readonly Iso8211WriterOptions Options = Iso8211WriterOptions.Default;
    /// <summary>
    /// Serializes the supplied document to an in-memory ISO 8211 byte buffer.
    /// </summary>
    /// <param name="document">The document to serialize.</param>
    /// <returns>The encoded ISO 8211 dataset bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public static byte[] Write(S101Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Iso8211DocumentWriter.Write(BuildDocument(document), Options);
    }

    /// <summary>
    /// Serializes the supplied document to a stream.
    /// </summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="document">The document to serialize.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static void Write(Stream stream, S101Document document)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(document);
        Iso8211DocumentWriter.Write(stream, BuildDocument(document), Options);
    }

    /// <summary>
    /// Asynchronously serializes the supplied document to a stream.
    /// </summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="document">The document to serialize.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the document has been written.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static Task WriteAsync(Stream stream, S101Document document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(document);
        return Iso8211DocumentWriter.WriteAsync(stream, BuildDocument(document), Options, cancellationToken);
    }

    /// <summary>
    /// Serializes the supplied document to a file, overwriting any existing file.
    /// </summary>
    /// <param name="path">The destination file path.</param>
    /// <param name="document">The document to serialize.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public static void WriteToFile(string path, S101Document document)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(document);
        Iso8211DocumentWriter.WriteToFile(path, BuildDocument(document), Options);
    }

    /// <summary>
    /// Asynchronously serializes the supplied document to a file, overwriting any existing file.
    /// </summary>
    /// <param name="path">The destination file path.</param>
    /// <param name="document">The document to serialize.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the file has been written.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public static Task WriteToFileAsync(string path, S101Document document, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(document);
        return Iso8211DocumentWriter.WriteToFileAsync(path, BuildDocument(document), Options, cancellationToken);
    }

    private static Iso8211Document BuildDocument(S101Document doc)
    {
        var builder = new Iso8211DocumentBuilder();
        var fieldDefs = BuildFieldDefinitions();

        // The DDR carries the full canonical field definition set, including definitions not emitted in this document.
        builder.AddRecord(Iso8211DataDescriptiveRecordWriter.BuildDdr(fieldDefs.Values, options: Options));

        builder.AddRecord(BuildDatasetRecord(doc, fieldDefs));

        foreach (var p in doc.Points.Values)
            builder.AddRecord(BuildPointRecord(p, fieldDefs));
        foreach (var m in doc.MultiPoints.Values)
            builder.AddRecord(BuildMultiPointRecord(m, fieldDefs));
        foreach (var c in doc.CurveSegments.Values)
            builder.AddRecord(BuildCurveRecord(c, fieldDefs));
        foreach (var cc in doc.CompositeCurves.Values)
            builder.AddRecord(BuildCompositeCurveRecord(cc, fieldDefs));
        foreach (var s in doc.Surfaces.Values)
            builder.AddRecord(BuildSurfaceRecord(s, fieldDefs));
        foreach (var info in doc.InformationTypes.Values)
            builder.AddRecord(BuildInformationRecord(info, fieldDefs));
        foreach (var f in doc.Features)
            builder.AddRecord(BuildFeatureRecord(f, fieldDefs));

        return builder.Build();
    }

    // ── Record builders ─────────────────────────────────────────────────

    private static Iso8211Record BuildDatasetRecord(
        S101Document doc,
        IReadOnlyDictionary<string, Iso8211FieldDefinition> fieldDefs)
    {
        var id = doc.Identification;
        var si = doc.StructureInfo;

        var dsid = Field("DSID", fieldDefs)
            .AddSubfields(
                (int)(id.RecordName == 0 ? RcnmDataset : id.RecordName),
                id.RecordId,
                id.EncodingSpecification,
                id.EncodingSpecificationEdition,
                id.ProductSpecification,
                id.ProductSpecificationEdition,
                id.ApplicationProfile,
                id.DatasetName,
                id.DatasetTitle,
                id.DatasetReferenceDate,
                id.DatasetLanguage,
                id.DatasetAbstract,
                id.DatasetEdition,
                0 /* DSTC */);

        var dssi = Field("DSSI", fieldDefs)
            .AddSubfields(
                0d, 0d, 0d, // DCOX/DCOY/DCOZ
                si.CoordinateMultiplicationFactorX,
                si.CoordinateMultiplicationFactorY,
                si.CoordinateMultiplicationFactorZ,
                doc.InformationTypes.Count, // NOIR
                doc.Points.Count,           // NOPN
                doc.MultiPoints.Count,      // NOMN
                doc.CurveSegments.Count,    // NOCN
                doc.CompositeCurves.Count,  // NOXN
                doc.Surfaces.Count,         // NOSN
                doc.Features.Count);        // NOFR

        var record = new Iso8211RecordBuilder(Options)
            .AddField(dsid)
            .AddField(dssi);

        AddCatalogue(record, "FTCS", doc.FeatureTypeCatalogue, fieldDefs);
        AddCatalogue(record, "ATCS", doc.AttributeTypeCatalogue, fieldDefs);
        AddCatalogue(record, "ITCS", doc.InformationTypeCatalogue, fieldDefs);
        AddCatalogue(record, "IACS", doc.InformationAssociationCatalogue, fieldDefs);
        AddCatalogue(record, "FACS", doc.FeatureAssociationCatalogue, fieldDefs);
        AddCatalogue(record, "ARCS", doc.RoleCatalogue, fieldDefs);

        return record.Build();
    }

    private static void AddCatalogue(
        Iso8211RecordBuilder record,
        string tag,
        IReadOnlyDictionary<ushort, string> entries,
        IReadOnlyDictionary<string, Iso8211FieldDefinition> fieldDefs)
    {
        if (entries.Count == 0)
            return;

        var field = Field(tag, fieldDefs);
        foreach (var (code, acronym) in entries)
            field.AddSubfields(acronym, (int)code);
        record.AddField(field);
    }

    private static Iso8211Record BuildPointRecord(S101PointRecord p, IReadOnlyDictionary<string, Iso8211FieldDefinition> fieldDefs)
    {
        var prid = Field("PRID", fieldDefs).AddSubfields(
            (int)RcnmPoint, p.RecordId, (int)p.RecordVersion, (int)p.UpdateInstruction);
        var c2it = Field("C2IT", fieldDefs).AddSubfields(p.Y, p.X);
        return new Iso8211RecordBuilder(Options).AddField(prid).AddField(c2it).Build();
    }

    private static Iso8211Record BuildMultiPointRecord(S101MultiPointRecord m, IReadOnlyDictionary<string, Iso8211FieldDefinition> fieldDefs)
    {
        var mrid = Field("MRID", fieldDefs).AddSubfields(
            (int)RcnmMultiPoint, m.RecordId, (int)m.RecordVersion, (int)m.UpdateInstruction);
        var c3il = Field("C3IL", fieldDefs);
        // Leading VCID (b11) followed by repeating Y/X/Z triples (rep@1).
        c3il.AddSubfield(0);
        foreach (var (y, x, z) in m.Points)
            c3il.AddSubfields(y, x, z);

        return new Iso8211RecordBuilder(Options).AddField(mrid).AddField(c3il).Build();
    }

    private static Iso8211Record BuildCurveRecord(S101CurveSegmentRecord c, IReadOnlyDictionary<string, Iso8211FieldDefinition> fieldDefs)
    {
        var record = new Iso8211RecordBuilder(Options)
            .AddField(Field("CRID", fieldDefs).AddSubfields(
                (int)RcnmCurve, c.RecordId, (int)c.RecordVersion, (int)c.UpdateInstruction));

        if (c.PointAssociations.Count > 0)
        {
            var ptas = Field("PTAS", fieldDefs);
            foreach (var a in c.PointAssociations)
                ptas.AddSubfields((int)a.RecordName, a.RecordId, (int)a.Topology);
            record.AddField(ptas);
        }

        if (c.IntermediateCoordinates.Count > 0)
        {
            var c2il = Field("C2IL", fieldDefs);
            foreach (var (y, x) in c.IntermediateCoordinates)
                c2il.AddSubfields(y, x);
            record.AddField(c2il);
        }

        return record.Build();
    }

    private static Iso8211Record BuildCompositeCurveRecord(S101CompositeCurveRecord cc, IReadOnlyDictionary<string, Iso8211FieldDefinition> fieldDefs)
    {
        var record = new Iso8211RecordBuilder(Options)
            .AddField(Field("CCID", fieldDefs).AddSubfields(
                (int)RcnmCompositeCurve, cc.RecordId, (int)cc.RecordVersion, (int)cc.UpdateInstruction));

        if (cc.CurveComponents.Count > 0)
        {
            var cuco = Field("CUCO", fieldDefs);
            foreach (var u in cc.CurveComponents)
                cuco.AddSubfields((int)u.RecordName, u.RecordId, (int)u.Orientation);
            record.AddField(cuco);
        }

        return record.Build();
    }

    private static Iso8211Record BuildSurfaceRecord(S101SurfaceRecord s, IReadOnlyDictionary<string, Iso8211FieldDefinition> fieldDefs)
    {
        var record = new Iso8211RecordBuilder(Options)
            .AddField(Field("SRID", fieldDefs).AddSubfields(
                (int)RcnmSurface, s.RecordId, (int)s.RecordVersion, (int)s.UpdateInstruction));

        if (s.RingAssociations.Count > 0)
        {
            var rias = Field("RIAS", fieldDefs);
            foreach (var r in s.RingAssociations)
                rias.AddSubfields((int)r.RecordName, r.RecordId, (int)r.Orientation, (int)r.Usage, 0 /* RAUI */);
            record.AddField(rias);
        }

        return record.Build();
    }

    private static Iso8211Record BuildFeatureRecord(S101FeatureRecord f, IReadOnlyDictionary<string, Iso8211FieldDefinition> fieldDefs)
    {
        var record = new Iso8211RecordBuilder(Options)
            .AddField(Field("FRID", fieldDefs).AddSubfields(
                (int)RcnmFeature, f.RecordId, (int)f.FeatureTypeCode, (int)f.RecordVersion, (int)f.UpdateInstruction))
            .AddField(Field("FOID", fieldDefs).AddSubfields(
                (int)f.ProducingAgency, f.FeatureIdentificationNumber, (int)f.FeatureIdentificationSubdivision));

        AddAttributes(record, f.Attributes, fieldDefs);

        if (f.SpatialAssociations.Count > 0)
        {
            var spas = Field("SPAS", fieldDefs);
            foreach (var a in f.SpatialAssociations)
                spas.AddSubfields((int)a.RecordName, a.RecordId, (int)a.Orientation, 0 /* SMIN */, 0 /* SMAX */, (int)a.UpdateInstruction);
            record.AddField(spas);
        }

        if (f.FeatureAssociations.Count > 0)
        {
            foreach (var a in f.FeatureAssociations)
            {
                record.AddField(Field("FASC", fieldDefs).AddSubfields(
                    (int)RcnmFeature, a.RecordId, (int)a.NumericCode, (int)a.RoleCode, (int)a.UpdateInstruction));
            }
        }

        if (f.InformationAssociations.Count > 0)
        {
            foreach (var a in f.InformationAssociations)
            {
                record.AddField(Field("INAS", fieldDefs).AddSubfields(
                    (int)RcnmInformation, a.RecordId, (int)a.NumericCode, (int)a.RoleCode, (int)a.UpdateInstruction));
            }
        }

        return record.Build();
    }

    private static Iso8211Record BuildInformationRecord(S101InformationRecord info, IReadOnlyDictionary<string, Iso8211FieldDefinition> fieldDefs)
    {
        var record = new Iso8211RecordBuilder(Options)
            .AddField(Field("IRID", fieldDefs).AddSubfields(
                (int)RcnmInformation, info.RecordId, (int)info.InformationTypeCode, (int)info.RecordVersion, (int)info.UpdateInstruction));

        AddAttributes(record, info.Attributes, fieldDefs);
        return record.Build();
    }

    private static void AddAttributes(
        Iso8211RecordBuilder record,
        IReadOnlyList<S101Attribute> attributes,
        IReadOnlyDictionary<string, Iso8211FieldDefinition> fieldDefs)
    {
        if (attributes.Count == 0)
            return;

        var attr = Field("ATTR", fieldDefs);
        foreach (var a in attributes)
            attr.AddSubfields((int)a.NumericCode, (int)a.Index, (int)a.ParentIndex, (int)a.UpdateInstruction, a.Value);
        record.AddField(attr);
    }

    private static Iso8211FieldBuilder Field(string tag, IReadOnlyDictionary<string, Iso8211FieldDefinition> fieldDefs) => new(fieldDefs[tag], Options);

    // ── Field definitions (the DDR) ─────────────────────────────────────

    private static Iso8211SubfieldFormat U1 => new() { FormatType = Iso8211SubfieldFormatType.UnsignedInteger, Width = 1 };
    private static Iso8211SubfieldFormat U2 => new() { FormatType = Iso8211SubfieldFormatType.UnsignedInteger, Width = 2 };
    private static Iso8211SubfieldFormat U4 => new() { FormatType = Iso8211SubfieldFormatType.UnsignedInteger, Width = 4 };
    private static Iso8211SubfieldFormat S4 => new() { FormatType = Iso8211SubfieldFormatType.SignedInteger, Width = 4 };
    private static Iso8211SubfieldFormat F8 => new() { FormatType = Iso8211SubfieldFormatType.FloatingPoint, Width = 8 };
    private static Iso8211SubfieldFormat A => new() { FormatType = Iso8211SubfieldFormatType.CharacterData, Width = 0 };

    private static IReadOnlyDictionary<string, Iso8211FieldDefinition> BuildFieldDefinitions()
    {
        var defs = new List<Iso8211FieldDefinition>
        {
            Def("DSID", "Data Set Identification", Iso8211DataStructureCode.ConcatenatedArray, Iso8211DataTypeCode.MixedDataTypes, -1,
                ("RCNM", U1), ("RCID", U4), ("ENSP", A), ("ENED", A), ("PRSP", A), ("PRED", A), ("PROF", A),
                ("DSNM", A), ("DSTL", A), ("DSRD", A), ("DSLG", A), ("DSAB", A), ("DSED", A), ("DSTC", U1)),
            Def("DSSI", "Data Set Structure Information", Iso8211DataStructureCode.Vector, Iso8211DataTypeCode.MixedDataTypes, -1,
                ("DCOX", F8), ("DCOY", F8), ("DCOZ", F8), ("CMFX", U4), ("CMFY", U4), ("CMFZ", U4),
                ("NOIR", U4), ("NOPN", U4), ("NOMN", U4), ("NOCN", U4), ("NOXN", U4), ("NOSN", U4), ("NOFR", U4)),

            Def("FTCS", "Feature Type Codes", Iso8211DataStructureCode.Array, Iso8211DataTypeCode.MixedDataTypes, 0, ("FTCD", A), ("FTNC", U2)),
            Def("ATCS", "Attribute Codes", Iso8211DataStructureCode.Array, Iso8211DataTypeCode.MixedDataTypes, 0, ("ATCD", A), ("ANCD", U2)),
            Def("ITCS", "Information Type Codes", Iso8211DataStructureCode.Array, Iso8211DataTypeCode.MixedDataTypes, 0, ("ITCD", A), ("ITNC", U2)),
            Def("IACS", "Information Association Codes", Iso8211DataStructureCode.Array, Iso8211DataTypeCode.MixedDataTypes, 0, ("IACD", A), ("IANC", U2)),
            Def("FACS", "Feature Association Codes", Iso8211DataStructureCode.Array, Iso8211DataTypeCode.MixedDataTypes, 0, ("FACD", A), ("FANC", U2)),
            Def("FASC", "Feature Association", Iso8211DataStructureCode.Vector, Iso8211DataTypeCode.MixedDataTypes, -1,
                ("RRNM", U1), ("RRID", U4), ("NFAC", U2), ("NARC", U2), ("FAUI", U1)),
            Def("ARCS", "Association Role Codes", Iso8211DataStructureCode.Array, Iso8211DataTypeCode.MixedDataTypes, 0, ("ARCD", A), ("ARNC", U2)),

            Def("PRID", "Point Record Identifier", Iso8211DataStructureCode.Vector, Iso8211DataTypeCode.ImplicitPoint, -1,
                ("RCNM", U1), ("RCID", U4), ("RVER", U2), ("RUIN", U1)),
            Def("C2IT", "2-D Integer Coordinate Tuple", Iso8211DataStructureCode.Vector, Iso8211DataTypeCode.ImplicitPoint, -1,
                ("YCOO", S4), ("XCOO", S4)),

            Def("MRID", "Multi Point Record Identifier", Iso8211DataStructureCode.Vector, Iso8211DataTypeCode.ImplicitPoint, -1,
                ("RCNM", U1), ("RCID", U4), ("RVER", U2), ("RUIN", U1)),
            Def("C3IL", "3-D Integer Coordinate List", Iso8211DataStructureCode.ConcatenatedArray, Iso8211DataTypeCode.ImplicitPoint, 1,
                ("VCID", U1), ("YCOO", S4), ("XCOO", S4), ("ZCOO", S4)),

            Def("CRID", "Curve Record Identifier", Iso8211DataStructureCode.Vector, Iso8211DataTypeCode.ImplicitPoint, -1,
                ("RCNM", U1), ("RCID", U4), ("RVER", U2), ("RUIN", U1)),
            Def("PTAS", "Point Association", Iso8211DataStructureCode.Array, Iso8211DataTypeCode.ImplicitPoint, 0,
                ("RRNM", U1), ("RRID", U4), ("TOPI", U1)),
            Def("C2IL", "2-D Integer Coordinate List", Iso8211DataStructureCode.Array, Iso8211DataTypeCode.ImplicitPoint, 0,
                ("YCOO", S4), ("XCOO", S4)),

            Def("CCID", "Composite Curve Record Identifier", Iso8211DataStructureCode.Vector, Iso8211DataTypeCode.ImplicitPoint, -1,
                ("RCNM", U1), ("RCID", U4), ("RVER", U2), ("RUIN", U1)),
            Def("CUCO", "Curve Component", Iso8211DataStructureCode.Array, Iso8211DataTypeCode.ImplicitPoint, 0,
                ("RRNM", U1), ("RRID", U4), ("ORNT", U1)),

            Def("SRID", "Surface Record Identifier", Iso8211DataStructureCode.Vector, Iso8211DataTypeCode.ImplicitPoint, -1,
                ("RCNM", U1), ("RCID", U4), ("RVER", U2), ("RUIN", U1)),
            Def("RIAS", "Ring Association", Iso8211DataStructureCode.Array, Iso8211DataTypeCode.ImplicitPoint, 0,
                ("RRNM", U1), ("RRID", U4), ("ORNT", U1), ("USAG", U1), ("RAUI", U1)),

            Def("FRID", "Feature Type Record Identifier", Iso8211DataStructureCode.Vector, Iso8211DataTypeCode.ImplicitPoint, -1,
                ("RCNM", U1), ("RCID", U4), ("NFTC", U2), ("RVER", U2), ("RUIN", U1)),
            Def("FOID", "Feature Object Identifier", Iso8211DataStructureCode.Vector, Iso8211DataTypeCode.ImplicitPoint, -1,
                ("AGEN", U2), ("FIDN", U4), ("FIDS", U2)),
            Def("ATTR", "Attribute", Iso8211DataStructureCode.Array, Iso8211DataTypeCode.MixedDataTypes, 0,
                ("NATC", U2), ("ATIX", U2), ("PAIX", U2), ("ATIN", U1), ("ATVL", A)),
            Def("SPAS", "Spatial Association", Iso8211DataStructureCode.Array, Iso8211DataTypeCode.ImplicitPoint, 0,
                ("RRNM", U1), ("RRID", U4), ("ORNT", U1), ("SMIN", U4), ("SMAX", U4), ("SAUI", U1)),
            Def("INAS", "Information Association", Iso8211DataStructureCode.Vector, Iso8211DataTypeCode.MixedDataTypes, -1,
                ("RRNM", U1), ("RRID", U4), ("NIAC", U2), ("NARC", U2), ("IUIN", U1)),

            Def("IRID", "Information Type Record Identifier", Iso8211DataStructureCode.Vector, Iso8211DataTypeCode.ImplicitPoint, -1,
                ("RCNM", U1), ("RCID", U4), ("NITC", U2), ("RVER", U2), ("RUIN", U1)),
        };

        return defs.ToDictionary(d => d.Tag, d => d);
    }

    private static Iso8211FieldDefinition Def(
        string tag,
        string name,
        Iso8211DataStructureCode structureCode,
        Iso8211DataTypeCode typeCode,
        int repeatingStartIndex,
        params (string Name, Iso8211SubfieldFormat Format)[] subfields)
    {
        var subfieldDefs = new List<Iso8211SubfieldDefinition>(subfields.Length);
        for (int i = 0; i < subfields.Length; i++)
        {
            subfieldDefs.Add(new Iso8211SubfieldDefinition
            {
                Name = subfields[i].Name,
                Format = subfields[i].Format,
                Index = i,
                IsRepeating = repeatingStartIndex >= 0 && i >= repeatingStartIndex,
            });
        }

        var formatControls = "(" + string.Join(",", subfields.Select(s => s.Format.ToString())) + ")";

        return new Iso8211FieldDefinition
        {
            Tag = tag,
            FieldName = name,
            DataStructureCode = structureCode,
            DataTypeCode = typeCode,
            RepeatingSubfieldStartIndex = repeatingStartIndex,
            SubfieldDefinitions = subfieldDefs,
            FormatControls = formatControls,
        };
    }
}
