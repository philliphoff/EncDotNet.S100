using System.Collections.Immutable;
using EncDotNet.Iso8211;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// Reads an S-101 ISO 8211 encoded file into an <see cref="S101Document"/>.
/// </summary>
internal static class S101DocumentReader
{
    public static S101Document ReadFromFile(string path)
    {
        var iso = Iso8211DocumentReader.ReadFromFile(path);
        return Parse(iso);
    }

    public static S101Document ReadFromStream(Stream stream)
    {
        var iso = Iso8211DocumentReader.Read(stream);
        return Parse(iso);
    }

    private static S101Document Parse(Iso8211Document iso)
    {
        var ddr = iso.DataDescriptiveRecord is not null
            ? Iso8211DataDescriptiveRecordReader.Read(iso.DataDescriptiveRecord)
            : throw new InvalidOperationException("ISO 8211 document has no Data Descriptive Record.");

        S101DatasetIdentification? dsid = null;
        S101DatasetStructureInfo? dssi = null;
        var featureTypeCatalogue = ImmutableDictionary.CreateBuilder<ushort, string>();
        var attributeTypeCatalogue = ImmutableDictionary.CreateBuilder<ushort, string>();
        var informationTypeCatalogue = ImmutableDictionary.CreateBuilder<ushort, string>();
        var informationAssociationCatalogue = ImmutableDictionary.CreateBuilder<ushort, string>();
        var featureAssociationCatalogue = ImmutableDictionary.CreateBuilder<ushort, string>();
        var roleCatalogue = ImmutableDictionary.CreateBuilder<ushort, string>();
        var points = ImmutableDictionary.CreateBuilder<uint, S101PointRecord>();
        var curveSegments = ImmutableDictionary.CreateBuilder<uint, S101CurveSegmentRecord>();
        var compositeCurves = ImmutableDictionary.CreateBuilder<uint, S101CompositeCurveRecord>();
        var surfaces = ImmutableDictionary.CreateBuilder<uint, S101SurfaceRecord>();
        var features = ImmutableArray.CreateBuilder<S101FeatureRecord>();
        var informationTypes = ImmutableDictionary.CreateBuilder<uint, S101InformationRecord>();

        foreach (var record in iso.DataRecords)
        {
            var firstTag = record.Fields.Count > 0 ? record.Fields[0].Tag : "";

            switch (firstTag)
            {
                case "DSID":
                    dsid = ParseDsid(record, ddr);
                    break;

                case "DSSI" when dsid is null:
                    // DSSI is on the same record as DSID; handled within DSID case.
                    break;

                case "PRID":
                    var pt = ParsePoint(record, ddr);
                    if (pt is not null)
                        points[pt.RecordId] = pt;
                    break;

                case "CRID":
                    var cs = ParseCurveSegment(record, ddr);
                    if (cs is not null)
                        curveSegments[cs.RecordId] = cs;
                    break;

                case "CCID":
                    var cc = ParseCompositeCurve(record, ddr);
                    if (cc is not null)
                        compositeCurves[cc.RecordId] = cc;
                    break;

                case "SRID":
                    var sf = ParseSurface(record, ddr);
                    if (sf is not null)
                        surfaces[sf.RecordId] = sf;
                    break;

                case "FRID":
                    var feat = ParseFeature(record, ddr);
                    if (feat is not null)
                        features.Add(feat);
                    break;

                case "IRID":
                    var info = ParseInformationType(record, ddr);
                    if (info is not null)
                        informationTypes[info.RecordId] = info;
                    break;
            }

            // Check for DSSI, FTCS, ATCS on DSID record
            if (firstTag == "DSID")
            {
                dssi = ParseDssi(record, ddr);
                ParseCatalogue(record, ddr, "FTCS", "FTCD", "FTNC", featureTypeCatalogue);
                ParseCatalogue(record, ddr, "ATCS", "ATCD", "ANCD", attributeTypeCatalogue);
                ParseCatalogue(record, ddr, "ITCS", "ITCD", "ITNC", informationTypeCatalogue);
                ParseCatalogue(record, ddr, "IACS", "IACD", "IANC", informationAssociationCatalogue);
                ParseCatalogue(record, ddr, "FACS", "FACD", "FANC", featureAssociationCatalogue);
                ParseCatalogue(record, ddr, "ARCS", "ARCD", "ARNC", roleCatalogue);
            }
        }

        return new S101Document
        {
            Identification = dsid ?? new S101DatasetIdentification(),
            StructureInfo = dssi ?? new S101DatasetStructureInfo(),
            FeatureTypeCatalogue = featureTypeCatalogue.ToImmutable(),
            AttributeTypeCatalogue = attributeTypeCatalogue.ToImmutable(),
            Points = points.ToImmutable(),
            CurveSegments = curveSegments.ToImmutable(),
            CompositeCurves = compositeCurves.ToImmutable(),
            Surfaces = surfaces.ToImmutable(),
            Features = features.ToImmutable(),
            InformationTypes = informationTypes.ToImmutable(),
            InformationTypeCatalogue = informationTypeCatalogue.ToImmutable(),
            InformationAssociationCatalogue = informationAssociationCatalogue.ToImmutable(),
            FeatureAssociationCatalogue = featureAssociationCatalogue.ToImmutable(),
            RoleCatalogue = roleCatalogue.ToImmutable(),
        };
    }

    private static void ParseCatalogue(
        Iso8211Record record,
        Iso8211DataDescriptiveRecord ddr,
        string fieldTag,
        string nameSubfield,
        string codeSubfield,
        ImmutableDictionary<ushort, string>.Builder builder)
    {
        var field = record.GetFieldByTag(fieldTag);
        if (field is null) return;

        var fieldDef = ddr.GetFieldDefinition(fieldTag);
        if (fieldDef is null) return;

        var reader = new Iso8211FieldReader(fieldDef, field.Data);
        foreach (var group in reader.GetSubfieldGroups())
        {
            var name = group.GetSubfield<string>(nameSubfield);
            var codeStr = group.GetSubfield<string>(codeSubfield);
            if (ushort.TryParse(codeStr, out var code))
            {
                builder[code] = name;
            }
        }
    }

    private static S101DatasetIdentification ParseDsid(Iso8211Record record, Iso8211DataDescriptiveRecord ddr)
    {
        var field = record.GetFieldByTag("DSID");
        if (field is null) return new S101DatasetIdentification();

        var fieldDef = ddr.GetFieldDefinition("DSID")!;
        var reader = new Iso8211FieldReader(fieldDef, field.Data);

        reader.TryGetSubfield<byte>("RCNM", out var rcnm);
        reader.TryGetSubfield<uint>("RCID", out var rcid);
        reader.TryGetSubfield<string>("ENSP", out var ensp);
        reader.TryGetSubfield<string>("ENED", out var ened);
        reader.TryGetSubfield<string>("PRSP", out var prsp);
        reader.TryGetSubfield<string>("PRED", out var pred);
        reader.TryGetSubfield<string>("PROF", out var prof);
        reader.TryGetSubfield<string>("DSNM", out var dsnm);
        reader.TryGetSubfield<string>("DSTL", out var dstl);
        reader.TryGetSubfield<string>("DSRD", out var dsrd);
        reader.TryGetSubfield<string>("DSLG", out var dslg);

        return new S101DatasetIdentification
        {
            RecordName = rcnm,
            RecordId = rcid,
            EncodingSpecification = ensp ?? "",
            EncodingSpecificationEdition = ened ?? "",
            ProductSpecification = prsp ?? "",
            ProductSpecificationEdition = pred ?? "",
            ApplicationProfile = prof ?? "",
            DatasetName = dsnm ?? "",
            DatasetTitle = dstl ?? "",
            DatasetReferenceDate = dsrd ?? "",
            DatasetLanguage = dslg ?? "",
        };
    }

    private static S101DatasetStructureInfo ParseDssi(Iso8211Record record, Iso8211DataDescriptiveRecord ddr)
    {
        var field = record.GetFieldByTag("DSSI");
        if (field is null) return new S101DatasetStructureInfo();

        var fieldDef = ddr.GetFieldDefinition("DSSI")!;
        var reader = new Iso8211FieldReader(fieldDef, field.Data);

        reader.TryGetSubfield<uint>("CMFX", out var cmfx);
        reader.TryGetSubfield<uint>("CMFY", out var cmfy);
        reader.TryGetSubfield<uint>("CMFZ", out var cmfz);

        return new S101DatasetStructureInfo
        {
            CoordinateMultiplicationFactorX = cmfx,
            CoordinateMultiplicationFactorY = cmfy,
            CoordinateMultiplicationFactorZ = cmfz,
        };
    }

    private static S101PointRecord? ParsePoint(Iso8211Record record, Iso8211DataDescriptiveRecord ddr)
    {
        var pridField = record.GetFieldByTag("PRID");
        if (pridField is null) return null;

        var pridDef = ddr.GetFieldDefinition("PRID")!;
        var pridReader = new Iso8211FieldReader(pridDef, pridField.Data);
        var rcid = pridReader.GetSubfield<uint>("RCID");

        int y = 0, x = 0;
        var c2it = record.GetFieldByTag("C2IT");
        if (c2it is not null)
        {
            var c2itDef = ddr.GetFieldDefinition("C2IT")!;
            var c2itReader = new Iso8211FieldReader(c2itDef, c2it.Data);
            y = c2itReader.GetSubfield<int>("YCOO");
            x = c2itReader.GetSubfield<int>("XCOO");
        }

        return new S101PointRecord { RecordId = rcid, Y = y, X = x };
    }

    private static S101CurveSegmentRecord? ParseCurveSegment(Iso8211Record record, Iso8211DataDescriptiveRecord ddr)
    {
        var cridField = record.GetFieldByTag("CRID");
        if (cridField is null) return null;

        var cridDef = ddr.GetFieldDefinition("CRID")!;
        var cridReader = new Iso8211FieldReader(cridDef, cridField.Data);
        var rcid = cridReader.GetSubfield<uint>("RCID");

        // PTAS — point topology associations (start/end)
        var ptas = ImmutableArray.CreateBuilder<S101PointAssociation>();
        foreach (var ptasField in record.GetFieldsByTag("PTAS"))
        {
            var ptasDef = ddr.GetFieldDefinition("PTAS")!;
            var ptasReader = new Iso8211FieldReader(ptasDef, ptasField.Data);
            foreach (var group in ptasReader.GetSubfieldGroups())
            {
                var rrnm = group.GetSubfield<byte>("RRNM");
                var rrid = group.GetSubfield<uint>("RRID");
                var topi = group.GetSubfield<byte>("TOPI");
                ptas.Add(new S101PointAssociation(rrnm, rrid, topi));
            }
        }

        // C2IL — intermediate 2D coordinates
        var coords = ImmutableArray.CreateBuilder<(int Y, int X)>();
        foreach (var c2ilField in record.GetFieldsByTag("C2IL"))
        {
            var c2ilDef = ddr.GetFieldDefinition("C2IL")!;
            var c2ilReader = new Iso8211FieldReader(c2ilDef, c2ilField.Data);
            foreach (var group in c2ilReader.GetSubfieldGroups())
            {
                var cy = group.GetSubfield<int>("YCOO");
                var cx = group.GetSubfield<int>("XCOO");
                coords.Add((cy, cx));
            }
        }

        return new S101CurveSegmentRecord
        {
            RecordId = rcid,
            PointAssociations = ptas.ToImmutable(),
            IntermediateCoordinates = coords.ToImmutable(),
        };
    }

    private static S101CompositeCurveRecord? ParseCompositeCurve(Iso8211Record record, Iso8211DataDescriptiveRecord ddr)
    {
        var ccidField = record.GetFieldByTag("CCID");
        if (ccidField is null) return null;

        var ccidDef = ddr.GetFieldDefinition("CCID")!;
        var ccidReader = new Iso8211FieldReader(ccidDef, ccidField.Data);
        var rcid = ccidReader.GetSubfield<uint>("RCID");

        var components = ImmutableArray.CreateBuilder<S101CurveUsage>();
        foreach (var cucoField in record.GetFieldsByTag("CUCO"))
        {
            var cucoDef = ddr.GetFieldDefinition("CUCO")!;
            var cucoReader = new Iso8211FieldReader(cucoDef, cucoField.Data);
            foreach (var group in cucoReader.GetSubfieldGroups())
            {
                var rrnm = group.GetSubfield<byte>("RRNM");
                var rrid = group.GetSubfield<uint>("RRID");
                var ornt = group.GetSubfield<byte>("ORNT");
                components.Add(new S101CurveUsage(rrnm, rrid, ornt));
            }
        }

        return new S101CompositeCurveRecord
        {
            RecordId = rcid,
            CurveComponents = components.ToImmutable(),
        };
    }

    private static S101SurfaceRecord? ParseSurface(Iso8211Record record, Iso8211DataDescriptiveRecord ddr)
    {
        var sridField = record.GetFieldByTag("SRID");
        if (sridField is null) return null;

        var sridDef = ddr.GetFieldDefinition("SRID")!;
        var sridReader = new Iso8211FieldReader(sridDef, sridField.Data);
        var rcid = sridReader.GetSubfield<uint>("RCID");

        var rings = ImmutableArray.CreateBuilder<S101RingAssociation>();
        foreach (var riasField in record.GetFieldsByTag("RIAS"))
        {
            var riasDef = ddr.GetFieldDefinition("RIAS")!;
            var riasReader = new Iso8211FieldReader(riasDef, riasField.Data);
            foreach (var group in riasReader.GetSubfieldGroups())
            {
                var rrnm = group.GetSubfield<byte>("RRNM");
                var rrid = group.GetSubfield<uint>("RRID");
                var ornt = group.GetSubfield<byte>("ORNT");
                var usag = group.GetSubfield<byte>("USAG");
                rings.Add(new S101RingAssociation(rrnm, rrid, ornt, usag));
            }
        }

        return new S101SurfaceRecord
        {
            RecordId = rcid,
            RingAssociations = rings.ToImmutable(),
        };
    }

    private static S101FeatureRecord? ParseFeature(Iso8211Record record, Iso8211DataDescriptiveRecord ddr)
    {
        var fridField = record.GetFieldByTag("FRID");
        if (fridField is null) return null;

        var fridDef = ddr.GetFieldDefinition("FRID")!;
        var fridReader = new Iso8211FieldReader(fridDef, fridField.Data);
        var rcid = fridReader.GetSubfield<uint>("RCID");
        fridReader.TryGetSubfield<ushort>("NFTC", out var nftc);

        // FOID
        ushort agen = 0;
        uint fidn = 0;
        ushort fids = 0;
        var foidField = record.GetFieldByTag("FOID");
        if (foidField is not null)
        {
            var foidDef = ddr.GetFieldDefinition("FOID")!;
            var foidReader = new Iso8211FieldReader(foidDef, foidField.Data);
            agen = foidReader.GetSubfield<ushort>("AGEN");
            fidn = foidReader.GetSubfield<uint>("FIDN");
            fids = foidReader.GetSubfield<ushort>("FIDS");
        }

        // ATTR
        var attributes = ImmutableArray.CreateBuilder<S101Attribute>();
        foreach (var attrField in record.GetFieldsByTag("ATTR"))
        {
            var attrDef = ddr.GetFieldDefinition("ATTR")!;
            var attrReader = new Iso8211FieldReader(attrDef, attrField.Data);
            foreach (var group in attrReader.GetSubfieldGroups())
            {
                var natc = group.GetSubfield<ushort>("NATC");
                var atix = group.GetSubfield<ushort>("ATIX");
                string atvl;
                try { atvl = group.GetSubfield<string>("ATVL"); }
                catch (KeyNotFoundException) { atvl = ""; }
                attributes.Add(new S101Attribute(natc, atix, atvl));
            }
        }

        // SPAS
        var spatials = ImmutableArray.CreateBuilder<S101SpatialAssociation>();
        foreach (var spasField in record.GetFieldsByTag("SPAS"))
        {
            var spasDef = ddr.GetFieldDefinition("SPAS")!;
            var spasReader = new Iso8211FieldReader(spasDef, spasField.Data);
            foreach (var group in spasReader.GetSubfieldGroups())
            {
                var rrnm = group.GetSubfield<byte>("RRNM");
                var rrid = group.GetSubfield<uint>("RRID");
                var ornt = group.GetSubfield<byte>("ORNT");
                spatials.Add(new S101SpatialAssociation(rrnm, rrid, ornt));
            }
        }

        // FACS — Feature associations
        var featureAssociations = ImmutableArray.CreateBuilder<S101FeatureAssociation>();
        foreach (var facsField in record.GetFieldsByTag("FACS"))
        {
            var facsDef = ddr.GetFieldDefinition("FACS");
            if (facsDef is null) break;
            var facsReader = new Iso8211FieldReader(facsDef, facsField.Data);
            foreach (var group in facsReader.GetSubfieldGroups())
            {
                var nfac = group.GetSubfield<ushort>("NFAC");
                var rrid = group.GetSubfield<uint>("RRID");
                ushort narc = 0;
                try { narc = group.GetSubfield<ushort>("NARC"); } catch (KeyNotFoundException) { }
                featureAssociations.Add(new S101FeatureAssociation(nfac, rrid, narc));
            }
        }

        // INAS — Information associations
        var informationAssociations = ImmutableArray.CreateBuilder<S101InformationAssociation>();
        foreach (var inasField in record.GetFieldsByTag("INAS"))
        {
            var inasDef = ddr.GetFieldDefinition("INAS");
            if (inasDef is null) break;
            var inasReader = new Iso8211FieldReader(inasDef, inasField.Data);
            foreach (var group in inasReader.GetSubfieldGroups())
            {
                var niac = group.GetSubfield<ushort>("NIAC");
                var rrid = group.GetSubfield<uint>("RRID");
                ushort narc = 0;
                try { narc = group.GetSubfield<ushort>("NARC"); } catch (KeyNotFoundException) { }
                informationAssociations.Add(new S101InformationAssociation(niac, rrid, narc));
            }
        }

        return new S101FeatureRecord
        {
            RecordId = rcid,
            FeatureTypeCode = nftc,
            ProducingAgency = agen,
            FeatureIdentificationNumber = fidn,
            FeatureIdentificationSubdivision = fids,
            Attributes = attributes.ToImmutable(),
            SpatialAssociations = spatials.ToImmutable(),
            FeatureAssociations = featureAssociations.ToImmutable(),
            InformationAssociations = informationAssociations.ToImmutable(),
        };
    }

    private static S101InformationRecord? ParseInformationType(Iso8211Record record, Iso8211DataDescriptiveRecord ddr)
    {
        var iridField = record.GetFieldByTag("IRID");
        if (iridField is null) return null;

        var iridDef = ddr.GetFieldDefinition("IRID")!;
        var iridReader = new Iso8211FieldReader(iridDef, iridField.Data);
        var rcid = iridReader.GetSubfield<uint>("RCID");
        iridReader.TryGetSubfield<ushort>("NITC", out var nitc);

        var attributes = ImmutableArray.CreateBuilder<S101Attribute>();
        foreach (var attrField in record.GetFieldsByTag("ATTR"))
        {
            var attrDef = ddr.GetFieldDefinition("ATTR")!;
            var attrReader = new Iso8211FieldReader(attrDef, attrField.Data);
            foreach (var group in attrReader.GetSubfieldGroups())
            {
                var natc = group.GetSubfield<ushort>("NATC");
                var atix = group.GetSubfield<ushort>("ATIX");
                string atvl;
                try { atvl = group.GetSubfield<string>("ATVL"); }
                catch (KeyNotFoundException) { atvl = ""; }
                attributes.Add(new S101Attribute(natc, atix, atvl));
            }
        }

        return new S101InformationRecord
        {
            RecordId = rcid,
            InformationTypeCode = nitc,
            Attributes = attributes.ToImmutable(),
        };
    }
}
