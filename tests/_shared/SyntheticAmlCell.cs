using EncDotNet.S57;

namespace EncDotNet.S100.TestSupport;

/// <summary>
/// Writes small synthetic NATO Additional Military Layers (AML) cells with the
/// upstream <see cref="S57DocumentWriter"/>, so tests never need the
/// (uncommittable) public AML samples. Each cell has an <c>M_COVR</c> coverage
/// square, one point feature of the product's first AML-only class, and
/// optionally an <c>m_clas</c> security meta feature.
/// </summary>
internal static class SyntheticAmlCell
{
    /// <summary>The coverage square: 50.0–50.1° N, 1.0–1.1° W.</summary>
    public const double South = 50.0, North = 50.1, West = -1.1, East = -1.0;

    /// <summary>The point feature's position, inside the coverage.</summary>
    public const double PointLatitude = 50.05, PointLongitude = -1.05;

    /// <summary>The point feature's record id.</summary>
    public const int PointRecordId = 1;

    /// <summary>The <c>M_COVR</c> feature's record id.</summary>
    public const int CoverageRecordId = 2;

    private const int Comf = 10_000_000;
    private const int MetaCoverage = 302;
    private const int CategoryOfCoverage = 18;
    private const int SecurityMetaFeature = 21485;
    private const int ProtectiveMarkingAttribute = 20664;

    /// <summary>The settings of one cell.</summary>
    public sealed record Options
    {
        /// <summary>The <c>DSID</c> <c>PRSP</c>, 51–57 for AML.</summary>
        public int ProductSpecification { get; init; } = 54;

        /// <summary>The <c>DSID</c> <c>PRED</c>.</summary>
        public string Edition { get; init; } = "1.0";

        /// <summary>The <c>DSID</c> <c>PROF</c>, the AML base profile of the product by default.</summary>
        public int? ApplicationProfile { get; init; }

        /// <summary>The <c>DSID</c> <c>INTU</c>: 100 + the scale band.</summary>
        public int IntendedUsage { get; init; } = 106;

        /// <summary>The <c>DSID</c> <c>COMT</c>; empty, as in the AML 3.0 trial cells, by default.</summary>
        public string Comment { get; init; } = string.Empty;

        /// <summary>The <c>m_clas</c> <c>secpmk</c> code (507 = Unclassified), or null for no <c>m_clas</c>.</summary>
        public int? SecurityMarkingCode { get; init; }

        /// <summary>Whether to write the <c>M_COVR</c> coverage area.</summary>
        public bool WithCoverage { get; init; } = true;

        /// <summary>
        /// More point features at the point position, record ids
        /// <see cref="ExtraPointRecordId"/> on: a class acronym and its
        /// attributes (acronym, value as encoded) from the cell's catalogue.
        /// </summary>
        public IReadOnlyList<ExtraPoint> ExtraPoints { get; init; } = [];
    }

    /// <summary>A point feature of <see cref="Options.ExtraPoints"/>.</summary>
    /// <param name="ClassAcronym">The object class acronym (<c>OBSTRN</c>, <c>contct</c>).</param>
    /// <param name="Attributes">The attributes: acronym and value as encoded.</param>
    public sealed record ExtraPoint(string ClassAcronym, params (string Acronym, string Value)[] Attributes);

    /// <summary>The record id of the first of <see cref="Options.ExtraPoints"/>.</summary>
    public const int ExtraPointRecordId = 10;

    /// <summary>The usual header comment of a UKHO AML 1.0 cell.</summary>
    public static string UkhoComment(string marking = "UNCLASSIFIED") =>
        $"\"title=North Atlantic Ocean - English Channel - 50°.622N - 1°.196W\"; IDO Status = NATO: North Atlantic Treaty Organisation; Protective Marking = {marking}; Owner Authority = UK";

    /// <summary>Writes a cell named <paramref name="fileName"/> into <paramref name="directory"/> and returns its path.</summary>
    public static string Write(string directory, string fileName, Options options)
    {
        var path = Path.Combine(directory, fileName);
        S57DocumentWriter.WriteToFile(path, Build(Path.GetFileName(fileName), options));
        return path;
    }

    /// <summary>Builds the cell's document.</summary>
    public static S57Document Build(string dataSetName, Options options)
    {
        var dsid = new S57DataSetIdentification
        {
            RecordName = new S57RecordName { RecordNameCode = 10, RecordId = 1 },
            ExchangePurpose = 1,
            IntendedUsage = options.IntendedUsage,
            DataSetName = dataSetName,
            EditionNumber = "1",
            UpdateNumber = "0",
            UpdateApplicationDate = "20260101",
            IssueDate = "20260101",
            S57EditionNumber = "03.1",
            ProductSpecification = options.ProductSpecification,
            ProductSpecificationEdition = options.Edition,
            ApplicationProfile = options.ApplicationProfile ?? BaseProfile(options.ProductSpecification),
            ProducingAgency = 540,
            DataStructure = 2,
            AttfLexicalLevel = 1,
            NatfLexicalLevel = 1,
            Comment = options.Comment,
        };

        var catalogue = S57ObjectCatalogue.For(S57ProductIdentity.FromDataSetIdentification(dsid));
        // An ENC control cell (PRSP 1) has no AML class; it gets a light (LIGHTS, 75).
        var amlClass = catalogue.ObjectClasses.FirstOrDefault(c => c.Code >= 20000 && c.Acronym != "m_clas")
            ?? catalogue.GetObjectClass(75)!;

        var vectors = new List<S57VectorRecord>
        {
            // The point feature's isolated node.
            Vector(110, 1, [Coordinate(PointLongitude, PointLatitude)]),
        };

        var features = new List<S57FeatureRecord>
        {
            Feature(PointRecordId, S57GeometricPrimitive.Point, amlClass.Code, [], [Pointer(110, 1)]),
        };

        if (options.WithCoverage)
        {
            // Four connected nodes and four edges round the square.
            (double Lon, double Lat)[] corners = [(West, South), (East, South), (East, North), (West, North)];
            for (var i = 0; i < 4; i++)
                vectors.Add(Vector(120, 10 + i, [Coordinate(corners[i].Lon, corners[i].Lat)]));
            for (var i = 0; i < 4; i++)
                vectors.Add(Edge(20 + i, 10 + i, 10 + ((i + 1) % 4)));

            features.Add(Feature(
                CoverageRecordId,
                S57GeometricPrimitive.Area,
                MetaCoverage,
                [new S57AttributeValue { AttributeCode = CategoryOfCoverage, Value = "1" }],
                [.. Enumerable.Range(0, 4).Select(i => Pointer(130, 20 + i, usage: S57UsageIndicator.Exterior))]));
        }

        for (var i = 0; i < options.ExtraPoints.Count; i++)
        {
            var extra = options.ExtraPoints[i];
            features.Add(Feature(
                ExtraPointRecordId + i,
                S57GeometricPrimitive.Point,
                catalogue.GetObjectClass(extra.ClassAcronym)!.Code,
                [.. extra.Attributes.Select(a => new S57AttributeValue { AttributeCode = catalogue.GetAttribute(a.Acronym)!.Code, Value = a.Value })],
                [Pointer(110, 1)]));
        }

        if (options.SecurityMarkingCode is { } marking)
        {
            features.Add(Feature(
                3,
                S57GeometricPrimitive.None,
                SecurityMetaFeature,
                [new S57AttributeValue { AttributeCode = ProtectiveMarkingAttribute, Value = marking.ToString(System.Globalization.CultureInfo.InvariantCulture) }],
                []));
        }

        return new S57Document
        {
            DataSetIdentification = dsid,
            DataSetParameters = new S57DataSetParameters
            {
                RecordName = new S57RecordName { RecordNameCode = 20, RecordId = 1 },
                HorizontalDatum = 2,
                CoordinateMultiplicationFactor = Comf,
                SoundingMultiplicationFactor = 10,
                CompilationScale = 50_000,
                DepthUnits = 1,
                HeightUnits = 1,
                PositionalUnits = 1,
                CoordinateUnits = 1,
            },
            VectorRecords = vectors,
            FeatureRecords = features,
        };
    }

    // The AML base application profiles: CLB 4, ESB 6, LBO 10, MFF 12, RAL 16,
    // SBO 18; AML 3.0 uses 20.
    private static int BaseProfile(int productSpecification) => productSpecification switch
    {
        51 => 12,
        52 => 16,
        53 => 10,
        54 => 4,
        55 => 6,
        56 => 18,
        _ => 20,
    };

    private static S57Coordinate2D Coordinate(double lon, double lat) =>
        new() { X = (int)Math.Round(lon * Comf), Y = (int)Math.Round(lat * Comf) };

    private static S57VectorRecord Vector(int rcnm, int id, S57Coordinate2D[] coordinates) => new()
    {
        RecordName = new S57RecordName { RecordNameCode = rcnm, RecordId = id },
        RecordVersion = 1,
        UpdateInstruction = S57UpdateInstruction.Insert,
        VectorPointers = [],
        Coordinates2D = coordinates,
        Soundings = [],
        Attributes = [],
    };

    private static S57VectorRecord Edge(int id, int begin, int end) => new()
    {
        RecordName = new S57RecordName { RecordNameCode = 130, RecordId = id },
        RecordVersion = 1,
        UpdateInstruction = S57UpdateInstruction.Insert,
        VectorPointers =
        [
            NodePointer(begin, S57TopologyIndicator.Beginning),
            NodePointer(end, S57TopologyIndicator.End),
        ],
        Coordinates2D = [],
        Soundings = [],
        Attributes = [],
    };

    private static S57VectorPointer NodePointer(int node, S57TopologyIndicator topology) => new()
    {
        Name = new S57RecordName { RecordNameCode = 120, RecordId = node },
        Orientation = (S57Orientation)255,
        Usage = (S57UsageIndicator)255,
        Topology = topology,
        Mask = (S57MaskingIndicator)255,
    };

    private static S57SpatialPointer Pointer(int rcnm, int id, S57UsageIndicator usage = (S57UsageIndicator)255) => new()
    {
        Name = new S57RecordName { RecordNameCode = rcnm, RecordId = id },
        Orientation = rcnm == 130 ? S57Orientation.Forward : (S57Orientation)255,
        Usage = usage,
        Mask = (S57MaskingIndicator)255,
    };

    private static S57FeatureRecord Feature(
        int id, S57GeometricPrimitive primitive, int objectCode, S57AttributeValue[] attributes, S57SpatialPointer[] pointers) => new()
        {
            RecordName = new S57RecordName
            {
                RecordNameCode = 100,
                RecordId = id,
                AgencyCode = 540,
                FeatureId = id,
                FeatureSubdivision = 1,
            },
            Primitive = primitive,
            RecordVersion = 1,
            UpdateInstruction = S57UpdateInstruction.Insert,
            Group = 255,
            ObjectCode = (S57ObjectCode)objectCode,
            Attributes = attributes,
            NationalAttributes = [],
            SpatialPointers = pointers,
            FeaturePointers = [],
        };
}
