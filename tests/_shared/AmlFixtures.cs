using EncDotNet.S57;
using EncDotNet.S57.Aml;
using EncDotNet.S57.Charts;

namespace EncDotNet.S100.TestSupport;

/// <summary>
/// Builds synthetic NATO Additional Military Layers (AML) cells for tests, in
/// memory, with chain-node topology: connected nodes, edges (straight or arcs),
/// and point, line and area features. The real AML samples must never be
/// committed, so every AML test draws from cells built here.
/// </summary>
/// <remarks>
/// Positions are (longitude, latitude) in decimal degrees. Attributes are
/// given by acronym and resolved through the product's
/// <see cref="S57ObjectCatalogue"/>, as are the object classes.
/// </remarks>
internal sealed class AmlCellBuilder
{
    private const int Comf = 10_000_000;
    private const int Agency = 0x021C;

    private readonly S57ObjectCatalogue _catalogue;
    private readonly int _productSpecification;
    private readonly string _edition;
    private readonly int _profile;
    private readonly string _name;
    private readonly List<S57VectorRecord> _vectors = [];
    private readonly List<S57FeatureRecord> _features = [];
    private readonly Dictionary<(int, int), int> _nodes = [];
    private int _nextNode = 1;
    private int _nextEdge = 1;
    private int _nextIsolated = 1;

    private AmlCellBuilder(S57ObjectCatalogue catalogue, int productSpecification, string edition, int profile, string name)
    {
        _catalogue = catalogue;
        _productSpecification = productSpecification;
        _edition = edition;
        _profile = profile;
        _name = name;
    }

    /// <summary>An AML 1.0 Routes, Areas and Limits (RAL) cell: DSID PRSP 52, PRED 1.0.</summary>
    public static AmlCellBuilder Ral(string name = "UKR0TEST.000") => new(S57ObjectCatalogue.Aml10, 52, "1.0", 16, name);

    /// <summary>An AML 3.0 integrated cell (the FAI/MFI trials): DSID PRSP 57, PRED 3.0.</summary>
    public static AmlCellBuilder Aml30(string name = "AMLTEST0U_FAI.000") => new(S57ObjectCatalogue.Aml30, 57, "3.0", 20, name);

    /// <summary>Adds a point feature.</summary>
    public AmlCellBuilder Point(string acronym, (double Lon, double Lat) position, params (string Acronym, string Value)[] attributes)
    {
        var node = new S57RecordName { RecordNameCode = 110, RecordId = _nextIsolated++ };
        _vectors.Add(Vector(node, [Coordinate(position)], []));
        AddFeature(acronym, S57GeometricPrimitive.Point, attributes, [Pointer(node, S57Orientation.NotApplicable, S57UsageIndicator.NotApplicable)]);
        return this;
    }

    /// <summary>Adds a line feature along straight edges between <paramref name="positions"/>.</summary>
    public AmlCellBuilder Line(string acronym, (double Lon, double Lat)[] positions, params (string Acronym, string Value)[] attributes)
    {
        var edge = Edge(positions);
        AddFeature(acronym, S57GeometricPrimitive.Line, attributes, [Pointer(edge, S57Orientation.Forward, S57UsageIndicator.NotApplicable)]);
        return this;
    }

    /// <summary>
    /// Adds an area feature whose exterior is the closed ring through
    /// <paramref name="ring"/> (first point repeated last), as one edge, in
    /// the given order (clockwise or not).
    /// </summary>
    public AmlCellBuilder Area(string acronym, (double Lon, double Lat)[] ring, params (string Acronym, string Value)[] attributes)
    {
        var edge = Edge(ring);
        AddFeature(acronym, S57GeometricPrimitive.Area, attributes, [Pointer(edge, S57Orientation.Forward, S57UsageIndicator.Exterior)]);
        return this;
    }

    /// <summary>
    /// Adds a circular area feature: one arc edge (ARCC/AR2D) clockwise about
    /// <paramref name="centre"/> from the point due north of it back to itself,
    /// as two half arcs.
    /// </summary>
    public AmlCellBuilder Circle(string acronym, (double Lon, double Lat) centre, double radiusDegrees, params (string Acronym, string Value)[] attributes)
    {
        var north = (centre.Lon, centre.Lat + radiusDegrees);
        var south = (centre.Lon, centre.Lat - radiusDegrees);
        var top = NodeAt(north);
        var bottom = NodeAt(south);
        var east = new S57RecordName { RecordNameCode = 130, RecordId = _nextEdge++ };
        var west = new S57RecordName { RecordNameCode = 130, RecordId = _nextEdge++ };
        _vectors.Add(ArcEdge(east, top, bottom, north, Coordinate(centre), south));
        _vectors.Add(ArcEdge(west, bottom, top, south, Coordinate(centre), north));
        AddFeature(
            acronym,
            S57GeometricPrimitive.Area,
            attributes,
            [Pointer(east, S57Orientation.Forward, S57UsageIndicator.Exterior), Pointer(west, S57Orientation.Forward, S57UsageIndicator.Exterior)]);
        return this;
    }

    /// <summary>The cell as a document.</summary>
    public S57Document BuildDocument() => new()
    {
        DataSetIdentification = new S57DataSetIdentification
        {
            RecordName = new S57RecordName { RecordNameCode = 10, RecordId = 1 },
            ExchangePurpose = 1,
            IntendedUsage = 5,
            DataSetName = _name,
            EditionNumber = "1",
            UpdateNumber = "0",
            UpdateApplicationDate = "20261101",
            IssueDate = "20261101",
            S57EditionNumber = "03.1",
            ProductSpecification = _productSpecification,
            ProductSpecificationEdition = _edition,
            ApplicationProfile = _profile,
            ProducingAgency = Agency,
            DataStructure = 2,
            AttfLexicalLevel = 1,
            NatfLexicalLevel = 1,
            Comment = "title=Synthetic test cell; Protective Marking=UNCLASSIFIED",
        },
        DataSetParameters = new S57DataSetParameters
        {
            RecordName = new S57RecordName { RecordNameCode = 20, RecordId = 1 },
            HorizontalDatum = 2,
            CompilationScale = 50_000,
            DepthUnits = 1,
            HeightUnits = 2,
            PositionalUnits = 1,
            CoordinateUnits = 1,
            CoordinateMultiplicationFactor = Comf,
            SoundingMultiplicationFactor = 10,
        },
        VectorRecords = _vectors,
        FeatureRecords = _features,
    };

    /// <summary>The cell as an AML data set.</summary>
    public AmlDataSet Build() => new(S57Chart.FromDocument(BuildDocument()));

    private void AddFeature(string acronym, S57GeometricPrimitive primitive, (string Acronym, string Value)[] attributes, IReadOnlyList<S57SpatialPointer> pointers)
    {
        var objectClass = _catalogue.GetObjectClass(acronym)
            ?? throw new ArgumentException($"{_catalogue.Name} has no class {acronym}.", nameof(acronym));
        int id = _features.Count + 1;
        _features.Add(new S57FeatureRecord
        {
            RecordName = new S57RecordName { RecordNameCode = 100, RecordId = id, AgencyCode = Agency, FeatureId = id, FeatureSubdivision = 1 },
            Primitive = primitive,
            Group = 2,
            ObjectCode = (S57ObjectCode)objectClass.Code,
            RecordVersion = 1,
            UpdateInstruction = S57UpdateInstruction.Insert,
            Attributes = [.. attributes.Select(a => new S57AttributeValue { AttributeCode = (_catalogue.GetAttribute(a.Acronym) ?? throw new ArgumentException($"{_catalogue.Name} has no attribute {a.Acronym}.")).Code, Value = a.Value })],
            NationalAttributes = [],
            SpatialPointers = pointers,
            FeaturePointers = [],
        });
    }

    private S57RecordName Edge((double Lon, double Lat)[] positions)
    {
        if (positions.Length < 2)
            throw new ArgumentException("An edge needs two positions.", nameof(positions));
        var begin = NodeAt(positions[0]);
        var end = NodeAt(positions[^1]);
        var name = new S57RecordName { RecordNameCode = 130, RecordId = _nextEdge++ };
        _vectors.Add(Vector(
            name,
            [.. positions[1..^1].Select(Coordinate)],
            [NodePointer(begin, S57TopologyIndicator.Beginning), NodePointer(end, S57TopologyIndicator.End)]));
        return name;
    }

    private S57VectorRecord ArcEdge(S57RecordName name, S57RecordName begin, S57RecordName end, (double, double) start, S57Coordinate2D centre, (double, double) finish) => new()
    {
        RecordName = name,
        RecordVersion = 1,
        UpdateInstruction = S57UpdateInstruction.Insert,
        Attributes = [],
        VectorPointers = [NodePointer(begin, S57TopologyIndicator.Beginning), NodePointer(end, S57TopologyIndicator.End)],
        Coordinates2D = [],
        Soundings = [],
        ArcCurves =
        [
            new S57ArcCurve
            {
                Type = S57ArcCurveType.ArcThreePointCentre,
                Surface = S57ConstructionSurface.Ellipsoidal,
                Order = 2,
                Resolution = 0,
                FloatingPointMultiplicationFactor = Comf,
                Arcs = [new S57Arc { Start = Coordinate(start), Centre = centre, End = Coordinate(finish) }],
                EllipticalArcs = [],
                ControlPoints = [],
            },
        ],
    };

    private S57RecordName NodeAt((double Lon, double Lat) position)
    {
        var coordinate = Coordinate(position);
        if (_nodes.TryGetValue((coordinate.X, coordinate.Y), out var id))
            return new S57RecordName { RecordNameCode = 120, RecordId = id };
        id = _nextNode++;
        _nodes[(coordinate.X, coordinate.Y)] = id;
        var name = new S57RecordName { RecordNameCode = 120, RecordId = id };
        _vectors.Add(Vector(name, [coordinate], []));
        return name;
    }

    private static S57VectorRecord Vector(S57RecordName name, IReadOnlyList<S57Coordinate2D> coordinates, IReadOnlyList<S57VectorPointer> pointers) => new()
    {
        RecordName = name,
        RecordVersion = 1,
        UpdateInstruction = S57UpdateInstruction.Insert,
        Attributes = [],
        VectorPointers = pointers,
        Coordinates2D = coordinates,
        Soundings = [],
        ArcCurves = [],
    };

    private static S57VectorPointer NodePointer(S57RecordName node, S57TopologyIndicator topology) => new()
    {
        Name = node,
        Orientation = S57Orientation.NotApplicable,
        Usage = S57UsageIndicator.NotApplicable,
        Topology = topology,
        Mask = S57MaskingIndicator.NotApplicable,
    };

    private static S57SpatialPointer Pointer(S57RecordName name, S57Orientation orientation, S57UsageIndicator usage) => new()
    {
        Name = name,
        Orientation = orientation,
        Usage = usage,
        Mask = S57MaskingIndicator.NotApplicable,
    };

    private static S57Coordinate2D Coordinate((double Lon, double Lat) position) => new()
    {
        X = (int)Math.Round(position.Lon * Comf),
        Y = (int)Math.Round(position.Lat * Comf),
    };
}

/// <summary>The synthetic AML cells the tests draw.</summary>
internal static class AmlFixtures
{
    /// <summary>
    /// A RAL cell with every built-in RAL class, about 50.5°N 1.2°W, laid out
    /// like the design's B2a and B2b frames: two ATS routes through a NAVAID
    /// and waypoints, a danger area with limits, controlled airspace as an arc
    /// circle, exercise airspace; a practice area, a Q-route with turning
    /// points, a patrol area and internal waters. Also a radar station, a RAL
    /// class with no style.
    /// </summary>
    public static AmlDataSet Ral() => RalBuilder().Build();

    /// <summary>The builder of <see cref="Ral"/>, for tests that write the cell to a file.</summary>
    public static AmlCellBuilder RalBuilder() => AmlCellBuilder.Ral()
        // Air.
        .Line("atsctl", [(-1.40, 50.62), (-1.20, 50.52), (-1.02, 50.44)], ("OBJNAM", "N864"))
        .Line("atsctl", [(-1.46, 50.49), (-1.20, 50.52), (-1.10, 50.57)], ("OBJNAM", "UL975"))
        .Point("navaid", (-1.20, 50.52), ("OBJNAM", "SUM VOR"))
        .Area(
            "airres",
            [(-1.12, 50.62), (-1.02, 50.62), (-1.02, 50.56), (-1.12, 50.56), (-1.12, 50.62)],
            ("OBJNAM", "D 613"), ("catasr", "501"), ("maxftl", "245"), ("minalt", "0"), ("HUNITS", "2"))
        .Circle("ctlasp", (-1.30, 50.44), 0.03, ("OBJNAM", "CTA 8"), ("maxalt", "3000"), ("minalt", "500"), ("HUNITS", "2"))
        .Area(
            "mexasp",
            [(-1.44, 50.47), (-1.34, 50.48), (-1.33, 50.43), (-1.43, 50.42), (-1.44, 50.47)],
            ("OBJNAM", "Exercise airspace"), ("maxalt", "5000"), ("minalt", "0"), ("HUNITS", "2"))
        // Operational.
        .Area(
            "MIPARE",
            [(-1.16, 50.48), (-1.06, 50.48), (-1.06, 50.43), (-1.16, 50.43), (-1.16, 50.48)],
            ("OBJNAM", "Firing practice area X5"), ("CATMPA", "3"))
        .Line("qroute", [(-1.24, 50.66), (-1.22, 50.60), (-1.18, 50.56)], ("OBJNAM", "Q-route 12"))
        .Point("turnpt", (-1.24, 50.66), ("OBJNAM", "TP 3"))
        .Point("turnpt", (-1.22, 50.60), ("OBJNAM", "TP 4"))
        .Area(
            "patare",
            [(-1.40, 50.58), (-1.30, 50.60), (-1.28, 50.54), (-1.38, 50.53), (-1.40, 50.58)],
            ("OBJNAM", "Patrol area"))
        .Area("intwtr", [(-1.46, 50.66), (-1.36, 50.66), (-1.36, 50.62), (-1.46, 50.62), (-1.46, 50.66)])
        // No style.
        .Point("RADSTA", (-1.08, 50.66), ("OBJNAM", "Radar"));

    /// <summary>An AML 3.0 cell (FAI-like): an ATS route with coded limits, air waypoints, a NAVAID and an airspace restriction with coded limits.</summary>
    public static AmlDataSet Fai() => AmlCellBuilder.Aml30()
        .Line("atsctl", [(9.0, 53.6), (9.6, 53.9), (9.8, 54.0)], ("OBJNAM", "UL619"), ("limitu", "FL660"), ("limitl", "FL245"))
        .Point("awaypt", (9.0, 53.6), ("identy", "OSTOR"))
        .Point("awaypt", (9.8, 54.0), ("identy", "BOGMU"))
        .Point("navaid", (9.6, 53.9), ("OBJNAM", "ELBE"))
        .Area(
            "airres",
            [(9.1, 54.0), (9.3, 54.0), (9.3, 53.9), (9.1, 53.9), (9.1, 54.0)],
            ("OBJNAM", "TRA-WESER 2"), ("efaltu", "FL100"), ("efaltl", "01000AGL"), ("SCAMIN", "1500000"))
        .Point("AIRARE", (9.6, 53.7), ("OBJNAM", "HAMBURG"))
        .Build();
}
